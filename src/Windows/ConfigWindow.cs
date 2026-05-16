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
        if (!ImGui.BeginTabBar("SayusGagExtenderConfigTabs"))
            return;

        if (ImGui.BeginTabItem("General"))
        {
            DrawGeneralTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Hand Guard"))
        {
            DrawHandGuardTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Blocks"))
        {
            DrawMoodleBlocksTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Moodle Enforcer"))
        {
            DrawMoodleEnforcerTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Penumbra Enforcer"))
        {
            DrawPenumbraEnforcerTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("C+ Enforcer"))
        {
            DrawCustomizePlusEnforcerTab();
            ImGui.EndTabItem();
        }
        
        if (ImGui.BeginTabItem("Emote Enforcer"))
        {
            DrawEmoteEnforcerTab();
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
            DrawGagSpeakMirrorTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    

    

    
    

    

    

    














    
    
    
}
