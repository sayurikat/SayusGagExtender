using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static SayusGagExtender.RandomVibeSender;
using static SayusGagExtender.RandomZapSender;

namespace SayusGagExtender.Windows;

public class ConfigWindow : Window, IDisposable
{
    public Plugin plugin;
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("Sayu's Gag Extender Config###SayusGagExtenderConfig")
    {
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(520, 360);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("SayusGagExtenderConfigTabs"))
            return;

        if (ImGui.BeginTabItem("General"))
        {
            DrawGuardsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Hand Guard"))
        {
            DrawHandGuardTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Blocks"))
        {
            DrawBlocksTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Shock Collar"))
        {
            DrawAutoZapTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Vibrator"))
        {
            DrawAutoVibeTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Chat2"))
        {
            DrawChat2Tab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("GagSpeak Mirror"))
        {
            DrawGagSpeakTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawGuardsTab()
    {
        var emoteGuardEnabled = configuration.EmoteGuardEnabled;
        if (ImGui.Checkbox("Enable Emote Guard", ref emoteGuardEnabled))
        {
            configuration.EmoteGuardEnabled = emoteGuardEnabled;
            configuration.Save();
        }

        ImGui.TextWrapped("Makes sure emotes can be used when running, in combat or mounted.");
        ImGui.TextWrapped("Will block most keys until emote is executed.");
        ImGui.TextWrapped("Will also dismount for certain emotes.");

        ImGui.Spacing();

    }
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

    private void DrawBlocksTab()
    {
        DrawMoodleBlockSetting(
            "Enable Teleport Block Feature",
            "Teleport Block Moodle ID",
            configuration.TeleportBlockFeature,
            configuration.TeleportBlockMoodle,
            enabled =>
            {
                configuration.TeleportBlockFeature = enabled;
                configuration.Save();
            },
            moodle =>
            {
                configuration.TeleportBlockMoodle = moodle;
                configuration.Save();
            });

        ImGui.Spacing();

        DrawMoodleBlockSetting(
            "Enable Mount Block Feature",
            "Mount Block Moodle ID",
            configuration.MountBlockFeature,
            configuration.MountBlockMoodle,
            enabled =>
            {
                configuration.MountBlockFeature = enabled;
                configuration.Save();
            },
            moodle =>
            {
                configuration.MountBlockMoodle = moodle;
                configuration.Save();
            });

        ImGui.Spacing();

        DrawMoodleBlockSetting(
            "Enable Job Switch Block Feature",
            "Job Switch Block Moodle ID",
            configuration.JobSwitchBlockFeature,
            configuration.JobSwitchBlockMoodle,
            enabled =>
            {
                configuration.JobSwitchBlockFeature = enabled;
                configuration.Save();
            },
            moodle =>
            {
                configuration.JobSwitchBlockMoodle = moodle;
                configuration.Save();
            });
    }

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

    private void DrawChat2Tab()
    {
        if (ImGui.Button("Save Chat2 position for Blindfold"))
        {
            if (plugin.Chat2Api.TryGetPositionAndSize(out var bounds))
            {
                if (bounds != null)
                {
                    configuration.Chat2Bounds = bounds;
                    configuration.Save();
                }
            }
        }

        if (ImGui.Button("Apply Chat2 position for Blindfold"))
        {
            plugin.Chat2Api.SetPositionAndSize(configuration.Chat2Bounds);
        }

        ImGui.Spacing();

        ImGui.TextWrapped("Pick a tab Chat2 stays locked on while vanilla Chatbox is hidden by GagSpeak.");

        ImGui.SetNextItemWidth(160);
        var chat2HiddenTabName = configuration.Chat2HiddenTabName;
        if (ImGui.InputText("Hide Chat2 Tab Name", ref chat2HiddenTabName))
        {
            configuration.Chat2HiddenTabName = chat2HiddenTabName;
            configuration.Save();
        }

        if (ImGui.Button("Set current tab as Hide Chat2 tab"))
        {
            var activeTabName = plugin.Chat2Api.GetActiveTabName();
            if (activeTabName != null)
            {
                configuration.Chat2HiddenTabName = activeTabName;
                configuration.Save();
            }
        }

        /*
        if (ImGui.Button("Enable Chat2 Inputs"))
        {
            plugin.Chat2Api.EnableInputInAllTabs();
        }

        if (ImGui.Button("Disable Chat2 Inputs"))
        {
            plugin.Chat2Api.DisableInputInAllTabs();
        }

        if (ImGui.Button("Set Hidden tab as current"))
        {
            plugin.Chat2Api.SetActiveTab(configuration.Chat2HiddenTabName);
        }
        */
    }

    private void DrawGagSpeakTab()
    {
        var restraintCloner = configuration.GagSpeakRestraintCloner;
        if (ImGui.Checkbox("Clone restraints to alt characters", ref restraintCloner))
        {
            configuration.GagSpeakRestraintCloner = restraintCloner;
            configuration.Save();
        }

        ImGui.Spacing();

        ImGui.SetNextItemWidth(160);
        var gagSpeakMasterName = configuration.GagSpeakMasterName;
        if (ImGui.InputText("GagSpeak Main Char Name", ref gagSpeakMasterName))
        {
            configuration.GagSpeakMasterName = gagSpeakMasterName;
            configuration.Save();
        }

        ImGui.SetNextItemWidth(160);
        var gagSpeakMasterWorld = configuration.GagSpeakMasterWorld;
        if (ImGui.InputText("GagSpeak Main Char World", ref gagSpeakMasterWorld))
        {
            configuration.GagSpeakMasterWorld = gagSpeakMasterWorld;
            configuration.Save();
        }

        var name = Plugin.ObjectTable.LocalPlayer?.Name.ToString() ?? "";
        var homeWorld = Plugin.ObjectTable.LocalPlayer?.HomeWorld.RowId ?? 0u;
        var world = plugin.Utils.WorldRowIDToString(homeWorld);

        if (ImGui.Button($"Use {name}@{world}"))
        {
            configuration.GagSpeakMasterName = name;
            configuration.GagSpeakMasterWorld = world;
            configuration.Save();
        }

        /*
        if (ImGui.Button("Save active restraints"))
        {
            var restraintSet = plugin.GagSpeakRestraintSetApi.GetActiveRestraintSet();
            Plugin.ChatGui.Print($"Active Restraint Set: {restraintSet}");
            configuration.ActiveRestraintSet = restraintSet;

            var restrictions = plugin.GagSpeakRestrictionsApi.GetActiveRestrictions();
            foreach (var restriction in restrictions)
            {
                Plugin.ChatGui.Print($"Active Restriction: {restriction}");
            }
            configuration.ActiveRestrictions = restrictions;

            var gags = plugin.GagSpeakGagsApi.GetActiveGags();
            foreach (var gag in gags)
            {
                Plugin.ChatGui.Print($"Active Gag: {gag}");
            }
            configuration.ActiveGags = gags;

            configuration.Save();
        }
        */
    }

    private static void DrawMoodleBlockSetting(
        string checkboxLabel,
        string inputLabel,
        bool currentEnabled,
        string currentMoodle,
        Action<bool> onEnabledChanged,
        Action<string> onMoodleChanged)
    {
        var enabled = currentEnabled;
        if (ImGui.Checkbox(checkboxLabel, ref enabled))
        {
            onEnabledChanged(enabled);
        }

        ImGui.SetNextItemWidth(300);
        var moodle = currentMoodle;
        if (ImGui.InputText(inputLabel, ref moodle))
        {
            onMoodleChanged(moodle);
        }
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

        foreach (var (guid, name) in blockedItems)
        {
            ImGui.TextUnformatted(name);

            ImGui.SameLine();

            var buttonWidth = ImGui.CalcTextSize("X").X + ImGui.GetStyle().FramePadding.X * 2;
            var availableWidth = ImGui.GetContentRegionAvail().X;

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - buttonWidth);

            if (!ctrlHeld)
                ImGui.BeginDisabled();

            if (ImGui.Button($"X##DeleteHandGuardItem{guid}"))
            {
                configuration.HandGuardBlockedItems.Remove(guid);
                configuration.Save();
            }

            if (!ctrlHeld)
                ImGui.EndDisabled();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(ctrlHeld
                    ? "Remove this restriction"
                    : "Hold Ctrl to remove this restriction");
            }
        }
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




    private List<KeyValuePair<Guid, string>> availableAutoZapRestraints = new();
    private string autoZapRestraintSearch = "";
    private Guid? selectedAutoZapRestraintToAdd;
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
    private string newZapCommand = "";
    private string newZapCommandWeight = "10";
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



    private List<KeyValuePair<Guid, string>> availableAutoVibeRestraints = new();
    private string autoVibeRestraintSearch = "";
    private Guid? selectedAutoVibeRestraintToAdd;
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

        foreach (var (guid, name) in requiredRestraints)
        {
            ImGui.TextUnformatted(name);

            ImGui.SameLine();

            var buttonWidth = ImGui.CalcTextSize("X").X + ImGui.GetStyle().FramePadding.X * 2;
            var availableWidth = ImGui.GetContentRegionAvail().X;

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - buttonWidth);

            if (!ctrlHeld)
                ImGui.BeginDisabled();

            if (ImGui.Button($"X##DeleteAutoVibeRequiredRestraint{guid}"))
            {
                configuration.AutoVibeRequiredRestrictions.Remove(guid);
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

    private string newVibeCommand = "";
    private string newVibeCommandWeight = "10";
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
