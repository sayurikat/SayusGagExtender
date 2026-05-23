using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using static FFXIVClientStructs.FFXIV.Client.Game.ActionManager;
using static SayusGagExtender.Configuration;

namespace SayusGagExtender;

public unsafe sealed class MountBlocker : IDisposable
{
    private readonly Plugin plugin;
    public bool IsActive => this.IsAnyMountBlockActive();

    private delegate bool UseActionDelegate(
        ActionManager* actionManager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        UseActionMode mode,
        uint comboRouteId,
        bool* outOptAreaTargeted);

    private readonly Hook<UseActionDelegate> useActionHook;

    private long nextMountedCheckMs;
    private long nextMoodleRefreshMs;
    private long nextQuotaMaintenanceMs;

    private bool cachedMoodleActive;

    private Guid requestedQuotaRunningMoodleId = Guid.Empty;
    private Guid requestedQuotaEmptyMoodleId = Guid.Empty;
    private DateTime mountCountCooldown = DateTime.MinValue;

    public bool Enabled => plugin.Configuration.MountBlockFeature;

    public enum MountQuotaWindow
    {
        Hour = 0,
        Day = 1,
    }

    public MountBlocker(Plugin plugin)
    {
        this.plugin = plugin;

        this.useActionHook = Plugin.GameInterop.HookFromAddress<UseActionDelegate>(
            (nint)ActionManager.MemberFunctionPointers.UseAction,
            this.UseActionDetour);

        this.useActionHook.Enable();

        Plugin.Framework.Update += this.OnFrameworkUpdate;

    }

    public void Enable()
    {
        plugin.Configuration.MountBlockFeature = true;
    }

    public void Disable()
    {
        plugin.Configuration.MountBlockFeature = false;
        this.RemoveQuotaMoodleIfApplied();
    }

    private bool UseActionDetour(
        ActionManager* actionManager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        UseActionMode mode,
        uint comboRouteId,
        bool* outOptAreaTargeted)
    {
        if (this.Enabled && this.ShouldBlockMountAction(actionType, actionId))
        {
            Plugin.ChatGui.Print("Blocked mount action");
            return false;
        }

        var result = this.useActionHook.Original(
            actionManager,
            actionType,
            actionId,
            targetId,
            extraParam,
            mode,
            comboRouteId,
            outOptAreaTargeted);

        if (this.Enabled && result && this.IsMountAction(actionType, actionId) && !Plugin.Condition[ConditionFlag.Mounted] && mountCountCooldown  < DateTime.UtcNow)
        {
            mountCountCooldown = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            this.LogMountAction();
            this.UpdateQuotaMoodleState();
        }

        return result;
    }

    private bool ShouldBlockMountAction(ActionType actionType, uint actionId)
    {
        if (!this.IsMountAction(actionType, actionId))
            return false;

        // Important: if already mounted, don't block mount-related actions.
        // This helps avoid accidentally blocking dismount behavior.
        if (Plugin.Condition[ConditionFlag.Mounted])
            return false;

        if (this.IsBlockMoodleActiveCached())
            return true;

        if (this.IsQuotaExhausted())
            return true;

        return false;
    }

    private bool IsMountAction(ActionType actionType, uint actionId)
    {
        // Individual mounts are ActionType.Mount with the mount row/id.
        // Do not check specific ids; this should catch all normal mount actions.
        return actionType == ActionType.Mount;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!this.Enabled)
        {
            this.RemoveQuotaMoodleIfApplied();
            return;
        }

        var nowMs = Environment.TickCount64;

        if (nowMs >= this.nextQuotaMaintenanceMs)
        {
            this.nextQuotaMaintenanceMs = nowMs + 30000;

            if (this.PruneOldQuotaEntries())
                plugin.Configuration.Save();

            this.UpdateQuotaMoodleState();
        }

        if (!Plugin.Condition[ConditionFlag.Mounted])
            return;

        // Only check while mounted every 5 seconds.
        if (nowMs < this.nextMountedCheckMs)
            return;

        this.nextMountedCheckMs = nowMs + 5000;

        if (!this.IsAnyMountBlockActive(forceRefresh: true))
            return;

        Plugin.ChatGui.Print("Dismounting due to restriction");

        plugin.Utils.ExecuteNativeCommand("/mount");
    }

    private bool IsAnyMountBlockActive(bool forceRefresh = false)
    {
        if (this.IsBlockMoodleActiveCached(forceRefresh))
            return true;

        //if (this.IsQuotaExhausted())
        //    return true;

        return false;
    }

    public bool IsBlockMoodleActiveCached(bool forceRefresh = false)
    {
        var moodles = plugin.Configuration.MountBlockMoodles;
        if (moodles == null || moodles.Count == 0)
        {
            this.cachedMoodleActive = false;
            return false;
        }

        var now = Environment.TickCount64;
        if (!forceRefresh && now < this.nextMoodleRefreshMs)
            return this.cachedMoodleActive;

        var anyActive = false;

        foreach (var moodle in moodles)
        {
            var id = moodle.Key;
            if (id == Guid.Empty)
                continue;

            if (plugin.MoodlesApi.IsStatusActive(id))
            {
                anyActive = true;
                break;
            }
        }

        this.cachedMoodleActive = anyActive;
        this.nextMoodleRefreshMs = now + 5000;

        return this.cachedMoodleActive;
    }

    private bool IsQuotaEnabled()
    {
        return plugin.Configuration.MountQuotaEnabled &&
               plugin.Configuration.MountQuotaActions > -1;
    }

    private TimeSpan GetQuotaWindow()
    {
        return plugin.Configuration.MountQuotaWindow switch
        {
            QuotaWindow.Day => TimeSpan.FromHours(24),
            _ => TimeSpan.FromHours(1),
        };
    }

    private int GetUsedQuotaCount()
    {
        if (!this.IsQuotaEnabled())
            return 0;

        this.EnsureQuotaLog();

        var now = DateTime.UtcNow;
        var cutoff = now - this.GetQuotaWindow();

        var count = 0;

        foreach (var entryUtc in plugin.Configuration.MountQuotaActionLogUtc)
        {
            if (entryUtc >= cutoff)
                count++;
        }

        return count;
    }

    public int GetRemainingQuota()
    {
        if (!this.IsQuotaEnabled())
            return int.MaxValue;

        var remaining = plugin.Configuration.MountQuotaActions - this.GetUsedQuotaCount();
        return Math.Max(0, remaining);
    }

    public bool IsQuotaExhausted()
    {
        if (!this.IsQuotaEnabled())
            return false;

        this.PruneOldQuotaEntries();

        return this.GetRemainingQuota() <= 0;
    }

    private void LogMountAction()
    {
        if (!this.IsQuotaEnabled())
            return;

        this.EnsureQuotaLog();

        this.PruneOldQuotaEntries();

        plugin.Configuration.MountQuotaActionLogUtc.Add(DateTime.UtcNow);
        plugin.Configuration.Save();

        Plugin.ChatGui.Print($"Mount usage counted, remaining: {GetRemainingQuota()}");
    }

    private bool PruneOldQuotaEntries()
    {
        this.EnsureQuotaLog();

        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(24);

        var before = plugin.Configuration.MountQuotaActionLogUtc.Count;
        plugin.Configuration.MountQuotaActionLogUtc.RemoveAll(x => x < cutoff);

        return plugin.Configuration.MountQuotaActionLogUtc.Count != before;
    }

    private void EnsureQuotaLog()
    {
        plugin.Configuration.MountQuotaActionLogUtc ??= new List<DateTime>();
    }

    private void UpdateQuotaMoodleState()
    {
        if (!this.Enabled || !this.IsQuotaEnabled())
        {
            this.RemoveQuotaMoodleIfApplied();
            return;
        }

        var remaining = this.GetRemainingQuota();
        var quotaEmpty = remaining <= 0;

        var runningMoodleId = plugin.Configuration.MountQuotaMoodleId;
        var emptyMoodleId = plugin.Configuration.MountQuotaEmptyMoodleId;

        var wantedRunningMoodleId = !quotaEmpty ? runningMoodleId : Guid.Empty;
        var wantedEmptyMoodleId = quotaEmpty ? emptyMoodleId : Guid.Empty;

        // If both states use the same Moodle, keep one stable request instead of remove/add churn.
        if (runningMoodleId != Guid.Empty && runningMoodleId == emptyMoodleId)
        {
            wantedRunningMoodleId = runningMoodleId;
            wantedEmptyMoodleId = Guid.Empty;
        }

        this.SetQuotaMoodleRequest(ref this.requestedQuotaRunningMoodleId, wantedRunningMoodleId);
        this.SetQuotaMoodleRequest(ref this.requestedQuotaEmptyMoodleId, wantedEmptyMoodleId);
    }

    private void SetQuotaMoodleRequest(ref Guid currentlyRequestedMoodleId, Guid wantedMoodleId)
    {
        if (currentlyRequestedMoodleId == wantedMoodleId)
            return;

        if (currentlyRequestedMoodleId != Guid.Empty)
        {
            plugin.MoodleEnforcer.RemoveEnforcedMoodle(currentlyRequestedMoodleId, this);
            currentlyRequestedMoodleId = Guid.Empty;
        }

        if (wantedMoodleId == Guid.Empty)
            return;

        plugin.MoodleEnforcer.AddEnforcedMoodle(wantedMoodleId, this);
        currentlyRequestedMoodleId = wantedMoodleId;
    }

    private void RemoveQuotaMoodleIfApplied()
    {
        this.SetQuotaMoodleRequest(ref this.requestedQuotaRunningMoodleId, Guid.Empty);
        this.SetQuotaMoodleRequest(ref this.requestedQuotaEmptyMoodleId, Guid.Empty);
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= this.OnFrameworkUpdate;

        this.RemoveQuotaMoodleIfApplied();

        this.useActionHook.Dispose();
    }
}
