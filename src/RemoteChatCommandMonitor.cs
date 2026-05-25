using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons;
using Lumina.Data.Parsing.Layer;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using static SayusGagExtender.Configuration;

namespace SayusGagExtender;

public sealed class RemoteChatCommandMonitor : IDisposable
{
    private readonly Plugin plugin;
    private bool disposed;

    private readonly SemaphoreSlim tellSendLock = new(1, 1);

    private const int RemoteHonorificPriority = 500;
    private const int RemoteHonorificTempPriority = 600;

    private bool hasSubmittedRemoteTitleRequest;
    private string lastSubmittedRemoteTitleJson = string.Empty;

    public bool IsActive => plugin.Configuration.RemoteChatCommandsEnabled;

    private enum RemoteStatusType
    {
        Zap,
        Vibe,
        Mount,
        Teleport,
        Job,
        Title,
    }
    public RemoteChatCommandMonitor(Plugin plugin)
    {
        this.plugin = plugin;

        Plugin.ChatGui.ChatMessage += OnChatMessage;

        ApplySavedRemotePermanentTitle();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        Plugin.ChatGui.ChatMessage -= OnChatMessage;
    }

    public void HandleSgeCommand(string args, XivChatType type, string senderName, string senderWorld)
    {
        if (args.StartsWith("help", StringComparison.OrdinalIgnoreCase))
        {
            _ = SendTellLinesAsync(senderName, senderWorld,
            [
                "commands: sge help → This",
                "commands: sge status → Display all status/settings",
                "commands: sge autozap [always/distant/offline] → Toggles when autozap feature is active",
                "commands: sge zapcount [count] → Amount of automated zaps per hour",
                "commands: sge autovibe [always/distant/offline] → Toggles when autovibe feature is active",
                "commands: sge vibecount [count] → Amount of automated vibrations per hour",
                "commands: sge mountlimit [day/hour] [count] → How many times a mount can be used per day/hour, or: sge mountlimit unlimited",
                "commands: sge teleportlimit [day/hour] [count] → How many times a teleport can be used per day/hour, or: sge teleportlimit unlimited",
                "commands: sge joblimit [day/hour] [count] → How many times jobs can be changed per day/hour, or: sge joblimit unlimited",
                "commands: sge settitle [title] → Sets permanent Honorific title",
                "commands: sge settemptitle [seconds] [title] → Sets temporary Honorific title",
                "commands: sge cleartitle → Clears the permanent remote Honorific title",
            ]);

            return;
        }


        if (args.StartsWith("status", StringComparison.OrdinalIgnoreCase))
        {
            _ = SendTellLinesAsync(senderName, senderWorld, [
                Status(RemoteStatusType.Zap),
                Status(RemoteStatusType.Vibe),
                Status(RemoteStatusType.Mount),
                Status(RemoteStatusType.Teleport),
                Status(RemoteStatusType.Job),
                Status(RemoteStatusType.Title),
            ]);

            return;
        }

        string[] arguments = args.Split(' ');

        if (arguments[0].Equals("autozap", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 2)
        {
            if (arguments[1].Equals("always", StringComparison.OrdinalIgnoreCase))
            {
                plugin.Configuration.AutoZapWhen = RandomZapSender.OperateWhen.Always;
                plugin.Configuration.AutoZapEnabled = true;
                plugin.Configuration.Save();
                _ = SendTellLinesAsync(senderName, senderWorld,
                [
                    $"Autozap will always run, regardless of where you are. <me> can never change this.",
                    Status(RemoteStatusType.Zap),
                ]);
                return;
            }
            if (arguments[1].Equals("distant", StringComparison.OrdinalIgnoreCase))
            {
                plugin.Configuration.AutoZapWhen = RandomZapSender.OperateWhen.Distant;
                plugin.Configuration.AutoZapEnabled = true;
                plugin.Configuration.Save();
                _ = SendTellLinesAsync(senderName, senderWorld,
                [
                    $"Autozap will only run when you are out of range. <me> can never change this.",
                    Status(RemoteStatusType.Zap),
                ]);
                return;
            }
            if (arguments[1].Equals("offline", StringComparison.OrdinalIgnoreCase))
            {
                plugin.Configuration.AutoZapWhen = RandomZapSender.OperateWhen.Offline;
                plugin.Configuration.AutoZapEnabled = true;
                plugin.Configuration.Save();
                _ = SendTellLinesAsync(senderName, senderWorld,
                [
                    $"Autozap will only run when you are offline. <me> can never change this.",
                    Status(RemoteStatusType.Zap),
                ]);
                return;
            }


            _ = SendTellLinesAsync(senderName, senderWorld,
            [
                "cannot recognize command, set when Autozap feature will be active with: sge autovibe [always/distant/offline]",
                Status(RemoteStatusType.Zap),
            ]);

            return;
        }
        if (arguments[0].Equals("autovibe", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 2)
        {
            if (arguments[1].Equals("always", StringComparison.OrdinalIgnoreCase))
            {
                plugin.Configuration.AutoVibeWhen = RandomVibeSender.OperateWhen.Always;
                plugin.Configuration.AutoVibeEnabled = true;
                plugin.Configuration.Save();
                _ = SendTellLinesAsync(senderName, senderWorld,
                [
                    $"Autovibe will always run, regardless of where you are. <me> can never change this.",
                    Status(RemoteStatusType.Vibe),
                ]);
                return;
            }
            if (arguments[1].Equals("distant", StringComparison.OrdinalIgnoreCase))
            {
                plugin.Configuration.AutoVibeWhen = RandomVibeSender.OperateWhen.Distant;
                plugin.Configuration.AutoVibeEnabled = true;
                plugin.Configuration.Save();
                _ = SendTellLinesAsync(senderName, senderWorld,
                [
                    $"Autovibe will only run when you are out of range. <me> can never change this.",
                    Status(RemoteStatusType.Zap),
                ]);
                return;
            }
            if (arguments[1].Equals("offline", StringComparison.OrdinalIgnoreCase))
            {
                plugin.Configuration.AutoVibeWhen = RandomVibeSender.OperateWhen.Offline;
                plugin.Configuration.AutoVibeEnabled = true;
                plugin.Configuration.Save();
                _ = SendTellLinesAsync(senderName, senderWorld,
                [
                    $"Autovibe will only run when you are offline. <me> can never change this.",
                    Status(RemoteStatusType.Zap),
                ]);
                return;
            }


            _ = SendTellLinesAsync(senderName, senderWorld,
            [
                "cannot recognize command, set when Autovibe feature will be active with: sge autovibe [always/distant/offline]",
                Status(RemoteStatusType.Zap),
            ]);

            return;
        }

        if (arguments[0].Equals("zapcount", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 2)
        {
            if (arguments[1].Equals("unlock", StringComparison.OrdinalIgnoreCase))
                arguments[1] = "-1";

            if (int.TryParse(arguments[1], out var count))
            {
                if (count == -1)
                {
                    plugin.Configuration.AutoZapCountControllerLocked = false;
                    plugin.Configuration.Save();
                    _ = SendTellLinesAsync(senderName, senderWorld,
                    [
                        //$"Zaps per hour settings released.",
                        Status(RemoteStatusType.Zap),
                    ]);
                    return;
                }
                if (count >= 0)
                {
                    plugin.Configuration.AutoZapEnabled = true;
                    plugin.Configuration.AutoZapCount = count;
                    plugin.Configuration.AutoZapCountControllerLocked = true;
                    plugin.Configuration.Save();
                    _ = SendTellLinesAsync(senderName, senderWorld,
                    [
                        //$"Zaps per hour changed to {plugin.Configuration.AutoZapCount}",
                        Status(RemoteStatusType.Zap),
                        $"<me> cannot change this or disable the feature unless you unlock the setting with: sge zapcount unlock",
                    ]);
                    return;
                }
            }
            _ = SendTellLinesAsync(senderName, senderWorld,
            [
                "cannot recognize command, to set desired zaps per hour, use: sge zapcount [count]",
                Status(RemoteStatusType.Zap),
            ]);

            return;
        }
        if (arguments[0].Equals("vibecount", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 2)
        {
            if (arguments[1].Equals("unlock", StringComparison.OrdinalIgnoreCase))
                arguments[1] = "-1";

            if (int.TryParse(arguments[1], out var count))
            {
                if (count == -1)
                {
                    plugin.Configuration.AutoVibeCountControllerLocked = false;
                    plugin.Configuration.Save();
                    _ = SendTellLinesAsync(senderName, senderWorld,
                    [
                        //$"Zaps per hour settings released.",
                        Status(RemoteStatusType.Vibe),
                    ]);
                    return;
                }
                if (count >= 0)
                {
                    plugin.Configuration.AutoVibeEnabled = true;
                    plugin.Configuration.AutoVibeCount = count;
                    plugin.Configuration.AutoVibeCountControllerLocked = true;
                    plugin.Configuration.Save();
                    _ = SendTellLinesAsync(senderName, senderWorld,
                    [
                        //$"Vibes per hour changed to {plugin.Configuration.AutoVibeCount}",
                        Status(RemoteStatusType.Vibe),
                        $"<me> cannot change this or disable the feature unless you unlock the setting with: sge vibecount unlock",
                    ]);
                    return;
                }
            }
            _ = SendTellLinesAsync(senderName, senderWorld,
            [
                "cannot recognize command, to set desired vibes per hour, use: sge vibecount [count]",
                Status(RemoteStatusType.Vibe),
            ]);

            return;
        }

        if (arguments[0].Equals("mountlimit", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 2)
        {
            if (arguments[1].Equals("unlimited", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("");
                arguments[2] = "-1";
            }

            if (arguments.Length >= 3)
            {
                bool validParameter = false;
                if (arguments[2].Equals("unlimited", StringComparison.OrdinalIgnoreCase))
                {
                    arguments[2] = "-1";
                }

                if (arguments[1].Equals("day", StringComparison.OrdinalIgnoreCase))
                {
                    plugin.Configuration.MountQuotaWindow = QuotaWindow.Day;
                    plugin.Configuration.Save();
                    validParameter = true;
                }
                if (arguments[1].Equals("hour", StringComparison.OrdinalIgnoreCase))
                {
                    plugin.Configuration.MountQuotaWindow = QuotaWindow.Hour;
                    plugin.Configuration.Save();
                    validParameter = true;
                }

                if (int.TryParse(arguments[2], out var count))
                {
                    plugin.Configuration.MountQuotaActions = count;
                    if (count >= 0)
                    {
                        plugin.Configuration.MountQuotaEnabled = true;
                    }
                    else
                    {
                        plugin.Configuration.MountQuotaActionLogUtc.Clear();
                        plugin.Configuration.MountQuotaEnabled = false;
                    }
                    
                    plugin.Configuration.Save();
                    validParameter = true;
                }

                _ = SendTellLinesAsync(senderName, senderWorld,
                [
                    Status(RemoteStatusType.Mount),
                    $"<me> can never change this. To remove limit, use: sge mountlimit unlimited",
                ]);

                if (validParameter) 
                    return;

            }
            
            _ = SendTellLinesAsync(senderName, senderWorld,
            [
                "cannot recognize command, to limit number of mount usage, use: sge mountlimit [day/hour] [count]]",
                Status(RemoteStatusType.Mount),
            ]);

            return;
        }
        if (arguments[0].Equals("teleportlimit", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 2)
        {
            if (arguments[1].Equals("unlimited", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("");
                arguments[2] = "-1";
            }

            if (arguments.Length >= 3)
            {
                bool validParameter = false;

                if (arguments[2].Equals("unlimited", StringComparison.OrdinalIgnoreCase))
                {
                    arguments[2] = "-1";
                }

                if (arguments[1].Equals("day", StringComparison.OrdinalIgnoreCase))
                {
                    plugin.Configuration.TeleportQuotaWindow = QuotaWindow.Day;
                    plugin.Configuration.Save();
                    validParameter = true;
                }

                if (arguments[1].Equals("hour", StringComparison.OrdinalIgnoreCase))
                {
                    plugin.Configuration.TeleportQuotaWindow = QuotaWindow.Hour;
                    plugin.Configuration.Save();
                    validParameter = true;
                }

                if (int.TryParse(arguments[2], out var count))
                {
                    plugin.Configuration.TeleportQuotaActions = count;

                    if (count >= 0)
                    {
                        plugin.Configuration.TeleportQuotaEnabled = true;
                    }
                    else
                    {
                        plugin.Configuration.TeleportQuotaActionLogUtc.Clear();
                        plugin.Configuration.TeleportQuotaEnabled = false;
                    }

                    plugin.Configuration.Save();
                    validParameter = true;
                }

                _ = SendTellLinesAsync(senderName, senderWorld,
                [
                    Status(RemoteStatusType.Teleport),
            $"<me> can never change this. To remove limit, use: sge teleportlimit unlimited",
        ]);

                if (validParameter)
                    return;
            }

            _ = SendTellLinesAsync(senderName, senderWorld,
            [
                "cannot recognize command, to limit number of teleport usage, use: sge teleportlimit [day/hour] [count]",
        Status(RemoteStatusType.Teleport),
    ]);

            return;
        }
        if (arguments[0].Equals("joblimit", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 2)
        {
            if (arguments[1].Equals("unlimited", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("");
                arguments[2] = "-1";
            }

            if (arguments.Length >= 3)
            {
                bool validParameter = false;

                if (arguments[2].Equals("unlimited", StringComparison.OrdinalIgnoreCase))
                {
                    arguments[2] = "-1";
                }

                if (arguments[1].Equals("day", StringComparison.OrdinalIgnoreCase))
                {
                    plugin.Configuration.JobSwitchQuotaWindow = QuotaWindow.Day;
                    plugin.Configuration.Save();
                    validParameter = true;
                }

                if (arguments[1].Equals("hour", StringComparison.OrdinalIgnoreCase))
                {
                    plugin.Configuration.JobSwitchQuotaWindow = QuotaWindow.Hour;
                    plugin.Configuration.Save();
                    validParameter = true;
                }

                if (int.TryParse(arguments[2], out var count))
                {
                    plugin.Configuration.JobSwitchQuotaActions = count;

                    if (count >= 0)
                    {
                        plugin.Configuration.JobSwitchQuotaEnabled = true;
                    }
                    else
                    {
                        plugin.Configuration.JobSwitchQuotaActionLogUtc.Clear();
                        plugin.Configuration.JobSwitchQuotaEnabled = false;
                    }

                    plugin.Configuration.Save();
                    validParameter = true;
                }

                _ = SendTellLinesAsync(senderName, senderWorld,
                [
                        Status(RemoteStatusType.Job),
                    $"<me> can never change this. To remove limit, use: sge joblimit unlimited",
                ]);

                if (validParameter)
                    return;
            }

            _ = SendTellLinesAsync(senderName, senderWorld,
            [
                "cannot recognize command, to limit number of job switches, use: sge joblimit [day/hour] [count]",
                Status(RemoteStatusType.Job),
            ]);

            return;
        }
        if (arguments[0].Equals("settitle", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 2)
        {
            var title = arguments[1].Trim();
            var json = BuildRemoteTitleJsonFromCurrent(title);

            if (string.IsNullOrWhiteSpace(json))
            {
                _ = SendTellLinesAsync(senderName, senderWorld, [
                    "failed to build Honorific title.",
                    ]);
                return;
            }
            ApplyRemotePermanentTitleJson(json);
            _ = SendTellLinesAsync(senderName, senderWorld, [
                Status(RemoteStatusType.Title), 
            ]);
            return;



            _ = SendTellLinesAsync(senderName, senderWorld,
                [
                    "cannot recognize command, to set Honorific title, use: sge settitle [title]",
                Status(RemoteStatusType.Zap),
            ]);

            return;
            
        }
        if (arguments[0].Equals("settemptitle", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 3)
        {

            if (!int.TryParse(arguments[1], out int seconds))
            {
                _ = SendTellLinesAsync(senderName, senderWorld, [
                    "seconds not recognized, use: sge settemptitle [seconds] [title]",
                    ]);
                return;
            }
            var title = arguments[2].Trim();
            var json = BuildRemoteTitleJsonFromCurrent(title);

            if (string.IsNullOrWhiteSpace(json))
            {
                _ = SendTellLinesAsync(senderName, senderWorld, [
                    "failed to build temporary Honorific title.",
                    ]);
                return;
            }
            plugin.HonorificManager.SetTitle(json, TimeSpan.FromSeconds(seconds), RemoteHonorificTempPriority, this);
            _ = SendTellLinesAsync(senderName, senderWorld, [
                $"Temporary Honorific title set to: {title} for {seconds}s, temp title cannot be cancelled or removed before timer expires."
                //, Status(RemoteStatusType.Title), 
            ]);
            return;


            _ = SendTellLinesAsync(senderName, senderWorld,
                [
                    "cannot recognize command, to set temporary title, use: sge settemptitle [seconds] [title]",
                Status(RemoteStatusType.Zap),
            ]);

            return;
        }
        if (arguments[0].Equals("cleartitle", StringComparison.OrdinalIgnoreCase))
        {
            RecallRemotePermanentTitleRequest();
            _ = SendTellLinesAsync(senderName, senderWorld, [
                Status(RemoteStatusType.Title),
            ]); 
            return;
        }


        _ = SendTellLinesAsync(senderName, senderWorld,
            [
                "cannot recognize command, to display all commands, use: sge help",
        ]);
    }

    private string Status(RemoteStatusType type)
    {
        return type switch
        {
            RemoteStatusType.Zap =>
                $"status: sge zapcount [count] → {plugin.Configuration.AutoZapCount} [{(plugin.Configuration.AutoZapEnabled ? "Enabled" : "Disabled")}] [When:{plugin.Configuration.AutoZapWhen.ToString()}] {(plugin.Configuration.AutoZapCountControllerLocked ? "[Locked]" : "")}",

            RemoteStatusType.Vibe =>
                $"status: sge vibecount [count] → {plugin.Configuration.AutoVibeCount} [{(plugin.Configuration.AutoVibeEnabled ? "Enabled" : "Disabled")}] [When:{plugin.Configuration.AutoVibeWhen.ToString()}] {(plugin.Configuration.AutoVibeCountControllerLocked ? "[Locked]" : "")}",

            RemoteStatusType.Mount =>
                $"status: sge mountlimit [day/hour] [count] → {(plugin.Configuration.MountQuotaActions < 0 ? "unlimited" : plugin.Configuration.MountQuotaActions)} per {plugin.Configuration.MountQuotaWindow.ToString()} [Locked]",

            RemoteStatusType.Teleport =>
                $"status: sge teleportlimit [day/hour] [count] → {(plugin.Configuration.TeleportQuotaActions < 0 ? "unlimited" : plugin.Configuration.TeleportQuotaActions)} per {plugin.Configuration.TeleportQuotaWindow.ToString()} [Locked]",

            RemoteStatusType.Job =>
                $"status: sge joblimit [day/hour] [count] → {(plugin.Configuration.JobSwitchQuotaActions < 0 ? "unlimited" : plugin.Configuration.JobSwitchQuotaActions)} per {plugin.Configuration.JobSwitchQuotaWindow.ToString()} [Locked]",
            RemoteStatusType.Title =>
                $"status: sge settitle [title] → {GetRemotePermanentTitleDisplay()} [Priority:{RemoteHonorificPriority}] [Locked]",

            _ => "status: unknown",
        };
    }

    private void OnChatMessage(IHandleableChatMessage context)
    {
        XivChatType type = context.LogKind;
        var sender = context.Sender;
        var message = context.Message;
        try
        {
            HandleChatMessage(type, sender, message);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Remote chat command monitor failed.");
        }
    }

    //DateTime timeout = DateTime.Now;
    private void HandleChatMessage(XivChatType type, SeString sender, SeString message)
    {
        
        //if (timeout > DateTime.Now)
        //    return;
        //timeout = DateTime.Now + TimeSpan.FromSeconds(1);

        //Plugin.Log.Info($"HandleChatMessage {type} {sender} {message}");

        if (!plugin.Configuration.RemoteChatCommandsEnabled)
            return;

        if (type == XivChatType.None)
            return;

        if (!plugin.Configuration.RemoteAcceptedChannels.Contains(type))
            return;

        var messageText = message.TextValue?.Trim();
        if (string.IsNullOrWhiteSpace(messageText))
            return;

        var prefix = plugin.Configuration.RemoteChatCommandPrefix;
        if (string.IsNullOrWhiteSpace(prefix))
            prefix = "sge";

        if (!TryStripPrefix(messageText, prefix, out var args))
            return;

        if (!TryGetSenderCharacter(sender, out var senderName, out var senderWorld))
            return;

        if (!IsAllowedSender(senderName, senderWorld))
            return;

        if (string.IsNullOrWhiteSpace(args))
        {
            Plugin.ChatGui.PrintError("Remote SGE command ignored: missing command.");
            return;
        }

        Plugin.Log.Information($"Remote SGE command from {senderName}@{senderWorld}: {args}");

        HandleSgeCommand(args, type, senderName, senderWorld);
    }

    private bool IsAllowedSender(string senderName, string senderWorld)
    {
        return string.Equals(
                   senderName,
                   plugin.Configuration.RemoteControllerName,
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   senderWorld,
                   plugin.Configuration.RemoteControllerWorld,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryStripPrefix(
        string message,
        string prefix,
        out string args)
    {
        args = string.Empty;

        message = message.Trim();
        prefix = prefix.Trim();

        if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        // Require either exact "sge" or "sge ..."
        if (message.Length > prefix.Length &&
            !char.IsWhiteSpace(message[prefix.Length]))
        {
            return false;
        }

        args = message[prefix.Length..].Trim();
        return true;
    }

    private bool TryGetSenderCharacter(
        SeString sender,
        out string name,
        out string world)
    {
        name = string.Empty;
        world = string.Empty;

        foreach (var payload in sender.Payloads)
        {
            if (payload is not PlayerPayload player)
                continue;

            name = player.PlayerName;
            world = plugin.Utils.WorldRowIDToString(player.World.RowId);
            Plugin.Log.Information($"TryGetSenderCharacter {name}@{world}");
            return !string.IsNullOrWhiteSpace(name)
                   && !string.IsNullOrWhiteSpace(world)
                   && world != "Unknown";
        }

        // Fallback for channels/sender formats that do not expose PlayerPayload.
        return TryParseSenderText(sender.TextValue, out name, out world);
    }

    private static bool TryParseSenderText(
        string? senderText,
        out string name,
        out string world)
    {
        name = string.Empty;
        world = string.Empty;

        if (string.IsNullOrWhiteSpace(senderText))
            return false;

        senderText = senderText.Trim();

        // Common format: Name@World
        var atIndex = senderText.LastIndexOf('@');
        if (atIndex > 0 && atIndex < senderText.Length - 1)
        {
            name = senderText[..atIndex].Trim();
            world = senderText[(atIndex + 1)..].Trim();
            return true;
        }

        // Common fallback format: First Last World
        var parts = senderText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3)
        {
            world = parts[^1];
            name = string.Join(' ', parts[..^1]);
            return true;
        }

        return false;
    }
    private async Task SendTellLinesAsync(
    string senderName,
    string senderWorld,
    IEnumerable<string> lines,
    int delayMs = 1500)
    {
        await tellSendLock.WaitAsync();

        try
        {
            foreach (var line in lines)
            {
                if (disposed)
                    return;

                var safeLine = line.Trim();

                if (string.IsNullOrWhiteSpace(safeLine))
                    continue;

                await Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    if (disposed)
                        return;

                    plugin.Utils.ExecuteNativeCommand($"/t {senderName}@{senderWorld} {safeLine}");
                });

                await Task.Delay(delayMs);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to send remote command tell response.");
        }
        finally
        {
            tellSendLock.Release();
        }
    }
    private void ApplySavedRemotePermanentTitle()
    {
        var json = plugin.Configuration.RemotePermanentHonorificTitleJson;

        if (string.IsNullOrWhiteSpace(json))
            return;

        _ = Plugin.Framework.RunOnFrameworkThread(() =>
        {
            if (disposed)
                return;

            ApplyRemotePermanentTitleJson(json);
        });
    }

    private void ApplyRemotePermanentTitleJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            RecallRemotePermanentTitleRequest();
            return;
        }

        _ = Plugin.Framework.RunOnFrameworkThread(() =>
        {
            if (disposed)
                return;

            if (hasSubmittedRemoteTitleRequest &&
                string.Equals(lastSubmittedRemoteTitleJson, json, StringComparison.Ordinal))
            {
                return;
            }

            plugin.HonorificManager.SetTitle(
                json,
                RemoteHonorificPriority,
                this);

            hasSubmittedRemoteTitleRequest = true;
            lastSubmittedRemoteTitleJson = json;
        });
    }

    private void RecallRemotePermanentTitleRequest()
    {
        _ = Plugin.Framework.RunOnFrameworkThread(() =>
        {
            plugin.HonorificManager.RecallTitle(this);

            hasSubmittedRemoteTitleRequest = false;
            lastSubmittedRemoteTitleJson = string.Empty;
        });
    }

    private string BuildRemoteTitleJsonFromCurrent(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        title = title.Trim();

        if (title.Length > API.HonorificApi.MaxTitleLength)
            title = title[..API.HonorificApi.MaxTitleLength];

        var currentJson = string.Empty;

        try
        {
            if (plugin.HonorificApi.IsAvailable() &&
                Plugin.ObjectTable.LocalPlayer != null &&
                !Plugin.Condition.Any(
                    ConditionFlag.BetweenAreas,
                    ConditionFlag.BetweenAreas51,
                    ConditionFlag.WatchingCutscene,
                    ConditionFlag.WatchingCutscene78,
                    ConditionFlag.OccupiedInCutSceneEvent))
            {
                currentJson = plugin.HonorificApi.GetLocalTitleJson();
            }
        }
        catch
        {
            currentJson = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(currentJson))
        {
            try
            {
                var token = JObject.Parse(currentJson);
                token["Title"] = title;
                return token.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch
            {
                // Fall through to blank/default JSON.
            }
        }

        return plugin.HonorificManager.BuildTitleJson(
            title,
            new Vector3(1f, 1f, 1f),
            new Vector3(0f, 0f, 0f));
    }

    private string GetRemotePermanentTitleDisplay()
    {
        var json = plugin.Configuration.RemotePermanentHonorificTitleJson;

        if (string.IsNullOrWhiteSpace(json))
            return "none";
        
        try
        {
            var token = JObject.Parse(json);
            var title = token["Title"]?.ToString();

            return string.IsNullOrWhiteSpace(title)
                ? "set"
                : title;
        }
        catch
        {
            return "set";
        }
    }
}
