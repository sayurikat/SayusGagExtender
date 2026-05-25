using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Dalamud.Bindings.ImGui;
using Newtonsoft.Json;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;

namespace SayusGagExtender;

public sealed class HonorificManager : IDisposable
{
    private readonly Plugin plugin;

    private readonly Dictionary<object, HonorificTitleRequest> permanentRequests =
        new(new ReferenceEqualityComparer());

    private readonly List<HonorificTitleRequest> timedRequests = new();

    private readonly TimeSpan VerifyInterval = TimeSpan.FromSeconds(30);

    private DateTime nextVerifyUtc = DateTime.MinValue;

    private bool disposed;
    private bool dirty;

    private long nextSequence;

    private bool originalTitleCaptured;
    private string originalTitleJson = string.Empty;

    private string wantedTitleJson = string.Empty;
    private string lastAppliedTitleJson = string.Empty;

    public bool IsActive => !string.IsNullOrWhiteSpace(wantedTitleJson);
    public int PermanentRequestCount => permanentRequests.Count;
    public int TimedRequestCount => timedRequests.Count(x => !x.IsExpired(DateTime.UtcNow));

    public HonorificManager(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    /// <summary>
    /// Sets a persistent managed Honorific title for this owner.
    /// This remains active until the same owner recalls it, replaces it,
    /// or the manager is disposed.
    /// </summary>
    public void SetTitle(string titleJson, int priority, object owner)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));

        if (string.IsNullOrWhiteSpace(titleJson))
        {
            RecallTitle(owner);
            return;
        }

        permanentRequests[owner] = new HonorificTitleRequest
        {
            Json = titleJson,
            Priority = priority,
            Owner = owner,
            ExpiresAtUtc = null,
            Sequence = ++nextSequence,
        };

        dirty = true;
        EvaluateAndApply(forceVerifyCurrentTitle: false);
    }

    /// <summary>
    /// Sets a temporary managed Honorific title.
    /// Temporary requests cannot be recalled. They expire by duration only.
    /// Owner is kept for diagnostics/source tracking, but not for recall.
    /// </summary>
    public void SetTitle(string titleJson, TimeSpan duration, int priority, object owner)
    {
        if (string.IsNullOrWhiteSpace(titleJson))
            return;

        if (duration <= TimeSpan.Zero)
            return;

        timedRequests.Add(new HonorificTitleRequest
        {
            Json = titleJson,
            Priority = priority,
            Owner = owner,
            ExpiresAtUtc = DateTime.UtcNow + duration,
            Sequence = ++nextSequence,
        });

        dirty = true;
        EvaluateAndApply(forceVerifyCurrentTitle: false);
    }

    /// <summary>
    /// Recalls the persistent request from this exact owner instance.
    /// Does not affect timed requests.
    /// </summary>
    public bool RecallTitle(object owner)
    {
        if (owner == null)
            return false;

        var removed = permanentRequests.Remove(owner);

        if (removed)
        {
            dirty = true;
            EvaluateAndApply(forceVerifyCurrentTitle: false);
        }

        return removed;
    }

    /// <summary>
    /// Clears all managed requests and restores the title that existed
    /// before this manager first applied anything.
    /// </summary>
    public void ClearManagedTitles()
    {
        permanentRequests.Clear();
        timedRequests.Clear();

        wantedTitleJson = string.Empty;
        dirty = false;

        RestoreOriginalTitle();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        Plugin.Framework.Update -= OnFrameworkUpdate;

        permanentRequests.Clear();
        timedRequests.Clear();

        RestoreOriginalTitle();
    }
    public bool DrawTitleConfigEditors(
ref string title,
ref Vector3 color,
ref Vector3 glow,
ref int durationSeconds,
ref int priority,
float titleWidth = 120f,
float durationWidth = 50f,
float priorityWidth = 50f)
    {
        var changed = false;

        ImGui.SetNextItemWidth(titleWidth);

        if (ImGui.InputTextWithHint("##honorific-title", "disabled", ref title, 64))
        {
            if (title.Length > API.HonorificApi.MaxTitleLength)
                title = title[..API.HonorificApi.MaxTitleLength];

            changed = true;
        }

        ImGui.SameLine();

        if (ImGui.ColorEdit3(
                "##honorific-color",
                ref color,
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
        {
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Honorific title color");

        ImGui.SameLine();

        if (ImGui.ColorEdit3(
                "##honorific-glow",
                ref glow,
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
        {
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Honorific glow color");

        ImGui.SameLine();

        var durationText = durationSeconds.ToString();
        ImGui.SetNextItemWidth(durationWidth);

        if (ImGui.InputTextWithHint(
                "##honorific-duration",
                "sec",
                ref durationText,
                8,
                ImGuiInputTextFlags.CharsDecimal))
        {
            if (int.TryParse(durationText.Trim(), out var parsed))
                durationSeconds = Math.Clamp(parsed, 1, 3600);

            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Honorific title duration in seconds");

        ImGui.SameLine();

        var priorityText = priority.ToString();
        ImGui.SetNextItemWidth(priorityWidth);

        if (ImGui.InputTextWithHint(
                "##honorific-priority",
                "prio",
                ref priorityText,
                8,
                ImGuiInputTextFlags.CharsDecimal))
        {
            if (int.TryParse(priorityText.Trim(), out var parsed))
                priority = Math.Max(0, parsed);

            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Honorific title priority. Higher wins.");

        return changed;
    }
    public bool DrawPermanentTitleConfigEditors(
    ref string title,
    ref Vector3 color,
    ref Vector3 glow,
    ref int priority,
    float titleWidth = 120f,
    float priorityWidth = 50f)
    {
        var changed = false;

        ImGui.SetNextItemWidth(titleWidth);

        if (ImGui.InputTextWithHint("##honorific-title", "disabled", ref title, 64))
        {
            if (title.Length > API.HonorificApi.MaxTitleLength)
                title = title[..API.HonorificApi.MaxTitleLength];

            changed = true;
        }

        ImGui.SameLine();

        if (ImGui.ColorEdit3(
                "##honorific-color",
                ref color,
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
        {
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Honorific title color");

        ImGui.SameLine();

        if (ImGui.ColorEdit3(
                "##honorific-glow",
                ref glow,
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
        {
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Honorific glow color");

        ImGui.SameLine();

        var priorityText = priority.ToString();
        ImGui.SetNextItemWidth(priorityWidth);

        if (ImGui.InputTextWithHint(
                "##honorific-priority",
                "prio",
                ref priorityText,
                8,
                ImGuiInputTextFlags.CharsDecimal))
        {
            if (int.TryParse(priorityText.Trim(), out var parsed))
                priority = Math.Max(0, parsed);

            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Honorific title priority. Higher wins.");

        return changed;
    }
    public string BuildTitleJson(
        string title,
        Vector3 color,
        Vector3 glow,
        bool isPrefix = false)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        title = title.Trim();

        if (title.Length > API.HonorificApi.MaxTitleLength)
            title = title[..API.HonorificApi.MaxTitleLength];

        var titleData = new HonorificTitleData
        {
            Title = title,
            IsPrefix = isPrefix,
            Color = color,
            Glow = glow,
        };

        return JsonConvert.SerializeObject(titleData);
    }

    private sealed class HonorificTitleData
    {
        public string Title { get; set; } = string.Empty;
        public bool IsPrefix { get; set; }
        public Vector3? Color { get; set; }
        public Vector3? Glow { get; set; }
    }
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (disposed)
            return;

        var now = DateTime.UtcNow;

        if (RemoveExpiredTimedRequests(now))
            dirty = true;

        var shouldVerify = now >= nextVerifyUtc;

        if (shouldVerify)
            nextVerifyUtc = now + VerifyInterval;

        if (!dirty && !shouldVerify)
            return;

        EvaluateAndApply(forceVerifyCurrentTitle: shouldVerify);
    }

    private void EvaluateAndApply(bool forceVerifyCurrentTitle)
    {
        if (disposed)
            return;

        var now = DateTime.UtcNow;

        RemoveExpiredTimedRequests(now);

        var winner = GetWinningRequest(now);
        var newWantedJson = winner?.Json ?? string.Empty;

        var wantedChanged = !JsonEquals(wantedTitleJson, newWantedJson);
        wantedTitleJson = newWantedJson;

        // Important:
        // Do not clear dirty until we know it is safe to touch Honorific.
        // This lets other systems submit requests during login/loading screens.
        if (!CanUseHonorificApiNow())
        {
            dirty = true;
            return;
        }

        dirty = false;

        if (!string.IsNullOrWhiteSpace(wantedTitleJson))
        {
            CaptureOriginalTitleIfNeeded();

            var currentJson = forceVerifyCurrentTitle
                ? plugin.HonorificApi.GetLocalTitleJson()
                : lastAppliedTitleJson;

            if (!wantedChanged && JsonEquals(currentJson, wantedTitleJson))
                return;

            if (plugin.HonorificApi.SetLocalTitleJson(wantedTitleJson))
                lastAppliedTitleJson = wantedTitleJson;

            return;
        }

        if (!wantedChanged && !forceVerifyCurrentTitle)
            return;

        RestoreOriginalTitle();
    }
    private bool CanUseHonorificApiNow()
    {
        try
        {
            if (!plugin.HonorificApi.IsAvailable())
                return false;

            if (Plugin.ObjectTable.LocalPlayer == null)
                return false;

            if (Plugin.Condition.Any(
                    ConditionFlag.BetweenAreas,
                    ConditionFlag.BetweenAreas51,
                    ConditionFlag.WatchingCutscene,
                    ConditionFlag.WatchingCutscene78,
                    ConditionFlag.OccupiedInCutSceneEvent))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private HonorificTitleRequest? GetWinningRequest(DateTime now)
    {
        HonorificTitleRequest? best = null;

        foreach (var request in permanentRequests.Values)
            best = PickBetter(best, request, now);

        foreach (var request in timedRequests)
            best = PickBetter(best, request, now);

        return best;
    }

    private static HonorificTitleRequest? PickBetter(
        HonorificTitleRequest? currentBest,
        HonorificTitleRequest candidate,
        DateTime now)
    {
        if (candidate.IsExpired(now))
            return currentBest;

        if (currentBest == null)
            return candidate;

        if (candidate.Priority > currentBest.Priority)
            return candidate;

        if (candidate.Priority < currentBest.Priority)
            return currentBest;

        // Same priority: newest request wins.
        return candidate.Sequence > currentBest.Sequence
            ? candidate
            : currentBest;
    }

    private bool RemoveExpiredTimedRequests(DateTime now)
    {
        var before = timedRequests.Count;
        timedRequests.RemoveAll(x => x.IsExpired(now));
        return timedRequests.Count != before;
    }

    private void CaptureOriginalTitleIfNeeded()
    {
        if (originalTitleCaptured)
            return;

        originalTitleJson = plugin.HonorificApi.GetLocalTitleJson();
        originalTitleCaptured = true;
    }

    private void RestoreOriginalTitle()
    {
        try
        {
            if (!CanUseHonorificApiNow())
                return;

            if (!originalTitleCaptured)
            {
                plugin.HonorificApi.ClearLocalTitle();
            }
            else if (string.IsNullOrWhiteSpace(originalTitleJson))
            {
                plugin.HonorificApi.ClearLocalTitle();
            }
            else
            {
                plugin.HonorificApi.SetLocalTitleJson(originalTitleJson);
            }

            wantedTitleJson = string.Empty;
            lastAppliedTitleJson = string.Empty;
            originalTitleCaptured = false;
            originalTitleJson = string.Empty;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to restore original Honorific title.");
        }
    }

    private static bool JsonEquals(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b))
            return true;

        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        try
        {
            var tokenA = JToken.Parse(a);
            var tokenB = JToken.Parse(b);

            return JToken.DeepEquals(tokenA, tokenB);
        }
        catch
        {
            return string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal);
        }
    }

    private sealed class HonorificTitleRequest
    {
        public string Json { get; init; } = string.Empty;
        public int Priority { get; init; }
        public object? Owner { get; init; }
        public DateTime? ExpiresAtUtc { get; init; }
        public long Sequence { get; init; }

        public bool IsExpired(DateTime now)
        {
            return ExpiresAtUtc.HasValue && now >= ExpiresAtUtc.Value;
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
