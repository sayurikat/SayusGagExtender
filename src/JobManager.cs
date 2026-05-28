using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using static SayusGagExtender.Configuration;

namespace SayusGagExtender;

public unsafe sealed class JobManager : IDisposable
{
    private readonly Plugin plugin;
    private readonly Random random = new();

    private delegate int EquipGearsetDelegate(
        RaptureGearsetModule* gearsetModule,
        int gearsetId,
        byte glamourPlateId);

    private readonly Hook<EquipGearsetDelegate> equipGearsetHook;

    private JobLockState currentLockedJob = new();

    private byte lastSeenClassJob;
    private int lastSeenGearsetId = -1;

    private bool internalGearsetChange;
    private bool requestCaptureAllowedState = true;
    private bool requestCaptureSeenState = true;
    private bool requestCountAcceptedJobSwitch;
    private bool requestRevertToLockedJob;

    private bool pendingRouletteSwitch;
    private int pendingRouletteGearsetId = -1;
    private byte pendingRouletteClassJob;
    private byte pendingRouletteOriginalClassJob;
    private DateTime pendingRouletteNextAttemptUtc = DateTime.MinValue;

    private long nextStateCheckMs;
    private long nextMoodleRefreshMs;
    private long nextPrintMs;
    private long nextQuotaMaintenanceMs;

    private bool cachedMoodleActive;
    private bool cachedOldMoodleBlockActiveForDetour;
    private bool wasBlockingActive;

    private Guid requestedQuotaRunningMoodleId = Guid.Empty;
    private Guid requestedQuotaEmptyMoodleId = Guid.Empty;
    private Guid requestedRouletteMoodleId = Guid.Empty;

    private const string JobManagerQuotaRunningMoodleSource = "JobManager.QuotaRunning";
    private const string JobManagerQuotaEmptyMoodleSource = "JobManager.QuotaEmpty";
    private const string JobManagerRouletteMoodleSource = "JobManager.JobRoulette";

    private string lastSubmittedRouletteHonorificJson = string.Empty;
    private int lastSubmittedRouletteHonorificPriority = -1;
    private bool hasSubmittedRouletteHonorificRequest;

    private DateTime jobSwitchCountCooldown = DateTime.MinValue;

    // When the last manual quota is spent, do not immediately lock/revert.
    // Give the gear/job change time to finish, then capture the final state.
    private DateTime quotaLockCaptureDueUtc = DateTime.MinValue;

    private readonly DateTime startupGraceUntilUtc = DateTime.UtcNow + TimeSpan.FromSeconds(15);

    private static readonly TimeSpan JobSwitchCountCooldown = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan QuotaFinalCaptureGrace = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RouletteRetryDelay = TimeSpan.FromSeconds(10);

    public bool Enabled => plugin.Configuration.JobSwitchBlockFeature;

    public bool RouletteEnabled => plugin.Configuration.JobRouletteEnabled;

    public bool RouletteLockEnabled => plugin.Configuration.JobRouletteLockManualChanges;

    public bool RouletteCanBypassLocksOrQuota => plugin.Configuration.JobRouletteSwapEvenIfLockedOrOutOfQuota;

    public bool QuotaActive => IsQuotaEnabled();

    public bool QuotaEmpty => IsQuotaExhausted();

    public bool IsActive => IsAnyJobSwitchBlockActive() || IsRouletteLockActive;

    private bool IsRouletteLockActive =>
        RouletteEnabled &&
        RouletteLockEnabled &&
        plugin.Configuration.JobRouletteWhitelistedGearsets.Count > 0;

    public JobManager(Plugin plugin)
    {
        this.plugin = plugin;

        // Do not touch gearset/client state in the constructor.
        // At boot/login, RaptureGearsetModule and LocalPlayer can be half-ready.
        requestCaptureAllowedState = true;
        requestCaptureSeenState = true;

        equipGearsetHook = Plugin.GameInterop.HookFromAddress<EquipGearsetDelegate>(
            (nint)RaptureGearsetModule.MemberFunctionPointers.EquipGearset,
            EquipGearsetDetour);

        equipGearsetHook.Enable();

        Plugin.Framework.Update += OnFrameworkUpdate;
        RegisterQuotaMoodles();
        RegisterRouletteMoodle();
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;

        RemoveQuotaMoodleIfApplied();
        RemoveRouletteEffectIfApplied();

        equipGearsetHook.Disable();
        equipGearsetHook.Dispose();
    }

    public void Enable()
    {
        plugin.Configuration.JobSwitchBlockFeature = true;
        requestCaptureAllowedState = true;
        requestCaptureSeenState = true;
    }

    public void Disable()
    {
        plugin.Configuration.JobSwitchBlockFeature = false;
        cachedOldMoodleBlockActiveForDetour = false;

        // Do not remove quota Moodle here if quota is still enabled.
        // Framework update will derive the correct state.
        if (!IsQuotaEnabled())
        {
            wasBlockingActive = false;
            quotaLockCaptureDueUtc = DateTime.MinValue;
            RemoveQuotaMoodleIfApplied();
        }

        requestCaptureAllowedState = true;
        requestCaptureSeenState = true;
    }

    /// <summary>
    /// Returns all valid player gearsets with id, name, class/job id, and job title.
    /// This is intended for config UI whitelist selection.
    /// </summary>
    public IReadOnlyList<GearsetInfo> GetAllGearsets()
    {
        var result = new List<GearsetInfo>();

        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return result;

        for (var i = 0; i < 100; i++)
        {
            if (!module->IsValidGearset(i))
                continue;

            var gearset = module->GetGearset(i);
            if (gearset == null)
                continue;

            var classJob = gearset->ClassJob;
            if (classJob == 0)
                continue;

            var name = TryGetGearsetName(gearset);
            var jobName = GetJobName(classJob);

            result.Add(new GearsetInfo
            {
                GearsetId = i,
                Name = name,
                ClassJobId = classJob,
                JobName = jobName,
            });
        }

        return result
            .OrderBy(x => x.GearsetId)
            .ToList();
    }

    /// <summary>
    /// Rolls and attempts to apply a new random whitelisted gearset.
    /// Missing/renamed whitelist entries are pruned before rolling.
    /// Gearsets for the currently active job are excluded.
    /// Quota never blocks roulette; successful roulette only counts while quota remains.
    /// </summary>
    public bool TryRollRandomWhitelistedGearset()
    {
        pendingRouletteSwitch = false;
        pendingRouletteGearsetId = -1;
        pendingRouletteClassJob = 0;
        pendingRouletteOriginalClassJob = 0;
        pendingRouletteNextAttemptUtc = DateTime.MinValue;

        if (!RouletteEnabled)
            return false;

        if (ShouldPostponeRouletteForLockOrQuota(out var blockReason))
        {
            ScheduleNextRouletteSwitch();
            PrintThrottled($"Job roulette skipped because {blockReason}.");
            return false;
        }

        if (!TryPickRandomWhitelistedGearset(out var selected))
        {
            ScheduleNextRouletteSwitch();
            return false;
        }

        pendingRouletteSwitch = true;
        pendingRouletteGearsetId = selected.GearsetId;
        pendingRouletteClassJob = selected.ClassJobId;
        pendingRouletteOriginalClassJob = GetCurrentClassJob();

        TryCompletePendingRouletteSwitch();
        return true;
    }

    private int EquipGearsetDetour(
        RaptureGearsetModule* gearsetModule,
        int gearsetId,
        byte glamourPlateId)
    {
        // Important:
        // Keep this hook tiny. Do not call EquipGearset, Save(), roulette rolls,
        // RunOnFrameworkThread, or heavy API work from inside the detour.

        var targetClassJob = GetGearsetClassJob(gearsetModule, gearsetId);

        if (!internalGearsetChange && targetClassJob != 0 && ShouldBlockTargetJob(targetClassJob))
        {
            PrintThrottled("Blocked gearset change");
            requestRevertToLockedJob = true;
            return -1;
        }

        var beforeJob = GetCurrentClassJob();

        var result = equipGearsetHook.Original(
            gearsetModule,
            gearsetId,
            glamourPlateId);

        // Count accepted manual gearset switches from framework update.
        // Framework polling will also catch main-hand weapon job changes.
        if (!internalGearsetChange &&
            result >= 0 &&
            targetClassJob != 0 &&
            beforeJob != 0 &&
            targetClassJob != beforeJob)
        {
            requestCountAcceptedJobSwitch = true;
            requestCaptureSeenState = true;
        }

        // If not blocking, keep current state fresh.
        // If quota just hit zero, this is held by final-capture grace logic instead.
        if (!internalGearsetChange &&
            !IsAnyJobSwitchBlockActiveFromCachedState() &&
            !IsQuotaFinalCapturePending())
        {
            requestCaptureAllowedState = true;
        }

        return result;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            OnFrameworkUpdateInner();
        }
        catch (Exception ex)
        {
            pendingRouletteSwitch = false;
            pendingRouletteGearsetId = -1;
            pendingRouletteClassJob = 0;
            pendingRouletteOriginalClassJob = 0;
            pendingRouletteNextAttemptUtc = DateTime.MinValue;

            requestCaptureAllowedState = false;
            requestCaptureSeenState = false;
            requestCountAcceptedJobSwitch = false;
            requestRevertToLockedJob = false;

            Plugin.Log.Error(ex, "JobManager framework update crashed.");
            PrintThrottled($"JobManager error: {ex.Message}");
        }
    }

    private void OnFrameworkUpdateInner()
    {
        var now = DateTime.UtcNow;
        var nowMs = Environment.TickCount64;

        UpdateRouletteEffectState();

        if (!Enabled && !IsQuotaEnabled() && !RouletteEnabled)
        {
            wasBlockingActive = false;
            quotaLockCaptureDueUtc = DateTime.MinValue;
            pendingRouletteSwitch = false;
            RemoveQuotaMoodleIfApplied();
            RemoveRouletteEffectIfApplied();

            if (IsPlayerReadyForGearsetAccess())
            {
                CaptureAllowedState();
                CaptureSeenState();
            }

            return;
        }

        if (nowMs >= nextQuotaMaintenanceMs)
        {
            nextQuotaMaintenanceMs = nowMs + 30000;

            if (PruneOldQuotaEntries())
                plugin.Configuration.Save();

            UpdateQuotaMoodleState();
        }

        if (nowMs < nextStateCheckMs)
            return;

        nextStateCheckMs = nowMs + 500;

        if (!IsPlayerReadyForGearsetAccess())
            return;

        if (requestCaptureAllowedState)
        {
            requestCaptureAllowedState = false;
            CaptureAllowedState();
        }

        if (requestCaptureSeenState)
        {
            requestCaptureSeenState = false;
            CaptureSeenState();
        }

        var currentJob = GetCurrentClassJob();
        if (currentJob == 0)
            return;

        if (lastSeenClassJob == 0)
        {
            CaptureSeenState();
            CaptureAllowedState();
            return;
        }

        // Give scheduled/pending roulette first chance to confirm itself,
        // so the generic job-change detector does not count roulette as manual.
        HandleJobRoulette();

        currentJob = GetCurrentClassJob();
        if (currentJob == 0)
            return;

        // Count accepted gearset switch requests that passed through the hook.
        if (requestCountAcceptedJobSwitch)
        {
            requestCountAcceptedJobSwitch = false;
            TryCountJobSwitchUsage();
        }

        // This catches job changes caused by equipping a different main-hand weapon,
        // or any other path that changes ClassJob without using EquipGearset.
        if (!internalGearsetChange &&
            !pendingRouletteSwitch &&
            currentJob != lastSeenClassJob)
        {
            if (ShouldRejectObservedJobChange(currentJob))
            {
                PrintThrottled(
                    $"Detected blocked job switch. Reverting from Job={currentJob} to Job={currentLockedJob.ClassJobId}, Gearset={currentLockedJob.GearsetId}");

                RevertToAllowedGearset();
                CaptureSeenState();
                return;
            }

            TryCountJobSwitchUsage();

            lastSeenClassJob = currentJob;
            lastSeenGearsetId = GetCurrentGearsetId();
        }

        if (IsQuotaFinalCaptureDue(now))
        {
            quotaLockCaptureDueUtc = DateTime.MinValue;

            // Capture exactly once: the completed final allowed job switch.
            CaptureAllowedState();
            CaptureSeenState();

            wasBlockingActive = true;
            UpdateQuotaMoodleState();

            Plugin.ChatGui.Print(
                $"Job quota empty. Locked to Job={currentLockedJob.ClassJobId}, Gearset={currentLockedJob.GearsetId}");
            return;
        }

        if (requestRevertToLockedJob)
        {
            requestRevertToLockedJob = false;

            if (CanChangeJobNow(out _))
                RevertToAllowedGearset();
        }

        HandleMoodleAndQuotaLocks(currentJob);
    }

    private void HandleMoodleAndQuotaLocks(byte currentJob)
    {
        // Roulette applies through an internal gearset change, but the client can need
        // a few framework ticks before CurrentGearsetIndex catches up. During that
        // settling window, do not let the normal Moodle/quota/roulette-lock rollback
        // treat the new roulette job as a forbidden manual change.
        if (pendingRouletteSwitch &&
            pendingRouletteClassJob != 0 &&
            currentJob == pendingRouletteClassJob)
        {
            return;
        }

        var blockingNow = ShouldBlock();
        cachedOldMoodleBlockActiveForDetour = Enabled && IsBlockMoodleActiveCached();

        // Moodle/quota just became active.
        if (blockingNow && !wasBlockingActive && !IsQuotaFinalCapturePending())
        {
            // Normal Moodle block may capture current state.
            // Already-empty quota must NOT capture current state, otherwise illegal
            // weapon swaps become the new locked job.
            if (!IsQuotaEnabled() || !IsQuotaExhausted())
                CaptureAllowedState();

            CaptureSeenState();
            wasBlockingActive = true;

            Plugin.ChatGui.Print(
                $"Job switch block active. Locked to Job={currentLockedJob.ClassJobId}, Gearset={currentLockedJob.GearsetId}");
        }

        // Moodle/quota just became inactive.
        if (!blockingNow && wasBlockingActive)
        {
            wasBlockingActive = false;
            CaptureAllowedState();
            return;
        }

        // During grace after spending the last quota, do not revert for quota alone.
        // Roulette manual-lock still wins here, because bypassed manual changes
        // must not become the final captured quota job.
        if (IsQuotaFinalCapturePending() && !IsRouletteLockActive)
            return;

        // While not blocking and roulette manual-lock is not active, keep allowed state fresh.
        if (!blockingNow && !IsRouletteLockActive)
        {
            CaptureAllowedState();
            return;
        }

        if (currentLockedJob.ClassJobId == 0)
        {
            CaptureAllowedState();
            return;
        }

        if (currentJob == currentLockedJob.ClassJobId)
            return;

        var shouldRevert =
            blockingNow ||
            IsRouletteLockActive;

        if (!shouldRevert)
            return;

        PrintThrottled(
            $"Detected blocked job switch. Reverting from Job={currentJob} to Job={currentLockedJob.ClassJobId}, Gearset={currentLockedJob.GearsetId}");

        RevertToAllowedGearset();
    }

    private bool ShouldPostponeRouletteForLockOrQuota(out string reason)
    {
        reason = string.Empty;

        // When this option is enabled, roulette is allowed to bypass normal job locks
        // and quota exhaustion. Manual changes are still controlled by their own lock logic.
        if (RouletteCanBypassLocksOrQuota)
            return false;

        if (Enabled && IsBlockMoodleActiveCached(forceRefresh: true))
        {
            reason = "job switch lock is active";
            return true;
        }

        if (IsQuotaEnabled() && IsQuotaExhausted())
        {
            reason = "job switch quota is empty";
            return true;
        }

        return false;
    }

    private void HandleJobRoulette()
    {
        if (DateTime.UtcNow < startupGraceUntilUtc)
            return;

        if (!RouletteEnabled)
            return;

        if (plugin.Configuration.JobRouletteWhitelistedGearsets.Count == 0)
            return;

        var now = DateTime.UtcNow;

        if (plugin.Configuration.NextScheduledJobSwitch == DateTime.MinValue)
        {
            ScheduleNextRouletteSwitch();
            return;
        }

        if (pendingRouletteSwitch)
        {
            TryCompletePendingRouletteSwitch();
            return;
        }

        if (now < plugin.Configuration.NextScheduledJobSwitch)
            return;

        if (ShouldPostponeRouletteForLockOrQuota(out var blockReason))
        {
            ScheduleNextRouletteSwitch();
            PrintThrottled($"Job roulette skipped because {blockReason}.");
            return;
        }

        TryRollRandomWhitelistedGearset();
    }

    private void TryCompletePendingRouletteSwitch()
    {
        if (!pendingRouletteSwitch)
            return;

        if (DateTime.UtcNow < pendingRouletteNextAttemptUtc)
            return;

        if (pendingRouletteGearsetId < 0)
        {
            ClearPendingRouletteSwitch();
            return;
        }

        if (ShouldPostponeRouletteForLockOrQuota(out var blockReason))
        {
            ClearPendingRouletteSwitch();
            ScheduleNextRouletteSwitch();
            PrintThrottled($"Job roulette cancelled because {blockReason}.");
            return;
        }

        if (!CanChangeJobNow(out var reason))
        {
            PostponeRouletteSwitch(reason, keepPending: true);
            return;
        }

        var module = RaptureGearsetModule.Instance();
        if (module == null)
        {
            PostponeRouletteSwitch("gearset module is unavailable", keepPending: true);
            return;
        }

        if (!module->IsValidGearset(pendingRouletteGearsetId))
        {
            RemoveMissingWhitelistEntry(pendingRouletteGearsetId);
            ClearPendingRouletteSwitch();
            ScheduleNextRouletteSwitch();
            return;
        }

        var target = module->GetGearset(pendingRouletteGearsetId);
        if (target == null || target->ClassJob != pendingRouletteClassJob)
        {
            RemoveMissingWhitelistEntry(pendingRouletteGearsetId);
            ClearPendingRouletteSwitch();
            ScheduleNextRouletteSwitch();
            return;
        }

        try
        {
            internalGearsetChange = true;

            var result = module->EquipGearset(pendingRouletteGearsetId, 0);
            if (result < 0)
            {
                PostponeRouletteSwitch("gearset equip failed", keepPending: true);
                return;
            }
        }
        finally
        {
            internalGearsetChange = false;
        }

        var currentJob = GetCurrentClassJob();
        var currentGearsetId = GetCurrentGearsetId();

        if (currentJob != pendingRouletteClassJob || currentGearsetId != pendingRouletteGearsetId)
        {
            // The equip request may need a bit to settle, or it may have failed silently.
            // Try again later and only reset the schedule after confirmation.
            PostponeRouletteSwitch("gearset switch has not completed yet", keepPending: true);
            return;
        }

        currentLockedJob = new JobLockState
        {
            ClassJobId = currentJob,
            GearsetId = currentGearsetId,
            GearsetName = TryGetCurrentGearsetName(),
            LockedAtUtc = DateTime.UtcNow,
        };

        lastSeenClassJob = currentJob;
        lastSeenGearsetId = currentGearsetId;

        var rouletteChangedJob =
            pendingRouletteOriginalClassJob != 0 &&
            pendingRouletteOriginalClassJob != currentJob;

        ClearPendingRouletteSwitch();

        // Roulette quota behavior:
        // - quota never blocks roulette;
        // - successful roulette counts only while quota remains;
        // - if quota was already empty, do not increment above the cap.
        if (rouletteChangedJob)
            TryCountJobSwitchUsage(ignoreCooldown: true, scheduleFinalCapture: false);

        UpdateQuotaMoodleState();
        ScheduleNextRouletteSwitch();

        Plugin.ChatGui.Print(
            $"Job roulette switched to {currentLockedJob.GearsetName} ({GetJobName(currentLockedJob.ClassJobId)}).");
    }

    private void ClearPendingRouletteSwitch()
    {
        pendingRouletteSwitch = false;
        pendingRouletteGearsetId = -1;
        pendingRouletteClassJob = 0;
        pendingRouletteOriginalClassJob = 0;
        pendingRouletteNextAttemptUtc = DateTime.MinValue;
    }

    private bool TryPickRandomWhitelistedGearset(out GearsetInfo selected)
    {
        selected = default;

        var available = GetAllGearsets();
        PruneMissingWhitelistedGearsets(available);

        var currentJob = GetCurrentClassJob();

        var whitelist = plugin.Configuration.JobRouletteWhitelistedGearsets;

        var candidates = available
            .Where(x => whitelist.Any(w => WhitelistEntryMatches(w, x)))
            .Where(x => x.ClassJobId != currentJob)
            .ToList();

        if (candidates.Count == 0)
            return false;

        selected = candidates[random.Next(candidates.Count)];
        return true;
    }

    private void PruneMissingWhitelistedGearsets(IReadOnlyList<GearsetInfo> existingGearsets)
    {
        var before = plugin.Configuration.JobRouletteWhitelistedGearsets.Count;

        plugin.Configuration.JobRouletteWhitelistedGearsets.RemoveAll(w =>
            !existingGearsets.Any(g => WhitelistEntryMatches(w, g)));

        if (plugin.Configuration.JobRouletteWhitelistedGearsets.Count != before)
            plugin.Configuration.Save();
    }

    private void RemoveMissingWhitelistEntry(int gearsetId)
    {
        var before = plugin.Configuration.JobRouletteWhitelistedGearsets.Count;

        plugin.Configuration.JobRouletteWhitelistedGearsets.RemoveAll(x =>
            x.GearsetId == gearsetId);

        if (plugin.Configuration.JobRouletteWhitelistedGearsets.Count != before)
            plugin.Configuration.Save();
    }

    private static bool WhitelistEntryMatches(
        JobRouletteGearsetConfig whitelistEntry,
        GearsetInfo gearset)
    {
        // Requirement: the gearset must still exist with the same name.
        // Also keep the id/job check so a renamed or overwritten gearset slot is pruned.
        return whitelistEntry.GearsetId == gearset.GearsetId
               && whitelistEntry.ClassJobId == gearset.ClassJobId
               && string.Equals(
                   whitelistEntry.GearsetName ?? string.Empty,
                   gearset.Name ?? string.Empty,
                   StringComparison.Ordinal);
    }

    private void ScheduleNextRouletteSwitch()
    {
        var interval = plugin.Configuration.JobRouletteInterval;
        if (interval <= TimeSpan.Zero)
            interval = TimeSpan.FromHours(1);

        plugin.Configuration.NextScheduledJobSwitch = DateTime.UtcNow + interval;
        plugin.Configuration.Save();
    }

    private void PostponeRouletteSwitch(string reason, bool keepPending)
    {
        if (keepPending)
        {
            pendingRouletteNextAttemptUtc = DateTime.UtcNow + RouletteRetryDelay;
        }
        else
        {
            ClearPendingRouletteSwitch();
        }

        plugin.Configuration.NextScheduledJobSwitch = DateTime.UtcNow + RouletteRetryDelay;
        plugin.Configuration.Save();

        PrintThrottled($"Job roulette postponed because {reason}.");
    }

    private bool CanChangeJobNow(out string reason)
    {
        reason = string.Empty;

        if (Plugin.ObjectTable.LocalPlayer == null)
        {
            reason = "your character is not available";
            return false;
        }

        if (!Plugin.Condition[ConditionFlag.NormalConditions])
        {
            reason = "you are not in normal conditions";
            return false;
        }

        if (Plugin.Condition.Any(
                ConditionFlag.InCombat,
                ConditionFlag.BetweenAreas,
                ConditionFlag.BetweenAreas51,
                ConditionFlag.WatchingCutscene,
                ConditionFlag.WatchingCutscene78,
                ConditionFlag.Occupied,
                ConditionFlag.Occupied30,
                ConditionFlag.OccupiedInEvent,
                ConditionFlag.OccupiedInQuestEvent,
                ConditionFlag.Occupied33,
                ConditionFlag.OccupiedInCutSceneEvent,
                ConditionFlag.Casting,
                ConditionFlag.Crafting,
                ConditionFlag.Gathering,
                ConditionFlag.Fishing,
                ConditionFlag.Performing,
                ConditionFlag.TradeOpen))
        {
            reason = "your current state does not allow job changes";
            return false;
        }

        return true;
    }

    private static bool IsPlayerReadyForGearsetAccess()
    {
        if (Plugin.ObjectTable.LocalPlayer == null)
            return false;

        if (!Plugin.Condition[ConditionFlag.NormalConditions])
            return false;

        if (Plugin.Condition.Any(
                ConditionFlag.BetweenAreas,
                ConditionFlag.BetweenAreas51,
                ConditionFlag.WatchingCutscene,
                ConditionFlag.WatchingCutscene78))
        {
            return false;
        }

        return RaptureGearsetModule.Instance() != null;
    }

    private byte GetGearsetClassJob(RaptureGearsetModule* gearsetModule, int gearsetId)
    {
        if (gearsetModule == null || gearsetId < 0 || gearsetId >= 100)
            return 0;

        if (!gearsetModule->IsValidGearset(gearsetId))
            return 0;

        var targetGearset = gearsetModule->GetGearset(gearsetId);
        return targetGearset != null
            ? targetGearset->ClassJob
            : (byte)0;
    }

    private bool ShouldRejectObservedJobChange(byte currentJob)
    {
        if (currentJob == 0)
            return false;

        if (currentLockedJob.ClassJobId == 0)
            return false;

        if (currentJob == currentLockedJob.ClassJobId)
            return false;

        if (Enabled && IsBlockMoodleActiveCached())
            return true;

        // This is the important bypass path: changing main-hand weapons can change
        // ClassJob without going through EquipGearsetDetour. Roulette lock must still
        // pull that back and must not spend quota for it.
        if (IsRouletteLockActive)
            return true;

        if (IsQuotaEnabled() && IsQuotaExhausted() && !IsQuotaFinalCapturePending())
            return true;

        return false;
    }

    private bool ShouldBlockTargetJob(byte targetClassJob)
    {
        if (targetClassJob == 0)
            return false;

        // Normal Moodle block only applies when JobSwitchBlockFeature is enabled.
        if (Enabled && IsBlockMoodleActiveCached())
        {
            return currentLockedJob.ClassJobId != 0 &&
                   targetClassJob != currentLockedJob.ClassJobId;
        }

        // Roulette lock is optional and only blocks manual/user changes.
        if (IsRouletteLockActive)
        {
            return currentLockedJob.ClassJobId != 0 &&
                   targetClassJob != currentLockedJob.ClassJobId;
        }

        if (!IsQuotaEnabled() || !IsQuotaExhausted())
            return false;

        // If the last quota was just spent, the final allowed job is not stable yet.
        // Block additional changes away from the current job, but don't use old
        // currentLockedJob until grace capture has completed.
        if (IsQuotaFinalCapturePending())
        {
            var currentJob = GetCurrentClassJob();
            return currentJob != 0 && targetClassJob != currentJob;
        }

        return currentLockedJob.ClassJobId != 0 &&
               targetClassJob != currentLockedJob.ClassJobId;
    }

    private bool ShouldBlock()
    {
        if (Enabled && IsBlockMoodleActiveCached())
            return true;

        if (IsQuotaEnabled() && IsQuotaExhausted())
            return true;

        return false;
    }

    private bool IsAnyJobSwitchBlockActive(bool forceRefresh = false)
    {
        if (Enabled && IsBlockMoodleActiveCached(forceRefresh))
            return true;

        if (IsQuotaEnabled() && IsQuotaExhausted())
            return true;

        return false;
    }

    private bool IsAnyJobSwitchBlockActiveFromCachedState()
    {
        if (cachedOldMoodleBlockActiveForDetour)
            return true;

        if (IsQuotaEnabled() && IsQuotaExhausted())
            return true;

        if (IsRouletteLockActive)
            return true;

        return false;
    }

    public bool IsBlockMoodleActiveCached(bool forceRefresh = false)
    {
        if (!Enabled)
        {
            cachedMoodleActive = false;
            return false;
        }

        var moodles = plugin.Configuration.JobSwitchBlockMoodles;
        if (moodles == null || moodles.Count == 0)
        {
            cachedMoodleActive = false;
            return false;
        }

        var now = Environment.TickCount64;
        if (!forceRefresh && now < nextMoodleRefreshMs)
            return cachedMoodleActive;

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

        cachedMoodleActive = anyActive;
        cachedOldMoodleBlockActiveForDetour = anyActive;
        nextMoodleRefreshMs = now + 5000;

        return cachedMoodleActive;
    }

    private void TryCountJobSwitchUsage(
        bool ignoreCooldown = false,
        bool scheduleFinalCapture = true)
    {
        if (internalGearsetChange)
            return;

        if (!IsQuotaEnabled())
            return;

        // If quota is already empty, this is not a spend.
        // Do NOT schedule another final capture.
        var remainingBefore = GetRemainingQuota();
        if ((!plugin.Configuration.JobRouletteSpendOutOfQuotaLimit && remainingBefore <= 0)
            || plugin.Configuration.JobSwitchQuotaEnabled == false)
            return;

        if (!ignoreCooldown && jobSwitchCountCooldown > DateTime.UtcNow)
            return;

        if (!ignoreCooldown)
            jobSwitchCountCooldown = DateTime.UtcNow + JobSwitchCountCooldown;

        LogJobSwitchAction();

        var remainingAfter = GetRemainingQuota();

        // Safe with the strict state machine; lets Moodle switch quickly after usage changes.
        UpdateQuotaMoodleState();

        // Only manual/user action that spends the final quota gets grace capture.
        // Roulette confirms its target before counting, so it does not need grace capture.
        if (scheduleFinalCapture && remainingBefore > 0 && remainingAfter <= 0)
            ScheduleQuotaFinalCapture();
    }

    private void ScheduleQuotaFinalCapture()
    {
        if (!IsQuotaEnabled())
            return;

        if (!IsQuotaExhausted())
            return;

        // Already scheduled. Do not refresh/extend it.
        if (quotaLockCaptureDueUtc > DateTime.UtcNow)
            return;

        // Already in blocking mode from quota. Do not schedule again.
        if (wasBlockingActive)
            return;

        quotaLockCaptureDueUtc = DateTime.UtcNow + QuotaFinalCaptureGrace;

        Plugin.ChatGui.Print("Job quota empty. Waiting briefly for gear change to complete before locking current job.");
    }

    private bool IsQuotaFinalCapturePending()
    {
        return quotaLockCaptureDueUtc > DateTime.UtcNow;
    }

    private bool IsQuotaFinalCaptureDue(DateTime now)
    {
        return quotaLockCaptureDueUtc != DateTime.MinValue &&
               now >= quotaLockCaptureDueUtc;
    }

    private bool IsQuotaEnabled()
    {
        return plugin.Configuration.JobSwitchQuotaEnabled &&
               plugin.Configuration.JobSwitchQuotaActions > -1;
    }

    private TimeSpan GetQuotaWindow()
    {
        return plugin.Configuration.JobSwitchQuotaWindow switch
        {
            QuotaWindow.Day => TimeSpan.FromHours(24),
            _ => TimeSpan.FromHours(1),
        };
    }

    public int GetUsedQuotaCount()
    {
        if (!IsQuotaEnabled())
            return 0;

        EnsureQuotaLog();

        var cutoff = DateTime.UtcNow - GetQuotaWindow();
        var count = 0;

        foreach (var entryUtc in plugin.Configuration.JobSwitchQuotaActionLogUtc)
        {
            if (entryUtc >= cutoff)
                count++;
        }

        return count;
    }

    public int GetRemainingQuota()
    {
        if (!IsQuotaEnabled())
            return int.MaxValue;

        var remaining = plugin.Configuration.JobSwitchQuotaActions - GetUsedQuotaCount();
        return Math.Max(0, remaining);
    }

    public bool IsQuotaExhausted()
    {
        if (!IsQuotaEnabled())
            return false;

        PruneOldQuotaEntries();

        return GetRemainingQuota() <= 0;
    }

    private void LogJobSwitchAction()
    {
        if (!IsQuotaEnabled())
            return;

        EnsureQuotaLog();
        PruneOldQuotaEntries();

        plugin.Configuration.JobSwitchQuotaActionLogUtc.Add(DateTime.UtcNow);
        plugin.Configuration.Save();

        Plugin.ChatGui.Print($"Job switch usage counted, remaining: {GetRemainingQuota()}");
    }

    private bool PruneOldQuotaEntries()
    {
        EnsureQuotaLog();

        // Old behavior pruned to 24 hours because Day is the maximum supported quota window.
        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(24);

        var before = plugin.Configuration.JobSwitchQuotaActionLogUtc.Count;
        plugin.Configuration.JobSwitchQuotaActionLogUtc.RemoveAll(x => x < cutoff);

        return plugin.Configuration.JobSwitchQuotaActionLogUtc.Count != before;
    }

    private void EnsureQuotaLog()
    {
        plugin.Configuration.JobSwitchQuotaActionLogUtc ??= new List<DateTime>();
    }

    private void RegisterRouletteMoodle()
    {
        plugin.MoodleEnforcer.RegisterExternalMoodle(plugin.Configuration.JobRouletteEffect.MoodleId, JobManagerRouletteMoodleSource);
    }

    private void UpdateRouletteEffectState()
    {
        RegisterRouletteMoodle();
        var config = plugin.Configuration.JobRouletteEffect;
        var shouldBeActive = RouletteEnabled && plugin.Configuration.JobRouletteWhitelistedGearsets.Count > 0;

        SetRouletteMoodleRequest(shouldBeActive ? config.MoodleId : Guid.Empty);
        SetRouletteHonorificRequest(shouldBeActive ? config : null);
    }

    private void SetRouletteMoodleRequest(Guid wantedMoodleId)
    {
        if (requestedRouletteMoodleId == wantedMoodleId)
            return;

        if (requestedRouletteMoodleId != Guid.Empty)
        {
            plugin.MoodleEnforcer.RemoveEnforcedMoodle(JobManagerRouletteMoodleSource);
            requestedRouletteMoodleId = Guid.Empty;
        }

        if (wantedMoodleId == Guid.Empty)
            return;

        plugin.MoodleEnforcer.AddEnforcedMoodle(wantedMoodleId, JobManagerRouletteMoodleSource);
        requestedRouletteMoodleId = wantedMoodleId;
    }

    private void SetRouletteHonorificRequest(JobRouletteEffectConfig? config)
    {
        if (config == null)
        {
            RemoveRouletteHonorificRequest();
            return;
        }

        var json = BuildRouletteHonorificJson(config);
        if (string.IsNullOrWhiteSpace(json))
        {
            RemoveRouletteHonorificRequest();
            return;
        }

        if (hasSubmittedRouletteHonorificRequest && lastSubmittedRouletteHonorificPriority == config.HonorificPriority && string.Equals(lastSubmittedRouletteHonorificJson, json, StringComparison.Ordinal))
            return;

        plugin.HonorificManager.SetTitle(json, config.HonorificPriority, this);
        hasSubmittedRouletteHonorificRequest = true;
        lastSubmittedRouletteHonorificJson = json;
        lastSubmittedRouletteHonorificPriority = config.HonorificPriority;
    }

    private string BuildRouletteHonorificJson(JobRouletteEffectConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.HonorificSourceJson))
            return config.HonorificSourceJson;

        return plugin.HonorificManager.BuildTitleJson(config.HonorificTitle, config.HonorificColor, config.HonorificGlow);
    }

    private void RemoveRouletteHonorificRequest()
    {
        if (!hasSubmittedRouletteHonorificRequest)
            return;

        plugin.HonorificManager.RecallTitle(this);
        hasSubmittedRouletteHonorificRequest = false;
        lastSubmittedRouletteHonorificJson = string.Empty;
        lastSubmittedRouletteHonorificPriority = -1;
    }

    private void RemoveRouletteEffectIfApplied()
    {
        SetRouletteMoodleRequest(Guid.Empty);
        RemoveRouletteHonorificRequest();
    }


    private void RegisterQuotaMoodles()
    {
        plugin.MoodleEnforcer.RegisterExternalMoodle(plugin.Configuration.JobSwitchQuotaMoodleId, JobManagerQuotaRunningMoodleSource);
        plugin.MoodleEnforcer.RegisterExternalMoodle(plugin.Configuration.JobSwitchQuotaEmptyMoodleId, JobManagerQuotaEmptyMoodleSource);
    }

    private void UpdateQuotaMoodleState()
    {
        RegisterQuotaMoodles();
        var runningMoodleId = plugin.Configuration.JobSwitchQuotaMoodleId;
        var emptyMoodleId = plugin.Configuration.JobSwitchQuotaEmptyMoodleId;

        if (!IsQuotaEnabled())
        {
            SetQuotaMoodleRequest(ref requestedQuotaRunningMoodleId, Guid.Empty, JobManagerQuotaRunningMoodleSource);
            SetQuotaMoodleRequest(ref requestedQuotaEmptyMoodleId, Guid.Empty, JobManagerQuotaEmptyMoodleSource);
            return;
        }

        // Compute quota state once for this update.
        // Do not call IsQuotaExhausted() through another helper, because that causes
        // extra prune/recount work and makes Moodle transitions harder to reason about.
        EnsureQuotaLog();

        var remaining = GetRemainingQuota();
        var quotaEmpty = remaining <= 0;

        var wantedRunningMoodleId = !quotaEmpty ? runningMoodleId : Guid.Empty;
        var wantedEmptyMoodleId = quotaEmpty ? emptyMoodleId : Guid.Empty;

        // If both quota states are configured to the same Moodle, treat it as one stable request.
        // This avoids remove/add churn when crossing between "running" and "empty".
        if (runningMoodleId != Guid.Empty &&
            runningMoodleId == emptyMoodleId)
        {
            wantedRunningMoodleId = runningMoodleId;
            wantedEmptyMoodleId = Guid.Empty;
        }

        SetQuotaMoodleRequest(ref requestedQuotaRunningMoodleId, wantedRunningMoodleId, JobManagerQuotaRunningMoodleSource);
        SetQuotaMoodleRequest(ref requestedQuotaEmptyMoodleId, wantedEmptyMoodleId, JobManagerQuotaEmptyMoodleSource);
    }

    private void SetQuotaMoodleRequest(ref Guid currentlyRequestedMoodleId, Guid wantedMoodleId, string sourceKey)
    {
        // Already correct for this slot.
        if (currentlyRequestedMoodleId == wantedMoodleId)
            return;

        if (currentlyRequestedMoodleId != Guid.Empty)
        {
            plugin.MoodleEnforcer.RemoveEnforcedMoodle(sourceKey);
            currentlyRequestedMoodleId = Guid.Empty;
        }

        if (wantedMoodleId == Guid.Empty)
            return;

        plugin.MoodleEnforcer.AddEnforcedMoodle(wantedMoodleId, sourceKey);
        currentlyRequestedMoodleId = wantedMoodleId;
    }

    private void RemoveQuotaMoodleIfApplied()
    {
        SetQuotaMoodleRequest(ref requestedQuotaRunningMoodleId, Guid.Empty, JobManagerQuotaRunningMoodleSource);
        SetQuotaMoodleRequest(ref requestedQuotaEmptyMoodleId, Guid.Empty, JobManagerQuotaEmptyMoodleSource);
    }

    private void CaptureAllowedState()
    {
        if (!IsPlayerReadyForGearsetAccess())
            return;

        var currentJob = GetCurrentClassJob();
        if (currentJob != 0)
            currentLockedJob.ClassJobId = currentJob;

        var currentGearset = GetCurrentGearsetId();
        if (currentGearset >= 0)
        {
            currentLockedJob.GearsetId = currentGearset;
            currentLockedJob.GearsetName = TryGetCurrentGearsetName();
            currentLockedJob.LockedAtUtc = DateTime.UtcNow;
        }
    }

    private void CaptureSeenState()
    {
        if (!IsPlayerReadyForGearsetAccess())
            return;

        var currentJob = GetCurrentClassJob();
        if (currentJob != 0)
            lastSeenClassJob = currentJob;

        lastSeenGearsetId = GetCurrentGearsetId();
    }

    private int GetCurrentGearsetId()
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return -1;

        var currentGearset = module->CurrentGearsetIndex;

        if (currentGearset >= 0 && currentGearset < 100 && module->IsValidGearset(currentGearset))
            return currentGearset;

        return -1;
    }

    private void RevertToAllowedGearset()
    {
        if (currentLockedJob.GearsetId < 0)
        {
            PrintThrottled("Cannot revert job switch: no valid previous gearset stored.");
            return;
        }

        if (!CanChangeJobNow(out _))
            return;

        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return;

        if (!module->IsValidGearset(currentLockedJob.GearsetId))
        {
            PrintThrottled($"Cannot revert job switch: stored gearset {currentLockedJob.GearsetId} is no longer valid.");
            return;
        }

        try
        {
            internalGearsetChange = true;

            // glamourPlateId = 0 means use the linked gearset plate if any.
            module->EquipGearset(currentLockedJob.GearsetId, 0);
        }
        finally
        {
            internalGearsetChange = false;
        }
    }

    private byte GetCurrentClassJob()
    {
        return Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId is uint rowId
            ? (byte)rowId
            : (byte)0;
    }

    private string TryGetCurrentGearsetName()
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return string.Empty;

        var currentGearset = module->CurrentGearsetIndex;
        if (currentGearset < 0 || currentGearset >= 100 || !module->IsValidGearset(currentGearset))
            return string.Empty;

        var gearset = module->GetGearset(currentGearset);
        return gearset == null ? string.Empty : TryGetGearsetName(gearset);
    }

    private static string TryGetGearsetName(RaptureGearsetModule.GearsetEntry* gearset)
    {
        if (gearset == null)
            return string.Empty;

        try
        {
            return gearset->NameString;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetJobName(byte classJobId)
    {
        try
        {
            var row = Plugin.DataManager.GetExcelSheet<ClassJob>()?.GetRow(classJobId);
            var name = row?.Name.ToString();

            return string.IsNullOrWhiteSpace(name)
                ? $"Job {classJobId}"
                : name;
        }
        catch
        {
            return $"Job {classJobId}";
        }
    }

    private void PrintThrottled(string message)
    {
        var now = Environment.TickCount64;

        if (now < nextPrintMs)
            return;

        nextPrintMs = now + 2000;

        Plugin.ChatGui.Print(message);
    }

    public sealed class JobLockState
    {
        public byte ClassJobId { get; set; }
        public int GearsetId { get; set; } = -1;
        public string GearsetName { get; set; } = string.Empty;
        public DateTime LockedAtUtc { get; set; } = DateTime.MinValue;
    }

    public readonly record struct GearsetInfo
    {
        public int GearsetId { get; init; }
        public string Name { get; init; }
        public byte ClassJobId { get; init; }
        public string JobName { get; init; }

        public string DisplayName =>
            string.IsNullOrWhiteSpace(Name)
                ? $"Gearset {GearsetId} - {JobName}"
                : $"{GearsetId}: {Name} - {JobName}";
    }
}
