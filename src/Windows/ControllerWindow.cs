using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace SayusGagExtender.Windows;

public class ControllerWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private int selectedUserIndex = -1;
    private string newUserName = string.Empty;
    private string newUserWorld = string.Empty;
    private readonly Dictionary<string, ControllerUserInputState> inputStates = new();
    private DateTime commandButtonsDisabledUntilUtc = DateTime.MinValue;
    private readonly RemotePackageTransport packageTransport = new();

    private sealed class ControllerUserInputState
    {
        public int ZapCount = 8;
        public int VibeCount = 8;
        public int MountCount = 3;
        public int TeleportCount = 3;
        public int JobCount = 3;
        public int RouletteMinutes = 30;
        public int TempTitleSeconds = 60;
        public string Title = string.Empty;
        public string TitleTemp = string.Empty;
        public int MountWindow = 0;
        public int TeleportWindow = 0;
        public int JobWindow = 0; 
        public Vector3 TitleColor = new(1f, 1f, 1f);
        public Vector3 TitleGlow = new(0f, 0f, 0f);
        public Vector3 TitleTempColor = new(1f, 1f, 1f);
        public Vector3 TitleTempGlow = new(0f, 0f, 0f);
        public string TitleTempSourceJson = string.Empty;
    }
    private XivChatType selectedRemoteChannelToAdd = XivChatType.TellIncoming;

    private static readonly XivChatType[] RemoteChannelOptions =
    [
        XivChatType.TellIncoming,

            // CWLS
            XivChatType.CrossLinkShell1,
            XivChatType.CrossLinkShell2,
            XivChatType.CrossLinkShell3,
            XivChatType.CrossLinkShell4,
            XivChatType.CrossLinkShell5,
            XivChatType.CrossLinkShell6,
            XivChatType.CrossLinkShell7,
            XivChatType.CrossLinkShell8,

            // Optional future-use channels.
            XivChatType.Party,
            XivChatType.Alliance,
            XivChatType.FreeCompany,
            XivChatType.Ls1,
            XivChatType.Ls2,
            XivChatType.Ls3,
            XivChatType.Ls4,
            XivChatType.Ls5,
            XivChatType.Ls6,
            XivChatType.Ls7,
            XivChatType.Ls8,
            XivChatType.Say,
            XivChatType.Yell,
            XivChatType.Shout,
        ];
    public ControllerWindow(Plugin plugin) : base("Sayu's Gag Extender Controller###SayusGagExtenderControllerWindow", ImGuiWindowFlags.None)
    {
        Size = new Vector2(760, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(560, 320), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        DrawHeader();
        ImGui.Separator();
        ImGui.Spacing();
        const float navigationWidth = 170f;
        ImGui.BeginChild("SayusGagExtenderControllerNavigation", new Vector2(navigationWidth, 0), true, ImGuiWindowFlags.None);
        DrawNavigation();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("SayusGagExtenderControllerContent", new Vector2(0, 0), false, ImGuiWindowFlags.HorizontalScrollbar);
        if (selectedUserIndex < 0) DrawGeneralTab();
        else DrawUserTab(GetSelectedUser());
        ImGui.EndChild();
    }

    private void DrawHeader()
    {
        ImGui.TextUnformatted("Controller Interface");
        ImGui.SameLine();
        var buttonWidth = ImGui.CalcTextSize("Main").X + ImGui.GetStyle().FramePadding.X * 2;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - buttonWidth);
        if (ImGui.Button("Main"))
        {
            configuration.ControllerWindowPreferred = false;
            configuration.Save();
            plugin.ToggleMainUi();
            IsOpen = false;
        }
    }

    private void DrawNavigation()
    {
        if (ImGui.Selectable("General", selectedUserIndex < 0, ImGuiSelectableFlags.None, new Vector2(0, ImGui.GetFrameHeight()))) selectedUserIndex = -1;
        ImGui.Separator();
        for (var i = 0; i < configuration.ControllerUsers.Count; i++)
        {
            var user = configuration.ControllerUsers[i];
            var label = string.IsNullOrWhiteSpace(user.World) ? user.Name : $"{user.Name}@{user.World}";
            if (string.IsNullOrWhiteSpace(label)) label = $"User {i + 1}";
            if (ImGui.Selectable($"{label}###controller-user-{i}", selectedUserIndex == i, ImGuiSelectableFlags.None, new Vector2(0, ImGui.GetFrameHeight()))) selectedUserIndex = i;
        }
    }

    private void DrawGeneralTab()
    {
        ImGui.TextUnformatted("General");
        ImGui.TextDisabled("Add GagSpeak users you want to control through remote tell commands.");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(220);
        ImGui.InputText("Name", ref newUserName, 64);
        ImGui.SetNextItemWidth(160);
        ImGui.InputText("World", ref newUserWorld, 64);
        if (ImGui.Button("Add User"))
        {
            AddUser();
        }
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (configuration.ControllerUsers.Count == 0)
        {
            ImGui.TextDisabled("No controlled users added yet.");
            return;
        }
        if (ImGui.BeginTable("ControllerUsersTable", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableSetupColumn("User", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Last Status", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Open", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Remove", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableHeadersRow();
            for (var i = 0; i < configuration.ControllerUsers.Count; i++) DrawUserRow(i);
            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawRemoteAcceptedChannels();
    }

    private void DrawUserRow(int index)
    {
        var user = configuration.ControllerUsers[index];
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(GetUserDisplayName(user));
        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(FormatLastStatus(user.LastStatusUtc));
        ImGui.TableSetColumnIndex(2);
        if (ImGui.SmallButton($"Open###open-controller-user-{index}")) selectedUserIndex = index;
        ImGui.TableSetColumnIndex(3);
        if (ImGui.SmallButton($"Remove###remove-controller-user-{index}")) RemoveUser(index);
    }

    private void DrawUserTab(Configuration.ControllerUserConfig? user)
    {
        if (user == null)
        {
            selectedUserIndex = -1;
            DrawGeneralTab();
            return;
        }
        var state = GetInputState(user);
        ImGui.TextUnformatted(GetUserDisplayName(user));
        ImGui.SameLine();
        if (CommandButton("Request Status")) SendCommand(user, "statuspack");
        ImGui.SameLine();
        ImGui.Spacing();
        DrawCachedStatus(user);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawZapCommands(user, state);
        ImGui.Spacing();
        DrawVibeCommands(user, state);
        ImGui.Spacing();
        DrawQuotaCommands(user, state);
        ImGui.Spacing();
        DrawRouletteCommands(user, state);
        ImGui.Spacing();
        DrawTitleCommands(user, state);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("sge help → Shows available commands");
        ImGui.Text("sge status → Display all status/settings");
        ImGui.Text("sge autozap [always/distant/offline] → Toggles when autozap feature is active");
        ImGui.Text("sge zapcount [count] → Amount of automated zaps per hour");
        ImGui.Text("sge autovibe [always/distant/offline] → Toggles when autovibe feature is active");
        ImGui.Text("sge vibecount [count] → Amount of automated vibrations per hour");
        ImGui.Text("sge mountlimit [day/hour] [count] → How many times a mount can be used per day/hour, or: sge mountlimit unlimited");
        ImGui.Text("sge teleportlimit [day/hour] [count] → How many times a teleport can be used per day/hour, or: sge teleportlimit unlimited");
        ImGui.Text("sge joblimit [day/hour] [count] → How many times jobs can be changed per day/hour, or: sge joblimit unlimited");
        ImGui.Text("sge jobroulette [minutes] → Enable job roulette with required interval in minutes");
        ImGui.Text("sge stopjobroulette → Stop job roulette and unlock local roulette settings");
        ImGui.Text("sge settitle [title] → Sets permanent Honorific title");
        ImGui.Text("sge settemptitle [seconds] [title] → Sets temporary Honorific title");
        ImGui.Text("sge cleartitle → Clears the permanent remote Honorific title");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawPuppeteerAliasesSection(user);
    }

    private void DrawPuppeteerAliasesSection(Configuration.ControllerUserConfig user)
    {
        ImGui.TextUnformatted("Puppeteer Aliases");
        ImGui.SameLine();
        if (CommandButton("Request Aliases")) SendPuppeteerAliasesRequest(user);
        ImGui.TextDisabled($"Last alias package: {FormatLastStatus(user.LastPuppeteerAliasesUtc)}");

        var channel = user.PuppeteerAliasChatType;
        if (!RemoteChannelOptions.Contains(channel)) channel = XivChatType.TellIncoming;
        var channelPreview = GetRemoteChannelLabel(channel);
        ImGui.SetNextItemWidth(160);
        if (ImGui.BeginCombo("Chat kind##PuppeteerAliasChatKind", channelPreview))
        {
            foreach (var option in RemoteChannelOptions)
            {
                var selected = option == channel;
                if (ImGui.Selectable($"{GetRemoteChannelLabel(option)}##puppeteer-alias-chat-{option}", selected))
                {
                    user.PuppeteerAliasChatType = option;
                    configuration.Save();
                    ImGui.CloseCurrentPopup();
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        var triggerPrefix = user.PuppeteerAliasTriggerPrefix ?? string.Empty;
        ImGui.SetNextItemWidth(220);
        if (ImGui.InputTextWithHint("Trigger prefix", "optional text before alias trigger", ref triggerPrefix, 128))
        {
            user.PuppeteerAliasTriggerPrefix = triggerPrefix;
            configuration.Save();
        }

        var aliases = user.PuppeteerAliases ?? new List<Configuration.ControllerPuppeteerAliasConfig>();
        if (aliases.Count == 0)
        {
            ImGui.TextDisabled("No aliases cached for this user yet.");
            return;
        }

        if (!ImGui.BeginTable("ControllerPuppeteerAliasTable", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupColumn("Folder", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Trigger", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Send", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableHeadersRow();

        for (var i = 0; i < aliases.Count; i++)
        {
            var alias = aliases[i];
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(alias.Folder) ? "<root>" : alias.Folder);
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(alias.Name ?? string.Empty);
            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(alias.Trigger ?? string.Empty);
            ImGui.TableSetColumnIndex(3);
            ImGui.TextWrapped(alias.Note ?? string.Empty);
            ImGui.TableSetColumnIndex(4);
            if (CommandButton($"Send command##send-puppeteer-alias-{i}")) SendPuppeteerAliasCommand(user, alias);
        }

        ImGui.EndTable();
    }

    private void DrawCachedStatus(Configuration.ControllerUserConfig user)
    {
        ImGui.TextUnformatted("Cached Status");
        if (ImGui.BeginTable("ControllerCachedStatusTable", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 170);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            DrawCacheRow("Last package", FormatLastStatus(user.LastStatusUtc));
            DrawCacheRow("Auto Zap", $"{FormatEnabled(user.AutoZapEnabled)}, {user.AutoZapCount}/hour, {FormatBlank(user.AutoZapWhen)}");
            DrawCacheRow("Auto Vibe", $"{FormatEnabled(user.AutoVibeEnabled)}, {user.AutoVibeCount}/hour, {FormatBlank(user.AutoVibeWhen)}");
            DrawCacheRow("Mount limit", FormatQuota(user.MountQuotaEnabled, user.MountQuotaActions, user.MountQuotaUsed, user.MountQuotaWindow));
            DrawCacheRow("Teleport limit", FormatQuota(user.TeleportQuotaEnabled, user.TeleportQuotaActions, user.TeleportQuotaUsed, user.TeleportQuotaWindow));
            DrawCacheRow("Job limit", FormatQuota(user.JobQuotaEnabled, user.JobQuotaActions, user.JobQuotaUsed, user.JobQuotaWindow));
            DrawCacheRow("Job roulette", FormatJobRouletteStatus(user));
            DrawCacheRow("Remote title", string.IsNullOrWhiteSpace(user.RemoteTitle) ? "None" : user.RemoteTitle);
            ImGui.EndTable();
        }
    }

    private void DrawZapCommands(Configuration.ControllerUserConfig user, ControllerUserInputState state)
    {
        ImGui.TextUnformatted("Shock Collar");
        ImGui.SetNextItemWidth(90);
        ImGui.InputInt("Zaps per hour", ref state.ZapCount);
        ImGui.SameLine();
        if (CommandButton("Set Zap Count")) SendCommand(user, $"zapcount {Math.Max(0, state.ZapCount)}");
        if (CommandButton("Autozap Always", user.AutoZapEnabled && user.AutoZapWhen.Equals("Always", StringComparison.OrdinalIgnoreCase))) SendCommand(user, "autozap always");
        ImGui.SameLine();
        if (CommandButton("Autozap Distant", user.AutoZapEnabled && user.AutoZapWhen.Equals("Distant", StringComparison.OrdinalIgnoreCase))) SendCommand(user, "autozap distant");
        ImGui.SameLine();
        if (CommandButton("Autozap Offline", user.AutoZapEnabled && user.AutoZapWhen.Equals("Offline", StringComparison.OrdinalIgnoreCase))) SendCommand(user, "autozap offline");
    }

    private void DrawVibeCommands(Configuration.ControllerUserConfig user, ControllerUserInputState state)
    {
        ImGui.TextUnformatted("Vibrator");
        ImGui.SetNextItemWidth(90);
        ImGui.InputInt("Vibes per hour", ref state.VibeCount);
        ImGui.SameLine();
        if (CommandButton("Set Vibe Count")) SendCommand(user, $"vibecount {Math.Max(0, state.VibeCount)}");
        if (CommandButton("Autovibe Always", user.AutoVibeEnabled && user.AutoVibeWhen.Equals("Always", StringComparison.OrdinalIgnoreCase))) SendCommand(user, "autovibe always");
        ImGui.SameLine();
        if (CommandButton("Autovibe Distant", user.AutoVibeEnabled && user.AutoVibeWhen.Equals("Distant", StringComparison.OrdinalIgnoreCase))) SendCommand(user, "autovibe distant");
        ImGui.SameLine();
        if (CommandButton("Autovibe Offline", user.AutoVibeEnabled && user.AutoVibeWhen.Equals("Offline", StringComparison.OrdinalIgnoreCase))) SendCommand(user, "autovibe offline");
    }

    private void DrawQuotaCommands(Configuration.ControllerUserConfig user, ControllerUserInputState state)
    {
        ImGui.TextUnformatted("Quotas");
        DrawQuotaCommand("Mount", user, "mountlimit", ref state.MountWindow, ref state.MountCount, user.MountQuotaEnabled, user.MountQuotaActions);
        DrawQuotaCommand("Teleport", user, "teleportlimit", ref state.TeleportWindow, ref state.TeleportCount, user.TeleportQuotaEnabled, user.TeleportQuotaActions);
        DrawQuotaCommand("Job", user, "joblimit", ref state.JobWindow, ref state.JobCount, user.JobQuotaEnabled, user.JobQuotaActions);
    }

    private void DrawQuotaCommand(string label, Configuration.ControllerUserConfig user, string command, ref int window, ref int count, bool quotaEnabled, int quotaActions)
    {
        ImGui.PushID(command);
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.Combo("Window", ref window, "hour\0day\0");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.InputInt("Count", ref count);
        ImGui.SameLine();
        if (CommandButton("Set", quotaEnabled && quotaActions >= 0)) SendCommand(user, $"{command} {GetWindowCommandText(window)} {Math.Max(0, count)}");
        ImGui.SameLine();
        if (CommandButton("Unlimited", !quotaEnabled || quotaActions < 0)) SendCommand(user, $"{command} {GetWindowCommandText(window)} unlimited");
        ImGui.PopID();
    }

    private void DrawRouletteCommands(Configuration.ControllerUserConfig user, ControllerUserInputState state)
    {
        ImGui.TextUnformatted("Job Roulette");
        ImGui.SetNextItemWidth(90);
        ImGui.InputInt("Interval minutes", ref state.RouletteMinutes);
        ImGui.SameLine();
        if (CommandButton("Start Roulette", user.JobRouletteEnabled)) SendCommand(user, $"jobroulette {Math.Max(1, state.RouletteMinutes)}");
        ImGui.SameLine();
        if (CommandButton("Stop Roulette", !user.JobRouletteEnabled)) SendCommand(user, "stopjobroulette");
    }

    private void DrawTitleCommands(Configuration.ControllerUserConfig user, ControllerUserInputState state)
    {
        ImGui.TextUnformatted("Honorific Title");
        var permanentChanged = false;
        ImGui.SetNextItemWidth(260);
        if (ImGui.InputText("Title", ref state.Title, 128)) permanentChanged = true;
        if (state.Title.Length > API.HonorificApi.MaxTitleLength) state.Title = state.Title[..API.HonorificApi.MaxTitleLength];
        ImGui.SameLine();
        if (ImGui.ColorEdit3("Color##PermanentTitleColor", ref state.TitleColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel)) permanentChanged = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Honorific title color");
        ImGui.SameLine();
        if (ImGui.ColorEdit3("Glow##PermanentTitleGlow", ref state.TitleGlow, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel)) permanentChanged = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Honorific glow color");

        if (permanentChanged) SavePermanentHonorificEditorState(user, state);

        ImGui.SameLine();
        if (CommandButton("Clone Current")) SendCloneCurrentHonorificRequest(user);


        if (CommandButton("Set Permanent Title")) SendPermanentHonorificTitle(user, state);
        ImGui.SameLine();
        if (CommandButton("Clear Title")) ClearPermanentHonorificTitle(user);

        ImGui.Spacing();
        ImGui.TextUnformatted("Temporary Honorific Title");

        ImGui.SetNextItemWidth(260);
        ImGui.InputText("TitleTemp", ref state.TitleTemp, 128);
        if (state.TitleTemp.Length > API.HonorificApi.MaxTitleLength) state.TitleTemp = state.TitleTemp[..API.HonorificApi.MaxTitleLength];
        ImGui.SameLine();
        ImGui.ColorEdit3("Color##TempTitleColor", ref state.TitleTempColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Temporary Honorific title color");
        ImGui.SameLine();
        ImGui.ColorEdit3("Glow##TempTitleGlow", ref state.TitleTempGlow, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Temporary Honorific glow color");

        ImGui.SameLine();
        if (CommandButton("Clone Current Temp")) SendCloneCurrentTempHonorificRequest(user);

        if (CommandButton("Set Temporary Title")) SendTemporaryHonorificTitle(user, state);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        ImGui.InputInt("Seconds", ref state.TempTitleSeconds);
    }
    private void SendCloneCurrentTempHonorificRequest(Configuration.ControllerUserConfig user)
    {
        var package = new RemotePackage(6);
        package.WriteInt(1);
        SendPackage(user, package);
    }
    private void SendPuppeteerAliasesRequest(Configuration.ControllerUserConfig user)
    {
        var package = new RemotePackage(8);
        package.WriteInt(1);
        SendPackage(user, package);
    }
    private void SendPuppeteerAliasCommand(Configuration.ControllerUserConfig user, Configuration.ControllerPuppeteerAliasConfig alias)
    {
        var trigger = alias.Trigger?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trigger)) return;
        var prefix = user.PuppeteerAliasTriggerPrefix?.Trim() ?? string.Empty;
        var message = string.IsNullOrWhiteSpace(prefix) ? trigger : $"{prefix} {trigger}";
        var command = BuildChatCommand(user, user.PuppeteerAliasChatType, message);
        if (string.IsNullOrWhiteSpace(command)) return;
        if (command.Length > 500)
        {
            Plugin.ChatGui.PrintError("Puppeteer alias command is too long for chat.");
            return;
        }
        plugin.Utils.ExecuteNativeCommand(command);
        commandButtonsDisabledUntilUtc = DateTime.UtcNow.AddSeconds(1);
    }
    private static string BuildChatCommand(Configuration.ControllerUserConfig user, XivChatType channel, string message)
    {
        message = message.Trim();
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;
        if (channel == XivChatType.TellIncoming) return string.IsNullOrWhiteSpace(user.Name) || string.IsNullOrWhiteSpace(user.World) ? string.Empty : $"/t {user.Name}@{user.World} {message}";
        var slash = GetChatSlashCommand(channel);
        return string.IsNullOrWhiteSpace(slash) ? string.Empty : $"{slash} {message}";
    }
    private static string GetChatSlashCommand(XivChatType channel)
    {
        return channel switch
        {
            XivChatType.CrossLinkShell1 => "/cwl1",
            XivChatType.CrossLinkShell2 => "/cwl2",
            XivChatType.CrossLinkShell3 => "/cwl3",
            XivChatType.CrossLinkShell4 => "/cwl4",
            XivChatType.CrossLinkShell5 => "/cwl5",
            XivChatType.CrossLinkShell6 => "/cwl6",
            XivChatType.CrossLinkShell7 => "/cwl7",
            XivChatType.CrossLinkShell8 => "/cwl8",
            XivChatType.Party => "/p",
            XivChatType.Alliance => "/a",
            XivChatType.FreeCompany => "/fc",
            XivChatType.Ls1 => "/l1",
            XivChatType.Ls2 => "/l2",
            XivChatType.Ls3 => "/l3",
            XivChatType.Ls4 => "/l4",
            XivChatType.Ls5 => "/l5",
            XivChatType.Ls6 => "/l6",
            XivChatType.Ls7 => "/l7",
            XivChatType.Ls8 => "/l8",
            XivChatType.Say => "/s",
            XivChatType.Yell => "/y",
            XivChatType.Shout => "/sh",
            _ => string.Empty,
        };
    }
    private void DrawCacheRow(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);
        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(value);
    }
    private void SendPermanentHonorificTitle(Configuration.ControllerUserConfig user, ControllerUserInputState state)
    {
        SavePermanentHonorificEditorState(user, state);
        var package = new RemotePackage(2);
        package.WriteInt(1);
        package.WriteString(user.HonorificTitle.Json);
        SendPackage(user, package);
    }
    private void SendTemporaryHonorificTitle(Configuration.ControllerUserConfig user, ControllerUserInputState state)
    {
        var json = BuildHonorificJson(state.TitleTempSourceJson, state.TitleTemp, state.TitleTempColor, state.TitleTempGlow);
        if (string.IsNullOrWhiteSpace(json)) return;
        var package = new RemotePackage(3);
        package.WriteInt(1);
        package.WriteInt(Math.Max(1, state.TempTitleSeconds));
        package.WriteString(json);
        SendPackage(user, package);
    }
    private void SendCloneCurrentHonorificRequest(Configuration.ControllerUserConfig user)
    {
        var package = new RemotePackage(4);
        package.WriteInt(1);
        SendPackage(user, package);
    }
    private void ClearPermanentHonorificTitle(Configuration.ControllerUserConfig user)
    {
        user.HonorificTitle.Json = string.Empty;
        user.HonorificTitle.Title = string.Empty;
        user.HonorificTitle.Color = new Vector3(1f, 1f, 1f);
        user.HonorificTitle.Glow = new Vector3(0f, 0f, 0f);
        configuration.Save();
        RefreshUserInputState(user.Name, user.World);
        SendCommand(user, "cleartitle");
    }
    private void SavePermanentHonorificEditorState(Configuration.ControllerUserConfig user, ControllerUserInputState state)
    {
        user.HonorificTitle.Title = state.Title.Trim();
        user.HonorificTitle.Color = state.TitleColor;
        user.HonorificTitle.Glow = state.TitleGlow;
        user.HonorificTitle.Json = BuildHonorificJson(user.HonorificTitle.Json, state.Title, state.TitleColor, state.TitleGlow);
        configuration.Save();
    }
    private void SendPackage(Configuration.ControllerUserConfig user, RemotePackage package)
    {
        if (string.IsNullOrWhiteSpace(user.Name) || string.IsNullOrWhiteSpace(user.World)) return;
        var prefix = string.IsNullOrWhiteSpace(configuration.RemoteChatCommandPrefix) ? "sge" : configuration.RemoteChatCommandPrefix.Trim();

        foreach (var line in packageTransport.BuildTellLines(package, prefix))
        {
            var command = $"/t {user.Name}@{user.World} {line}";

            if (command.Length > 500)
            {
                Plugin.ChatGui.PrintError("Controller package line is too long for chat.");
                return;
            }

            plugin.Utils.ExecuteNativeCommand(command);
        }

        commandButtonsDisabledUntilUtc = DateTime.UtcNow.AddSeconds(2);
    }
    private static bool HighlightButton(string label, bool highlighted)
    {
        if (!highlighted)
            return ImGui.Button(label);

        var style = ImGui.GetStyle();
        ImGui.PushStyleColor(ImGuiCol.Button, style.Colors[(int)ImGuiCol.ButtonActive]);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.ButtonHovered]);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, style.Colors[(int)ImGuiCol.ButtonActive]);

        var clicked = ImGui.Button(label);

        ImGui.PopStyleColor(3);
        return clicked;
    }

    private void AddUser()
    {
        var name = newUserName.Trim();
        var world = newUserWorld.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(world)) return;
        configuration.ControllerUsers.Add(new Configuration.ControllerUserConfig { Name = name, World = world });
        configuration.Save();
        selectedUserIndex = configuration.ControllerUsers.Count - 1;
        newUserName = string.Empty;
        newUserWorld = string.Empty;
    }

    private void RemoveUser(int index)
    {
        if (index < 0 || index >= configuration.ControllerUsers.Count) return;
        var user = configuration.ControllerUsers[index];
        inputStates.Remove(GetUserKey(user));
        configuration.ControllerUsers.RemoveAt(index);
        configuration.Save();
        if (selectedUserIndex >= configuration.ControllerUsers.Count) selectedUserIndex = configuration.ControllerUsers.Count - 1;
        if (configuration.ControllerUsers.Count == 0) selectedUserIndex = -1;
    }

    private Configuration.ControllerUserConfig? GetSelectedUser()
    {
        if (selectedUserIndex < 0 || selectedUserIndex >= configuration.ControllerUsers.Count) return null;
        return configuration.ControllerUsers[selectedUserIndex];
    }

    private ControllerUserInputState GetInputState(Configuration.ControllerUserConfig user)
    {
        var key = GetUserKey(user);
        if (inputStates.TryGetValue(key, out var state)) return state;
        state = CreateInputStateFromUser(user);
        inputStates[key] = state;
        return state;
    }
    private bool CommandButton(string label, bool highlighted = false)
    {
        var disabled = DateTime.UtcNow < commandButtonsDisabledUntilUtc;

        if (disabled)
            ImGui.BeginDisabled();

        var clicked = HighlightButton(label, highlighted);

        if (disabled)
        {
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Please wait before sending another command.");

            ImGui.EndDisabled();
        }

        return clicked && !disabled;
    }
    private void SendCommand(Configuration.ControllerUserConfig user, string args, bool hidden = true)
    {
        var hiddenToken = (hidden ? "::" : " ");
        args = args.Trim();
        if (string.IsNullOrWhiteSpace(user.Name) || string.IsNullOrWhiteSpace(user.World) || string.IsNullOrWhiteSpace(args)) return;
        var prefix = string.IsNullOrWhiteSpace(configuration.RemoteChatCommandPrefix) ? "sge" : configuration.RemoteChatCommandPrefix.Trim();
        var command = $"/t {user.Name}@{user.World} {prefix}{hiddenToken}{args}";
        if (command.Length > 500)
        {
            Plugin.ChatGui.PrintError("Controller command is too long for chat.");
            return;
        }
        plugin.Utils.ExecuteNativeCommand(command);
        commandButtonsDisabledUntilUtc = DateTime.UtcNow.AddSeconds(2);
    }

    private static string GetUserKey(Configuration.ControllerUserConfig user)
    {
        return $"{user.Name.Trim()}@{user.World.Trim()}".ToLowerInvariant();
    }

    private static string GetUserDisplayName(Configuration.ControllerUserConfig user)
    {
        if (string.IsNullOrWhiteSpace(user.World)) return user.Name;
        return $"{user.Name}@{user.World}";
    }

    private static string GetWindowCommandText(int window)
    {
        return window == 1 ? "day" : "hour";
    }

    private static string FormatLastStatus(DateTime utc)
    {
        if (utc == DateTime.MinValue) return "Never";
        var age = DateTime.UtcNow - utc;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age.TotalDays >= 1) return $"{(int)age.TotalDays}d ago";
        if (age.TotalHours >= 1) return $"{(int)age.TotalHours}h ago";
        if (age.TotalMinutes >= 1) return $"{(int)age.TotalMinutes}m ago";
        return $"{Math.Max(0, age.Seconds)}s ago";
    }

    private static string FormatEnabled(bool enabled)
    {
        return enabled ? "Enabled" : "Disabled";
    }

    private static string FormatLocked(bool locked)
    {
        return locked ? "Locked" : "Unlocked";
    }

    private static string FormatBlank(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
    }

    private static string FormatQuota(bool enabled, int count, int used, Configuration.QuotaWindow window)
    {
        used = Math.Max(0, used);

        if (!enabled) return $"Disabled";
        if (count < 0) return $"Unlimited";

        return $"{used}/{count} used this {window.ToString().ToLowerInvariant()}";
    }


    private void DrawRemoteAcceptedChannels()
    {
        var remoteCommandsEnabled = configuration.RemoteChatCommandsEnabled;
        if (ImGui.Checkbox("Enable controller commands", ref remoteCommandsEnabled))
        {
            configuration.RemoteChatCommandsEnabled = remoteCommandsEnabled;
            configuration.Save();
        }
        ImGui.Text("Warning, this also enables remote commands from the user you have listed as your controller, if any.");

        ImGui.Spacing();

        ImGui.Text("Data channels");
        ImGui.TextWrapped("Remote data will only be accepted from the configured controller in the selected channels.");

        configuration.RemoteAcceptedChannels ??= new List<XivChatType>();

        var selectedChannels = configuration.RemoteAcceptedChannels
            .Distinct()
            .ToList();

        if (selectedChannels.Count != configuration.RemoteAcceptedChannels.Count)
        {
            configuration.RemoteAcceptedChannels = selectedChannels;
            configuration.Save();
        }

        var availableChannels = RemoteChannelOptions
            .Where(x => !selectedChannels.Contains(x))
            .ToList();

        if (availableChannels.Count > 0 && !availableChannels.Contains(selectedRemoteChannelToAdd))
            selectedRemoteChannelToAdd = availableChannels[0];

        var preview = availableChannels.Count == 0
            ? "No channels available"
            : GetRemoteChannelLabel(selectedRemoteChannelToAdd);

        ImGui.SetNextItemWidth(260);

        if (availableChannels.Count == 0)
            ImGui.BeginDisabled();

        if (ImGui.BeginCombo("##RemoteAcceptedChannelCombo", preview))
        {
            foreach (var channel in availableChannels)
            {
                var selected = channel == selectedRemoteChannelToAdd;

                if (ImGui.Selectable($"{GetRemoteChannelLabel(channel)}##remote-channel-{channel}", selected))
                {
                    selectedRemoteChannelToAdd = channel;
                    ImGui.CloseCurrentPopup();
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (availableChannels.Count == 0)
            ImGui.EndDisabled();

        ImGui.SameLine();

        var canAdd = availableChannels.Count > 0 &&
                     !configuration.RemoteAcceptedChannels.Contains(selectedRemoteChannelToAdd);

        if (!canAdd)
            ImGui.BeginDisabled();

        if (ImGui.Button("Add Channel"))
        {
            configuration.RemoteAcceptedChannels.Add(selectedRemoteChannelToAdd);
            configuration.Save();

            var nextAvailable = RemoteChannelOptions
                .FirstOrDefault(x => !configuration.RemoteAcceptedChannels.Contains(x));

            selectedRemoteChannelToAdd = nextAvailable;
        }

        if (!canAdd)
            ImGui.EndDisabled();

        ImGui.Spacing();

        if (configuration.RemoteAcceptedChannels.Count == 0)
        {
            ImGui.TextDisabled("No accepted channels configured.");
            return;
        }

        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        const float selectedRowWidth = 260f;

        ImGui.Indent();

        for (var i = configuration.RemoteAcceptedChannels.Count - 1; i >= 0; i--)
        {
            var channel = configuration.RemoteAcceptedChannels[i];

            ImGui.PushID($"remote-accepted-channel-{channel}");

            if (DrawRemoteChannelRow(GetRemoteChannelLabel(channel), selectedRowWidth, ctrlHeld))
            {
                configuration.RemoteAcceptedChannels.RemoveAt(i);
                configuration.Save();
            }

            ImGui.PopID();
        }

        ImGui.Unindent();
    }

    private static bool DrawRemoteChannelRow(string text, float width, bool removeEnabled)
    {
        var style = ImGui.GetStyle();

        const string removeLabel = "X";

        var rowHeight = ImGui.GetFrameHeight();

        var removeWidth =
            ImGui.CalcTextSize(removeLabel).X +
            style.FramePadding.X * 2f;

        var selectableWidth =
            width -
            removeWidth -
            style.ItemSpacing.X;

        selectableWidth = Math.Max(selectableWidth, 1f);

        ImGui.Selectable(
            $"{text}##selected-remote-channel-row",
            true,
            ImGuiSelectableFlags.None,
            new System.Numerics.Vector2(selectableWidth, rowHeight));

        ImGui.SameLine();

        if (!removeEnabled)
            ImGui.BeginDisabled();

        var clicked = ImGui.SmallButton(removeLabel);

        if (!removeEnabled)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(removeEnabled ? "Remove channel" : "Hold CTRL to remove channel");

        return clicked;
    }

    private static string GetRemoteChannelLabel(XivChatType channel)
    {
        return channel switch
        {
            XivChatType.TellIncoming => "Tell",

            XivChatType.CrossLinkShell1 => "CWLS 1",
            XivChatType.CrossLinkShell2 => "CWLS 2",
            XivChatType.CrossLinkShell3 => "CWLS 3",
            XivChatType.CrossLinkShell4 => "CWLS 4",
            XivChatType.CrossLinkShell5 => "CWLS 5",
            XivChatType.CrossLinkShell6 => "CWLS 6",
            XivChatType.CrossLinkShell7 => "CWLS 7",
            XivChatType.CrossLinkShell8 => "CWLS 8",

            XivChatType.Ls1 => "Linkshell 1",
            XivChatType.Ls2 => "Linkshell 2",
            XivChatType.Ls3 => "Linkshell 3",
            XivChatType.Ls4 => "Linkshell 4",
            XivChatType.Ls5 => "Linkshell 5",
            XivChatType.Ls6 => "Linkshell 6",
            XivChatType.Ls7 => "Linkshell 7",
            XivChatType.Ls8 => "Linkshell 8",

            XivChatType.Party => "Party",
            XivChatType.Alliance => "Alliance",
            XivChatType.FreeCompany => "Free Company",
            XivChatType.Say => "Say",
            XivChatType.Yell => "Yell",
            XivChatType.Shout => "Shout",

            _ => channel.ToString(),
        };
    }
    public void RefreshUserInputState(string name, string world)
    {
        var user = configuration.ControllerUsers.FirstOrDefault(user => IsSameCharacter(user.Name, user.World, name, world));
        if (user == null) return;

        var key = GetUserKey(user);
        inputStates.TryGetValue(key, out var oldState);

        var newState = CreateInputStateFromUser(user);

        if (oldState != null)
        {
            newState.TitleTemp = oldState.TitleTemp;
            newState.TitleTempColor = oldState.TitleTempColor;
            newState.TitleTempGlow = oldState.TitleTempGlow;
            newState.TitleTempSourceJson = oldState.TitleTempSourceJson;
            newState.TempTitleSeconds = oldState.TempTitleSeconds;
        }

        inputStates[key] = newState;
    }
    private ControllerUserInputState CreateInputStateFromUser(Configuration.ControllerUserConfig user)
    {
        var state = new ControllerUserInputState();
        state.ZapCount = Math.Max(0, user.AutoZapCount);
        state.VibeCount = Math.Max(0, user.AutoVibeCount);
        state.MountCount = Math.Max(0, user.MountQuotaActions);
        state.TeleportCount = Math.Max(0, user.TeleportQuotaActions);
        state.JobCount = Math.Max(0, user.JobQuotaActions);
        state.RouletteMinutes = Math.Max(1, (int)Math.Round(user.JobRouletteInterval.TotalMinutes));
        state.Title = user.RemoteTitle ?? string.Empty;
        state.TitleTemp = string.Empty;
        state.MountWindow = user.MountQuotaWindow == Configuration.QuotaWindow.Day ? 1 : 0;
        state.TeleportWindow = user.TeleportQuotaWindow == Configuration.QuotaWindow.Day ? 1 : 0;
        state.JobWindow = user.JobQuotaWindow == Configuration.QuotaWindow.Day ? 1 : 0;
        state.Title = string.IsNullOrWhiteSpace(user.HonorificTitle.Title) ? GetHonorificJsonTitle(user.HonorificTitle.Json) : user.HonorificTitle.Title;
        state.TitleColor = user.HonorificTitle.Color;
        state.TitleGlow = user.HonorificTitle.Glow;
        return state;
    }
    private static bool IsSameCharacter(string leftName, string leftWorld, string rightName, string rightWorld)
    {
        return string.Equals(leftName?.Trim(), rightName?.Trim(), StringComparison.OrdinalIgnoreCase) && string.Equals(leftWorld?.Trim(), rightWorld?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
    private static string FormatJobRouletteStatus(Configuration.ControllerUserConfig user)
    {
        var whitelistText = user.JobRouletteWhitelistedGearsetCount < 0 ? "unknown gearsets" : $"{user.JobRouletteWhitelistedGearsetCount} whitelisted gearsets";

        if (!user.JobRouletteEnabled)
            return $"Disabled, {whitelistText}";

        return $"Enabled, interval {FormatCompactTimeSpan(user.JobRouletteInterval)}, next {FormatLocalTime(user.NextScheduledJobSwitchUtc)}, {whitelistText}";
    }

    private static string FormatCompactTimeSpan(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;

        if (value.TotalDays >= 1)
            return $"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m";

        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}h {value.Minutes}m";

        if (value.TotalMinutes >= 1)
            return $"{(int)value.TotalMinutes}m";

        return $"{Math.Max(0, value.Seconds)}s";
    }

    private static string FormatLocalTime(DateTime utc)
    {
        if (utc == DateTime.MinValue)
            return "not scheduled";

        if (utc.Kind == DateTimeKind.Unspecified)
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        return utc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
    }












    private static string BuildHonorificJson(string sourceJson, string title, Vector3 color, Vector3 glow)
    {
        title = title.Trim();

        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        if (title.Length > API.HonorificApi.MaxTitleLength)
            title = title[..API.HonorificApi.MaxTitleLength];

        JObject token;

        try
        {
            token = string.IsNullOrWhiteSpace(sourceJson) ? new JObject() : JObject.Parse(sourceJson);
        }
        catch
        {
            token = new JObject();
        }

        token["Title"] = title;
        token["IsPrefix"] ??= false;
        token["Color"] = CreateVectorToken(color);
        token["Glow"] = CreateVectorToken(glow);

        return token.ToString(Newtonsoft.Json.Formatting.None);
    }
    private static JObject CreateVectorToken(Vector3 value)
    {
        return new JObject { ["X"] = value.X, ["Y"] = value.Y, ["Z"] = value.Z };
    }
    private static string GetHonorificJsonTitle(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;

        try
        {
            var token = JObject.Parse(json);
            return token["Title"]?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
    private static Vector3 ReadHonorificVector(string json, string property, Vector3 fallback)
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;

        try
        {
            var value = JObject.Parse(json)[property];
            if (value == null) return fallback;
            return new Vector3(value["X"]?.Value<float>() ?? fallback.X, value["Y"]?.Value<float>() ?? fallback.Y, value["Z"]?.Value<float>() ?? fallback.Z);
        }
        catch
        {
            return fallback;
        }
    }
    public void SetTempHonorificInputState(string name, string world, string json)
    {
        var user = configuration.ControllerUsers.FirstOrDefault(user => IsSameCharacter(user.Name, user.World, name, world));
        if (user == null) return;
        var state = GetInputState(user);
        state.TitleTemp = GetHonorificJsonTitle(json);
        state.TitleTempColor = ReadHonorificVector(json, "Color", new Vector3(1f, 1f, 1f));
        state.TitleTempGlow = ReadHonorificVector(json, "Glow", new Vector3(0f, 0f, 0f));
        state.TitleTempSourceJson = json ?? string.Empty;
    }






}
