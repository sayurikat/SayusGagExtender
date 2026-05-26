using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static SayusGagExtender.API.GagSpeak.GagSpeakReflectionContext;

namespace SayusGagExtender;

public sealed class HonorificEnforcer : IDisposable
{
    private readonly Plugin plugin;

    private DateTime onUpdateNextUTC = DateTime.MinValue;
    private readonly TimeSpan OnUpdateCooldown = TimeSpan.FromSeconds(1);

    private string lastSubmittedJson = string.Empty;
    private int lastSubmittedPriority = -1;
    private bool hasSubmittedRequest;

    private bool needsEvaluation = true;
    private DateTime suppressEvaluationUntilUtc = DateTime.MinValue;
    private static readonly TimeSpan ConfigEditApplyDelay = TimeSpan.FromSeconds(2);

    public bool IsActive = false;

    public sealed class HonorificEnforcerConfig
    {
        public string HonorificTitle { get; set; } = string.Empty;
        public Vector3 HonorificColor { get; set; } = new(1.0f, 1.0f, 1.0f);
        public Vector3 HonorificGlow { get; set; } = new(0.0f, 0.0f, 0.0f);
        public string HonorificSourceJson { get; set; } = string.Empty;
        public int HonorificPriority { get; set; } = 100;
        public List<GagSpeakItem> RestraintSets { get; set; } = new();
        public List<GagSpeakItem> Restrictions { get; set; } = new();
        public List<GagSpeakItem> Gags { get; set; } = new();
    }

    public HonorificEnforcer(Plugin plugin)
    {
        this.plugin = plugin;

        Plugin.Framework.Update += OnFrameworkUpdate;

        plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged += OnAnyChanged;
        plugin.GagSpeakGagsApi.OnGagsChanged += OnAnyChanged;
        plugin.GagSpeakRestraintSetApi.OnRestraintSetChanged += OnAnyChanged;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;

        plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged -= OnAnyChanged;
        plugin.GagSpeakGagsApi.OnGagsChanged -= OnAnyChanged;
        plugin.GagSpeakRestraintSetApi.OnRestraintSetChanged -= OnAnyChanged;

        RecallCurrentRequest();
    }

    private void OnAnyChanged(object obj)
    {
        MarkConfigDirty();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;

        if (now < suppressEvaluationUntilUtc)
            return;

        if (!needsEvaluation && onUpdateNextUTC > now)
            return;

        onUpdateNextUTC = now + OnUpdateCooldown;
        needsEvaluation = false;

        Enforce();
    }

    public void Enforce()
    {
        IsActive = false;

        if (!plugin.Configuration.HonorificEnforcerEnabled)
        {
            RecallCurrentRequest();
            return;
        }

        if (plugin.Configuration.HonorificEnforcerTitles.Count == 0)
        {
            RecallCurrentRequest();
            return;
        }

        var activeState = GetActiveState();

        HonorificEnforcerConfig? winner = null;

        foreach (var config in plugin.Configuration.HonorificEnforcerTitles)
        {
            if (string.IsNullOrWhiteSpace(config.HonorificTitle))
                continue;

            if (config.Restrictions.Count + config.Gags.Count + config.RestraintSets.Count <= 0)
                continue;

            if (!ShouldTitleBeActive(config, activeState))
                continue;

            if (winner == null || config.HonorificPriority > winner.HonorificPriority)
                winner = config;
        }

        if (winner == null)
        {
            RecallCurrentRequest();
            return;
        }

        var json = plugin.HonorificManager.BuildTitleJson(winner.HonorificSourceJson, winner.HonorificTitle, winner.HonorificColor, winner.HonorificGlow);

        if (string.IsNullOrWhiteSpace(json))
        {
            RecallCurrentRequest();
            return;
        }

        IsActive = true;

        if (hasSubmittedRequest &&
            lastSubmittedPriority == winner.HonorificPriority &&
            string.Equals(lastSubmittedJson, json, StringComparison.Ordinal))
        {
            return;
        }

        plugin.HonorificManager.SetTitle(
            json,
            winner.HonorificPriority,
            this);

        hasSubmittedRequest = true;
        lastSubmittedJson = json;
        lastSubmittedPriority = winner.HonorificPriority;
    }
    public void MarkConfigDirty(bool delayApply = false)
    {
        needsEvaluation = true;

        if (delayApply)
            suppressEvaluationUntilUtc = DateTime.UtcNow + ConfigEditApplyDelay;
    }
    private void RecallCurrentRequest()
    {
        if (!hasSubmittedRequest)
        {
            IsActive = false;
            return;
        }

        plugin.HonorificManager.RecallTitle(this);

        hasSubmittedRequest = false;
        lastSubmittedJson = string.Empty;
        lastSubmittedPriority = -1;
        IsActive = false;
    }

    private HonorificEnforcerActiveState GetActiveState()
    {
        var activeGags = plugin.GagSpeakGagsApi
            .GetActiveGags()
            .Values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var activeRestraintSet = plugin.GagSpeakRestraintSetApi.GetActiveRestraintSet();

        var activeRestraintSetIds = new HashSet<Guid>();

        if (activeRestraintSet.Key != Guid.Empty)
            activeRestraintSetIds.Add(activeRestraintSet.Key);

        var activeRestraintSetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(activeRestraintSet.Value))
            activeRestraintSetNames.Add(activeRestraintSet.Value);

        var activeRestrictions = plugin.GagSpeakRestrictionsApi.GetActiveRestrictionsWithId();

        var activeRestrictionIds = activeRestrictions
            .Where(x => x.Key != Guid.Empty)
            .Select(x => x.Key)
            .ToHashSet();

        var activeRestrictionNames = activeRestrictions
            .Values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new HonorificEnforcerActiveState
        {
            ActiveGagNames = activeGags,
            ActiveRestraintSetIds = activeRestraintSetIds,
            ActiveRestraintSetNames = activeRestraintSetNames,
            ActiveRestrictionIds = activeRestrictionIds,
            ActiveRestrictionNames = activeRestrictionNames,
        };
    }

    private static bool ShouldTitleBeActive(
        HonorificEnforcerConfig config,
        HonorificEnforcerActiveState activeState)
    {
        if (ContainsAnyByIdOrName(
                config.RestraintSets,
                activeState.ActiveRestraintSetIds,
                activeState.ActiveRestraintSetNames))
        {
            return true;
        }

        if (ContainsAnyByIdOrName(
                config.Restrictions,
                activeState.ActiveRestrictionIds,
                activeState.ActiveRestrictionNames))
        {
            return true;
        }

        if (ContainsAnyByName(
                config.Gags,
                activeState.ActiveGagNames))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsAnyByIdOrName(
        List<GagSpeakItem> configuredItems,
        HashSet<Guid> activeIds,
        HashSet<string> activeNames)
    {
        foreach (var item in configuredItems)
        {
            if (item.Id != Guid.Empty && activeIds.Contains(item.Id))
                return true;

            if (!string.IsNullOrWhiteSpace(item.Name) && activeNames.Contains(item.Name))
                return true;
        }

        return false;
    }

    private static bool ContainsAnyByName(
        List<GagSpeakItem> configuredItems,
        HashSet<string> activeNames)
    {
        foreach (var item in configuredItems)
        {
            if (!string.IsNullOrWhiteSpace(item.Name) && activeNames.Contains(item.Name))
                return true;
        }

        return false;
    }

    private sealed class HonorificEnforcerActiveState
    {
        public HashSet<string> ActiveGagNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<Guid> ActiveRestraintSetIds { get; init; } = new();
        public HashSet<string> ActiveRestraintSetNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<Guid> ActiveRestrictionIds { get; init; } = new();
        public HashSet<string> ActiveRestrictionNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
