using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using static FFXIVClientStructs.FFXIV.Client.Game.ActionManager;
using static SayusGagExtender.Configuration;

namespace SayusGagExtender;

public unsafe sealed class TeleportBlocker : IDisposable
{
    private readonly Plugin plugin;

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

    public bool Enabled => plugin.Configuration.TeleportBlockFeature;
    public bool IsActive => this.IsAnyTeleportBlockActive();

    private bool cachedMoodleActive = false;
    private long nextMoodleRefreshMs;
    private long nextQuotaMaintenanceMs;

    private bool quotaMoodleApplied;
    private Guid appliedQuotaMoodleId = Guid.Empty;

    private const string TeleportQuotaMoodleSource = "TeleportBlocker.Quota";
    private const string TeleportQuotaEmptyMoodleSource = "TeleportBlocker.QuotaEmpty";

    private DateTime teleportCountCooldown = DateTime.MinValue;

    public TeleportBlocker(Plugin plugin)
    {
        this.plugin = plugin;

        this.useActionHook = Plugin.GameInterop.HookFromAddress<UseActionDelegate>(
            (nint)ActionManager.MemberFunctionPointers.UseAction,
            this.UseActionDetour);

        this.useActionHook.Enable();

        Plugin.Framework.Update += this.OnFrameworkUpdate;
        this.RegisterQuotaMoodle();

    }

    public void Enable()
    {
        plugin.Configuration.TeleportBlockFeature = true;
    }

    public void Disable()
    {
        plugin.Configuration.TeleportBlockFeature = false;
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
        if (this.Enabled && this.IsTeleportOrReturn(actionType, actionId))
        {
            if (this.ShouldBlockTeleportAction(actionType, actionId) && plugin.MirrorGagSpeak.IsMasterCharacter())
            {
                Plugin.ChatGui.Print("Blocked teleport / return action");
                return false;
            }
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

        if (this.Enabled && result && this.IsTeleportOrReturn(actionType, actionId) && this.teleportCountCooldown < DateTime.UtcNow && plugin.MirrorGagSpeak.IsMasterCharacter())
        {
            // Teleport / Return can be noisy depending on how it is started.
            // Cooldown avoids double-counting the same attempt.
            this.teleportCountCooldown = DateTime.UtcNow + TimeSpan.FromSeconds(10);

            this.LogTeleportAction();
            this.UpdateQuotaMoodleState();
        }

        return result;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!this.Enabled)
        {
            this.RemoveQuotaMoodleIfApplied();
            return;
        }

        var nowMs = Environment.TickCount64;

        if (nowMs < this.nextQuotaMaintenanceMs)
            return;

        this.nextQuotaMaintenanceMs = nowMs + 30000;

        if (this.PruneOldQuotaEntries())
            plugin.Configuration.Save();

        this.UpdateQuotaMoodleState();
    }

    private bool ShouldBlockTeleportAction(ActionType actionType, uint actionId)
    {
        if (!this.IsTeleportOrReturn(actionType, actionId))
            return false;

        if (this.IsBlockMoodleActiveCached(forceRefresh: true))
            return true;

        if (this.IsQuotaExhausted())
            return true;

        return false;
    }

    private bool IsAnyTeleportBlockActive(bool forceRefresh = false)
    {
        if (this.IsBlockMoodleActiveCached(forceRefresh))
            return true;

        if (this.IsQuotaExhausted())
            return true;

        return false;
    }

    public bool IsBlockMoodleActiveCached(bool forceRefresh = false)
    {
        var moodles = plugin.Configuration.TeleportBlockMoodles;
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
        return plugin.Configuration.TeleportQuotaEnabled &&
               plugin.Configuration.TeleportQuotaActions > -1;
    }

    private TimeSpan GetQuotaWindow()
    {
        return plugin.Configuration.TeleportQuotaWindow switch
        {
            QuotaWindow.Day => TimeSpan.FromHours(24),
            _ => TimeSpan.FromHours(1),
        };
    }

    public int GetUsedQuotaCount()
    {
        if (!this.IsQuotaEnabled())
            return 0;

        this.EnsureQuotaLog();

        var cutoff = DateTime.UtcNow - this.GetQuotaWindow();
        var count = 0;

        foreach (var entryUtc in plugin.Configuration.TeleportQuotaActionLogUtc)
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

        var remaining = plugin.Configuration.TeleportQuotaActions - this.GetUsedQuotaCount();
        return Math.Max(0, remaining);
    }

    public bool IsQuotaExhausted()
    {
        if (!this.IsQuotaEnabled())
            return false;

        this.PruneOldQuotaEntries();

        return this.GetRemainingQuota() <= 0;
    }

    private void LogTeleportAction()
    {
        if (!this.IsQuotaEnabled())
            return;

        this.EnsureQuotaLog();
        this.PruneOldQuotaEntries();

        plugin.Configuration.TeleportQuotaActionLogUtc.Add(DateTime.UtcNow);
        plugin.Configuration.Save();

        Plugin.ChatGui.Print($"Teleport usage counted, remaining: {this.GetRemainingQuota()}");
    }

    private bool PruneOldQuotaEntries()
    {
        this.EnsureQuotaLog();

        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(24);

        var before = plugin.Configuration.TeleportQuotaActionLogUtc.Count;
        plugin.Configuration.TeleportQuotaActionLogUtc.RemoveAll(x => x < cutoff);

        return plugin.Configuration.TeleportQuotaActionLogUtc.Count != before;
    }

    private void EnsureQuotaLog()
    {
        plugin.Configuration.TeleportQuotaActionLogUtc ??= new List<DateTime>();
    }

    private void RegisterQuotaMoodle()
    {
        plugin.MoodleEnforcer.RegisterExternalMoodle(plugin.Configuration.TeleportQuotaMoodleId, TeleportQuotaMoodleSource);
        plugin.MoodleEnforcer.RegisterExternalMoodle(plugin.Configuration.TeleportQuotaEmptyMoodleId, TeleportQuotaEmptyMoodleSource);
    }

    private void UpdateQuotaMoodleState()
    {
        this.RegisterQuotaMoodle();
        if (!this.Enabled || !this.IsQuotaEnabled())
        {
            this.RemoveQuotaMoodleIfApplied();
            return;
        }

        var quotaEmpty = this.IsQuotaExhausted();
        var wantedMoodleId = quotaEmpty ? plugin.Configuration.TeleportQuotaEmptyMoodleId : plugin.Configuration.TeleportQuotaMoodleId;
        var wantedSourceKey = quotaEmpty ? TeleportQuotaEmptyMoodleSource : TeleportQuotaMoodleSource;

        if (wantedMoodleId == Guid.Empty)
        {
            this.RemoveQuotaMoodleIfApplied();
            return;
        }

        if (this.quotaMoodleApplied && this.appliedQuotaMoodleId == wantedMoodleId)
            return;

        this.RemoveQuotaMoodleIfApplied();

        plugin.MoodleEnforcer.AddEnforcedMoodle(wantedMoodleId, wantedSourceKey);

        this.quotaMoodleApplied = true;
        this.appliedQuotaMoodleId = wantedMoodleId;
    }

    private void RemoveQuotaMoodleIfApplied()
    {
        if (!this.quotaMoodleApplied || this.appliedQuotaMoodleId == Guid.Empty)
            return;

        plugin.MoodleEnforcer.RemoveEnforcedMoodle(TeleportQuotaMoodleSource);
        plugin.MoodleEnforcer.RemoveEnforcedMoodle(TeleportQuotaEmptyMoodleSource);

        this.quotaMoodleApplied = false;
        this.appliedQuotaMoodleId = Guid.Empty;
    }

    private bool IsTeleportOrReturn(ActionType actionType, uint actionId)
    {
        // Hotbar/menu GeneralActions:
        // 7 = Return
        // 8 = Teleport
        if (actionType == ActionType.GeneralAction && actionId is 7 or 8)
            return true;

        // Casted actions:
        // 5 = Return
        // 6 = Teleport
        // 11408 = additional teleport-like action used by the client/plugin reference
        if (actionType == ActionType.Action && actionId is 5 or 6 or 11408)
            return true;

        return false;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= this.OnFrameworkUpdate;

        this.RemoveQuotaMoodleIfApplied();

        this.useActionHook.Dispose();
    }
}
