using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static SayusGagExtender.RandomVibeSender;
using static SayusGagExtender.RandomZapSender;

namespace SayusGagExtender.Windows;

public partial class ConfigWindow : Window, IDisposable
{

    public Plugin plugin;
    private readonly Configuration configuration;

    private ConfigTab selectedTab = ConfigTab.General;

    private enum ConfigTab
    {
        General,
        Controller,
        HandGuard,
        Blocks,
        Quotas,
        Fatigue,
        MoodleEnforcer,
        PenumbraEnforcer,
        CustomizePlusEnforcer,
        EmoteEnforcer,
        ShockCollar,
        Vibrator,
        Chat2,
        GagSpeakMirror,
        HonorificEnforcer,
        CammnyEnforcer,
        XIVMessenger,
    }

    public ConfigWindow(Plugin plugin) : base("Sayu's Gag Extender Config###SayusGagExtenderConfig")
    {
        Flags = ImGuiWindowFlags.None;

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
        const float navigationWidth = 170f;

        ImGui.BeginChild(
            "SayusGagExtenderConfigNavigation",
            new Vector2(navigationWidth, 0),
            true);

        ImGui.TextUnformatted("Settings");
        ImGui.Separator();
        ImGui.Spacing();

        DrawNavigationItem("General", ConfigTab.General);
        DrawNavigationItem("Controller", ConfigTab.Controller);
        DrawNavigationItem("Hand Guard", ConfigTab.HandGuard);
        DrawNavigationItem("Blocks", ConfigTab.Blocks);
        DrawNavigationItem("Quotas", ConfigTab.Quotas);
        DrawNavigationItem("Fatigue", ConfigTab.Fatigue);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawNavigationItem("Moodle Enforcer", ConfigTab.MoodleEnforcer);
        DrawNavigationItem("Penumbra Enforcer", ConfigTab.PenumbraEnforcer);
        DrawNavigationItem("C+ Enforcer", ConfigTab.CustomizePlusEnforcer);
        DrawNavigationItem("Emote Enforcer", ConfigTab.EmoteEnforcer);
        DrawNavigationItem("Honorific Enforcer", ConfigTab.HonorificEnforcer);
        DrawNavigationItem("Cammny Enforcer", ConfigTab.CammnyEnforcer);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawNavigationItem("Shock Collar", ConfigTab.ShockCollar);
        DrawNavigationItem("Vibrator", ConfigTab.Vibrator);
        DrawNavigationItem("Chat2", ConfigTab.Chat2);
        DrawNavigationItem("XIVMessenger", ConfigTab.XIVMessenger);
        DrawNavigationItem("GagSpeak Mirror", ConfigTab.GagSpeakMirror);

        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild(
            "SayusGagExtenderConfigContent",
            new Vector2(0, 0),
            false,
            flags: ImGuiWindowFlags.HorizontalScrollbar);

        DrawSelectedTabHeader();
        ImGui.Separator();
        ImGui.Spacing();

        switch (selectedTab)
        {
            case ConfigTab.General:
                DrawGeneralTab();
                break;

            case ConfigTab.Controller:
                DrawControllerTab();
                break;

            case ConfigTab.HandGuard:
                DrawHandGuardTab();
                break;

            case ConfigTab.Blocks:
                DrawMoodleBlocksTab();
                break;

            case ConfigTab.Quotas:
                DrawQuotasTab();
                break;

            case ConfigTab.Fatigue:
                DrawFatigueTab();
                break;

            case ConfigTab.MoodleEnforcer:
                DrawMoodleEnforcerTab();
                break;

            case ConfigTab.PenumbraEnforcer:
                DrawPenumbraEnforcerTab();
                break;

            case ConfigTab.CustomizePlusEnforcer:
                DrawCustomizePlusEnforcerTab();
                break;

            case ConfigTab.EmoteEnforcer:
                DrawEmoteEnforcerTab();
                break;

            case ConfigTab.HonorificEnforcer:
                DrawHonorificEnforcerTab();
                break;

            case ConfigTab.CammnyEnforcer:
                DrawCammyEnforcerTab();
                break;

            case ConfigTab.ShockCollar:
                DrawAutoZapTab();
                break;

            case ConfigTab.Vibrator:
                DrawAutoVibeTab();
                break;

            case ConfigTab.Chat2:
                DrawChat2Tab();
                break;

            case ConfigTab.XIVMessenger:
                DrawXIVMessengerTab();
                break;

            case ConfigTab.GagSpeakMirror:
                DrawGagSpeakMirrorTab();
                break;
        }

        ImGui.EndChild();
    }

    private void DrawNavigationItem(string label, ConfigTab tab)
    {
        var selected = selectedTab == tab;

        if (ImGui.Selectable(label, selected, ImGuiSelectableFlags.None, new Vector2(0, ImGui.GetFrameHeight())))
            selectedTab = tab;
    }

    private void DrawSelectedTabHeader()
    {
        ImGui.TextUnformatted(GetSelectedTabName());
    }

    private string GetSelectedTabName()
    {
        return selectedTab switch
        {
            ConfigTab.General => "General",
            ConfigTab.HandGuard => "Hand Guard",
            ConfigTab.Blocks => "Blocks",
            ConfigTab.MoodleEnforcer => "Moodle Enforcer",
            ConfigTab.PenumbraEnforcer => "Penumbra Enforcer",
            ConfigTab.CustomizePlusEnforcer => "C+ Enforcer",
            ConfigTab.EmoteEnforcer => "Emote Enforcer",
            ConfigTab.ShockCollar => "Shock Collar",
            ConfigTab.Vibrator => "Vibrator",
            ConfigTab.Chat2 => "Chat2",
            ConfigTab.GagSpeakMirror => "GagSpeak Mirror",
            _ => "Settings",
        };
    }






























}
