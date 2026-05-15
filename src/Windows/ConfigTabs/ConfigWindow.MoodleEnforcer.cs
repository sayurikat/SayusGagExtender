using Dalamud.Bindings.ImGui;
using SayusGagExtender.API.GagSpeak;
using System;
using System.Collections.Generic;
using System.Linq;
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



                var moodleIsActive = plugin.MoodlesApi.IsStatusActive(moodleId);
                //var moodleIsActive = moodleConfig.IsMoodleEnabled;

                if (moodleIsActive)
                    ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.2f, 1.0f, 0.2f, 1.0f));

                var headerOpen = ImGui.CollapsingHeader($"{moodleName}##{moodleId}");

                if (moodleIsActive)
                    ImGui.PopStyleColor();

                if (headerOpen)
                {
                    ImGui.Indent();

                    //var isMoodleEnabled = moodleConfig.IsMoodleEnabled;
                    //if (ImGui.Checkbox($"Enable Moodle Enforcer for {moodleName}##enabled-{moodleId}", ref isMoodleEnabled))
                    //{
                    //    moodleConfig.IsMoodleEnabled = isMoodleEnabled;
                    //    configuration.Save();
                    //}

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

        private void DrawGagSpeakItemList(
    string label,
    List<GagSpeakItem> configuredItems,
    Dictionary<Guid, string> availableItems,
    string selectorKey)
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
            }

            if (configuredItems.Count == 0)
            {
                ImGui.TextDisabled($"No {label.ToLowerInvariant()} configured.");
                return;
            }

            ImGui.Indent();

            var ctrlHeld = ImGui.GetIO().KeyCtrl;

            for (var i = configuredItems.Count - 1; i >= 0; i--)
            {
                var item = configuredItems[i];

                ImGui.PushID($"{selectorKey}-{i}-{item.Id}");

                ImGui.BulletText(string.IsNullOrWhiteSpace(item.Name) ? item.Id.ToString() : item.Name);

                ImGui.SameLine();

                if (!ctrlHeld)
                    ImGui.BeginDisabled();

                if (ImGui.SmallButton("Remove"))
                {
                    if (ctrlHeld)
                    {
                        configuredItems.RemoveAt(i);
                        configuration.Save();
                    }
                }

                if (!ctrlHeld)
                    ImGui.EndDisabled();

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip(ctrlHeld
                        ? "Remove this entry."
                        : "Hold CTRL to remove this entry.");
                }


                ImGui.PopID();
            }

            ImGui.Unindent();
        }

        private void DrawSearchableMoodleEnforcerCombo(
        string selectorKey,
        string selectedName,
        List<KeyValuePair<Guid, string>> orderedAvailable,
        int selectedIndex)
        {
            ImGui.SetNextItemWidth(300);

            if (!moodleEnforcerSearchTexts.ContainsKey(selectorKey))
                moodleEnforcerSearchTexts[selectorKey] = string.Empty;

            if (ImGui.BeginCombo($"##add-{selectorKey}", selectedName))
            {
                var searchText = moodleEnforcerSearchTexts[selectorKey];

                ImGui.SetNextItemWidth(-1);

                var searchInputId = $"##search-{selectorKey}";

                if (ImGui.IsWindowAppearing())
                {
                    ImGui.SetKeyboardFocusHere();
                }

                if (ImGui.InputTextWithHint(searchInputId, "Search...", ref searchText, 128))
                {
                    moodleEnforcerSearchTexts[selectorKey] = searchText;
                }

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

                            // Optional: clear search after picking something.
                            moodleEnforcerSearchTexts[selectorKey] = string.Empty;

                            ImGui.CloseCurrentPopup();
                        }
                        
                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }
        }
    }
}
