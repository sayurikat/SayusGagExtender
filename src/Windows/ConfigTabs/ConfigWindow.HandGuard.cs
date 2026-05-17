using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {
        private void DrawHandGuardTab()
        {
            var handGuardEnabled = configuration.HandGuardEnabled;
            if (ImGui.Checkbox("Enable Hand Guard Feature", ref handGuardEnabled))
            {
                configuration.HandGuardEnabled = handGuardEnabled;
                configuration.Save();
            }

            ImGui.TextWrapped("Blocks auto attack and weapon sheathing.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawHandGuardBlockedItems();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawHandGuardAddItem();
        }
        private void DrawHandGuardBlockedItems()
        {
            ImGui.Text("Restrictions that enables Hand Guard");

            var blockedItems = configuration.HandGuardBlockedItems
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (blockedItems.Count == 0)
            {
                ImGui.TextDisabled("No restrictions added.");
                return;
            }

            var ctrlHeld = ImGui.GetIO().KeyCtrl;
            var selectedRowWidth = 300f;

            ImGui.Indent();

            foreach (var (guid, name) in blockedItems)
            {

                ImGui.PushID($"HandGuardBlocked-{guid}");

                if (DrawGagSpeakItem(string.IsNullOrWhiteSpace(name) ? guid.ToString() : name, selectedRowWidth, ctrlHeld))
                {
                    configuration.HandGuardBlockedItems.Remove(guid);
                    configuration.Save();
                }

                ImGui.PopID();

            }

            ImGui.Unindent();

        }

        private List<KeyValuePair<Guid, String>> availableHandGuardItems = new();
        private string handGuardRestrictionSearch = "";
        private Guid? selectedHandGuardItemToAdd;
        private void RefreshHandGuardAvailableItems()
        {
            var blockedItems = configuration.HandGuardBlockedItems;
            var availableRestrictions = plugin.GagSpeakRestrictionsApi.GetAvailableRestrictions();

            availableHandGuardItems = availableRestrictions?
                .Where(x => !blockedItems.ContainsKey(x.Key))
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<KeyValuePair<Guid, string>>();

            selectedHandGuardItemToAdd = null;
        }
        private void DrawHandGuardAddItem()
        {
            ImGui.Text("Add restriction");

            var availableItems = availableHandGuardItems;

            if (selectedHandGuardItemToAdd != null &&
                !availableItems.Any(x => x.Key == selectedHandGuardItemToAdd.Value))
            {
                selectedHandGuardItemToAdd = null;
            }

            var selectedName = selectedHandGuardItemToAdd != null
                ? availableItems.FirstOrDefault(x => x.Key == selectedHandGuardItemToAdd.Value).Value
                : "Select restriction...";

            ImGui.SetNextItemWidth(300);

            if (ImGui.BeginCombo("##HandGuardAvailableItems", selectedName))
            {
                ImGui.SetNextItemWidth(-1);

                if (ImGui.IsWindowAppearing())
                    ImGui.SetKeyboardFocusHere();

                ImGui.InputTextWithHint(
                    "##HandGuardRestrictionSearch",
                    "Search...",
                    ref handGuardRestrictionSearch,
                    128);

                ImGui.Separator();

                var filteredItems = string.IsNullOrWhiteSpace(handGuardRestrictionSearch)
                    ? availableItems
                    : availableItems
                        .Where(x => x.Value.Contains(
                            handGuardRestrictionSearch,
                            StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (filteredItems.Count == 0)
                {
                    ImGui.TextDisabled("No matches.");
                }
                else
                {
                    foreach (var (guid, name) in filteredItems)
                    {
                        var isSelected = selectedHandGuardItemToAdd == guid;

                        if (ImGui.Selectable($"{name}##AvailableHandGuardItem{guid}", isSelected))
                        {
                            selectedHandGuardItemToAdd = guid;
                            handGuardRestrictionSearch = "";
                            ImGui.CloseCurrentPopup();
                        }
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();

            var canAdd = selectedHandGuardItemToAdd != null &&
                         availableItems.Any(x => x.Key == selectedHandGuardItemToAdd.Value);

            if (!canAdd)
                ImGui.BeginDisabled();

            if (ImGui.Button("Add restriction"))
            {
                var selected = availableItems
                    .Cast<KeyValuePair<Guid, string>?>()
                    .FirstOrDefault(x => x?.Key == selectedHandGuardItemToAdd.Value);

                if (selected != null)
                {
                    configuration.HandGuardBlockedItems[selected.Value.Key] = selected.Value.Value;
                    configuration.Save();

                    availableHandGuardItems.RemoveAll(x => x.Key == selected.Value.Key);

                    selectedHandGuardItemToAdd = null;
                    handGuardRestrictionSearch = "";
                }
            }

            if (!canAdd)
                ImGui.EndDisabled();

            if (ImGui.Button("Reload restrictions from GagSpeak"))
            {
                RefreshHandGuardAvailableItems();
                handGuardRestrictionSearch = "";
            }

        }
    }
}
