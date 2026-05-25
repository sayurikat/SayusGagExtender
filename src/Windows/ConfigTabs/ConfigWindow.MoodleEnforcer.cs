using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using SayusGagExtender.API.GagSpeak;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;
using static SayusGagExtender.API.GagSpeak.GagSpeakReflectionContext;
using static SayusGagExtender.MoodleEnforcer;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {
        private readonly Dictionary<string, int> moodleEnforcerSelectedAddIndices = new();
        private readonly Dictionary<string, string> moodleEnforcerSearchTexts = new();

        private void DrawMoodleEnforcerTab()
        {
            var enabled = configuration.MoodleEnforcerEnabled;
            if (ImGui.Checkbox("Enable Moodle Enforcer", ref enabled))
            {
                configuration.MoodleEnforcerEnabled = enabled;
                configuration.Save();
            }

            ImGui.TextWrapped("Enforce Moodle to restraints. Moodle will not be usable outside of this context. Duplicate Moodle if needed.");
            ImGui.TextWrapped("If any restraints are added to a Moodle, that Moodle will be enforced on/off.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();


            var availableMoodles = plugin.MoodlesApi.GetAllMoodles();
            var availableRestrictions = plugin.GagSpeakRestrictionsApi.GetAvailableRestrictions();
            var availableRestraintSets = plugin.GagSpeakRestraintSetApi.GetAllRestraintSets();
            var availableGags = plugin.GagSpeakGagsApi.GetAvailableGags();

            if (availableMoodles.Count == 0)
            {
                ImGui.TextWrapped("No Moodles found. Make sure the Moodle source/plugin is loaded and available.");
                return;
            }

            foreach (var moodle in availableMoodles.OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase))
            {
                var moodleId = moodle.Key;
                var moodleName = moodle.Value;

                var moodleConfig = plugin.MoodleEnforcer.GetOrCreateMoodleEnforcerConfig(moodleId, moodleName);

                ImGui.PushID($"moodle-enforcer-{moodleId}");

                bool moodleIsActive = false;
                if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.NormalConditions])
                {
                    moodleIsActive = plugin.MoodlesApi.IsStatusActive(moodleId);
                }
                

                if (moodleIsActive)
                    ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.2f, 1.0f, 0.2f, 1.0f));

                var headerOpen = ImGui.CollapsingHeader($"{moodleName}##{moodleId}");

                if (moodleIsActive)
                    ImGui.PopStyleColor();

                if (headerOpen)
                {
                    ImGui.Indent();

                    ImGui.Spacing();

                    DrawGagSpeakItemList(
                        "Restraint Sets",
                        moodleConfig.RestraintSets,
                        availableRestraintSets,
                        $"restraints-{moodleId}");

                    ImGui.Spacing();

                    DrawGagSpeakItemList(
                        "Restrictions",
                        moodleConfig.Restrictions,
                        availableRestrictions,
                        $"restrictions-{moodleId}");

                    ImGui.Spacing();

                    DrawGagSpeakItemList(
                        "Gags",
                        moodleConfig.Gags,
                        availableGags,
                        $"gags-{moodleId}");

                    ImGui.Unindent();
                }

                ImGui.PopID();
            }
        }

        private void DrawGagSpeakItemList(string label, List<GagSpeakItem> configuredItems, Dictionary<Guid, string> availableItems, string selectorKey, Action? onChanged = null)
        {
            ImGui.Text(label);

            if (availableItems.Count == 0)
            {
                ImGui.TextDisabled($"No available {label.ToLowerInvariant()} found.");
                return;
            }

            var orderedAvailable = availableItems
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!moodleEnforcerSelectedAddIndices.TryGetValue(selectorKey, out var selectedIndex))
                selectedIndex = 0;

            if (selectedIndex < 0 || selectedIndex >= orderedAvailable.Count)
                selectedIndex = 0;

            moodleEnforcerSelectedAddIndices[selectorKey] = selectedIndex;

            var selectedName = orderedAvailable[selectedIndex].Value;

            ImGui.SetNextItemWidth(300);

            if (!moodleEnforcerSearchTexts.ContainsKey(selectorKey))
                moodleEnforcerSearchTexts[selectorKey] = string.Empty;

            if (ImGui.BeginCombo($"##add-{selectorKey}", selectedName))
            {
                var searchText = moodleEnforcerSearchTexts[selectorKey];

                ImGui.SetNextItemWidth(-1);

                if (ImGui.IsWindowAppearing())
                    ImGui.SetKeyboardFocusHere();

                if (ImGui.InputTextWithHint($"##search-{selectorKey}", "Search...", ref searchText, 128))
                    moodleEnforcerSearchTexts[selectorKey] = searchText;

                ImGui.Separator();

                var filteredItems = string.IsNullOrWhiteSpace(searchText)
                    ? orderedAvailable
                    : orderedAvailable
                        .Where(x => x.Value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (filteredItems.Count == 0)
                {
                    ImGui.TextDisabled("No matches.");
                }
                else
                {
                    foreach (var item in filteredItems)
                    {
                        var originalIndex = orderedAvailable.FindIndex(x => x.Key == item.Key);
                        var isSelected = originalIndex == selectedIndex;

                        if (ImGui.Selectable($"{item.Value}##{selectorKey}-{item.Key}", isSelected))
                        {
                            moodleEnforcerSelectedAddIndices[selectorKey] = originalIndex;
                            moodleEnforcerSearchTexts[selectorKey] = string.Empty;

                            ImGui.CloseCurrentPopup();
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();

            if (ImGui.Button($"Add##{selectorKey}"))
            {
                var item = orderedAvailable[moodleEnforcerSelectedAddIndices[selectorKey]];

                configuredItems.Add(new GagSpeakItem
                {
                    Id = item.Key,
                    Name = item.Value,
                });

                configuration.Save();
                onChanged?.Invoke();
            }

            if (configuredItems.Count == 0)
            {
                ImGui.TextDisabled($"No {label.ToLowerInvariant()} configured.");
                return;
            }

            ImGui.Indent();

            


            var ctrlHeld = ImGui.GetIO().KeyCtrl;
            var selectedRowWidth = 300f;

            for (var i = configuredItems.Count - 1; i >= 0; i--)
            {
                var item = configuredItems[i];

                ImGui.PushID($"{selectorKey}-{i}-{item.Id}");

                if (DrawGagSpeakItem(string.IsNullOrWhiteSpace(item.Name) ? item.Id.ToString() : item.Name, selectedRowWidth, ctrlHeld))
                {
                    configuredItems.RemoveAt(i);
                    configuration.Save();
                    onChanged?.Invoke();
                }

                ImGui.PopID();

            }

            ImGui.Unindent();
        }
        
        private static bool DrawGagSpeakItem(string text, float width, bool removeEnabled)
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
                ImGui.SetTooltip(removeEnabled ? "Remove Item" : "Hold CTRL to enable remove");

            return clicked;
        }
    }
}
