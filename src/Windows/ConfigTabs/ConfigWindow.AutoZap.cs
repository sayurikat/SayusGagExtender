using Dalamud.Bindings.ImGui;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using static SayusGagExtender.RandomZapSender;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {

        private List<KeyValuePair<Guid, string>> availableAutoZapRestraints = new();
        private string autoZapRestraintSearch = "";
        private Guid? selectedAutoZapRestraintToAdd;
        private string newZapCommand = "";
        private string newZapCommandWeight = "10";


        private void DrawAutoZapTab()
        {
            var autoZapEnabled = configuration.AutoZapEnabled;
            if (ImGui.Checkbox("Enable Auto Zap", ref autoZapEnabled))
            {
                configuration.AutoZapEnabled = autoZapEnabled;
                configuration.Save();
            }

            ImGui.SetNextItemWidth(80);
            var zapCount = configuration.AutoZapCount.ToString();
            if (ImGui.InputText(
                    "Auto Zap Count per hour",
                    ref zapCount,
                    flags: ImGuiInputTextFlags.CharsDecimal))
            {
                if (int.TryParse(zapCount.Trim(), out var newZapCount))
                {
                    configuration.AutoZapCount = newZapCount;
                    configuration.Save();
                }
            }

            ImGui.TextWrapped("Ignored when Zap Controller is online.");

            var zapControllerName = configuration.ZapControllerName;
            if (ImGui.InputText("Zap Controller Name", ref zapControllerName))
            {
                configuration.ZapControllerName = zapControllerName;
                configuration.Save();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawAutoZapRequiredRestraints();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawAutoZapAddRequiredRestraint();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawZapCommands();
        }
        private void DrawAutoZapRequiredRestraints()
        {
            ImGui.Text("Required restraints for Auto Zap");

            var requiredRestraints = configuration.AutoZapRequiredRestrictions
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (requiredRestraints.Count == 0)
            {
                ImGui.TextDisabled("No required restraints added.");
                return;
            }

            var ctrlHeld = ImGui.GetIO().KeyCtrl;

            foreach (var (guid, name) in requiredRestraints)
            {
                ImGui.TextUnformatted(name);

                ImGui.SameLine();

                var buttonWidth = ImGui.CalcTextSize("X").X + ImGui.GetStyle().FramePadding.X * 2;
                var availableWidth = ImGui.GetContentRegionAvail().X;

                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - buttonWidth);

                if (!ctrlHeld)
                    ImGui.BeginDisabled();

                if (ImGui.Button($"X##DeleteAutoZapRequiredRestraint{guid}"))
                {
                    configuration.AutoZapRequiredRestrictions.Remove(guid);
                    configuration.Save();
                }

                if (!ctrlHeld)
                    ImGui.EndDisabled();

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip(ctrlHeld
                        ? "Remove this required restraint"
                        : "Hold Ctrl to remove this required restraint");
                }
            }
        }

        private void RefreshAutoZapAvailableRestraints()
        {
            var requiredRestraints = configuration.AutoZapRequiredRestrictions;

            var availableRestraints = plugin.GagSpeakRestrictionsApi.GetAvailableRestrictions();

            availableAutoZapRestraints = availableRestraints?
                .Where(x => !requiredRestraints.ContainsKey(x.Key))
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<KeyValuePair<Guid, string>>();

            selectedAutoZapRestraintToAdd = null;
        }

        private void DrawAutoZapAddRequiredRestraint()
        {
            ImGui.Text("Add required restraint");

            var availableItems = availableAutoZapRestraints;

            if (selectedAutoZapRestraintToAdd != null &&
                !availableItems.Any(x => x.Key == selectedAutoZapRestraintToAdd.Value))
            {
                selectedAutoZapRestraintToAdd = null;
            }

            var selectedName = selectedAutoZapRestraintToAdd != null
                ? availableItems.FirstOrDefault(x => x.Key == selectedAutoZapRestraintToAdd.Value).Value
                : "Select restraint...";

            ImGui.SetNextItemWidth(300);

            if (ImGui.BeginCombo("##AutoZapAvailableRestraints", selectedName))
            {
                ImGui.SetNextItemWidth(-1);

                if (ImGui.IsWindowAppearing())
                    ImGui.SetKeyboardFocusHere();

                ImGui.InputTextWithHint(
                    "##AutoZapRestraintSearch",
                    "Search...",
                    ref autoZapRestraintSearch,
                    128);

                ImGui.Separator();

                var filteredItems = string.IsNullOrWhiteSpace(autoZapRestraintSearch)
                    ? availableItems
                    : availableItems
                        .Where(x => x.Value.Contains(
                            autoZapRestraintSearch,
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
                        var isSelected = selectedAutoZapRestraintToAdd == guid;

                        if (ImGui.Selectable($"{name}##AvailableAutoZapRestraint{guid}", isSelected))
                        {
                            selectedAutoZapRestraintToAdd = guid;
                            autoZapRestraintSearch = "";
                            ImGui.CloseCurrentPopup();
                        }
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();

            var canAdd = selectedAutoZapRestraintToAdd != null &&
                         availableItems.Any(x => x.Key == selectedAutoZapRestraintToAdd.Value);

            if (!canAdd)
                ImGui.BeginDisabled();

            if (ImGui.Button("Add restraint"))
            {
                var selected = availableItems
                    .Cast<KeyValuePair<Guid, string>?>()
                    .FirstOrDefault(x => x?.Key == selectedAutoZapRestraintToAdd.Value);

                if (selected != null)
                {
                    configuration.AutoZapRequiredRestrictions[selected.Value.Key] = selected.Value.Value;
                    configuration.Save();

                    availableAutoZapRestraints.RemoveAll(x => x.Key == selected.Value.Key);

                    selectedAutoZapRestraintToAdd = null;
                    autoZapRestraintSearch = "";
                }
            }

            if (!canAdd)
                ImGui.EndDisabled();

            if (ImGui.Button("Reload restraints from GagSpeak"))
            {
                RefreshAutoZapAvailableRestraints();
                autoZapRestraintSearch = "";
            }
        }
        private void DrawZapCommands()
        {
            ImGui.Text("Zap Commands");
            ImGui.TextWrapped("Commands used by Auto Zap. Weight controls how likely each command is to be picked.");

            ImGui.Spacing();

            if (configuration.AutoZapCommands.Count == 0)
            {
                ImGui.TextDisabled("No zap commands added.");
            }
            else
            {
                var ctrlHeld = ImGui.GetIO().KeyCtrl;

                for (var i = 0; i < configuration.AutoZapCommands.Count; i++)
                {
                    var zapCommand = configuration.AutoZapCommands[i];

                    ImGui.PushID($"ZapCommand{i}");

                    var command = zapCommand.Command;
                    var weight = zapCommand.Weight.ToString();

                    ImGui.SetNextItemWidth(330);
                    if (ImGui.InputText("##Command", ref command, 512))
                    {
                        zapCommand.Command = command;
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
                            zapCommand.Weight = Math.Max(0, parsedWeight);
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
                            ? "Remove this zap command"
                            : "Hold Ctrl to remove this zap command");
                    }

                    ImGui.PopID();

                    if (deleteClicked)
                    {
                        configuration.AutoZapCommands.RemoveAt(i);
                        configuration.Save();
                        break;
                    }
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Add Zap Command");

            ImGui.SetNextItemWidth(330);
            ImGui.InputTextWithHint(
                "##NewZapCommand",
                "/command",
                ref newZapCommand,
                512);

            ImGui.SameLine();

            ImGui.SetNextItemWidth(60);
            ImGui.InputTextWithHint(
                "##NewZapCommandWeight",
                "Weight",
                ref newZapCommandWeight,
                8,
                ImGuiInputTextFlags.CharsDecimal);

            ImGui.SameLine();
            int parsedNewWeight = 10;
            var canAdd =
                !string.IsNullOrWhiteSpace(newZapCommand) &&
                int.TryParse(newZapCommandWeight.Trim(), out parsedNewWeight);

            if (!canAdd)
                ImGui.BeginDisabled();

            if (ImGui.Button("Add"))
            {
                configuration.AutoZapCommands.Add(new WeightedZapCommand
                {
                    Command = newZapCommand.Trim(),
                    Weight = Math.Max(0, parsedNewWeight),
                });

                configuration.Save();

                newZapCommand = "";
                newZapCommandWeight = "10";
            }

            if (!canAdd)
                ImGui.EndDisabled();
        }
    }
}
