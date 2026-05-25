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

    public bool IsForceWalkActive { get; private set; }
    public bool IsForceStopActive { get; private set; }
    public bool IsForceSitActive { get; private set; }
 
    public FatigueHandler(Plugin plugin)
    {
        this.plugin = plugin;


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

        
        plugin.EmoteGuard?.QueueGuardedEmote("/groundsit");

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
        return;
        var now = DateTime.UtcNow;
        if (now < nextStatusPrintUtc)
            return;

        nextStatusPrintUtc = now + StatusPrintCooldown;
        Plugin.ChatGui.Print(message);
    }


    private bool IsSittingOrLyingByEmote()
    {
        var currentEmoteId = plugin.EmoteApi.GetCurrentLocalPlayerEmoteId();

        if (currentEmoteId == 0)
            return false;
        return plugin.EmoteApi.IsAnySitOrSleep(currentEmoteId) || plugin.EmoteApi.IsThisThatEmote(currentEmoteId, "/playdead");
    }

    private bool IsInAnyEmote()
    {
        return plugin.EmoteApi.GetCurrentLocalPlayerEmoteId() != 0;
    }
}
