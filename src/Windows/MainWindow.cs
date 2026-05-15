using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System;
using System.IO;
using System.Numerics;
using Dalamud.Interface.Textures.TextureWraps;

namespace SayusGagExtender.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly string iconPath;

    public MainWindow(Plugin plugin)
        : base("Sayu's Gag Extender###SayusGagExtenderMainWindow",
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 260),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        configuration = plugin.Configuration;
        this.iconPath = Path.Combine(Plugin.PluginInterface.AssemblyLocation.Directory?.FullName!, "icon_512.png");
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        DrawHeader();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawFeatureStatus();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawRuntimeStatus();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawActions();
    }

    private void DrawHeader()
    {
        var icon = Plugin.TextureProvider.GetFromFile(iconPath).GetWrapOrDefault();

        if (icon != null)
        {
            ImGui.Image(icon.Handle, new Vector2(48, 48));
            ImGui.SameLine();
        }

        ImGui.BeginGroup();

        ImGui.Text("Sayu's Gag Extender");
        ImGui.TextDisabled("Quick status and controls");

        ImGui.EndGroup();

        ImGui.SameLine();

        var buttonWidth = ImGui.CalcTextSize("Settings").X + ImGui.GetStyle().FramePadding.X * 2;
        var availableWidth = ImGui.GetContentRegionAvail().X;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - buttonWidth);

        if (ImGui.Button("Settings"))
        {
            plugin.ToggleConfigUi();
        }
    }

    private void DrawFeatureStatus()
    {
        ImGui.Text("Features");

        using (ImRaii.Table("FeatureStatusTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Feature", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 90);

            DrawFeaturesRow("Emote Guard", configuration.EmoteGuardEnabled);
            DrawFeaturesRow("Hand Guard", configuration.HandGuardEnabled);
            DrawFeaturesRow("Auto Zap", configuration.AutoZapEnabled);
            DrawFeaturesRow("Auto Vibe", configuration.AutoVibeEnabled);
            DrawFeaturesRow("Teleport Block", configuration.TeleportBlockFeature);
            DrawFeaturesRow("Mount Block", configuration.MountBlockFeature);
            DrawFeaturesRow("Job Switch Block", configuration.JobSwitchBlockFeature);
        }
    }

    private static void DrawFeaturesRow(string label, bool enabled)
    {
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);

        ImGui.TableSetColumnIndex(1);

        var color = enabled
            ? new Vector4(0.2f, 1.0f, 0.2f, 1.0f)
            : new Vector4(1.0f, 0.25f, 0.25f, 1.0f);

        ImGui.TextColored(color, enabled ? "Enabled" : "Disabled");
    }
    private void DrawRuntimeStatus()
    {
        ImGui.Text("Current Status");

        using (ImRaii.Table("RuntimeStatusTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 120);

            DrawStatusRow("Hand Restraints", plugin.WeaponSheather.wearsRestrictedItems);
            DrawStatusRow("Shock Collar", plugin.RandomZapSender.wearsRestrictedItems);
            DrawTextStatusRow(
                "Zap Controller",
                string.IsNullOrWhiteSpace(plugin.Configuration.ZapControllerName)
                    ? "Not set"
                    : plugin.Configuration.ZapControllerName,
                !string.IsNullOrWhiteSpace(plugin.Configuration.ZapControllerName));

            DrawStatusRow("Vibrator", plugin.RandomVibeSender.wearsRestrictedItems);
            DrawTextStatusRow(
                "Vibe Controller",
                string.IsNullOrWhiteSpace(plugin.Configuration.VibeControllerName)
                    ? "Not set"
                    : plugin.Configuration.VibeControllerName,
                !string.IsNullOrWhiteSpace(plugin.Configuration.VibeControllerName));

            DrawStatusRow("Mount block Moodle", plugin.MountBlocker.IsBlockMoodleActiveCached());
            DrawStatusRow("Job switch block Moodle", plugin.JobSwitchBlocker.IsBlockMoodleActiveCached());
            DrawStatusRow("Teleport block Moodle", plugin.TeleportBlocker.IsBlockMoodleActiveCached());

            DrawStatusRow("Chatbox hidden", plugin.ChatMonitor.chatboxHidden);
            DrawStatusRow("Chatbox input hidden", plugin.ChatMonitor.chatboxInputHidden);
            DrawStatusRow("Chatbox input disabled", plugin.ChatMonitor.chatboxInputDisabled);

            DrawStatusRow("Blindfolded", plugin.BlindfoldMonitor.blindfolded);
        }
    }
    private static void DrawStatusRow(string label, bool enabled)
    {
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);

        ImGui.TableSetColumnIndex(1);

        var color = enabled
            ? new Vector4(0.2f, 1.0f, 0.2f, 1.0f)
            : new Vector4(1.0f, 0.25f, 0.25f, 1.0f);

        ImGui.TextColored(color, enabled ? "Active" : "Inactive");
    }
    private static void DrawTextStatusRow(string label, string value, bool good)
    {
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);

        ImGui.TableSetColumnIndex(1);

        //var color = new Vector4(0.2f, 1.0f, 0.2f, 1.0f);

        ImGui.Text(value);
    }

    private void DrawActions()
    {
        ImGui.Text("Actions");

        //if (ImGui.Button("Open Settings"))
        //{
        //    plugin.ToggleConfigUi();
        //}
        //
        //ImGui.SameLine();
        //
        //if (ImGui.Button("Reload GagSpeak Restrictions"))
        //{
        //    // Add your own refresh method here if available.
        //    // Example:
        //    // plugin.RefreshGagSpeakRestrictions();
        //}
        //
        //if (ImGui.Button("Save Chat2 Blindfold Position"))
        //{
        //    if (plugin.Chat2Api.TryGetPositionAndSize(out var bounds) && bounds != null)
        //    {
        //        configuration.Chat2Bounds = bounds;
        //        configuration.Save();
        //    }
        //}
        //
        //ImGui.SameLine();
        //
        //if (ImGui.Button("Apply Chat2 Blindfold Position"))
        //{
        //    plugin.Chat2Api.SetPositionAndSize(configuration.Chat2Bounds);
        //}
    }
}
