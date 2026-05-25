using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using static SayusGagExtender.API.GagSpeak.GagSpeakReflectionContext;

namespace SayusGagExtender;

public sealed class CammyEnforcer : IDisposable
{
    private readonly Plugin plugin;

    private DateTime onUpdateNextUTC = DateTime.MinValue;
    private readonly TimeSpan OnUpdateCooldown = TimeSpan.FromSeconds(1);

    private string? currentEnforcedPresetName;
    private bool wasEnforcing;

    public bool IsActive = false;

    public sealed class CammyEnforcerConfig
    {
        public string PresetName { get; set; } = string.Empty;

        // Higher number wins when several presets match.
        public int Priority { get; set; } = 0;

        public List<GagSpeakItem> RestraintSets { get; set; } = new();
        public List<GagSpeakItem> Restrictions { get; set; } = new();
        public List<GagSpeakItem> Gags { get; set; } = new();
    }

    public CammyEnforcer(Plugin plugin)
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
    }

    private void OnAnyChanged(object obj)
    {
        Enforce();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (onUpdateNextUTC > DateTime.UtcNow)
            return;

        onUpdateNextUTC = DateTime.UtcNow + OnUpdateCooldown;

        Enforce();
    }

    public CammyEnforcerConfig GetOrCreateCammyEnforcerConfig(
        string presetName,
        int priority = 0)
    {
        var existing = plugin.Configuration.CammyEnforcerPresets
            .FirstOrDefault(x => string.Equals(
                x.PresetName,
                presetName,
                StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.PresetName = presetName;
            return existing;
        }

        var created = new CammyEnforcerConfig
        {
            PresetName = presetName,
            Priority = priority,
        };

        plugin.Configuration.CammyEnforcerPresets.Add(created);
        return created;
    }

    public void Enforce()
    {
        IsActive = false;

        if (!plugin.Configuration.CammyEnforcerEnabled)
        {
            ReleaseEnforcement(applyDefaultOnce: true);
            return;
        }

        if (plugin.Configuration.CammyEnforcerPresets.Count == 0)
        {
            ReleaseEnforcement(applyDefaultOnce: true);
            return;
        }

        if (!plugin.CammyApi.IsAvailable())
        {
            ReleaseEnforcement(applyDefaultOnce: false);
            return;
        }

        var activeState = GetActiveState();
        var wantedPreset = GetHighestPriorityWantedPreset(activeState);

        if (wantedPreset == null)
        {
            ReleaseEnforcement(applyDefaultOnce: true);
            return;
        }

        IsActive = true;
        wasEnforcing = true;

        EnsurePresetActive(wantedPreset);
    }

    private CammyEnforcerConfig? GetHighestPriorityWantedPreset(CammyEnforcerActiveState activeState)
    {
        return plugin.Configuration.CammyEnforcerPresets
            .Where(x => !string.IsNullOrWhiteSpace(x.PresetName))
            .Where(x => x.Restrictions.Count + x.Gags.Count + x.RestraintSets.Count > 0)
            .Where(x => ShouldPresetBeActive(x, activeState))
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.PresetName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private void EnsurePresetActive(CammyEnforcerConfig presetConfig)
    {
        var wantedName = presetConfig.PresetName;

        var current = plugin.CammyApi.GetCurrentActivePreset();

        if (current != null &&
            string.Equals(current.Value.Name, wantedName, StringComparison.OrdinalIgnoreCase))
        {
            currentEnforcedPresetName = wantedName;
            return;
        }

        var ok = plugin.CammyApi.SetActivePreset(wantedName);

        if (!ok)
        {
            Plugin.ChatGui.PrintError(
                $"Failed to set Cammy preset '{wantedName}'. Make sure Cammy is loaded and the preset still exists.");
            return;
        }

        currentEnforcedPresetName = wantedName;
    }

    private void ReleaseEnforcement(bool applyDefaultOnce)
    {
        IsActive = false;

        if (!wasEnforcing)
            return;

        wasEnforcing = false;
        currentEnforcedPresetName = null;

        if (!applyDefaultOnce)
            return;

        ApplyDefaultPresetOnce();
    }

    private void ApplyDefaultPresetOnce()
    {
        var defaultPresetName = plugin.Configuration.CammyEnforcerDefaultPresetName;

        if (string.IsNullOrWhiteSpace(defaultPresetName))
        {
            plugin.CammyApi.ClearActivePresetOverride();
            return;
        }

        var isAlsoLinkedPreset = plugin.Configuration.CammyEnforcerPresets
            .Any(x => string.Equals(
                x.PresetName,
                defaultPresetName,
                StringComparison.OrdinalIgnoreCase));

        // Avoid fighting linked presets. Linked/enforced config wins.
        if (isAlsoLinkedPreset)
            return;

        var ok = plugin.CammyApi.SetActivePreset(defaultPresetName);

        if (!ok)
        {
            Plugin.ChatGui.PrintError(
                $"Failed to apply default Cammy preset '{defaultPresetName}'. Make sure the preset still exists.");
        }
    }

    private CammyEnforcerActiveState GetActiveState()
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

        return new CammyEnforcerActiveState
        {
            ActiveGagNames = activeGags,
            ActiveRestraintSetIds = activeRestraintSetIds,
            ActiveRestraintSetNames = activeRestraintSetNames,
            ActiveRestrictionIds = activeRestrictionIds,
            ActiveRestrictionNames = activeRestrictionNames,
        };
    }

    private bool ShouldPresetBeActive(
        CammyEnforcerConfig presetConfig,
        CammyEnforcerActiveState activeState)
    {
        if (ContainsAnyByIdOrName(
                presetConfig.RestraintSets,
                activeState.ActiveRestraintSetIds,
                activeState.ActiveRestraintSetNames))
        {
            return true;
        }

        if (ContainsAnyByIdOrName(
                presetConfig.Restrictions,
                activeState.ActiveRestrictionIds,
                activeState.ActiveRestrictionNames))
        {
            return true;
        }

        if (ContainsAnyByName(
                presetConfig.Gags,
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

    private sealed class CammyEnforcerActiveState
    {
        public HashSet<string> ActiveGagNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<Guid> ActiveRestraintSetIds { get; init; } = new();
        public HashSet<string> ActiveRestraintSetNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<Guid> ActiveRestrictionIds { get; init; } = new();
        public HashSet<string> ActiveRestrictionNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
