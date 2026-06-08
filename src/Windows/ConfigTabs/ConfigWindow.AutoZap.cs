using Dalamud.Bindings.ImGui;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using static SayusGagExtender.RandomZapSender;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Numerics;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {

        private List<KeyValuePair<Guid, string>> availableAutoZapRestraints = new();
        private string autoZapRestraintSearch = "";
        private Guid? selectedAutoZapRestraintToAdd;
        private string newZapCommand = "";
        private string newZapCommandWeight = "10";
        private Guid? selectedAutoZapEngagedMoodleToSet;
        private string autoZapEngagedMoodleSearch = "";
        private Guid? selectedAutoZapControllerOnlineMoodleToSet;
        private string autoZapControllerOnlineMoodleSearch = "";
        private string newZapHonorificTriggerCommand = "";

        private void DrawAutoZapTab()
        {
            if (configuration.AutoZapCountControllerLocked)
                ImGui.BeginDisabled();

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
                    plugin.RandomZapSender.UpdateHourlyCount();
                }
            }
            if (configuration.AutoZapCountControllerLocked)
                ImGui.EndDisabled();

            if (configuration.AutoZapWhen == RandomZapSender.OperateWhen.Offline)
            {
                ImGui.TextWrapped("Ignored when Controller is online.");
            }
            else if (configuration.AutoZapWhen == RandomZapSender.OperateWhen.Distant)
            {
                ImGui.TextWrapped("Ignored when Controller within range.");
            }
            else if (configuration.AutoZapWhen == RandomZapSender.OperateWhen.Always)
            {
                //ImGui.TextWrapped("");
            }

            DrawAutoZapMoodleSetting(
                "Moodle when Auto Zap is engaged",
                "##AutoZapEngagedMoodle",
                configuration.AutoZapEngagedMoodleId,
                configuration.AutoZapEngagedMoodleName,
                ref selectedAutoZapEngagedMoodleToSet,
                ref autoZapEngagedMoodleSearch,
                (id, name) =>
                {
                    configuration.AutoZapEngagedMoodleId = id;
                    configuration.AutoZapEngagedMoodleName = name;
                    configuration.Save();
                });

            DrawAutoZapMoodleSetting(
                "Moodle when Controller is online",
                "##AutoZapControllerOnlineMoodle",
                configuration.AutoZapControllerOnlineMoodleId,
                configuration.AutoZapControllerOnlineMoodleName,
                ref selectedAutoZapControllerOnlineMoodleToSet,
                ref autoZapControllerOnlineMoodleSearch,
                (id, name) =>
                {
                    configuration.AutoZapControllerOnlineMoodleId = id;
                    configuration.AutoZapControllerOnlineMoodleName = name;
                    configuration.Save();
                });

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
        private void DrawAutoZapMoodleSetting(string label, string id, Guid currentMoodleId, string currentMoodleName, ref Guid? selectedMoodleToSet, ref string searchText, Action<Guid, string> onSet)
        {
            var availableMoodles = plugin.MoodlesApi.GetAllMoodles()
                .Where(x => x.Key != Guid.Empty)
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var selectedId = selectedMoodleToSet;

            if (selectedId != null &&
                !availableMoodles.Any(x => x.Key == selectedId.Value))
            {
                selectedMoodleToSet = null;
                selectedId = null;
            }

            ImGui.Spacing();

            ImGui.TextUnformatted(label);

            const float selectedRowWidth = 300f;

            if (currentMoodleId != Guid.Empty)
            {
                var currentMoodle = availableMoodles.FirstOrDefault(x => x.Key == currentMoodleId);

                var displayCurrentName = !string.IsNullOrWhiteSpace(currentMoodle.Value)
                    ? currentMoodle.Value
                    : currentMoodleName;

                if (string.IsNullOrWhiteSpace(displayCurrentName))
                    displayCurrentName = currentMoodleId.ToString();

                //ImGui.TextDisabled("Selected Moodle");

                var ctrlHeld = ImGui.GetIO().KeyCtrl;

                ImGui.Indent();

                ImGui.PushID($"{id}-selected-{currentMoodleId}");

                if (DrawGagSpeakItem(displayCurrentName, selectedRowWidth, ctrlHeld))
                {
                    plugin.RandomZapSender.MoodleConfigChange(); // AutoZap version
                    onSet(Guid.Empty, string.Empty);

                    selectedMoodleToSet = null;
                    searchText = "";
                }

                ImGui.PopID();

                ImGui.Unindent();
            }
            else
            {
                ImGui.TextDisabled("No Moodle selected.");
            }


            var selectedName = "Select Moodle...";

            if (selectedId != null)
            {
                var selectedMoodle = availableMoodles.FirstOrDefault(x => x.Key == selectedId.Value);
                if (!string.IsNullOrWhiteSpace(selectedMoodle.Value))
                    selectedName = selectedMoodle.Value;
            }

            ImGui.SetNextItemWidth(300);

            if (ImGui.BeginCombo($"{id}Combo", selectedName))
            {
                ImGui.SetNextItemWidth(-1);

                if (ImGui.IsWindowAppearing())
                    ImGui.SetKeyboardFocusHere();

                ImGui.InputTextWithHint(
                    $"{id}Search",
                    "Search...",
                    ref searchText,
                    128);

                ImGui.Separator();

                var localSearchText = searchText;

                var filteredMoodles = string.IsNullOrWhiteSpace(localSearchText)
                    ? availableMoodles
                    : availableMoodles
                        .Where(x => x.Value.Contains(localSearchText, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (filteredMoodles.Count == 0)
                {
                    ImGui.TextDisabled("No matches.");
                }
                else
                {
                    foreach (var moodle in filteredMoodles)
                    {
                        var guid = moodle.Key;
                        var name = moodle.Value;
                        var isSelected = selectedId == guid;

                        if (ImGui.Selectable($"{name}{id}{guid}", isSelected))
                        {
                            selectedMoodleToSet = guid;
                            selectedId = guid;
                            searchText = "";
                            ImGui.CloseCurrentPopup();
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();

            selectedId = selectedMoodleToSet;

            var nameToSet = string.Empty;
            var canSet = false;

            if (selectedId != null)
            {
                var selectedMoodle = availableMoodles.FirstOrDefault(x => x.Key == selectedId.Value);

                if (selectedMoodle.Key != Guid.Empty)
                {
                    nameToSet = string.IsNullOrWhiteSpace(selectedMoodle.Value)
                        ? selectedMoodle.Key.ToString()
                        : selectedMoodle.Value;

                    canSet = true;
                }
            }

            if (!canSet)
                ImGui.BeginDisabled();

            if (ImGui.Button($"Set{id}"))
            {
                plugin.RandomZapSender.MoodleConfigChange();
                onSet(selectedId!.Value, nameToSet);

                selectedMoodleToSet = null;
                searchText = "";
            }

            if (!canSet)
                ImGui.EndDisabled();


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
            var selectedRowWidth = 300f;

            ImGui.Indent();

            foreach (var (guid, name) in requiredRestraints)
            {

                ImGui.PushID($"HandGuardBlocked-{guid}");

                if (DrawGagSpeakItem(string.IsNullOrWhiteSpace(name) ? guid.ToString() : name, selectedRowWidth, ctrlHeld))
                {
                    configuration.AutoZapRequiredRestrictions.Remove(guid);
                    configuration.Save();
                }

                ImGui.PopID();

            }

            ImGui.Unindent();

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

                    var honorificTitle = zapCommand.HonorificTitle;
                    var honorificColor = zapCommand.HonorificColor;
                    var honorificGlow = zapCommand.HonorificGlow;
                    var honorificDuration = zapCommand.HonorificDurationSeconds;
                    var honorificPriority = zapCommand.HonorificPriority;

                    if (plugin.HonorificManager.DrawTitleConfigEditors(
                            ref honorificTitle,
                            ref honorificColor,
                            ref honorificGlow,
                            ref honorificDuration,
                            ref honorificPriority))
                    {
                        zapCommand.HonorificTitle = honorificTitle.Trim();
                        zapCommand.HonorificColor = honorificColor;
                        zapCommand.HonorificGlow = honorificGlow;
                        zapCommand.HonorificDurationSeconds = honorificDuration;
                        zapCommand.HonorificPriority = honorificPriority;

                        configuration.Save();
                    }

                    ImGui.SameLine();

                    var honorificTriggerCommand = zapCommand.HonorificTriggerCommand;
                    ImGui.SetNextItemWidth(160);
                    if (ImGui.InputTextWithHint(
                            "##HonorificTriggerCommand",
                            "Honorific trigger",
                            ref honorificTriggerCommand,
                            128))
                    {
                        zapCommand.HonorificTriggerCommand = honorificTriggerCommand;
                        configuration.Save();
                        plugin.RandomZapSender.RefreshHonorificEmoteSubscriptions();
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
                        plugin.RandomZapSender.RefreshHonorificEmoteSubscriptions();
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
