using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SayusGagExtender.RandomVibeSender;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {
        private List<KeyValuePair<Guid, string>> availableAutoVibeRestraints = new();
        private string autoVibeRestraintSearch = "";
        private Guid? selectedAutoVibeRestraintToAdd;

        private string newVibeCommand = "";
        private string newVibeCommandWeight = "10";

        private void DrawAutoVibeTab()
        {
            var autoVibeEnabled = configuration.AutoVibeEnabled;
            if (ImGui.Checkbox("Enable Auto Vibe", ref autoVibeEnabled))
            {
                configuration.AutoVibeEnabled = autoVibeEnabled;
                configuration.Save();
            }

            ImGui.SetNextItemWidth(80);
            var vibeCount = configuration.AutoVibeCount.ToString();
            if (ImGui.InputText(
                    "Auto Vibe Count per hour",
                    ref vibeCount,
                    flags: ImGuiInputTextFlags.CharsDecimal))
            {
                if (int.TryParse(vibeCount.Trim(), out var newVibeCount))
                {
                    configuration.AutoVibeCount = newVibeCount;
                    configuration.Save();
                }
            }

            ImGui.TextWrapped("Ignored when Vibe Controller is online.");

            var vibeControllerName = configuration.VibeControllerName;
            if (ImGui.InputText("Vibe Controller Name", ref vibeControllerName))
            {
                configuration.VibeControllerName = vibeControllerName;
                configuration.Save();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawAutoVibeRequiredRestraints();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawAutoVibeAddRequiredRestraint();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawVibeCommands();
        }
        private void DrawAutoVibeRequiredRestraints()
        {
            ImGui.Text("Required restraints for Auto Vibe");

            var requiredRestraints = configuration.AutoVibeRequiredRestrictions
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (requiredRestraints.Count == 0)
            {
                ImGui.TextDisabled("No required restraints added.");
                return;
            }

            var ctrlHeld = ImGui.GetIO().KeyCtrl;
            var selectedRowWidth = 300f;

            ImGui.Indent();

            foreach (var (guid, name) in requiredRestraints)
            {

                ImGui.PushID($"HandGuardBlocked-{guid}");

                if (DrawGagSpeakItem(string.IsNullOrWhiteSpace(name) ? guid.ToString() : name, selectedRowWidth, ctrlHeld))
                {
                    configuration.AutoVibeRequiredRestrictions.Remove(guid);
                    configuration.Save();
                }

                ImGui.PopID();

            }

            ImGui.Unindent();

        }

        private void RefreshAutoVibeAvailableRestraints()
        {
            var requiredRestraints = configuration.AutoVibeRequiredRestrictions;

            var availableRestraints = plugin.GagSpeakRestrictionsApi.GetAvailableRestrictions();

            availableAutoVibeRestraints = availableRestraints?
                .Where(x => !requiredRestraints.ContainsKey(x.Key))
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<KeyValuePair<Guid, string>>();

            selectedAutoVibeRestraintToAdd = null;
        }
        private void DrawAutoVibeAddRequiredRestraint()
        {
            ImGui.Text("Add required restraint");

            var availableItems = availableAutoVibeRestraints;

            if (selectedAutoVibeRestraintToAdd != null &&
                !availableItems.Any(x => x.Key == selectedAutoVibeRestraintToAdd.Value))
            {
                selectedAutoVibeRestraintToAdd = null;
            }

            var selectedName = selectedAutoVibeRestraintToAdd != null
                ? availableItems.FirstOrDefault(x => x.Key == selectedAutoVibeRestraintToAdd.Value).Value
                : "Select restraint...";

            ImGui.SetNextItemWidth(300);

            if (ImGui.BeginCombo("##AutoVibeAvailableRestraints", selectedName))
            {
                ImGui.SetNextItemWidth(-1);

                if (ImGui.IsWindowAppearing())
                    ImGui.SetKeyboardFocusHere();

                ImGui.InputTextWithHint(
                    "##AutoVibeRestraintSearch",
                    "Search...",
                    ref autoVibeRestraintSearch,
                    128);

                ImGui.Separator();

                var filteredItems = string.IsNullOrWhiteSpace(autoVibeRestraintSearch)
                    ? availableItems
                    : availableItems
                        .Where(x => x.Value.Contains(
                            autoVibeRestraintSearch,
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
                        var isSelected = selectedAutoVibeRestraintToAdd == guid;

                        if (ImGui.Selectable($"{name}##AvailableAutoVibeRestraint{guid}", isSelected))
                        {
                            selectedAutoVibeRestraintToAdd = guid;
                            autoVibeRestraintSearch = "";
                            ImGui.CloseCurrentPopup();
                        }
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();

            var canAdd = selectedAutoVibeRestraintToAdd != null &&
                         availableItems.Any(x => x.Key == selectedAutoVibeRestraintToAdd.Value);

            if (!canAdd)
                ImGui.BeginDisabled();

            if (ImGui.Button("Add restraint##AutoVibe"))
            {
                var selected = availableItems
                    .Cast<KeyValuePair<Guid, string>?>()
                    .FirstOrDefault(x => x?.Key == selectedAutoVibeRestraintToAdd.Value);

                if (selected != null)
                {
                    configuration.AutoVibeRequiredRestrictions[selected.Value.Key] = selected.Value.Value;
                    configuration.Save();

                    availableAutoVibeRestraints.RemoveAll(x => x.Key == selected.Value.Key);

                    selectedAutoVibeRestraintToAdd = null;
                    autoVibeRestraintSearch = "";
                }
            }

            if (!canAdd)
                ImGui.EndDisabled();

            if (ImGui.Button("Reload restraints from GagSpeak##AutoVibe"))
            {
                RefreshAutoVibeAvailableRestraints();
                autoVibeRestraintSearch = "";
            }
        }

        private void DrawVibeCommands()
        {
            ImGui.Text("Vibe Commands");
            ImGui.TextWrapped("Commands used by Auto Vibe. Weight controls how likely each command is to be picked.");

            ImGui.Spacing();

            if (configuration.AutoVibeCommands.Count == 0)
            {
                ImGui.TextDisabled("No vibe commands added.");
            }
            else
            {
                var ctrlHeld = ImGui.GetIO().KeyCtrl;

                for (var i = 0; i < configuration.AutoVibeCommands.Count; i++)
                {
                    var vibeCommand = configuration.AutoVibeCommands[i];

                    ImGui.PushID($"VibeCommand{i}");

                    var command = vibeCommand.Command;
                    var weight = vibeCommand.Weight.ToString();

                    ImGui.SetNextItemWidth(330);
                    if (ImGui.InputText("##Command", ref command, 512))
                    {
                        vibeCommand.Command = command;
                        configuration.Save();
                    }

                    ImGui.SameLine();

                    ImGui.SetNextItemWidth(60);
                    if (ImGui.InputText(
                            "##Weight",
                            ref weight,
                            8,
                            ImGuiInputTextFlags.CharsDecimal))
                    {
                        if (int.TryParse(weight.Trim(), out var parsedWeight))
                        {
                            vibeCommand.Weight = Math.Max(0, parsedWeight);
                            configuration.Save();
                        }
                    }

                    ImGui.SameLine();

                    if (!ctrlHeld)
                        ImGui.BeginDisabled();

                    var deleteClicked = ImGui.Button("X");

                    if (!ctrlHeld)
                        ImGui.EndDisabled();

                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.SetTooltip(ctrlHeld
                            ? "Remove this vibe command"
                            : "Hold Ctrl to remove this vibe command");
                    }

                    ImGui.PopID();

                    if (deleteClicked)
                    {
                        configuration.AutoVibeCommands.RemoveAt(i);
                        configuration.Save();
                        break;
                    }
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Add Vibe Command");

            ImGui.SetNextItemWidth(330);
            ImGui.InputTextWithHint(
                "##NewVibeCommand",
                "/command",
                ref newVibeCommand,
                512);

            ImGui.SameLine();

            ImGui.SetNextItemWidth(60);
            ImGui.InputTextWithHint(
                "##NewVibeCommandWeight",
                "Weight",
                ref newVibeCommandWeight,
                8,
                ImGuiInputTextFlags.CharsDecimal);

            ImGui.SameLine();

            int parsedNewWeight = 10;
            var canAdd =
                !string.IsNullOrWhiteSpace(newVibeCommand) &&
                int.TryParse(newVibeCommandWeight.Trim(), out parsedNewWeight);

            if (!canAdd)
                ImGui.BeginDisabled();

            if (ImGui.Button("Add##VibeCommand"))
            {
                configuration.AutoVibeCommands.Add(new WeightedVibeCommand
                {
                    Command = newVibeCommand.Trim(),
                    Weight = Math.Max(0, parsedNewWeight),
                });

                configuration.Save();

                newVibeCommand = "";
                newVibeCommandWeight = "10";
            }

            if (!canAdd)
                ImGui.EndDisabled();
        }
    }
}
