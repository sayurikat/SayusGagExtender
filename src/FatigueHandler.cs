using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace SayusGagExtender;

public sealed class FatigueHandler : IDisposable
{
    private readonly Plugin plugin;

    private const string SourceName = nameof(FatigueHandler);

 
    private DateTime nextSitAttemptUtc = DateTime.MinValue;
    private DateTime nextStatusPrintUtc = DateTime.MinValue;

    private readonly TimeSpan SitAttemptCooldown = TimeSpan.FromSeconds(2.0);
    private readonly TimeSpan StatusPrintCooldown = TimeSpan.FromSeconds(5.0);

    private bool stopBlockRequested;

    private readonly HashSet<uint> sittingEmoteIds = new();

    public bool IsForceWalkActive { get; private set; }
    public bool IsForceStopActive { get; private set; }
    public bool IsForceSitActive { get; private set; }
    private static readonly uint[] ManualSittingEmoteIds =
[
    // Ground sit
    52,
    97,
    98,
    117,

    // Chair sit
    50,
    95,
    96,
    254,
    255,

    // Doze / sleep
    88,
    99,
    100,

    // Playdead
    143,
];
    public FatigueHandler(Plugin plugin)
    {
        this.plugin = plugin;

        BuildSittingEmoteRegistry();

        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;

        ClearStopBlock();
        DisableForcedWalk();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            OnFrameworkUpdateInner();
        }
        catch (Exception ex)
        {
            Plugin.ChatGui.PrintError($"FatigueHandler error: {ex.Message}");
        }
    }

    private void OnFrameworkUpdateInner()
    {
        if (!plugin.Configuration.FatigueEnabled)
        {
            ResetAll();
            return;
        }

        var tracker = plugin.FatigueTracker;
        if (tracker == null)
        {
            ResetAll();
            return;
        }

        var invalid = IsInvalidState();

        IsForceWalkActive = tracker.ShouldForceWalk;
        IsForceStopActive = tracker.ShouldForceStop;
        IsForceSitActive = tracker.ShouldForceSit;

        HandleForceStop(tracker);

        if (invalid)
            return;

        HandleForceWalk(tracker);
        HandleForceSit(tracker);
    }

    private void HandleForceStop(FatigueTracker tracker)
    {
        if (tracker.ShouldForceStop)
        {
            RequestStopBlock();
            return;
        }

        ClearStopBlock();
    }

    private void HandleForceWalk(FatigueTracker tracker)
    {
        // Stop is stronger than walk.
        if (tracker.ShouldForceStop)
        {
            DisableForcedWalk();
            return;
        }

        if (!tracker.ShouldForceWalk)
        {
            DisableForcedWalk();
            return;
        }

        if (IsNonWalkingAllowedState())
        {
            DisableForcedWalk();
            return;
        }

        EnsureWalkEnabled();
    }

    private void HandleForceSit(FatigueTracker tracker)
    {
        if (!tracker.ShouldForceSit)
            return;

        if (plugin.EmoteEnforcer.ShouldBlockUserEmotes)
            return;

        if (IsNonStandingOrMounted())
            return;

        var now = DateTime.UtcNow;
        if (now < nextSitAttemptUtc)
            return;

        nextSitAttemptUtc = now + SitAttemptCooldown;

        
        plugin.EmoteGuard?.QueueGuardedEmote("/sit");

        MaybePrintStatus("Fatigue: forcing sit.");
    }

    private void RequestStopBlock()
    {
        if (stopBlockRequested)
            return;

        stopBlockRequested = true;
        plugin.MovementBlocker?.RequestBlock(SourceName);

        MaybePrintStatus("Fatigue: movement stopped.");
    }

    private void ClearStopBlock()
    {
        if (!stopBlockRequested)
            return;

        stopBlockRequested = false;
        plugin.MovementBlocker?.ClearBlock(SourceName);
    }

    private void EnsureWalkEnabled()
    {
        plugin.MovementBlocker?.RequestForceWalk(SourceName);

        MaybePrintStatus("Fatigue: forcing walk.");
    }

    private void DisableForcedWalk()
    {
        plugin.MovementBlocker?.ClearForceWalk(SourceName);
    }

    private void ResetAll()
    {
        IsForceWalkActive = false;
        IsForceStopActive = false;
        IsForceSitActive = false;

        ClearStopBlock();
        DisableForcedWalk();
    }

    private bool IsInvalidState()
    {
        return Plugin.ObjectTable.LocalPlayer == null
               || Plugin.Condition.Any(
                   ConditionFlag.BetweenAreas,
                   ConditionFlag.BetweenAreas51,
                   ConditionFlag.WatchingCutscene,
                   ConditionFlag.WatchingCutscene78,
                   ConditionFlag.OccupiedInCutSceneEvent,
                   ConditionFlag.Mounting,
                   ConditionFlag.Mounting71);
    }

    private bool IsNonWalkingAllowedState()
    {
        if (Plugin.Condition.Any(
                ConditionFlag.Mounted,
                ConditionFlag.RidingPillion))
            return true;

        if (IsSittingOrLyingByEmote())
            return true;

        if (Plugin.Condition.Any(
                ConditionFlag.Crafting,
                ConditionFlag.Gathering,
                ConditionFlag.Fishing,
                ConditionFlag.Performing,
                ConditionFlag.TradeOpen))
            return true;

        return false;
    }

    private bool IsNonStandingOrMounted()
    {
        if (Plugin.Condition.Any(
                ConditionFlag.Mounted,
                ConditionFlag.RidingPillion))
            return true;

        if (IsSittingOrLyingByEmote())
            return true;

        //// If they are already in some emote, do not spam /sit.
        //// Remove this if you want forced sit to interrupt standing emotes.
        //if (IsInAnyEmote())
        //    return true;

        if (Plugin.Condition.Any(
                ConditionFlag.Crafting,
                ConditionFlag.Gathering,
                ConditionFlag.Fishing,
                ConditionFlag.Performing,
                ConditionFlag.TradeOpen))
            return true;

        return false;
    }



    private void MaybePrintStatus(string message)
    {
        var now = DateTime.UtcNow;
        if (now < nextStatusPrintUtc)
            return;

        nextStatusPrintUtc = now + StatusPrintCooldown;
        Plugin.ChatGui.Print(message);
    }
    private void BuildSittingEmoteRegistry()
    {
        sittingEmoteIds.Clear();

        try
        {
            foreach (var emote in Plugin.DataManager.GetExcelSheet<Emote>())
            {
                var textCommand = emote.TextCommand.Value;

                var command = NormalizeCommandName(textCommand.Command.ToString());
                var shortCommand = NormalizeCommandName(textCommand.ShortCommand.ToString());

                if (IsSittingCommand(command) || IsSittingCommand(shortCommand))
                    sittingEmoteIds.Add(emote.RowId);
            }

            // Alternate sitting / resting pose IDs found by dev testing.
            // Ground sit poses: 52, 97, 98, 117
            // Chair sit poses: 50, 95, 96, 254, 255
            // Doze poses: 88, 99, 100
            foreach (var id in ManualSittingEmoteIds)
                sittingEmoteIds.Add(id);

        }
        catch (Exception ex)
        {
            Plugin.ChatGui.PrintError($"FatigueHandler failed to build sitting emote registry: {ex.Message}");
        }
    }

    private static string NormalizeCommandName(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return string.Empty;

        command = command.Trim();

        if (command.StartsWith('/'))
            command = command[1..];

        var spaceIndex = command.IndexOf(' ');
        if (spaceIndex >= 0)
            command = command[..spaceIndex];

        return command.Trim().ToLowerInvariant();
    }

    private static bool IsSittingCommand(string command)
    {
        return command is
            "sit"
            or "groundsit"
            or "doze"
            or "playdead";
    }

    private bool IsSittingOrLyingByEmote()
    {
        var currentEmoteId = plugin.EmoteApi.GetCurrentLocalPlayerEmoteId();

        if (currentEmoteId == 0)
            return false;

        return sittingEmoteIds.Contains(currentEmoteId);
    }

    private bool IsInAnyEmote()
    {
        return plugin.EmoteApi.GetCurrentLocalPlayerEmoteId() != 0;
    }
}
