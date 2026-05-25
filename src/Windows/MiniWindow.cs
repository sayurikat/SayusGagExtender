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

public class MiniWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly string iconPath;

    public MiniWindow(Plugin plugin)
        : base("Sayu's Gag Extender###SayusGagExtenderMiniWindow",
            ImGuiWindowFlags.None)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(260, 80),
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

        DrawRuntimeStatus();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawActions();
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
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 120);

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

            //  DrawSeparatorRow();
        }
        

        
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

    private string honorificJson = "";
    private void DrawActions()
    {
        ImGui.Text("Actions");
        if (ImGui.Button("Settings"))
        {
            plugin.ToggleConfigUi();
        }
        ImGui.SameLine();
        if (ImGui.Button("Refresh GagSpeak Cache"))
        {
            plugin.GagSpeakContext.RefreshGagSpeakVisualsAsync();
        }

        if (ImGui.Button("Restrained"))
        {
            plugin.HonorificApi.SetLocalTitle("Restrained", isPrefix: false);
        }
        if (ImGui.Button("Bound"))
        {
            plugin.HonorificApi.SetLocalTitle("Bound", isPrefix: true);
        }
        if (ImGui.Button("Shocked"))
        {
            plugin.HonorificApi.SetLocalTitle(
            "Shocked",
            isPrefix: false,
            color: new Vector3(1.0f, 0.2f, 0.2f),
            glow: new Vector3(1.0f, 0.0f, 0.0f));
        }
        if (ImGui.Button("Clear"))
        {
            plugin.HonorificApi.ClearLocalTitle();
        }
        if (ImGui.Button("Copy"))
        {
            honorificJson = plugin.HonorificApi.GetLocalTitleJson();
        }
        if (ImGui.Button("paste"))
        {
            plugin.HonorificApi.SetLocalTitleJson(honorificJson);
        }
        if (ImGui.Button("Alter"))
        {
            var currentJson = plugin.HonorificApi.GetLocalTitleJson();

            plugin.HonorificApi.SetLocalTitleFromEditedJson(
                currentJson,
                "Restrained");
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
}
