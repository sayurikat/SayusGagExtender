using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;
using static ECommons.Automation.Chat;

namespace SayusGagExtender.Windows;

public class ConfigWindow : Window, IDisposable
{
    public Plugin plugin;
    private readonly Configuration configuration;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Plugin plugin) : base("A Wonderful Configuration Window###With a constant ID")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(232, 90);
        SizeCondition = ImGuiCond.Always;

        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (configuration.IsConfigWindowMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
    }

    public override void Draw()
    {
        var emoteGuardEnabled = configuration.EmoteGuardEnabled;
        if (ImGui.Checkbox("Enable Emote Guard (making sure emotes can be used wehn running etc.", ref emoteGuardEnabled))
        {
            configuration.EmoteGuardEnabled = emoteGuardEnabled;
            configuration.Save();
        }

        var handGuardEnabled = configuration.HandGuardEnabled;
        if (ImGui.Checkbox("Enable Hand Guard (block auto attack and sheath weeapon)", ref handGuardEnabled))
        {
            configuration.HandGuardEnabled = handGuardEnabled;
            configuration.Save();
        }



        var teleportBlockFeature = configuration.TeleportBlockFeature;
        if (ImGui.Checkbox("Enable Teleport Block Feature", ref teleportBlockFeature))
        {
            configuration.TeleportBlockFeature = teleportBlockFeature;
            configuration.Save();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(300);
        string teleportBlockMoodle = configuration.TeleportBlockMoodle;
        if (ImGui.InputText(
                "Teleport Block Moodle ID",
                ref teleportBlockMoodle))
        {
            configuration.TeleportBlockMoodle = teleportBlockMoodle;
            configuration.Save();
        }


        var mountBlockFeature = configuration.MountBlockFeature;
        if (ImGui.Checkbox("Enable Mount Block Feature", ref mountBlockFeature))
        {
            configuration.MountBlockFeature = mountBlockFeature;
            configuration.Save();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(300);
        string mountBlockMoodle = configuration.MountBlockMoodle;
        if (ImGui.InputText(
                "Mount Block Moodle ID",
                ref mountBlockMoodle))
        {
            configuration.MountBlockMoodle = mountBlockMoodle;
            configuration.Save();
        }

        var jobSwitchBlockFeature = configuration.JobSwitchBlockFeature;
        if (ImGui.Checkbox("Enable Job Switch Block Feature", ref jobSwitchBlockFeature))
        {
            configuration.JobSwitchBlockFeature = jobSwitchBlockFeature;
            configuration.Save();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(300);
        string jobSwitchBlockMoodle = configuration.JobSwitchBlockMoodle;
        if (ImGui.InputText(
                "Job Switch Block Moodle ID",
                ref jobSwitchBlockMoodle))
        {
            configuration.JobSwitchBlockMoodle = jobSwitchBlockMoodle;
            configuration.Save();
        }

        var autoZapEnabled = configuration.AutoZapEnabled;
        if (ImGui.Checkbox("Enable Auto Zap", ref autoZapEnabled))
        {
            configuration.AutoZapEnabled = autoZapEnabled;
            configuration.Save();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(50);
        string zapCount = configuration.AutoZapCount.ToString();
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
        ImGui.Text("Iggnored when Zap Controller is Online");
        string zapControllerName = configuration.ZapControllerName;
        if (ImGui.InputText("Zap Controller Name", ref zapControllerName))
        {
            configuration.ZapControllerName = zapControllerName;
            configuration.Save();
        }




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
        if (ImGui.Button("Enable Chat2 Inputs"))
        {
            plugin.Chat2Api.EnableInputInAllTabs();
        }
        if (ImGui.Button("Disable Chat2 Inputs"))
        {
            plugin.Chat2Api.DisableInputInAllTabs();
        }

        ImGui.SetNextItemWidth(100);
        string chat2HiddenTabName = configuration.Chat2HiddenTabName;
        if (ImGui.InputText(
                "Chat2 Hidden Tab Name",
                ref chat2HiddenTabName))
        {
            configuration.Chat2HiddenTabName = chat2HiddenTabName;
            configuration.Save();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(300);
        if (ImGui.Button("Set active tab as hidden tab"))
        {
            var activeTabName = plugin.Chat2Api.GetActiveTabName();
            if (activeTabName != null)
            {
                configuration.Chat2HiddenTabName = activeTabName;
                configuration.Save();
            }
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(300);
        if (ImGui.Button("Set Hidden tab as current"))
        {
            plugin.Chat2Api.SetActiveTab(configuration.Chat2HiddenTabName);
        }

        //ImGui.SetNextItemWidth(300);
        //if (ImGui.Button("Save active restraints"))
        //{
        //
        //    var restraintSet = plugin.GagSpeakRestraintSetApi.GetActiveRestraintSet();
        //    Plugin.ChatGui.Print($"Active Restraint Set: {restraintSet}");
        //    configuration.ActiveRestraintSet = restraintSet;
        //
        //    var restrictions = plugin.GagSpeakRestrictionsApi.GetActiveRestrictions();
        //    foreach (var restriction in restrictions)
        //    {
        //        Plugin.ChatGui.Print($"Active Restriction: {restriction}");
        //    }
        //    configuration.ActiveRestrictions = restrictions;
        //
        //    var gags = plugin.GagSpeakGagsApi.GetActiveGags();
        //    foreach (var gag in gags)
        //    {
        //        Plugin.ChatGui.Print($"Active Gag: {gag}");
        //    }
        //    configuration.ActiveGags = gags;
        //    configuration.Save();
        //}


        ImGui.SetNextItemWidth(100);
        string gagSpeakMasterName = configuration.GagSpeakMasterName;
        if (ImGui.InputText(
                "GagSpeak Master Name",
                ref gagSpeakMasterName))
        {
            configuration.GagSpeakMasterName = gagSpeakMasterName;
            configuration.Save();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        string gagSpeakMasterWorld = configuration.GagSpeakMasterWorld;
        if (ImGui.InputText(
                "GagSpeak Master World",
                ref gagSpeakMasterWorld))
        {
            configuration.GagSpeakMasterWorld = gagSpeakMasterWorld;
            configuration.Save();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        string name = "";
        string world = "";
        name = Plugin.ObjectTable.LocalPlayer?.Name.ToString() ?? "";
        var homeWorld = Plugin.ObjectTable.LocalPlayer?.HomeWorld.RowId ?? 0u;
        world = plugin.Utils.WorldRowIDToString(homeWorld);
        if (ImGui.Button($"Use {name}@{world}"))
        {
            configuration.GagSpeakMasterName = name;
            configuration.GagSpeakMasterWorld = world;
            configuration.Save();
        }

        ImGui.SetNextItemWidth(100);
        string gagSpeakMasterID = configuration.GagSpeakMasterID;
        if (ImGui.InputText(
                "GagSpeak Master ID",
                ref gagSpeakMasterID))
        {
            configuration.GagSpeakMasterID = gagSpeakMasterID;
            configuration.Save();
        }
        ImGui.SetNextItemWidth(100);
        string gagSpeakSlaveID = configuration.GagSpeakSlaveID;
        if (ImGui.InputText(
                "GagSpeak Slave ID",
                ref gagSpeakSlaveID))
        {
            configuration.GagSpeakSlaveID = gagSpeakSlaveID;
            configuration.Save();
        }
    }
}
