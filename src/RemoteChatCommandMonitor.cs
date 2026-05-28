using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Data.Parsing.Layer;
using Newtonsoft.Json.Linq;
using SayusGagExtender.Windows;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static FFXIVClientStructs.FFXIV.Client.Game.StatusManager.Delegates;
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

    private const byte RemotePackageTypeStatus = 1;
    private const int RemoteStatusPackageVersion = 1;
    private const byte RemotePackageTypeSetPermanentTitle = 2;
    private const byte RemotePackageTypeSetTemporaryTitle = 3;
    private const byte RemotePackageTypeCloneCurrentTitleRequest = 4;
    private const byte RemotePackageTypeCloneCurrentTitleResponse = 5;
    private const byte RemotePackageTypeCloneCurrentTempTitleRequest = 6;
    private const byte RemotePackageTypeCloneCurrentTempTitleResponse = 7;
    private const byte RemotePackageTypePuppeteerAliasesRequest = 8;
    private const byte RemotePackageTypePuppeteerAliasesResponse = 9;
    private const int RemoteTitlePackageVersion = 1;
    private const int RemotePuppeteerAliasesPackageVersion = 1;
    private readonly RemotePackageTransport packageTransport = new();


    public bool IsActive => plugin.Configuration.RemoteChatCommandsEnabled;
    private enum RemoteCommandPrefixMode
    {
        VisibleOrHidden,
        HiddenOnly,
    }
    private enum RemoteStatusType
    {
        None,
        Zap,
        Vibe,
        Mount,
        Teleport,
        Job,
        Roulette,
        Title,
    }

    public unsafe RemoteChatCommandMonitor(Plugin plugin)
    {
        this.plugin = plugin;

        Plugin.ChatGui.ChatMessage += OnChatMessage;

        try
        {
            printMessageHook = Plugin.GameInterop.HookFromAddress<PrintMessageDelegate>(
                RaptureLogModule.Addresses.PrintMessage.Value, PrintMessageDetour);

            printMessageHook.Enable();

            Plugin.Log.Information("RemoteChatCommandMonitor PrintMessage detour enabled.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to enable RemoteChatCommandMonitor PrintMessage detour.");
        }

        ApplySavedRemotePermanentTitle();
    }
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        Plugin.ChatGui.ChatMessage -= OnChatMessage;

        try
        {
            printMessageHook?.Disable();
            printMessageHook?.Dispose();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to dispose RemoteChatCommandMonitor PrintMessage detour.");
        }
    }

    private void ReturnStatusUpdate(string senderName, string senderWorld, RemoteStatusType? type = null, string[]? messages = null, bool hidden = false, string prefix = "")
    {
        if (hidden)
        {
            _ = SendTellPackageAsync(senderName, senderWorld, BuildRemoteStatusPackage(), prefix);
            return;
        }

        string[] msqOutput = [];

        if (type == null || type == RemoteStatusType.Zap)
        {
            msqOutput.Add($"status: sge zapcount [count] → {plugin.Configuration.AutoZapCount} [{(plugin.Configuration.AutoZapEnabled ? "Enabled" : "Disabled")}] [When:{plugin.Configuration.AutoZapWhen.ToString()}] {(plugin.Configuration.AutoZapCountControllerLocked ? "[Locked]" : "")}");
        }
        if (type == null || type == RemoteStatusType.Vibe)
        {
            msqOutput.Add($"status: sge vibecount [count] → {plugin.Configuration.AutoVibeCount} [{(plugin.Configuration.AutoVibeEnabled ? "Enabled" : "Disabled")}] [When:{plugin.Configuration.AutoVibeWhen.ToString()}] {(plugin.Configuration.AutoVibeCountControllerLocked ? "[Locked]" : "")}");
        }
        if (type == null || type == RemoteStatusType.Mount)
        {
            msqOutput.Add($"status: sge mountlimit [day/hour] [count] → {(plugin.Configuration.MountQuotaActions < 0 ? "unlimited" : plugin.Configuration.MountQuotaActions)} per {plugin.Configuration.MountQuotaWindow.ToString()} [Locked]");
        }
        if (type == null || type == RemoteStatusType.Teleport)
        {
            msqOutput.Add($"status: sge teleportlimit [day/hour] [count] → {(plugin.Configuration.TeleportQuotaActions < 0 ? "unlimited" : plugin.Configuration.TeleportQuotaActions)} per {plugin.Configuration.TeleportQuotaWindow.ToString()} [Locked]");
        }
        if (type == null || type == RemoteStatusType.Job)
        {
            msqOutput.Add($"status: sge joblimit [day/hour] [count] → {(plugin.Configuration.JobSwitchQuotaActions < 0 ? "unlimited" : plugin.Configuration.JobSwitchQuotaActions)} per {plugin.Configuration.JobSwitchQuotaWindow.ToString()} [Locked]");
        }
        if (type == null || type == RemoteStatusType.Roulette)
        {
            msqOutput.Add(BuildRouletteStatus());
        }
        if (type == null || type == RemoteStatusType.Mount)
        {
            msqOutput.Add($"status: sge settitle [title] → {GetRemotePermanentTitleDisplay()} [Priority:{RemoteHonorificPriority}] [Locked]");
        }
        if (messages != null)
        {
            msqOutput.Add(messages);
        }


        _ = SendTellLinesAsync(senderName, senderWorld, msqOutput);
        return;
    }
    private string BuildRouletteStatus()
    {
        var whitelistCount = plugin.Configuration.JobRouletteWhitelistedGearsets?.Count ?? 0;
        var enabled = plugin.Configuration.JobRouletteEnabled;
        var locked = plugin.Configuration.JobRouletteRemoteSet;

        var parts = new List<string>
        {
            $"{whitelistCount} gearsets",
        };

        if (enabled)
        {
            parts.Add($"interval {FormatTimeSpan(plugin.Configuration.JobRouletteInterval)}");
            parts.Add($"next {FormatTimeUntil(plugin.Configuration.NextScheduledJobSwitch)}");
            parts.Add("[Enabled]");
        }
        else
        {
            parts.Add("[Disabled]");
        }

        if (locked)
            parts.Add("[Locked]");

        return $"status: sge jobroulette [minutes] / stoproulette → {string.Join(", ", parts)}";
    }
    private static string FormatTimeUntil(DateTime utc)
    {
        if (utc == DateTime.MinValue)
            return "not scheduled";

        return FormatTimeSpan(utc - DateTime.UtcNow);
    }
    private static string FormatTimeSpan(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;

        value = TimeSpan.FromSeconds(Math.Ceiling(value.TotalSeconds));

        if (value.TotalDays >= 1)
            return $"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m {value.Seconds}s";

        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}h {value.Minutes}m {value.Seconds}s";

        if (value.TotalMinutes >= 1)
            return $"{(int)value.TotalMinutes}m {value.Seconds}s";

        return $"{value.Seconds}s";
    }
    private void OnChatMessage(IHandleableChatMessage context)
    {
        try
        {
            if (!TryGetRemoteCommand(context.LogKind,context.Sender,context.Message,RemoteCommandPrefixMode.VisibleOrHidden, out var command))
            {
                return;
            }

            context.PreventOriginal();

            HandleParsedRemoteCommand(command, isHidden: false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Remote chat command monitor failed.");
        }
    }
    private unsafe delegate uint PrintMessageDelegate(RaptureLogModule* manager, XivChatType chatType, Utf8String* sender, Utf8String* message, int timestamp, byte silent);
    private readonly Hook<PrintMessageDelegate>? printMessageHook;
    private unsafe uint PrintMessageDetour(RaptureLogModule* manager, XivChatType chatType, Utf8String* sender, Utf8String* message, int timestamp, byte silent)
    {
        try
        {
            if (sender != null && message != null)
            {
                var senderSeString = SeString.Parse(sender->AsSpan());
                var messageSeString = SeString.Parse(message->AsSpan());

                if (ShouldHideOwnHiddenRemoteCommandDisplay(chatType, messageSeString))
                    return 0;

                if (TryGetRemoteCommand(chatType, senderSeString, messageSeString, RemoteCommandPrefixMode.HiddenOnly, out var command))
                {
                    HandleParsedRemoteCommand(command, isHidden: true);
                    return 0;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Remote hidden chat command detour failed.");
        }

        return printMessageHook!.Original(manager, chatType, sender, message, timestamp, silent);
    }
    private bool ShouldHideOwnHiddenRemoteCommandDisplay(XivChatType type, SeString message)
    {
        if (type != XivChatType.TellOutgoing)
            return false;

        var messageText = message.TextValue?.Trim();

        if (string.IsNullOrWhiteSpace(messageText))
            return false;

        var prefix = plugin.Configuration.RemoteChatCommandPrefix;

        if (string.IsNullOrWhiteSpace(prefix))
            prefix = "sge";

        return TryStripRemoteCommandPrefix(messageText, prefix, RemoteCommandPrefixMode.HiddenOnly, out _);
    }
    private bool TryGetRemoteCommand(XivChatType type, SeString sender, SeString message, RemoteCommandPrefixMode prefixMode, out ParsedRemoteCommand command)
    {
        command = default;
        if (!plugin.Configuration.RemoteChatCommandsEnabled) return false;
        if (type == XivChatType.None) return false;
        if (!plugin.Configuration.RemoteAcceptedChannels.Contains(type)) return false;
        var messageText = message.TextValue?.Trim();
        if (string.IsNullOrWhiteSpace(messageText)) return false;
        var prefix = plugin.Configuration.RemoteChatCommandPrefix;
        if (string.IsNullOrWhiteSpace(prefix)) prefix = "sge";
        if (!TryStripRemoteCommandPrefix(messageText, prefix, prefixMode, out var args)) return false;
        if (!TryGetSenderCharacter(sender, out var senderName, out var senderWorld)) return false;
        if (!IsAllowedSender(senderName, senderWorld) && !IsControlledUser(senderName, senderWorld)) return false;
        command = new ParsedRemoteCommand(type, senderName, senderWorld, args);
        return true;
    }
    private static bool TryStripRemoteCommandPrefix(string message,string prefix,RemoteCommandPrefixMode mode,out string args)
    {
        args = string.Empty;

        message = message.Trim();
        prefix = prefix.Trim();

        if (string.IsNullOrWhiteSpace(prefix))
            return false;

        if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var afterPrefix = message[prefix.Length..];

        // Accept: "sge::command"
        if (afterPrefix.StartsWith("::", StringComparison.Ordinal))
        {
            args = afterPrefix[2..].Trim();
            return true;
        }

        if (mode == RemoteCommandPrefixMode.HiddenOnly)
            return false;

        // Accept: exact "sge" or "sge command"
        if (afterPrefix.Length == 0 || char.IsWhiteSpace(afterPrefix[0]))
        {
            args = afterPrefix.Trim();
            return true;
        }

        // Reject: "sgefoo"
        return false;
    }
    private readonly record struct ParsedRemoteCommand(XivChatType Type,string SenderName,string SenderWorld,string Args);
    private void HandleParsedRemoteCommand(ParsedRemoteCommand command, bool isHidden)
    {
        if (string.IsNullOrWhiteSpace(command.Args))
        {
            Plugin.ChatGui.PrintError(isHidden ? "Remote SGE hidden command ignored: missing command." : "Remote SGE command ignored: missing command.");

            return;
        }

        Plugin.Log.Information($"{(isHidden ? "Hidden remote" : "Remote")} SGE command from " + $"{command.SenderName}@{command.SenderWorld}: {command.Args}");
        //Plugin.ChatGui.Print($"{(isHidden ? "Hidden remote" : "Remote")} SGE command from " + $"{command.SenderName}@{command.SenderWorld}: {command.Args}");

        HandleSgeCommand(command.Args, command.Type, command.SenderName, command.SenderWorld, isHidden);
    }
    private void HandleSgeCommand(string args, XivChatType type, string senderName, string senderWorld, bool isHidden)
    {
        if (packageTransport.ClearOldPendingPackages() > 0 && IsControlledUser(senderName, senderWorld))
        {
            plugin.NotifyControllerRemotePackageFailed(senderName, senderWorld, "Package timed out before all chunks arrived.");
        }

        var receiveResult = packageTransport.TryReceive(args, out var receivedPackage, out var packageProgress);
        if (receiveResult == RemotePackageTransport.ReceiveResult.Pending)
        {
            if (IsControlledUser(senderName, senderWorld)) plugin.NotifyControllerRemotePackageProgress(senderName, senderWorld, packageProgress.Part, packageProgress.Total);
            return;
        }
        if (receiveResult == RemotePackageTransport.ReceiveResult.Invalid)
        {
            if (IsControlledUser(senderName, senderWorld)) plugin.NotifyControllerRemotePackageFailed(senderName, senderWorld, "Package failed to decode.");
            return;
        }
        if (receiveResult == RemotePackageTransport.ReceiveResult.Complete)
        {
            if (packageProgress.IsChunked && IsControlledUser(senderName, senderWorld)) plugin.NotifyControllerRemotePackageCompleted(senderName, senderWorld, "Package received.");
            HandleRemotePackage(receivedPackage, type, senderName, senderWorld);
            return;
        }

        if (!IsAllowedSender(senderName, senderWorld))
        {
            Plugin.Log.Information($"Ignoring commands from non-controller message from {senderName}@{senderWorld}: {args}");
            return;
        }

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
                "commands: sge jobroulette [minutes] → Enable job roulette with required interval in minutes",
                "commands: sge stopjobroulette → Stop job roulette and unlock local roulette settings",
                "commands: sge settitle [title] → Sets permanent Honorific title",
                "commands: sge settemptitle [seconds] [title] → Sets temporary Honorific title",
                "commands: sge cleartitle → Clears the permanent remote Honorific title",
            ]);

            return;
        }

        var prefix = plugin.Configuration.RemoteChatCommandPrefix;
        if (string.IsNullOrWhiteSpace(prefix))
            prefix = "sge";

        if (args.StartsWith("statuspack", StringComparison.OrdinalIgnoreCase))
        {
            ReturnStatusUpdate(senderName, senderWorld, hidden:true, prefix: prefix);
            return;
        }

        if (args.StartsWith("status", StringComparison.OrdinalIgnoreCase))
        {
            ReturnStatusUpdate(senderName, senderWorld, hidden: false);
            return;
        }
        

        string[] arguments = args.Split(' ');
        string[] msq = [];
        // Convenience: "sge jobroulette 0" means stop roulette.
        // Keep the actual stop handling in the normal stopjobroulette branch below.
        if (arguments.Length >= 2 &&
            arguments[0].Equals("jobroulette", StringComparison.OrdinalIgnoreCase) &&
            arguments[1].Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            arguments[0] = "stopjobroulette";
        }


        if (arguments[0].Equals("autozap", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 2)
        {
            if (arguments[1].Equals("always", StringComparison.OrdinalIgnoreCase))
            {
                plugin.Configuration.AutoZapWhen = RandomZapSender.OperateWhen.Always;
                plugin.Configuration.AutoZapEnabled = true;
                plugin.Configuration.Save();
                msq = [$"Autozap will always run, regardless of where you are. <me> can never change this."];
                ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Zap, msq, hidden: isHidden, prefix: prefix);
                return;
            }
            if (arguments[1].Equals("distant", StringComparison.OrdinalIgnoreCase))
            {
                plugin.Configuration.AutoZapWhen = RandomZapSender.OperateWhen.Distant;
                plugin.Configuration.AutoZapEnabled = true;
                plugin.Configuration.Save();
                msq = [$"Autozap will only run when you are out of range. <me> can never change this."];
                ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Zap, msq, hidden: isHidden, prefix: prefix);
                return;
            }
            if (arguments[1].Equals("offline", StringComparison.OrdinalIgnoreCase))
            {
                plugin.Configuration.AutoZapWhen = RandomZapSender.OperateWhen.Offline;
                plugin.Configuration.AutoZapEnabled = true;
                plugin.Configuration.Save();
                msq = [$"Autozap will only run when you are offline. <me> can never change this."];
                ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Zap, msq, hidden: isHidden, prefix: prefix);
                return;
            }

            msq = [$"cannot recognize command, set when Autozap feature will be active with: sge autozap [always/distant/offline]"];
            ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Zap, msq, hidden: isHidden, prefix: prefix);
            return;
        }
        if (arguments[0].Equals("autovibe", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 2)
        {
            if (arguments[1].Equals("always", StringComparison.OrdinalIgnoreCase))
            {
                plugin.Configuration.AutoVibeWhen = RandomVibeSender.OperateWhen.Always;
                plugin.Configuration.AutoVibeEnabled = true;
                plugin.Configuration.Save();

                msq = [$"Autovibe will always run, regardless of where you are. <me> can never change this."];
                ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Vibe, msq, hidden: isHidden, prefix: prefix);
                return;
            }
            if (arguments[1].Equals("distant", StringComparison.OrdinalIgnoreCase))
            {
                plugin.Configuration.AutoVibeWhen = RandomVibeSender.OperateWhen.Distant;
                plugin.Configuration.AutoVibeEnabled = true;
                plugin.Configuration.Save();

                msq = [$"Autovibe will only run when you are out of range. <me> can never change this."];
                ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Vibe, msq, hidden: isHidden, prefix: prefix);
                return;
            }
            if (arguments[1].Equals("offline", StringComparison.OrdinalIgnoreCase))
            {
                plugin.Configuration.AutoVibeWhen = RandomVibeSender.OperateWhen.Offline;
                plugin.Configuration.AutoVibeEnabled = true;
                plugin.Configuration.Save();
                msq = [$"Autovibe will only run when you are offline. <me> can never change this."];
                ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Vibe, msq, hidden: isHidden, prefix: prefix);
                return;
            }

            msq = [$"cannot recognize command, set when Autovibe feature will be active with: sge autovibe [always/distant/offline]"];
            ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Vibe, msq, hidden: isHidden, prefix: prefix);
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
                    ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Zap, hidden: isHidden, prefix: prefix);
                    return;
                }
                if (count >= 0)
                {
                    plugin.Configuration.AutoZapEnabled = true;
                    plugin.Configuration.AutoZapCount = count;
                    plugin.Configuration.AutoZapCountControllerLocked = true;
                    plugin.Configuration.Save();
                    msq = [$"<me> cannot change this or disable the feature unless you unlock the setting with: sge zapcount unlock"];
                    ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Zap, msq, hidden: isHidden, prefix: prefix);
                    return;
                }
            }
            msq = [$"cannot recognize command, to set desired zaps per hour, use: sge zapcount [count]"];
            ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Zap, msq, hidden: isHidden, prefix: prefix);
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

                    ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Vibe, hidden: isHidden, prefix: prefix);
                    return;
                }
                if (count >= 0)
                {
                    plugin.Configuration.AutoVibeEnabled = true;
                    plugin.Configuration.AutoVibeCount = count;
                    plugin.Configuration.AutoVibeCountControllerLocked = true;
                    plugin.Configuration.Save();

                    msq = [$"<me> cannot change this or disable the feature unless you unlock the setting with: sge vibecount unlock"];
                    ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Vibe, msq, hidden: isHidden, prefix: prefix);
                    return;
                }
            }

            msq = [$"cannot recognize command, to set desired vibes per hour, use: sge vibecount [count]"];
            ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Vibe, msq, hidden: isHidden, prefix: prefix);
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

                msq = [$"<me> can never change this. To remove limit, use: sge mountlimit unlimited"];
                ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Mount, msq, hidden: isHidden, prefix: prefix);

                if (validParameter)
                    return;

            }

            msq = [$"cannot recognize command, to limit number of mount usage, use: sge mountlimit [day/hour] [count]"];
            ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Mount, msq, hidden: isHidden, prefix: prefix);

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

                msq = [$"<me> can never change this. To remove limit, use: sge teleportlimit unlimited"];
                ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Teleport, msq, hidden: isHidden, prefix: prefix);

                if (validParameter)
                    return;
            }

            msq = [$"cannot recognize command, to limit number of mount usage, use: sge teleportlimit [day/hour] [count]"];
            ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Teleport, msq, hidden: isHidden, prefix: prefix);
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

                msq = [$"<me> can never change this. To remove limit, use: sge joblimit unlimited"];
                ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Job, msq, hidden: isHidden, prefix: prefix);

                if (validParameter)
                    return;
            }

            msq = [$"cannot recognize command, to limit number of mount usage, use: sge joblimit [day/hour] [count]"];
            ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Job, msq, hidden: isHidden, prefix: prefix);
            return;
        }
        if (arguments[0].Equals("jobroulette", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 2)
        {
            if (int.TryParse(arguments[1], out var intervalMinutes))
            {
                if (intervalMinutes < 1)
                {
                    msq = [$"cannot start job roulette with interval less than 1 minute, to enable job roulette, use: sge jobroulette [minutes]"];
                    ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Roulette, msq, hidden: isHidden, prefix: prefix);
                    return;
                }

                var interval = TimeSpan.FromMinutes(intervalMinutes);

                plugin.Configuration.JobRouletteInterval = interval;
                plugin.Configuration.JobRouletteEnabled = true;
                plugin.Configuration.JobRouletteRemoteSet = true;
                //for now force this
                plugin.Configuration.JobRouletteSwapEvenIfLockedOrOutOfQuota = true;
                plugin.Configuration.JobRouletteLockManualChanges = false;

                plugin.Configuration.NextScheduledJobSwitch = DateTime.UtcNow + interval;
                plugin.Configuration.Save();

                msq = [$"Job roulette enabled with interval {FormatTimeSpan(interval)}. Job roulettes will also consume <me>'s Quota"];
                ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Roulette, msq, hidden: isHidden, prefix: prefix);
                return;
            }

            msq = [$"cannot recognize command, to enable job roulette, use: sge jobroulette [minutes]"];
            ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Roulette, msq, hidden: isHidden, prefix: prefix);
            return;
        }
        if (arguments[0].Equals("stopjobroulette", StringComparison.OrdinalIgnoreCase))
        {
            plugin.Configuration.JobRouletteEnabled = false;
            plugin.Configuration.NextScheduledJobSwitch = DateTime.MinValue;
            plugin.Configuration.Save();

            ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Roulette, hidden: isHidden, prefix: prefix);
            return;
        }

        if (arguments[0].Equals("settitle", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 2)
        {

            var title = string.Join(" ", arguments.Skip(1)).Trim();
            var json = BuildRemoteTitleJsonFromCurrent(title);

            if (string.IsNullOrWhiteSpace(json))
            {
                msq = [$"failed to build Honorific title."];
                ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Title, msq, hidden: isHidden, prefix: prefix);
                return;
            }
            ApplyRemotePermanentTitleJson(json);
            ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Title, hidden: isHidden, prefix: prefix);
            return;

        }
        if (arguments[0].Equals("settemptitle", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 3)
        {

            if (!int.TryParse(arguments[1], out int seconds))
            {
                msq = [$"seconds not recognized, use: sge settemptitle [seconds] [title]"];
                ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Title, msq, hidden: isHidden, prefix: prefix);
                return;
            }
            var title = string.Join(" ", arguments.Skip(2)).Trim();
            var json = BuildRemoteTitleJsonFromCurrent(title);

            if (string.IsNullOrWhiteSpace(json))
            {
                msq = [$"failed to build temporary Honorific title."];
                ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Title, msq, hidden: isHidden, prefix: prefix);
                return;
            }
            plugin.HonorificManager.SetTitle(json, TimeSpan.FromSeconds(seconds), RemoteHonorificTempPriority, this);
            msq = [$"Temporary Honorific title set to: {title} for {seconds}s, temp title cannot be cancelled or removed before timer expires."];
            ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Title, msq, hidden: isHidden, prefix: prefix);
            return;

        }
        if (arguments[0].Equals("cleartitle", StringComparison.OrdinalIgnoreCase))
        {
            RecallRemotePermanentTitleRequest();
            ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Title, hidden: isHidden, prefix: prefix);
            return;
        }

        msq = [$"cannot recognize command, to display all commands, use: sge help"];
        ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.None, msq, hidden: false);
        return;
    }


    private RemotePackage BuildRemoteStatusPackage()
    {
        var package = new RemotePackage(RemotePackageTypeStatus);

        package.WriteInt(RemoteStatusPackageVersion);

        package.WriteBool(plugin.Configuration.AutoZapEnabled);
        package.WriteInt(plugin.Configuration.AutoZapCount);
        package.WriteInt((int)plugin.Configuration.AutoZapWhen);
        package.WriteBool(plugin.Configuration.AutoZapCountControllerLocked);

        package.WriteBool(plugin.Configuration.AutoVibeEnabled);
        package.WriteInt(plugin.Configuration.AutoVibeCount);
        package.WriteInt((int)plugin.Configuration.AutoVibeWhen);
        package.WriteBool(plugin.Configuration.AutoVibeCountControllerLocked);

        package.WriteBool(plugin.Configuration.MountQuotaEnabled);
        package.WriteInt(plugin.Configuration.MountQuotaActions);
        package.WriteInt((int)plugin.Configuration.MountQuotaWindow);
        package.WriteInt(plugin.MountBlocker.GetUsedQuotaCount());

        package.WriteBool(plugin.Configuration.TeleportQuotaEnabled);
        package.WriteInt(plugin.Configuration.TeleportQuotaActions);
        package.WriteInt((int)plugin.Configuration.TeleportQuotaWindow);
        package.WriteInt(plugin.TeleportBlocker.GetUsedQuotaCount());

        package.WriteBool(plugin.Configuration.JobSwitchQuotaEnabled);
        package.WriteInt(plugin.Configuration.JobSwitchQuotaActions);
        package.WriteInt((int)plugin.Configuration.JobSwitchQuotaWindow);
        package.WriteInt(plugin.JobManager.GetUsedQuotaCount());

        package.WriteBool(plugin.Configuration.JobRouletteEnabled);
        package.WriteBool(plugin.Configuration.JobRouletteRemoteSet);
        package.WriteTimeSpan(plugin.Configuration.JobRouletteInterval);
        package.WriteDateTimeUtc(plugin.Configuration.NextScheduledJobSwitch);
        package.WriteInt(plugin.Configuration.JobRouletteWhitelistedGearsets?.Count ?? 0);

        package.WriteString(GetRemotePermanentTitleDisplay());
        package.WriteString("extra content to force multi package");
        //package.WriteString("kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk");
        //package.WriteString("kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk");
        //package.WriteString("kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk");
        //package.WriteString("kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk");

        return package;
    }

    private void HandleRemotePackage(RemotePackage package, XivChatType type, string senderName, string senderWorld)
    {
        try
        {
            if (package.PackageType == RemotePackageTypeStatus)
            {
                if (!TryApplyRemoteStatusPackage(package, senderName, senderWorld))
                {
                    Plugin.Log.Warning($"Invalid remote status package from {senderName}@{senderWorld}.");
                    return;
                }

                Plugin.Log.Information($"Saved remote status package from {senderName}@{senderWorld}.");
                return;
            }

            if (package.PackageType == RemotePackageTypeSetPermanentTitle)
            {
                HandleSetPermanentTitlePackage(package, senderName, senderWorld);
                return;
            }

            if (package.PackageType == RemotePackageTypeSetTemporaryTitle)
            {
                HandleSetTemporaryTitlePackage(package, senderName, senderWorld);
                return;
            }

            if (package.PackageType == RemotePackageTypeCloneCurrentTitleRequest)
            {
                HandleCloneCurrentTitleRequestPackage(package, senderName, senderWorld);
                return;
            }

            if (package.PackageType == RemotePackageTypeCloneCurrentTitleResponse)
            {
                HandleCloneCurrentTitleResponsePackage(package, senderName, senderWorld);
                return;
            }
            if (package.PackageType == RemotePackageTypeCloneCurrentTempTitleRequest)
            {
                HandleCloneCurrentTempTitleRequestPackage(package, senderName, senderWorld);
                return;
            }

            if (package.PackageType == RemotePackageTypeCloneCurrentTempTitleResponse)
            {
                HandleCloneCurrentTempTitleResponsePackage(package, senderName, senderWorld);
                return;
            }

            if (package.PackageType == RemotePackageTypePuppeteerAliasesRequest)
            {
                HandlePuppeteerAliasesRequestPackage(package, senderName, senderWorld);
                return;
            }

            if (package.PackageType == RemotePackageTypePuppeteerAliasesResponse)
            {
                HandlePuppeteerAliasesResponsePackage(package, senderName, senderWorld);
                return;
            }

            Plugin.Log.Warning($"Unknown remote package type {package.PackageType} from {senderName}@{senderWorld}.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Failed to handle remote package from {senderName}@{senderWorld}.");
        }
    }
    private void HandleSetPermanentTitlePackage(RemotePackage package, string senderName, string senderWorld)
    {
        if (!IsAllowedSender(senderName, senderWorld)) return;
        var version = package.ReadInt();
        if (version != RemoteTitlePackageVersion) return;
        var json = package.ReadString();
        ApplyRemotePermanentTitleJson(json);
        ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Title, hidden: true, prefix: GetRemotePrefix());
    }
    private void HandleSetTemporaryTitlePackage(RemotePackage package, string senderName, string senderWorld)
    {
        if (!IsAllowedSender(senderName, senderWorld)) return;
        var version = package.ReadInt();
        if (version != RemoteTitlePackageVersion) return;
        var seconds = package.ReadInt();
        var json = package.ReadString();
        if (string.IsNullOrWhiteSpace(json)) return;
        plugin.HonorificManager.SetTitle(json, TimeSpan.FromSeconds(Math.Max(1, seconds)), RemoteHonorificTempPriority, this);
        ReturnStatusUpdate(senderName, senderWorld, RemoteStatusType.Title, hidden: true, prefix: GetRemotePrefix());
    }
    private void HandleCloneCurrentTitleRequestPackage(RemotePackage package, string senderName, string senderWorld)
    {
        if (!IsAllowedSender(senderName, senderWorld)) return;
        var version = package.ReadInt();
        if (version != RemoteTitlePackageVersion) return;
        var json = string.Empty;

        try
        {
            if (plugin.HonorificApi.IsAvailable())
                json = plugin.HonorificApi.GetLocalTitleJson();
        }
        catch
        {
            json = string.Empty;
        }

        var response = new RemotePackage(RemotePackageTypeCloneCurrentTitleResponse);
        response.WriteInt(RemoteTitlePackageVersion);
        response.WriteString(json);
        _ = SendTellPackageAsync(senderName, senderWorld, response, GetRemotePrefix());
    }
    private void HandleCloneCurrentTitleResponsePackage(RemotePackage package, string senderName, string senderWorld)
    {
        if (!IsControlledUser(senderName, senderWorld)) return;
        var version = package.ReadInt();
        if (version != RemoteTitlePackageVersion) return;
        var json = package.ReadString();
        var user = GetOrCreateControlledUser(senderName, senderWorld);
        ApplyControllerHonorificJson(user, json);
        plugin.Configuration.Save();
        plugin.RefreshControllerUserInputState(senderName, senderWorld);
    }
    private void HandleCloneCurrentTempTitleRequestPackage(RemotePackage package, string senderName, string senderWorld)
    {
        if (!IsAllowedSender(senderName, senderWorld)) return;
        var version = package.ReadInt();
        if (version != RemoteTitlePackageVersion) return;
        var json = string.Empty;

        try
        {
            if (plugin.HonorificApi.IsAvailable())
                json = plugin.HonorificApi.GetLocalTitleJson();
        }
        catch
        {
            json = string.Empty;
        }

        var response = new RemotePackage(RemotePackageTypeCloneCurrentTempTitleResponse);
        response.WriteInt(RemoteTitlePackageVersion);
        response.WriteString(json);
        _ = SendTellPackageAsync(senderName, senderWorld, response, GetRemotePrefix());
    }
    private void HandleCloneCurrentTempTitleResponsePackage(RemotePackage package, string senderName, string senderWorld)
    {
        if (!IsControlledUser(senderName, senderWorld)) return;
        var version = package.ReadInt();
        if (version != RemoteTitlePackageVersion) return;
        var json = package.ReadString();
        plugin.SetControllerTempHonorificInputState(senderName, senderWorld, json);
    }
    private void HandlePuppeteerAliasesRequestPackage(RemotePackage package, string senderName, string senderWorld)
    {
        if (!IsAllowedSender(senderName, senderWorld)) return;
        var version = package.ReadInt();
        if (version != RemotePuppeteerAliasesPackageVersion) return;
        var aliases = BuildPuppeteerAliasesForController(senderName);
        var response = new RemotePackage(RemotePackageTypePuppeteerAliasesResponse);
        response.WriteInt(RemotePuppeteerAliasesPackageVersion);
        response.WriteString(aliases.ToString(Newtonsoft.Json.Formatting.None));
        _ = SendTellPackageAsync(senderName, senderWorld, response, GetRemotePrefix());
    }
    private void HandlePuppeteerAliasesResponsePackage(RemotePackage package, string senderName, string senderWorld)
    {
        if (!IsControlledUser(senderName, senderWorld)) return;
        var version = package.ReadInt();
        if (version != RemotePuppeteerAliasesPackageVersion) return;
        var json = package.ReadString();
        var user = GetOrCreateControlledUser(senderName, senderWorld);
        user.PuppeteerAliases = ParsePuppeteerAliasesResponse(json);
        user.LastPuppeteerAliasesUtc = DateTime.UtcNow;
        plugin.Configuration.Save();
        plugin.NotifyControllerRemotePackageCompleted(senderName, senderWorld, $"Alias package received: {user.PuppeteerAliases.Count} aliases.");
    }
    private JArray BuildPuppeteerAliasesForController(string controllerName)
    {
        var result = new JArray();

        try
        {
            var aliases = plugin.GagSpeakPuppeteerAliasesApi.GetAliases();
            foreach (var alias in aliases)
            {
                if (!alias.Enabled) continue;
                if (!AliasAllowsController(alias, controllerName)) continue;
                var note = alias.Id != Guid.Empty && plugin.Configuration.GagSpeakPuppeteerAliasNotes.TryGetValue(alias.Id, out var savedNote) ? savedNote : string.Empty;
                result.Add(new JObject { ["Folder"] = alias.FolderPath ?? string.Empty, ["Name"] = alias.Name ?? string.Empty, ["Trigger"] = alias.TriggerCommand ?? string.Empty, ["Note"] = note ?? string.Empty });
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to build Puppeteer alias response package.");
        }

        return result;
    }
    private static bool AliasAllowsController(API.GagSpeak.GagSpeakPuppeteerAliasesApi.PuppeteerAliasInfo alias, string controllerName)
    {
        if (string.IsNullOrWhiteSpace(controllerName)) return false;
        return alias.WhitelistedNames.Any(x => string.Equals(x?.Trim(), controllerName.Trim(), StringComparison.OrdinalIgnoreCase)) || alias.WhitelistedPlayerNames.Any(x => string.Equals(x?.Trim(), controllerName.Trim(), StringComparison.OrdinalIgnoreCase)) || alias.WhitelistedUids.Any(x => string.Equals(x?.Trim(), controllerName.Trim(), StringComparison.OrdinalIgnoreCase));
    }
    private static List<Configuration.ControllerPuppeteerAliasConfig> ParsePuppeteerAliasesResponse(string json)
    {
        var result = new List<Configuration.ControllerPuppeteerAliasConfig>();

        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            foreach (var token in JArray.Parse(json).OfType<JObject>())
            {
                result.Add(new Configuration.ControllerPuppeteerAliasConfig { Folder = token["Folder"]?.ToString() ?? string.Empty, Name = token["Name"]?.ToString() ?? string.Empty, Trigger = token["Trigger"]?.ToString() ?? string.Empty, Note = token["Note"]?.ToString() ?? string.Empty });
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to parse Puppeteer alias response package.");
        }

        return result
            .Where(x => !string.IsNullOrWhiteSpace(x.Trigger))
            .OrderBy(x => x.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    private bool TryApplyRemoteStatusPackage(RemotePackage package, string senderName, string senderWorld)
    {
        if (!IsControlledUser(senderName, senderWorld)) return false;

        var version = package.ReadInt();
        if (version != RemoteStatusPackageVersion) return false;

        var user = GetOrCreateControlledUser(senderName, senderWorld);
        user.LastStatusUtc = DateTime.UtcNow;
        user.AutoZapEnabled = package.ReadBool();
        user.AutoZapCount = package.ReadInt();
        user.AutoZapWhen = ReadOperateWhenName(package.ReadInt());
        user.AutoZapLocked = package.ReadBool();
        user.AutoVibeEnabled = package.ReadBool();
        user.AutoVibeCount = package.ReadInt();
        user.AutoVibeWhen = ReadOperateWhenName(package.ReadInt());
        user.AutoVibeLocked = package.ReadBool();
        user.MountQuotaEnabled = package.ReadBool();
        user.MountQuotaActions = package.ReadInt();
        user.MountQuotaWindow = ReadQuotaWindow(package.ReadInt());
        user.MountQuotaUsed = package.ReadInt();
        user.TeleportQuotaEnabled = package.ReadBool();
        user.TeleportQuotaActions = package.ReadInt();
        user.TeleportQuotaWindow = ReadQuotaWindow(package.ReadInt());
        user.TeleportQuotaUsed = package.ReadInt();
        user.JobQuotaEnabled = package.ReadBool();
        user.JobQuotaActions = package.ReadInt();
        user.JobQuotaWindow = ReadQuotaWindow(package.ReadInt());
        user.JobQuotaUsed = package.ReadInt();
        user.JobRouletteEnabled = package.ReadBool();
        user.JobRouletteLocked = package.ReadBool();
        user.JobRouletteInterval = package.ReadTimeSpan();
        user.NextScheduledJobSwitchUtc = package.ReadDateTimeUtc();
        user.JobRouletteWhitelistedGearsetCount = package.ReadInt();
        user.RemoteTitle = package.ReadString();

        plugin.Configuration.Save();
        plugin.RefreshControllerUserInputState(senderName, senderWorld);
        return true;
    }
    private string GetRemotePrefix()
    {
        var prefix = plugin.Configuration.RemoteChatCommandPrefix;
        return string.IsNullOrWhiteSpace(prefix) ? "sge" : prefix.Trim();
    }
    private bool IsControlledUser(string senderName, string senderWorld)
    {
        return plugin.Configuration.ControllerUsers.Any(user => IsSameCharacter(user.Name, user.World, senderName, senderWorld));
    }
    private Configuration.ControllerUserConfig GetOrCreateControlledUser(string senderName, string senderWorld)
    {
        var user = plugin.Configuration.ControllerUsers.FirstOrDefault(user => IsSameCharacter(user.Name, user.World, senderName, senderWorld));
        if (user != null) return user;

        user = new Configuration.ControllerUserConfig { Name = senderName, World = senderWorld };
        plugin.Configuration.ControllerUsers.Add(user);
        return user;
    }
    private static bool IsSameCharacter(string leftName, string leftWorld, string rightName, string rightWorld)
    {
        return string.Equals(leftName?.Trim(), rightName?.Trim(), StringComparison.OrdinalIgnoreCase) && string.Equals(leftWorld?.Trim(), rightWorld?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
    private static string ReadOperateWhenName(int value)
    {
        if (Enum.IsDefined(typeof(RandomZapSender.OperateWhen), value)) return ((RandomZapSender.OperateWhen)value).ToString();
        return "Unknown";
    }
    private static QuotaWindow ReadQuotaWindow(int value)
    {
        if (Enum.IsDefined(typeof(QuotaWindow), value)) return (QuotaWindow)value;
        return QuotaWindow.Hour;
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
    private bool TryGetSenderCharacter(SeString sender, out string name, out string world)
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
    private static bool TryParseSenderText(string? senderText,out string name,out string world)
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
    private Task SendTellPackageAsync(string senderName,string senderWorld,RemotePackage package,string prefix)
    {
        var tellPrefixLength = $"/t {senderName}@{senderWorld} ".Length;
        var maxLineLength = Math.Max(100, RemotePackageTransport.DefaultMaxLineLength - tellPrefixLength);
        return SendTellLinesAsync(senderName, senderWorld, packageTransport.BuildTellLines(package, prefix, maxLineLength));
    }
    private async Task SendTellLinesAsync(string senderName,string senderWorld,IEnumerable<string> lines,int delayMs = 1500)
    {
        var sendLines = lines.Select(line => line.Trim()).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        if (sendLines.Count == 0) return;

        await tellSendLock.WaitAsync();

        try
        {
            if (sendLines.Count > 1)
            {
                Plugin.ChatGui.Print($"SGE is sending {sendLines.Count} tell chunks to {senderName}@{senderWorld}. Avoid sending manual tells until it completes, or the transfer may fail due to tell cooldown.");
            }

            foreach (var safeLine in sendLines)
            {
                if (disposed)
                    return;

                await Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    if (disposed)
                        return;

                    plugin.Utils.ExecuteNativeCommand($"/t {senderName}@{senderWorld} {safeLine}");
                });

                await Task.Delay(delayMs);
            }

            if (sendLines.Count > 1)
            {
                Plugin.ChatGui.Print($"SGE tell chunk transfer to {senderName}@{senderWorld} completed.");
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

            plugin.Configuration.RemotePermanentHonorificTitleJson = json;
            plugin.Configuration.Save();

            plugin.HonorificManager.SetTitle(json, RemoteHonorificPriority, this);

            hasSubmittedRemoteTitleRequest = true;
            lastSubmittedRemoteTitleJson = json;
        });
    }
    private void RecallRemotePermanentTitleRequest()
    {
        _ = Plugin.Framework.RunOnFrameworkThread(() =>
        {
            if (disposed)
                return;

            plugin.Configuration.RemotePermanentHonorificTitleJson = string.Empty;
            plugin.Configuration.Save();

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
            return "";

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
    private static void ApplyControllerHonorificJson(Configuration.ControllerUserConfig user, string json)
    {
        user.HonorificTitle.Json = json ?? string.Empty;
        user.HonorificTitle.Title = GetHonorificJsonTitle(json);
        user.HonorificTitle.Color = ReadHonorificVector(json, "Color", new Vector3(1f, 1f, 1f));
        user.HonorificTitle.Glow = ReadHonorificVector(json, "Glow", new Vector3(0f, 0f, 0f));
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

}
