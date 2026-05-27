using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SayusGagExtender.Windows;

public partial class ConfigWindow
{
    private int rouletteSelectedAddIndex;
    private string rouletteSearchText = string.Empty;
    private string rouletteIntervalSecondsText = string.Empty;
    private readonly Dictionary<string, Guid> stagedRouletteMoodleSelections = new();
    private readonly Dictionary<string, string> rouletteMoodleSearchText = new();

    private void DrawRouletteTab()
    {
        RefreshMoodleBlockOptionsIfNeeded();

        ImGui.BeginDisabled();
        var remoteSet = configuration.JobRouletteRemoteSet;
        if (ImGui.Checkbox("Remote Set", ref remoteSet))
        {
            configuration.JobRouletteRemoteSet = remoteSet;
            configuration.Save();
        }
        ImGui.EndDisabled();
        ImGui.TextWrapped("When Remote Set is enabled, local roulette controls are locked. The remote command/config layer will own these settings later.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (configuration.JobRouletteRemoteSet)
            ImGui.BeginDisabled();

        DrawRouletteMainSettings();

        if (configuration.JobRouletteRemoteSet)
            ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        //DrawRouletteTimingControls();
        //
        //ImGui.Spacing();
        //ImGui.Separator();
        //ImGui.Spacing();

        DrawRouletteGearsetWhitelist();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawRouletteEffects();
    }

    private void DrawRouletteMainSettings()
    {

        var enabled = configuration.JobRouletteEnabled;
        if (ImGui.Checkbox("Enable Job Roulette", ref enabled))
        {
            configuration.JobRouletteEnabled = enabled;
            configuration.Save();
        }

        var swapEvenLocked = configuration.JobRouletteSwapEvenIfLockedOrOutOfQuota;
        if (ImGui.Checkbox("Swap even while locked / out of quota", ref swapEvenLocked))
        {
            configuration.JobRouletteSwapEvenIfLockedOrOutOfQuota = swapEvenLocked;
            configuration.Save();
        }

        var swapOverspend = configuration.JobRouletteSpendOutOfQuotaLimit;
        if (ImGui.Checkbox("Spend even while out of quota", ref swapOverspend))
        {
            configuration.JobRouletteSpendOutOfQuotaLimit = swapOverspend;
            configuration.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When enabled, scheduled roulette swaps are allowed even while normal job switching is locked or quota is empty. Quota is never increased above the configured limit.");

        var lockManual = configuration.JobRouletteLockManualChanges;
        if (ImGui.Checkbox("Lock manual job changes while roulette is active", ref lockManual))
        {
            configuration.JobRouletteLockManualChanges = lockManual;
            configuration.Save();
        }

        ImGui.TextDisabled($"Whitelisted gearsets: {configuration.JobRouletteWhitelistedGearsets.Count}");
        ImGui.TextDisabled($"Remaining job quota: {plugin.JobManager.GetRemainingQuota()}");



        ImGui.Text("Schedule");

        ImGui.TextDisabled("Interval");
        ImGui.SameLine();
        ImGui.TextUnformatted(plugin.Utils.FormatCompactTimeSpan(configuration.JobRouletteInterval));

        if (configuration.JobRouletteEnabled)
        {
            var untilNext = configuration.NextScheduledJobSwitch - DateTime.UtcNow;
            if (untilNext < TimeSpan.Zero)
                untilNext = TimeSpan.Zero;

            ImGui.TextDisabled("Next switch");
            ImGui.SameLine();
            ImGui.TextUnformatted(plugin.Utils.FormatCompactTimeSpan(untilNext));
        }

        var next = configuration.NextScheduledJobSwitch;
        var nextLabel = next == DateTime.MinValue
            ? "Not scheduled"
            : next <= DateTime.UtcNow
                ? "Due now"
                : $"{next:yyyy-MM-dd HH:mm:ss} UTC ({(next - DateTime.UtcNow).TotalSeconds:F0}s)";

        ImGui.TextDisabled($"Next switch: {nextLabel}");
    }

    private void DrawRouletteTimingControls()
    {
        ImGui.Text("Schedule");

        if (string.IsNullOrWhiteSpace(rouletteIntervalSecondsText))
            rouletteIntervalSecondsText = Math.Max(1, (int)configuration.JobRouletteInterval.TotalSeconds).ToString();

        ImGui.SetNextItemWidth(90);
        if (ImGui.InputText("Interval seconds", ref rouletteIntervalSecondsText, 16, ImGuiInputTextFlags.CharsDecimal))
        {
            if (int.TryParse(rouletteIntervalSecondsText.Trim(), out var seconds))
            {
                seconds = Math.Max(1, seconds);
                configuration.JobRouletteInterval = TimeSpan.FromSeconds(seconds);
                configuration.Save();
            }
        }

        var next = configuration.NextScheduledJobSwitch;
        var nextLabel = next == DateTime.MinValue
            ? "Not scheduled"
            : next <= DateTime.UtcNow
                ? "Due now"
                : $"{next:yyyy-MM-dd HH:mm:ss} UTC ({(next - DateTime.UtcNow).TotalSeconds:F0}s)";

        ImGui.TextDisabled($"Next switch: {nextLabel}");

        if (ImGui.Button("Set interval to 60s##roulette"))
        {
            configuration.JobRouletteInterval = TimeSpan.FromSeconds(60);
            rouletteIntervalSecondsText = "60";
            configuration.Save();
        }

        ImGui.SameLine();

        if (ImGui.Button("Make next switch due now##roulette"))
        {
            configuration.NextScheduledJobSwitch = DateTime.UtcNow;
            configuration.Save();
        }

        if (ImGui.Button("Schedule next switch from interval##roulette"))
        {
            var interval = configuration.JobRouletteInterval <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(60)
                : configuration.JobRouletteInterval;

            configuration.NextScheduledJobSwitch = DateTime.UtcNow + interval;
            configuration.Save();
        }
    }

    private void DrawRouletteGearsetWhitelist()
    {
        ImGui.Text("Roulette Gearsets");
        ImGui.TextWrapped("Add gearsets that roulette is allowed to pick. Missing or renamed gearsets are pruned by JobManager when it rolls.");

        var allGearsets = plugin.JobManager.GetAllGearsets()
            .OrderBy(x => x.JobName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.GearsetId)
            .ToList();

        DrawRouletteAddGearsetCombo(allGearsets);

        ImGui.Spacing();

        if (configuration.JobRouletteWhitelistedGearsets.Count == 0)
        {
            ImGui.TextDisabled("No roulette gearsets selected.");
            return;
        }

        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        const float rowWidth = 420f;

        var orderedSelected = configuration.JobRouletteWhitelistedGearsets
            .OrderBy(x => x.JobName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.GearsetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.GearsetId)
            .ToList();

        for (var displayIndex = 0; displayIndex < orderedSelected.Count; displayIndex++)
        {
            var gearset = orderedSelected[displayIndex];
            var actualIndex = configuration.JobRouletteWhitelistedGearsets.FindIndex(x =>
                x.GearsetId == gearset.GearsetId &&
                x.ClassJobId == gearset.ClassJobId &&
                string.Equals(x.GearsetName ?? string.Empty, gearset.GearsetName ?? string.Empty, StringComparison.Ordinal));

            if (actualIndex < 0)
                continue;

            ImGui.PushID($"roulette-selected-{gearset.GearsetId}-{gearset.ClassJobId}-{displayIndex}");

            var label = GetRouletteGearsetDisplayName(gearset);
            if (DrawRouletteSelectedRow(label, rowWidth, ctrlHeld))
            {
                configuration.JobRouletteWhitelistedGearsets.RemoveAt(actualIndex);
                configuration.Save();
                ImGui.PopID();
                break;
            }

            ImGui.PopID();
        }
    }

    private void DrawRouletteAddGearsetCombo(IReadOnlyList<JobManager.GearsetInfo> allGearsets)
    {
        var available = allGearsets
            .Where(x => !configuration.JobRouletteWhitelistedGearsets.Any(w => RouletteGearsetConfigMatches(w, x)))
            .ToList();

        if (available.Count == 0)
        {
            ImGui.TextDisabled(allGearsets.Count == 0
                ? "No gearsets found."
                : "All available gearsets are already whitelisted.");
            return;
        }

        if (rouletteSelectedAddIndex < 0 || rouletteSelectedAddIndex >= available.Count)
            rouletteSelectedAddIndex = 0;

        var selected = available[rouletteSelectedAddIndex];
        var preview = GetRouletteGearsetDisplayName(selected);

        ImGui.SetNextItemWidth(420);
        if (ImGui.BeginCombo("##roulette-add-gearset", preview))
        {
            ImGui.SetNextItemWidth(-1);

            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();

            ImGui.InputTextWithHint("##roulette-gearset-search", "Search gearsets...", ref rouletteSearchText, 128);
            ImGui.Separator();

            var filtered = string.IsNullOrWhiteSpace(rouletteSearchText)
                ? available
                : available
                    .Where(x =>
                        x.JobName.Contains(rouletteSearchText, StringComparison.OrdinalIgnoreCase) ||
                        x.Name.Contains(rouletteSearchText, StringComparison.OrdinalIgnoreCase) ||
                        x.GearsetId.ToString().Contains(rouletteSearchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (filtered.Count == 0)
            {
                ImGui.TextDisabled("No matches.");
            }
            else
            {
                foreach (var gearset in filtered)
                {
                    var originalIndex = available.FindIndex(x => x.GearsetId == gearset.GearsetId);
                    var isSelected = originalIndex == rouletteSelectedAddIndex;
                    var label = GetRouletteGearsetDisplayName(gearset);

                    if (ImGui.Selectable($"{label}##roulette-gearset-{gearset.GearsetId}", isSelected))
                    {
                        rouletteSelectedAddIndex = originalIndex;
                        rouletteSearchText = string.Empty;
                        ImGui.CloseCurrentPopup();
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        if (ImGui.Button("Add##roulette-gearset"))
        {
            var gearset = available[rouletteSelectedAddIndex];

            configuration.JobRouletteWhitelistedGearsets.Add(new Configuration.JobRouletteGearsetConfig
            {
                GearsetId = gearset.GearsetId,
                GearsetName = gearset.Name,
                ClassJobId = gearset.ClassJobId,
                JobName = gearset.JobName,
            });

            rouletteSelectedAddIndex = 0;
            rouletteSearchText = string.Empty;
            configuration.Save();
        }
    }

    private void DrawRouletteEffects()
    {
        ImGui.Text("Roulette Effects");
        ImGui.TextWrapped("Applies while Job Roulette is enabled and at least one gearset is whitelisted.");

        ImGui.Spacing();

        DrawSingleRouletteEffectEditor("Job Roulette active", "job-roulette-active-effect", configuration.JobRouletteEffect);

        ImGui.Spacing();

        if (ImGui.Button("Refresh Moodle list##roulette-effects"))
            _ = RefreshMoodleBlockOptionsAsync(force: true);

        if (moodleBlockOptionsLoading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Loading Moodles...");
        }
    }

    private void DrawSingleRouletteEffectEditor(string label, string controlId, Configuration.JobRouletteEffectConfig config)
    {
        ImGui.PushID(controlId);

        if (ImGui.CollapsingHeader($"{label}##header", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            DrawRouletteEffectMoodleEditor(controlId, config);

            ImGui.Spacing();

            DrawRouletteEffectHonorificEditor(config);

            ImGui.Unindent();
        }

        ImGui.PopID();
    }

    private void DrawRouletteEffectMoodleEditor(string controlId, Configuration.JobRouletteEffectConfig config)
    {
        ImGui.TextUnformatted("Moodle");

        var displayName = GetCurrentRouletteMoodleDisplayName(config.MoodleId, config.MoodleName);

        stagedRouletteMoodleSelections.TryGetValue(controlId, out var stagedId);

        var stagedPreview = stagedId != Guid.Empty && moodleBlockOptions.TryGetValue(stagedId, out var stagedName) ? stagedName : displayName;

        const float selectedRowWidth = 300f;

        if (config.MoodleId != Guid.Empty)
        {
            var ctrlHeld = ImGui.GetIO().KeyCtrl;

            ImGui.Indent();

            ImGui.PushID($"{controlId}-selected-{config.MoodleId}");

            if (DrawGagSpeakItem(displayName, selectedRowWidth, ctrlHeld))
            {
                config.MoodleId = Guid.Empty;
                config.MoodleName = string.Empty;
                configuration.Save();

                stagedRouletteMoodleSelections.Remove(controlId);
                rouletteMoodleSearchText[controlId] = string.Empty;
            }

            ImGui.PopID();

            ImGui.Unindent();
        }
        else
        {
            ImGui.TextDisabled("No Moodle selected.");
        }

        ImGui.SetNextItemWidth(300);

        if (ImGui.BeginCombo($"##{controlId}-moodle-combo", stagedPreview))
        {
            if (!rouletteMoodleSearchText.TryGetValue(controlId, out var searchText))
                searchText = string.Empty;

            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();

            ImGui.SetNextItemWidth(-1);

            if (ImGui.InputText($"##{controlId}-moodle-search", ref searchText, 128))
                rouletteMoodleSearchText[controlId] = searchText;

            ImGui.Separator();

            if (ImGui.Selectable($"None##{controlId}-none", config.MoodleId == Guid.Empty))
            {
                stagedRouletteMoodleSelections[controlId] = Guid.Empty;
                rouletteMoodleSearchText[controlId] = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.Separator();

            var filteredMoodles = moodleBlockOptions
                .Where(x => string.IsNullOrWhiteSpace(searchText) || x.Value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var moodle in filteredMoodles)
            {
                var isSelected = moodle.Key == config.MoodleId;
                var isStaged = moodle.Key == stagedId;

                if (ImGui.Selectable($"{moodle.Value}##{controlId}-{moodle.Key}", isSelected || isStaged))
                {
                    stagedRouletteMoodleSelections[controlId] = moodle.Key;
                    rouletteMoodleSearchText[controlId] = string.Empty;
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

        var stagedValid = stagedId == Guid.Empty || moodleBlockOptions.ContainsKey(stagedId);
        var disableSetButton = !stagedRouletteMoodleSelections.ContainsKey(controlId) || !stagedValid;

        if (disableSetButton)
            ImGui.BeginDisabled();

        if (ImGui.Button($"Set##{controlId}-moodle-set"))
        {
            if (stagedId == Guid.Empty)
            {
                config.MoodleId = Guid.Empty;
                config.MoodleName = string.Empty;
            }
            else if (moodleBlockOptions.TryGetValue(stagedId, out var selectedName))
            {
                config.MoodleId = stagedId;
                config.MoodleName = selectedName;
            }

            configuration.Save();

            stagedRouletteMoodleSelections.Remove(controlId);
            rouletteMoodleSearchText[controlId] = string.Empty;
        }

        if (disableSetButton)
            ImGui.EndDisabled();
    }

    private void DrawRouletteEffectHonorificEditor(Configuration.JobRouletteEffectConfig config)
    {
        ImGui.TextUnformatted("Honorific");

        var title = config.HonorificTitle;
        var color = config.HonorificColor;
        var glow = config.HonorificGlow;
        var sourceJson = config.HonorificSourceJson;
        var priority = config.HonorificPriority;

        if (plugin.HonorificManager.DrawPermanentTitleConfigEditors(ref title, ref color, ref glow, ref sourceJson, ref priority, titleWidth: 160f, priorityWidth: 50f))
        {
            config.HonorificTitle = title;
            config.HonorificColor = color;
            config.HonorificGlow = glow;
            config.HonorificSourceJson = sourceJson;
            config.HonorificPriority = priority;

            configuration.Save();
        }

        ImGui.SameLine();

        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        if (!ctrlHeld)
            ImGui.BeginDisabled();

        if (ImGui.SmallButton("Clear##honorific"))
        {
            config.HonorificTitle = string.Empty;
            config.HonorificColor = new Vector3(1f, 1f, 1f);
            config.HonorificGlow = new Vector3(0f, 0f, 0f);
            config.HonorificSourceJson = string.Empty;
            config.HonorificPriority = 0;

            configuration.Save();
        }

        if (!ctrlHeld)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(ctrlHeld ? "Clear this Honorific title" : "Hold CTRL to clear");
    }

    private string GetCurrentRouletteMoodleDisplayName(Guid id, string savedName)
    {
        if (id == Guid.Empty)
            return "None";

        if (moodleBlockOptions.TryGetValue(id, out var currentName))
            return currentName;

        if (!string.IsNullOrWhiteSpace(savedName))
            return $"{savedName} (missing)";

        return $"{id} (missing)";
    }


    private static bool RouletteGearsetConfigMatches(
        Configuration.JobRouletteGearsetConfig config,
        JobManager.GearsetInfo gearset)
    {
        return config.GearsetId == gearset.GearsetId
               && config.ClassJobId == gearset.ClassJobId
               && string.Equals(config.GearsetName ?? string.Empty, gearset.Name ?? string.Empty, StringComparison.Ordinal);
    }

    private static string GetRouletteGearsetDisplayName(JobManager.GearsetInfo gearset)
    {
        var name = string.IsNullOrWhiteSpace(gearset.Name)
            ? $"Gearset {gearset.GearsetId}"
            : gearset.Name;

        return $"{gearset.JobName} - {name} #{gearset.GearsetId}";
    }

    private static string GetRouletteGearsetDisplayName(Configuration.JobRouletteGearsetConfig gearset)
    {
        var name = string.IsNullOrWhiteSpace(gearset.GearsetName)
            ? $"Gearset {gearset.GearsetId}"
            : gearset.GearsetName;

        var job = string.IsNullOrWhiteSpace(gearset.JobName)
            ? $"Job {gearset.ClassJobId}"
            : gearset.JobName;

        return $"{job} - {name} #{gearset.GearsetId}";
    }

    private static bool DrawRouletteSelectedRow(string text, float width, bool removeEnabled)
    {
        var style = ImGui.GetStyle();
        const string removeLabel = "X";

        var rowHeight = ImGui.GetFrameHeight();
        var removeWidth = ImGui.CalcTextSize(removeLabel).X + style.FramePadding.X * 2f;
        var selectableWidth = Math.Max(1f, width - removeWidth - style.ItemSpacing.X);

        ImGui.Selectable(
            $"{text}##selected-row",
            true,
            ImGuiSelectableFlags.None,
            new Vector2(selectableWidth, rowHeight));

        ImGui.SameLine();

        if (!removeEnabled)
            ImGui.BeginDisabled();

        var clicked = ImGui.SmallButton(removeLabel);

        if (!removeEnabled)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(removeEnabled ? "Remove gearset" : "Hold CTRL to remove");

        return clicked;
    }
}
