using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Linq;

namespace SayusGagExtender.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly string iconPath;

    public MainWindow(Plugin plugin)
        : base("Sayu's Gag Extender###SayusGagExtenderMainWindow",
            ImGuiWindowFlags.None)
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
        //ImGui.Text($"Normal Conditions: {Plugin.Condition[ConditionFlag.BetweenAreas]}");
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
            ImGui.Image(icon.Handle, new Vector2(144, 144));
            //ImGui.SameLine();
        }

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Controller InterfaceInter").X + ImGui.GetStyle().FramePadding.X * 2);
        if (ImGui.Button("Controller Interface"))
        {
            configuration.ControllerWindowPreferred = true;
            configuration.Save();
            plugin.ToggleControllerUi();
            IsOpen = false;
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

        ImGui.SameLine();

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - buttonWidth*2.1f);

        if (ImGui.Button("Mini"))
        {
            plugin.ToggleMiniUi();
        }
        
    }

    private void DrawFeatureStatus()
    {
        ImGui.Text("Features");

        using (ImRaii.Table("FeatureStatusTable", 3, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Feature", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Active", ImGuiTableColumnFlags.WidthFixed, 90);

            DrawFeaturesRow("Emote Guard", configuration.EmoteGuardEnabled, plugin.EmoteGuard.IsActive);
            DrawFeaturesRow("Hand Guard", configuration.HandGuardEnabled, plugin.WeaponSheather.IsActive);
            DrawFeaturesRow("Teleport Block", configuration.TeleportBlockFeature, plugin.TeleportBlocker.IsActive);
            DrawFeaturesRow("Mount Block", configuration.MountBlockFeature, plugin.MountBlocker.IsActive);
            //DrawFeaturesRow("Job Switch Block", configuration.JobSwitchBlockFeature, plugin.JobSwitchBlocker.IsActive);
            DrawFeaturesRow("Job Manager", configuration.JobSwitchBlockFeature || configuration.JobRouletteEnabled, plugin.JobManager.IsActive);
            DrawFeaturesRow("Fatigue Tracker", configuration.FatigueEnabled, plugin.FatigueTracker.IsActive);
            DrawFeaturesRow("Moodle Enforcer", configuration.MoodleEnforcerEnabled, plugin.MoodleEnforcer.IsActive);
            DrawFeaturesRow("Penumbra Enforcer", configuration.PenumbraEnforcerEnabled, plugin.PenumbraEnforcer.IsActive);
            DrawFeaturesRow("Honorific Enforcer", configuration.HonorificEnforcerEnabled, plugin.HonorificEnforcer.IsActive);
            DrawFeaturesRow("Cammy Enforcer", configuration.CammyEnforcerEnabled, plugin.CammyEnforcer.IsActive);
            DrawFeaturesRow("C+ Enforcer", configuration.CustomizePlusEnforcerEnabled, plugin.CustomizePlusEnforcer.IsActive);
            DrawFeaturesRow("Emote Enforcer", configuration.EmoteEnforcerEnabled, plugin.EmoteEnforcer.IsActive);
            DrawFeaturesRow("Auto Zap", configuration.AutoZapEnabled, plugin.RandomZapSender.IsActive);
            DrawFeaturesRow("Auto Vibe", configuration.AutoVibeEnabled, plugin.RandomVibeSender.IsActive);
            DrawFeaturesRow("Chat2 Blindfold Feature", configuration.Chat2BlindfoldFeatureEnable, plugin.BlindfoldMonitor.IsActive);
            DrawFeaturesRow("XIVMessenger Feature", configuration.XivMessengerManagerEnabled, plugin.XIVMessengerManager.IsActive);
            DrawFeaturesRow("GagSpeak Mirror", configuration.GagSpeakRestraintCloner, plugin.MirrorGagSpeak.IsActive);
        }
    }

    private static void DrawFeaturesRow(string label, bool enabled, bool active)
    {
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);

        ImGui.TableSetColumnIndex(1);

        var colorEnable = enabled
            ? new Vector4(0.2f, 1.0f, 0.2f, 1.0f)
            : new Vector4(1.0f, 0.25f, 0.25f, 1.0f);

        ImGui.TextColored(colorEnable, enabled ? "Enabled" : "Disabled");

        ImGui.TableSetColumnIndex(2);

        var colorActive = active
            ? new Vector4(0.2f, 1.0f, 0.2f, 1.0f)
            : new Vector4(1.0f, 0.25f, 0.25f, 1.0f);

        ImGui.TextColored(colorActive, active ? "Active" : "Inactive");
    }
    private void DrawRuntimeStatus()
    {
        ImGui.Text("Current Status");
        ImGui.SameLine();
        ImGui.TextDisabled(ConfigWindow.GetFatigueStatusLabel(plugin.FatigueTracker.CurrentFatigueStatus));

        var fatiguePercent = configuration.FatigueCurrent * 100.0f;
        var statusColor = ConfigWindow.GetFatigueStatusColor(plugin.FatigueTracker.CurrentFatigueStatus);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, statusColor);
        ImGui.ProgressBar(configuration.FatigueCurrent, new Vector2(300, 0), $"{fatiguePercent:F1}%");
        ImGui.PopStyleColor();
        

        using (ImRaii.Table("RuntimeStatusTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 220);

            DrawTextStatusRow(
                "Auto Zap",
                plugin.RandomZapSender.AutonomousStatus,
                plugin.RandomZapSender.IsAutonomousRunning);

            DrawTextStatusRow(
                "Zap controller",
                plugin.RandomZapSender.ControllerPresenceStatus,
                !plugin.RandomZapSender.ControllerPresenceStatus.Contains("Offline", StringComparison.OrdinalIgnoreCase) &&
                !plugin.RandomZapSender.ControllerPresenceStatus.Contains("Not set", StringComparison.OrdinalIgnoreCase));

            DrawTextStatusRow(
                "Auto Vibe",
                plugin.RandomVibeSender.AutonomousStatus,
                plugin.RandomVibeSender.IsAutonomousRunning);

            DrawTextStatusRow(
                "Vibe controller",
                plugin.RandomVibeSender.ControllerPresenceStatus,
                !plugin.RandomVibeSender.ControllerPresenceStatus.Contains("Offline", StringComparison.OrdinalIgnoreCase) &&
                !plugin.RandomVibeSender.ControllerPresenceStatus.Contains("Not set", StringComparison.OrdinalIgnoreCase));


            DrawSeparatorRow();

            DrawTextStatusRow(
                "Mount quota",
                BuildQuotaStatus(
                    configuration.MountQuotaEnabled,
                    configuration.MountQuotaActions,
                    configuration.MountQuotaWindow,
                    configuration.MountQuotaActionLogUtc),
                IsQuotaGood(
                    configuration.MountQuotaEnabled,
                    configuration.MountQuotaActions,
                    configuration.MountQuotaWindow,
                    configuration.MountQuotaActionLogUtc));

            DrawTextStatusRow(
                "Teleport quota",
                BuildQuotaStatus(
                    configuration.TeleportQuotaEnabled,
                    configuration.TeleportQuotaActions,
                    configuration.TeleportQuotaWindow,
                    configuration.TeleportQuotaActionLogUtc),
                IsQuotaGood(
                    configuration.TeleportQuotaEnabled,
                    configuration.TeleportQuotaActions,
                    configuration.TeleportQuotaWindow,
                    configuration.TeleportQuotaActionLogUtc));

            DrawTextStatusRow(
                "Job switch quota",
                BuildQuotaStatus(
                    configuration.JobSwitchQuotaEnabled,
                    configuration.JobSwitchQuotaActions,
                    configuration.JobSwitchQuotaWindow,
                    configuration.JobSwitchQuotaActionLogUtc),
                IsQuotaGood(
                    configuration.JobSwitchQuotaEnabled,
                    configuration.JobSwitchQuotaActions,
                    configuration.JobSwitchQuotaWindow,
                    configuration.JobSwitchQuotaActionLogUtc));

            DrawSeparatorRow();

            DrawTextStatusRow(
                "Job roulette",
                BuildJobRouletteStatus(configuration),
                configuration.JobRouletteEnabled);

            DrawSeparatorRow();


            DrawStatusRow("Blindfolded", plugin.BlindfoldMonitor.blindfolded);
            DrawStatusRow("Chatbox hidden", plugin.ChatMonitor.chatboxHidden);
            DrawStatusRow("Chatbox input hidden", plugin.ChatMonitor.chatboxInputHidden);
            DrawStatusRow("Chatbox input disabled", plugin.ChatMonitor.chatboxInputDisabled);
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

        var color = good
            ? new Vector4(0.2f, 1.0f, 0.2f, 1.0f)
            : new Vector4(1.0f, 0.25f, 0.25f, 1.0f);

        ImGui.TextColored(color, value);
    }

    private void DrawActions()
    {
        ImGui.Text("Actions");
        if (ImGui.Button("Open Mini"))
        {
            plugin.ToggleMiniUi();
        }
        ImGui.SameLine();
        if (ImGui.Button("Refresh GagSpeak Cache"))
        {
            _ = plugin.GagSpeakContext.RefreshGagSpeakVisualsAsync();
        }


        

    }
    private static void DrawSeparatorRow()
    {
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.Separator();

        ImGui.TableSetColumnIndex(1);
        ImGui.Separator();
    }

    private static string BuildQuotaStatus(
        bool enabled,
        int actionLimit,
        Configuration.QuotaWindow window,
        List<DateTime>? actionLogUtc)
    {
        if (!enabled)
            return "Disabled";

        if (actionLimit < 0)
            return "Unlimited";

        var used = GetQuotaUsed(actionLogUtc, window);
        var remaining = Math.Max(0, actionLimit - used);
        var windowLabel = GetQuotaWindowLabel(window);

        if (remaining <= 0)
            return $"Empty ({used}/{actionLimit} per {windowLabel})";

        return $"{remaining} left ({used}/{actionLimit} per {windowLabel})";
    }

    private static bool IsQuotaGood(
        bool enabled,
        int actionLimit,
        Configuration.QuotaWindow window,
        List<DateTime>? actionLogUtc)
    {
        if (!enabled)
            return false;

        if (actionLimit < 0)
            return true;

        var used = GetQuotaUsed(actionLogUtc, window);
        var remaining = Math.Max(0, actionLimit - used);

        return remaining > 0;
    }

    private static int GetQuotaUsed(List<DateTime>? actionLogUtc, Configuration.QuotaWindow window)
    {
        if (actionLogUtc == null || actionLogUtc.Count == 0)
            return 0;

        var cutoff = DateTime.UtcNow - GetQuotaWindowDuration(window);

        return actionLogUtc.Count(x => x >= cutoff);
    }

    private static TimeSpan GetQuotaWindowDuration(Configuration.QuotaWindow window)
    {
        return window switch
        {
            Configuration.QuotaWindow.Day => TimeSpan.FromHours(24),
            _ => TimeSpan.FromHours(1),
        };
    }

    private static string GetQuotaWindowLabel(Configuration.QuotaWindow window)
    {
        return window switch
        {
            Configuration.QuotaWindow.Day => "day",
            _ => "hour",
        };
    }
    public static string BuildJobRouletteStatus(Configuration configuration)
    {
        if (!configuration.JobRouletteEnabled)
            return "Inactive";

        var untilNext = configuration.NextScheduledJobSwitch - DateTime.UtcNow;
        if (untilNext < TimeSpan.Zero)
            untilNext = TimeSpan.Zero;

        return $"Next in {FormatDuration(untilNext)}";
    }

    private static string FormatDuration(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
            return $"{(int)timeSpan.TotalDays}d {timeSpan.Hours}h {timeSpan.Minutes}m";

        if (timeSpan.TotalHours >= 1)
            return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";

        if (timeSpan.TotalMinutes >= 1)
            return $"{(int)timeSpan.TotalMinutes}m {timeSpan.Seconds}s";

        return $"{Math.Max(0, timeSpan.Seconds)}s";
    }
}
