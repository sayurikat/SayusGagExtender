using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {
        private readonly Dictionary<Guid, string> moodleBlockOptions = new();
        private readonly Dictionary<string, Guid> stagedMoodleBlockSelections = new();
        private readonly Dictionary<string, string> moodleBlockSearchText = new();
        private bool moodleBlockOptionsLoading;
        private DateTime moodleBlockOptionsLastRefresh = DateTime.MinValue;

        private void DrawMoodleBlocksTab()
        {
            RefreshMoodleBlockOptionsIfNeeded();

            ImGui.TextWrapped("Pick one or more Moodles for each blocker. A blocker is active when any selected Moodle is currently active.");
            ImGui.Spacing();

            DrawMoodleBlockSetting(
                "Enable Teleport Block Feature",
                "Teleport Block Moodles",
                configuration.TeleportBlockFeature,
                configuration.TeleportBlockMoodles,
                enabled =>
                {
                    configuration.TeleportBlockFeature = enabled;
                    configuration.Save();
                },
                moodles =>
                {
                    configuration.TeleportBlockMoodles = moodles;
                    configuration.Save();
                });

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawMoodleBlockSetting(
                "Enable Mount Block Feature",
                "Mount Block Moodles",
                configuration.MountBlockFeature,
                configuration.MountBlockMoodles,
                enabled =>
                {
                    configuration.MountBlockFeature = enabled;
                    configuration.Save();
                },
                moodles =>
                {
                    configuration.MountBlockMoodles = moodles;
                    configuration.Save();
                });

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawMoodleBlockSetting(
                "Enable Job Switch Block Feature",
                "Job Switch Block Moodles",
                configuration.JobSwitchBlockFeature,
                configuration.JobSwitchBlockMoodles,
                enabled =>
                {
                    configuration.JobSwitchBlockFeature = enabled;
                    configuration.Save();
                },
                moodles =>
                {
                    configuration.JobSwitchBlockMoodles = moodles;
                    configuration.Save();
                });

            ImGui.Spacing();

            if (ImGui.Button("Refresh Moodle list"))
                _ = RefreshMoodleBlockOptionsAsync(force: true);

            if (moodleBlockOptionsLoading)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("Loading Moodles...");
            }
        }

        private void DrawMoodleBlockSetting(
            string checkboxLabel,
            string comboLabel,
            bool currentEnabled,
            Dictionary<Guid, string>? currentMoodles,
            Action<bool> onEnabledChanged,
            Action<Dictionary<Guid, string>> onMoodlesChanged)
        {
            var enabled = currentEnabled;
            if (ImGui.Checkbox(checkboxLabel, ref enabled))
                onEnabledChanged(enabled);

            // Work on a copy so config only changes on explicit Add/remove actions.
            var selected = NormalizeSelectedMoodles(currentMoodles);
            var changed = false;
            var controlId = comboLabel.Replace(" ", string.Empty);

            stagedMoodleBlockSelections.TryGetValue(controlId, out var stagedId);
            var stagedName = stagedId != Guid.Empty && moodleBlockOptions.TryGetValue(stagedId, out var currentStagedName)
                ? currentStagedName
                : null;

            const float comboWidth = 300f;
            const float selectedRowWidth = 300;

            // Keep the label away from the combo/add row. The combo itself uses a hidden ## label.
            ImGui.TextUnformatted(comboLabel);

            var preview = string.IsNullOrWhiteSpace(stagedName)
                ? "Search and choose a Moodle..."
                : stagedName;

            ImGui.SetNextItemWidth(comboWidth);
            if (ImGui.BeginCombo($"##{controlId}-combo", preview))
            {
                if (!moodleBlockSearchText.TryGetValue(controlId, out var searchText))
                    searchText = string.Empty;

                if (ImGui.IsWindowAppearing())
                    ImGui.SetKeyboardFocusHere();

                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText($"##{controlId}-search", ref searchText, 128))
                    moodleBlockSearchText[controlId] = searchText;

                ImGui.Separator();

                var filteredMoodles = moodleBlockOptions
                    .Where(x => !selected.ContainsKey(x.Key))
                    .Where(x => string.IsNullOrWhiteSpace(searchText) || x.Value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var moodle in filteredMoodles)
                {
                    var isStaged = stagedId == moodle.Key;
                    var label = $"{moodle.Value}##{controlId}-{moodle.Key}";

                    if (ImGui.Selectable(label, isStaged))
                        stagedMoodleBlockSelections[controlId] = moodle.Key;

                    if (isStaged)
                        ImGui.SetItemDefaultFocus();
                }

                if (moodleBlockOptions.Count == 0)
                    ImGui.TextDisabled(moodleBlockOptionsLoading ? "Loading Moodles..." : "No Moodles found. Click Refresh Moodle list.");
                else if (filteredMoodles.Length == 0)
                    ImGui.TextDisabled("No matching unselected Moodles.");

                ImGui.EndCombo();
            }

            ImGui.SameLine();
            var nameToAdd = string.Empty;
            var canAdd = stagedId != Guid.Empty
                && moodleBlockOptions.TryGetValue(stagedId, out nameToAdd)
                && !selected.ContainsKey(stagedId);

            if (!canAdd)
                ImGui.BeginDisabled();

            if (ImGui.Button($"Add##{controlId}-add"))
            {
                selected[stagedId] = nameToAdd;
                stagedMoodleBlockSelections.Remove(controlId);
                moodleBlockSearchText[controlId] = string.Empty;
                changed = true;
            }

            if (!canAdd)
                ImGui.EndDisabled();

            ImGui.Spacing();

            if (selected.Count > 0)
            {
                ImGui.TextDisabled("Selected Moodles");

                var ctrlHeld = ImGui.GetIO().KeyCtrl;

                foreach (var moodle in selected.OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase).ToArray())
                {
                    ImGui.PushID($"{controlId}-{moodle.Key}");

                    if (DrawSelectedMoodleRow(moodle.Value, selectedRowWidth, ctrlHeld))
                    {
                        selected.Remove(moodle.Key);
                        changed = true;
                    }

                    ImGui.PopID();
                }
            }
            else
            {
                ImGui.TextDisabled("No Moodles selected.");
            }

            if (changed)
                onMoodlesChanged(selected);
        }
        private static bool DrawSelectedMoodleRow(string text, float width, bool removeEnabled)
        {
            var style = ImGui.GetStyle();

            const string removeLabel = "X";

            var rowHeight = ImGui.GetFrameHeight();

            var removeWidth =
                ImGui.CalcTextSize(removeLabel).X +
                style.FramePadding.X * 2f;

            var selectableWidth =
                width -
                removeWidth -
                style.ItemSpacing.X;

            selectableWidth = Math.Max(selectableWidth, 1f);

            ImGui.Selectable(
                $"{text}##selected-row",
                true,
                ImGuiSelectableFlags.None,
                new System.Numerics.Vector2(selectableWidth, rowHeight)
            );

            ImGui.SameLine();

            if (!removeEnabled)
                ImGui.BeginDisabled();

            var clicked = ImGui.SmallButton(removeLabel);

            if (!removeEnabled)
                ImGui.EndDisabled();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(removeEnabled ? "Remove Moodle" : "Hold CTRL to enable remove");

            return clicked;
        }
        private void RefreshMoodleBlockOptionsIfNeeded()
        {
            if (moodleBlockOptionsLoading)
                return;

            if (moodleBlockOptions.Count > 0 && DateTime.UtcNow - moodleBlockOptionsLastRefresh < TimeSpan.FromMinutes(2))
                return;

            _ = RefreshMoodleBlockOptionsAsync(force: false);
        }

        private async Task RefreshMoodleBlockOptionsAsync(bool force)
        {
            if (moodleBlockOptionsLoading)
                return;

            if (!force && moodleBlockOptions.Count > 0 && DateTime.UtcNow - moodleBlockOptionsLastRefresh < TimeSpan.FromMinutes(2))
                return;

            moodleBlockOptionsLoading = true;

            try
            {
                // Assumes the main ConfigWindow partial has access to the Plugin instance as `plugin`.
                // If your field/property is named differently, adjust this one line.
                var moodles = await plugin.MoodlesApi.GetAllMoodlesAsync();

                moodleBlockOptions.Clear();
                foreach (var moodle in moodles)
                {
                    if (moodle.Key == Guid.Empty)
                        continue;

                    moodleBlockOptions[moodle.Key] = string.IsNullOrWhiteSpace(moodle.Value)
                        ? moodle.Key.ToString()
                        : moodle.Value;
                }

                UpdateSavedMoodleNames(configuration.TeleportBlockMoodles);
                UpdateSavedMoodleNames(configuration.MountBlockMoodles);
                UpdateSavedMoodleNames(configuration.JobSwitchBlockMoodles);

                moodleBlockOptionsLastRefresh = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to load Moodles for blocker config: {ex}");
            }
            finally
            {
                moodleBlockOptionsLoading = false;
            }
        }

        private Dictionary<Guid, string> NormalizeSelectedMoodles(Dictionary<Guid, string>? selected)
        {
            var normalized = new Dictionary<Guid, string>();

            if (selected == null)
                return normalized;

            foreach (var moodle in selected)
            {
                if (moodle.Key == Guid.Empty)
                    continue;

                normalized[moodle.Key] = moodleBlockOptions.TryGetValue(moodle.Key, out var currentName)
                    ? currentName
                    : string.IsNullOrWhiteSpace(moodle.Value) ? moodle.Key.ToString() : moodle.Value;
            }

            return normalized;
        }

        private void UpdateSavedMoodleNames(Dictionary<Guid, string>? selected)
        {
            if (selected == null)
                return;

            var changed = false;
            foreach (var id in selected.Keys.ToArray())
            {
                if (!moodleBlockOptions.TryGetValue(id, out var currentName))
                    continue;

                if (selected[id] == currentName)
                    continue;

                selected[id] = currentName;
                changed = true;
            }

            if (changed)
                configuration.Save();
        }

        private string BuildMoodlePreview(IReadOnlyDictionary<Guid, string> selected)
        {
            if (selected.Count == 0)
                return "Select Moodles...";

            var names = selected
                .Values
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();

            var preview = string.Join(", ", names);
            return selected.Count > 2
                ? $"{preview} + {selected.Count - 2} more"
                : preview;
        }
    }
}
