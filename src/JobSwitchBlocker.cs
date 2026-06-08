using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System;
using System.Collections.Generic;
using static SayusGagExtender.Configuration;

namespace SayusGagExtender;

public unsafe sealed class JobSwitchBlocker : IDisposable
{
    private readonly Plugin plugin;

    private delegate int EquipGearsetDelegate(
        RaptureGearsetModule* gearsetModule,
        int gearsetId,
        byte glamourPlateId);

    private readonly Hook<EquipGearsetDelegate> equipGearsetHook;

    private byte lastAllowedClassJob;
    private int lastAllowedGearsetId = -1;

    private byte lastSeenClassJob;
    private int lastSeenGearsetId = -1;

    private bool reverting;

    private long nextStateCheckMs;
    private long nextMoodleRefreshMs;
    private long nextPrintMs;
    private long nextQuotaMaintenanceMs;

    private bool cachedMoodleActive;
    private bool wasBlockingActive;

    private Guid requestedQuotaRunningMoodleId = Guid.Empty;
    private Guid requestedQuotaEmptyMoodleId = Guid.Empty;

    private const string JobSwitchQuotaRunningMoodleSource = "JobSwitchBlocker.QuotaRunning";
    private const string JobSwitchQuotaEmptyMoodleSource = "JobSwitchBlocker.QuotaEmpty";

    private DateTime jobSwitchCountCooldown = DateTime.MinValue;

    // When the last quota is spent, do not immediately lock/revert.
    // Give the gear/job change time to finish, then capture the final state.
    private DateTime quotaLockCaptureDueUtc = DateTime.MinValue;

    private static readonly TimeSpan JobSwitchCountCooldown = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan QuotaFinalCaptureGrace = TimeSpan.FromSeconds(10);

    public bool Enabled => plugin.Configuration.JobSwitchBlockFeature;
    public bool IsActive => this.IsAnyJobSwitchBlockActive();
    public bool QuotaActive => this.IsQuotaEnabled();
    public bool QuotaEmpty => this.IsQuotaExhausted();

    public JobSwitchBlocker(Plugin plugin)
    {
        this.plugin = plugin;

        this.CaptureAllowedState();
        this.CaptureSeenState();

        this.equipGearsetHook = Plugin.GameInterop.HookFromAddress<EquipGearsetDelegate>(
            (nint)RaptureGearsetModule.MemberFunctionPointers.EquipGearset,
            this.EquipGearsetDetour);

        this.equipGearsetHook.Enable();

        Plugin.Framework.Update += this.OnFrameworkUpdate;
        this.RegisterQuotaMoodles();
    }

    public void Enable()
    {
        plugin.Configuration.JobSwitchBlockFeature = true;
        this.CaptureAllowedState();
        this.CaptureSeenState();
    }

    public void Disable()
    {
        plugin.Configuration.JobSwitchBlockFeature = false;

        // Do not remove quota Moodle here if quota is still enabled.
        // Framework update will derive the correct state.
        if (!this.IsQuotaEnabled())
        {
            this.wasBlockingActive = false;
            this.quotaLockCaptureDueUtc = DateTime.MinValue;
            this.RemoveQuotaMoodleIfApplied();
        }
    }

    private int EquipGearsetDetour(
        RaptureGearsetModule* gearsetModule,
        int gearsetId,
        byte glamourPlateId)
    {
        var targetClassJob = this.GetGearsetClassJob(gearsetModule, gearsetId);

        if (!this.reverting && targetClassJob != 0 && this.ShouldBlockTargetJob(targetClassJob) && plugin.MirrorGagSpeak.IsMasterCharacter())
        {
            this.PrintThrottled("Blocked gearset change");
            return -1;
        }

        var beforeJob = this.GetCurrentClassJob();

        var result = this.equipGearsetHook.Original(
            gearsetModule,
            gearsetId,
            glamourPlateId);

        // Count accepted gearset switches early.
        // Framework polling will also catch main-hand weapon job changes.
        if (!this.reverting &&
            result >= 0 &&
            targetClassJob != 0 &&
            beforeJob != 0 &&
            targetClassJob != beforeJob)
        {
            this.TryCountJobSwitchUsage();
        }

        // If not blocking, keep current state fresh.
        // If quota just hit zero, this will be held by the grace logic instead.
        if (!this.ShouldBlock() && !this.IsQuotaFinalCapturePending() && plugin.MirrorGagSpeak.IsMasterCharacter())
            this.CaptureAllowedState();

        return result;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;
        var nowMs = Environment.TickCount64;

        if (!this.Enabled && !this.IsQuotaEnabled())
        {
            this.wasBlockingActive = false;
            this.quotaLockCaptureDueUtc = DateTime.MinValue;
            this.RemoveQuotaMoodleIfApplied();
            this.CaptureAllowedState();
            this.CaptureSeenState();
            return;
        }
        if (!plugin.MirrorGagSpeak.IsMasterCharacter())
            return;

        if (nowMs >= this.nextQuotaMaintenanceMs)
        {
            this.nextQuotaMaintenanceMs = nowMs + 30000;

            if (this.PruneOldQuotaEntries())
                plugin.Configuration.Save();

            this.UpdateQuotaMoodleState();
        }

        // Check job state fairly often, but Moodle lookup is still throttled
        // inside ShouldBlock() / IsBlockMoodleActiveCached().
        if (nowMs < this.nextStateCheckMs)
            return;

        this.nextStateCheckMs = nowMs + 500;

        var currentJob = this.GetCurrentClassJob();
        if (currentJob == 0)
            return;

        // Initialize last-seen state once the player is available.
        if (this.lastSeenClassJob == 0)
        {
            this.CaptureSeenState();
            this.CaptureAllowedState();
            return;
        }

        // This catches job changes caused by equipping a different main-hand weapon,
        // or any other path that changes ClassJob without using EquipGearset.
        if (!this.reverting && currentJob != this.lastSeenClassJob)
        {
            this.TryCountJobSwitchUsage();

            this.lastSeenClassJob = currentJob;
            this.lastSeenGearsetId = this.GetCurrentGearsetId();
        }

        if (this.IsQuotaFinalCaptureDue(now))
        {
            this.quotaLockCaptureDueUtc = DateTime.MinValue;

            // Capture exactly once: the completed final allowed job switch.
            this.CaptureAllowedState();
            this.CaptureSeenState();

            this.wasBlockingActive = true;
            this.UpdateQuotaMoodleState();

            Plugin.ChatGui.Print($"Job quota empty. Locked to Job={this.lastAllowedClassJob}, Gearset={this.lastAllowedGearsetId}");
            return;
        }

        var blockingNow = this.ShouldBlock();

        // Moodle/quota just became active.
        if (blockingNow && !this.wasBlockingActive && !this.IsQuotaFinalCapturePending())
        {
            // Normal Moodle block may capture current state.
            // Already-empty quota must NOT capture current state, otherwise illegal
            // weapon swaps become the new locked job.
            if (!this.IsQuotaEnabled() || !this.IsQuotaExhausted())
                this.CaptureAllowedState();

            this.CaptureSeenState();
            this.wasBlockingActive = true;

            Plugin.ChatGui.Print($"Job switch block active. Locked to Job={this.lastAllowedClassJob}, Gearset={this.lastAllowedGearsetId}");
        }

        // Moodle/quota just became inactive.
        if (!blockingNow && this.wasBlockingActive)
        {
            this.wasBlockingActive = false;
            this.CaptureAllowedState();
            return;
        }

        // During the grace after spending the last quota, do not revert.
        // The gear/job change is still settling and will be captured when the grace expires.
        if (this.IsQuotaFinalCapturePending())
            return;

        // While not blocking, keep allowed state fresh.
        if (!blockingNow)
        {
            this.CaptureAllowedState();
            return;
        }

        if (this.lastAllowedClassJob == 0)
        {
            this.CaptureAllowedState();
            return;
        }

        if (currentJob == this.lastAllowedClassJob)
            return;

        this.PrintThrottled($"Detected blocked job switch. Reverting from Job={currentJob} to Job={this.lastAllowedClassJob}, Gearset={this.lastAllowedGearsetId}");

        this.RevertToAllowedGearset();
    }

    private byte GetGearsetClassJob(RaptureGearsetModule* gearsetModule, int gearsetId)
    {
        if (gearsetModule == null || gearsetId < 0 || gearsetId >= 100)
            return 0;

        var targetGearset = gearsetModule->GetGearset(gearsetId);
        return targetGearset != null
            ? targetGearset->ClassJob
            : (byte)0;
    }

    private bool ShouldBlockTargetJob(byte targetClassJob)
    {
        if (targetClassJob == 0)
            return false;

        // Normal Moodle block only applies when JobSwitchBlockFeature is enabled.
        if (this.Enabled && this.IsBlockMoodleActiveCached())
        {
            return this.lastAllowedClassJob != 0 &&
                   targetClassJob != this.lastAllowedClassJob;
        }

        if (!this.IsQuotaEnabled() || !this.IsQuotaExhausted())
            return false;

        // If the last quota was just spent, the final allowed job is not stable yet.
        // Block additional changes away from the current job, but don't use old
        // lastAllowedClassJob until grace capture has completed.
        if (this.IsQuotaFinalCapturePending())
        {
            var currentJob = this.GetCurrentClassJob();
            return currentJob != 0 && targetClassJob != currentJob;
        }

        return this.lastAllowedClassJob != 0 &&
               targetClassJob != this.lastAllowedClassJob;
    }

    private bool ShouldBlock()
    {
        if (this.Enabled && this.IsBlockMoodleActiveCached())
            return true;

        if (this.IsQuotaEnabled() && this.IsQuotaExhausted())
            return true;

        return false;
    }

    private bool IsAnyJobSwitchBlockActive(bool forceRefresh = false)
    {
        if (this.Enabled && this.IsBlockMoodleActiveCached(forceRefresh))
            return true;

        if (this.IsQuotaEnabled() && this.IsQuotaExhausted())
            return true;

        return false;
    }

    public bool IsBlockMoodleActiveCached(bool forceRefresh = false)
    {
        if (!this.Enabled)
        {
            this.cachedMoodleActive = false;
            return false;
        }

        var moodles = plugin.Configuration.JobSwitchBlockMoodles;
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

    private void TryCountJobSwitchUsage()
    {
        if (this.reverting)
            return;

        if (!this.IsQuotaEnabled())
            return;

        if (!plugin.MirrorGagSpeak.IsMasterCharacter())
            return;

        // If quota is already empty, this is not a spend.
        // Do NOT schedule another final capture.
        var remainingBefore = this.GetRemainingQuota();
        if (remainingBefore <= 0)
            return;

        if (this.jobSwitchCountCooldown > DateTime.UtcNow)
            return;

        this.jobSwitchCountCooldown = DateTime.UtcNow + JobSwitchCountCooldown;

        this.LogJobSwitchAction();

        var remainingAfter = this.GetRemainingQuota();

        // Safe with the strict state machine; lets Moodle switch quickly after usage changes.
        this.UpdateQuotaMoodleState();

        // Only the action that spends the final quota gets the grace capture.
        if (remainingBefore > 0 && remainingAfter <= 0)
            this.ScheduleQuotaFinalCapture();
    }

    private void ScheduleQuotaFinalCapture()
    {
        if (!this.IsQuotaEnabled())
            return;

        if (!this.IsQuotaExhausted())
            return;

        // Already scheduled. Do not refresh/extend it.
        if (this.quotaLockCaptureDueUtc > DateTime.UtcNow)
            return;

        // Already in blocking mode from quota. Do not schedule again.
        if (this.wasBlockingActive)
            return;

        this.quotaLockCaptureDueUtc = DateTime.UtcNow + QuotaFinalCaptureGrace;

        Plugin.ChatGui.Print("Job quota empty. Waiting briefly for gear change to complete before locking current job.");
    }

    private bool IsQuotaFinalCapturePending()
    {
        return this.quotaLockCaptureDueUtc > DateTime.UtcNow;
    }

    private bool IsQuotaFinalCaptureDue(DateTime now)
    {
        return this.quotaLockCaptureDueUtc != DateTime.MinValue &&
               now >= this.quotaLockCaptureDueUtc;
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

    private int GetUsedQuotaCount()
    {
        if (!this.IsQuotaEnabled())
            return 0;

        this.EnsureQuotaLog();

        var cutoff = DateTime.UtcNow - this.GetQuotaWindow();
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
        if (!this.IsQuotaEnabled())
            return int.MaxValue;

        var remaining = plugin.Configuration.JobSwitchQuotaActions - this.GetUsedQuotaCount();
        return Math.Max(0, remaining);
    }

    public bool IsQuotaExhausted()
    {
        if (!this.IsQuotaEnabled())
            return false;

        this.PruneOldQuotaEntries();

        return this.GetRemainingQuota() <= 0;
    }

    private void LogJobSwitchAction()
    {
        if (!this.IsQuotaEnabled())
            return;

        this.EnsureQuotaLog();
        this.PruneOldQuotaEntries();

        plugin.Configuration.JobSwitchQuotaActionLogUtc.Add(DateTime.UtcNow);
        plugin.Configuration.Save();

        Plugin.ChatGui.Print($"Job switch usage counted, remaining: {this.GetRemainingQuota()}");
    }

    private bool PruneOldQuotaEntries()
    {
        this.EnsureQuotaLog();

        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(24);

        var before = plugin.Configuration.JobSwitchQuotaActionLogUtc.Count;
        plugin.Configuration.JobSwitchQuotaActionLogUtc.RemoveAll(x => x < cutoff);

        return plugin.Configuration.JobSwitchQuotaActionLogUtc.Count != before;
    }

    private void EnsureQuotaLog()
    {
        plugin.Configuration.JobSwitchQuotaActionLogUtc ??= new List<DateTime>();
    }

    private void RegisterQuotaMoodles()
    {
        plugin.MoodleEnforcer.RegisterExternalMoodle(plugin.Configuration.JobSwitchQuotaMoodleId, JobSwitchQuotaRunningMoodleSource);
        plugin.MoodleEnforcer.RegisterExternalMoodle(plugin.Configuration.JobSwitchQuotaEmptyMoodleId, JobSwitchQuotaEmptyMoodleSource);
    }

    private void UpdateQuotaMoodleState()
    {
        this.RegisterQuotaMoodles();
        var runningMoodleId = plugin.Configuration.JobSwitchQuotaMoodleId;
        var emptyMoodleId = plugin.Configuration.JobSwitchQuotaEmptyMoodleId;

        if (!this.IsQuotaEnabled())
        {
            this.SetQuotaMoodleRequest(ref this.requestedQuotaRunningMoodleId, Guid.Empty, JobSwitchQuotaRunningMoodleSource);
            this.SetQuotaMoodleRequest(ref this.requestedQuotaEmptyMoodleId, Guid.Empty, JobSwitchQuotaEmptyMoodleSource);
            return;
        }

        // Compute quota state once for this update.
        // Do not call IsQuotaExhausted() through another helper, because that causes
        // extra prune/recount work and makes Moodle transitions harder to reason about.
        this.EnsureQuotaLog();

        var remaining = this.GetRemainingQuota();
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

        this.SetQuotaMoodleRequest(ref this.requestedQuotaRunningMoodleId, wantedRunningMoodleId, JobSwitchQuotaRunningMoodleSource);
        this.SetQuotaMoodleRequest(ref this.requestedQuotaEmptyMoodleId, wantedEmptyMoodleId, JobSwitchQuotaEmptyMoodleSource);
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
        this.SetQuotaMoodleRequest(ref this.requestedQuotaRunningMoodleId, Guid.Empty, JobSwitchQuotaRunningMoodleSource);
        this.SetQuotaMoodleRequest(ref this.requestedQuotaEmptyMoodleId, Guid.Empty, JobSwitchQuotaEmptyMoodleSource);
    }

    private void CaptureAllowedState()
    {
        _ = Plugin.Framework.RunOnFrameworkThread(() =>
        {
            var currentJob = this.GetCurrentClassJob();
            if (currentJob != 0)
                this.lastAllowedClassJob = currentJob;

            var currentGearset = this.GetCurrentGearsetId();
            if (currentGearset >= 0)
                this.lastAllowedGearsetId = currentGearset;
        });
    }

    private void CaptureSeenState()
    {
        _ = Plugin.Framework.RunOnFrameworkThread(() =>
        {
            var currentJob = this.GetCurrentClassJob();
            if (currentJob != 0)
                this.lastSeenClassJob = currentJob;

            this.lastSeenGearsetId = this.GetCurrentGearsetId();
        });
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
        if (this.lastAllowedGearsetId < 0)
        {
            this.PrintThrottled("Cannot revert job switch: no valid previous gearset stored.");
            return;
        }

        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return;

        if (!module->IsValidGearset(this.lastAllowedGearsetId))
        {
            this.PrintThrottled($"Cannot revert job switch: stored gearset {this.lastAllowedGearsetId} is no longer valid.");
            return;
        }

        try
        {
            this.reverting = true;

            // glamourPlateId = 0 means use the linked gearset plate if any.
            module->EquipGearset(this.lastAllowedGearsetId, 0);
        }
        finally
        {
            this.reverting = false;
        }
    }

    private byte GetCurrentClassJob()
    {
        return Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId is uint rowId
            ? (byte)rowId
            : (byte)0;
    }

    private void PrintThrottled(string message)
    {
        var now = Environment.TickCount64;

        if (now < this.nextPrintMs)
            return;

        this.nextPrintMs = now + 2000;

        Plugin.ChatGui.Print(message);
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= this.OnFrameworkUpdate;

        this.RemoveQuotaMoodleIfApplied();

        this.equipGearsetHook.Dispose();
    }
}
