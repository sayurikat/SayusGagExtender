using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using static SayusGagExtender.Configuration;

namespace SayusGagExtender.Windows;

public partial class ConfigWindow
{
    private readonly Dictionary<string, Guid> stagedQuotaMoodleSelections = new();
    private readonly Dictionary<string, string> quotaMoodleSearchText = new();

    private void DrawQuotasTab()
    {
        RefreshMoodleBlockOptionsIfNeeded();

        ImGui.TextWrapped("Configure action quotas. The quota Moodle is enforced while a quota is active. The empty Moodle is enforced when no quota remains.");
        ImGui.TextWrapped("Only entries older than 24 hours are cleared from quota logs.");
        ImGui.Spacing();

        DrawQuotaSetting(
            title: "Mount Quota",
            controlId: "mount-quota",
            enabled: configuration.MountQuotaEnabled,
            actionLimit: configuration.MountQuotaActions,
            window: configuration.MountQuotaWindow,
            actionLogUtc: configuration.MountQuotaActionLogUtc,
            quotaMoodleId: configuration.MountQuotaMoodleId,
            quotaMoodleName: configuration.MountQuotaMoodleName,
            emptyMoodleId: configuration.MountQuotaEmptyMoodleId,
            emptyMoodleName: configuration.MountQuotaEmptyMoodleName,

            onQuotaMoodleChanged: (id, name) =>
            {
                configuration.MountQuotaMoodleId = id;
                configuration.MountQuotaMoodleName = name;
                configuration.Save();
            },
            onEmptyMoodleChanged: (id, name) =>
            {
                configuration.MountQuotaEmptyMoodleId = id;
                configuration.MountQuotaEmptyMoodleName = name;
                configuration.Save();
            });

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawQuotaSetting(
            title: "Teleport Quota",
            controlId: "teleport-quota",
            enabled: configuration.TeleportQuotaEnabled,
            actionLimit: configuration.TeleportQuotaActions,
            window: configuration.TeleportQuotaWindow,
            actionLogUtc: configuration.TeleportQuotaActionLogUtc,
            quotaMoodleId: configuration.TeleportQuotaMoodleId,
            quotaMoodleName: configuration.TeleportQuotaMoodleName,
            emptyMoodleId: configuration.TeleportQuotaEmptyMoodleId,
            emptyMoodleName: configuration.TeleportQuotaEmptyMoodleName,

            onQuotaMoodleChanged: (id, name) =>
            {
                configuration.TeleportQuotaMoodleId = id;
                configuration.TeleportQuotaMoodleName = name;
                configuration.Save();
            },
            onEmptyMoodleChanged: (id, name) =>
            {
                configuration.TeleportQuotaEmptyMoodleId = id;
                configuration.TeleportQuotaEmptyMoodleName = name;
                configuration.Save();
            });

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawQuotaSetting(
            title: "Job Switch Quota",
            controlId: "job-switch-quota",
            enabled: configuration.JobSwitchQuotaEnabled,
            actionLimit: configuration.JobSwitchQuotaActions,
            window: configuration.JobSwitchQuotaWindow,
            actionLogUtc: configuration.JobSwitchQuotaActionLogUtc,
            quotaMoodleId: configuration.JobSwitchQuotaMoodleId,
            quotaMoodleName: configuration.JobSwitchQuotaMoodleName,
            emptyMoodleId: configuration.JobSwitchQuotaEmptyMoodleId,
            emptyMoodleName: configuration.JobSwitchQuotaEmptyMoodleName,

            onQuotaMoodleChanged: (id, name) =>
            {
                configuration.JobSwitchQuotaMoodleId = id;
                configuration.JobSwitchQuotaMoodleName = name;
                configuration.Save();
            },
            onEmptyMoodleChanged: (id, name) =>
            {
                configuration.JobSwitchQuotaEmptyMoodleId = id;
                configuration.JobSwitchQuotaEmptyMoodleName = name;
                configuration.Save();
            });

        ImGui.Spacing();

        if (ImGui.Button("Refresh Moodle list##quotas"))
            _ = RefreshMoodleBlockOptionsAsync(force: true);

        if (moodleBlockOptionsLoading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Loading Moodles...");
        }
    }

    private void DrawQuotaSetting(
    string title,
    string controlId,
    bool enabled,
    int actionLimit,
    QuotaWindow window,
    List<DateTime>? actionLogUtc,
    Guid quotaMoodleId,
    string quotaMoodleName,
    Guid emptyMoodleId,
    string emptyMoodleName,
    Action<Guid, string> onQuotaMoodleChanged,
    Action<Guid, string> onEmptyMoodleChanged)
    {
        ImGui.TextUnformatted(title);

        var used = GetQuotaUsed(actionLogUtc, window);
        var remaining = actionLimit <= 0
            ? 0
            : Math.Max(0, actionLimit - used);

        ImGui.TextDisabled($"Enabled: {(enabled ? "Yes" : "No")}");
        ImGui.TextDisabled($"Quota: {Math.Max(0, actionLimit)} per {GetQuotaWindowLabel(window)}");
        ImGui.TextDisabled($"Used: {used} / {Math.Max(0, actionLimit)}");
        ImGui.SameLine();
        ImGui.TextDisabled($"Left: {remaining}");

        if (enabled && actionLimit > -1 && remaining <= 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.25f, 0.25f, 1.0f), "Empty");
        }

        ImGui.Spacing();

        DrawSingleQuotaMoodlePicker(
            label: "Quota Moodle",
            controlId: $"{controlId}-quota-moodle",
            currentId: quotaMoodleId,
            currentName: quotaMoodleName,
            onChanged: onQuotaMoodleChanged);

        DrawSingleQuotaMoodlePicker(
            label: "Empty Moodle",
            controlId: $"{controlId}-empty-moodle",
            currentId: emptyMoodleId,
            currentName: emptyMoodleName,
            onChanged: onEmptyMoodleChanged);
    }
    private static string GetQuotaWindowLabel(QuotaWindow window)
    {
        return window switch
        {
            QuotaWindow.Day => "day",
            _ => "hour",
        };
    }

    private void DrawSingleQuotaMoodlePicker(
        string label,
        string controlId,
        Guid currentId,
        string currentName,
        Action<Guid, string> onChanged)
    {
        ImGui.TextUnformatted(label);

        var displayName = GetCurrentQuotaMoodleDisplayName(currentId, currentName);

        stagedQuotaMoodleSelections.TryGetValue(controlId, out var stagedId);

        var stagedPreview = stagedId != Guid.Empty && moodleBlockOptions.TryGetValue(stagedId, out var stagedName)
            ? stagedName
            : displayName;

        const float selectedRowWidth = 300f;

        if (currentId != Guid.Empty)
        {
            //ImGui.TextDisabled("Selected Moodle");

            var ctrlHeld = ImGui.GetIO().KeyCtrl;

            ImGui.Indent();

            ImGui.PushID($"{controlId}-selected-{currentId}");

            if (DrawGagSpeakItem(displayName, selectedRowWidth, ctrlHeld))
            {
                onChanged(Guid.Empty, string.Empty);

                stagedQuotaMoodleSelections.Remove(controlId);
                quotaMoodleSearchText[controlId] = string.Empty;
            }

            ImGui.PopID();

            ImGui.Unindent();
        }
        else
        {
            ImGui.TextDisabled("No Moodle selected.");
        }

        ImGui.SetNextItemWidth(300);

        if (ImGui.BeginCombo($"##{controlId}-combo", stagedPreview))
        {
            if (!quotaMoodleSearchText.TryGetValue(controlId, out var searchText))
                searchText = string.Empty;

            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText($"##{controlId}-search", ref searchText, 128))
                quotaMoodleSearchText[controlId] = searchText;

            ImGui.Separator();

            if (ImGui.Selectable($"None##{controlId}-none", currentId == Guid.Empty))
            {
                stagedQuotaMoodleSelections[controlId] = Guid.Empty;
                quotaMoodleSearchText[controlId] = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.Separator();

            var filteredMoodles = moodleBlockOptions
                .Where(x => string.IsNullOrWhiteSpace(searchText) ||
                            x.Value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var moodle in filteredMoodles)
            {
                var isSelected = moodle.Key == currentId;
                var isStaged = moodle.Key == stagedId;

                if (ImGui.Selectable($"{moodle.Value}##{controlId}-{moodle.Key}", isSelected || isStaged))
                {
                    stagedQuotaMoodleSelections[controlId] = moodle.Key;
                    quotaMoodleSearchText[controlId] = string.Empty;
                    ImGui.CloseCurrentPopup();
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            if (moodleBlockOptions.Count == 0)
                ImGui.TextDisabled(moodleBlockOptionsLoading ? "Loading Moodles..." : "No Moodles found. Click Refresh Moodle list.");
            else if (filteredMoodles.Length == 0)
                ImGui.TextDisabled("No matching Moodles.");

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        var stagedValid =
            stagedId == Guid.Empty ||
            moodleBlockOptions.ContainsKey(stagedId);

        if (!stagedQuotaMoodleSelections.ContainsKey(controlId) || !stagedValid)
            ImGui.BeginDisabled();

        if (ImGui.Button($"Set##{controlId}-set"))
        {
            if (stagedId == Guid.Empty)
            {
                onChanged(Guid.Empty, string.Empty);
            }
            else if (moodleBlockOptions.TryGetValue(stagedId, out var selectedName))
            {
                onChanged(stagedId, selectedName);
            }

            stagedQuotaMoodleSelections.Remove(controlId);
            quotaMoodleSearchText[controlId] = string.Empty;
        }

        if (!stagedQuotaMoodleSelections.ContainsKey(controlId) || !stagedValid)
            ImGui.EndDisabled();

        

        ImGui.Spacing();
    }

    private string GetCurrentQuotaMoodleDisplayName(Guid id, string savedName)
    {
        if (id == Guid.Empty)
            return "None";

        if (moodleBlockOptions.TryGetValue(id, out var currentName))
            return currentName;

        if (!string.IsNullOrWhiteSpace(savedName))
            return $"{savedName} (missing)";

        return $"{id} (missing)";
    }

    private static int GetQuotaUsed(List<DateTime>? actionLogUtc, QuotaWindow window)
    {
        if (actionLogUtc == null || actionLogUtc.Count == 0)
            return 0;

        var cutoff = DateTime.UtcNow - GetQuotaWindowDuration(window);

        return actionLogUtc.Count(x => x >= cutoff);
    }

    private static TimeSpan GetQuotaWindowDuration(QuotaWindow window)
    {
        return window switch
        {
            QuotaWindow.Day => TimeSpan.FromHours(24),
            _ => TimeSpan.FromHours(1),
        };
    }
}
