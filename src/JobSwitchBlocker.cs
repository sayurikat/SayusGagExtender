using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System;

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

    private bool reverting;

    private long nextStateCheckMs;
    private long nextMoodleRefreshMs;
    private long nextPrintMs;

    private bool cachedMoodleActive;
    private bool wasBlockingActive;

    public bool Enabled => plugin.Configuration.JobSwitchBlockFeature;
    public bool IsActive => IsBlockMoodleActiveCached();

    public JobSwitchBlocker(Plugin plugin)
    {
        this.plugin = plugin;

        this.CaptureAllowedState();

        this.equipGearsetHook = Plugin.GameInterop.HookFromAddress<EquipGearsetDelegate>(
            (nint)RaptureGearsetModule.MemberFunctionPointers.EquipGearset,
            this.EquipGearsetDetour);

        this.equipGearsetHook.Enable();

        Plugin.Framework.Update += this.OnFrameworkUpdate;
    }

    public void Enable()
    {
        plugin.Configuration.JobSwitchBlockFeature = true;
        this.CaptureAllowedState();
    }

    public void Disable()
    {
        plugin.Configuration.JobSwitchBlockFeature = false;
        Plugin.Framework.Update -= this.OnFrameworkUpdate;
    }

    private int EquipGearsetDetour(
        RaptureGearsetModule* gearsetModule,
        int gearsetId,
        byte glamourPlateId)
    {
        if (!this.reverting && this.ShouldBlock())
        {
            var targetGearset = gearsetModule != null && gearsetId >= 0 && gearsetId < 100
                ? gearsetModule->GetGearset(gearsetId)
                : null;

            var targetClassJob = targetGearset != null
                ? targetGearset->ClassJob
                : (byte)0;

            // If it would switch class/job, block it.
            // This still allows re-equipping the same job gearset if you want.
            if (targetClassJob != 0 && targetClassJob != this.lastAllowedClassJob)
            {
                //this.PrintThrottled($"Blocked gearset change due to active moodle: Gearset={gearsetId}, TargetJob={targetClassJob}, Moodle={plugin.Configuration.JobSwitchBlockMoodle}");
                this.PrintThrottled($"Blocked gearset change");

                return -1;
            }
        }

        var result = this.equipGearsetHook.Original(
            gearsetModule,
            gearsetId,
            glamourPlateId);

        // If not blocked, keep current state fresh.
        // This matters when the Moodle is inactive and the user legitimately swaps jobs.
        if (!this.ShouldBlock())
            this.CaptureAllowedState();

        return result;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!this.Enabled)
        {
            this.wasBlockingActive = false;
            this.CaptureAllowedState();
            return;
        }

        var now = Environment.TickCount64;

        // Check job state fairly often, but Moodle lookup is still throttled
        // inside ShouldBlock() / IsBlockMoodleActiveCached().
        if (now < this.nextStateCheckMs)
            return;

        this.nextStateCheckMs = now + 500;

        var blockingNow = this.ShouldBlock();

        // Moodle just became active:
        // lock the job/gearset the player currently has.
        if (blockingNow && !this.wasBlockingActive)
        {
            this.CaptureAllowedState();
            this.wasBlockingActive = true;

            Plugin.ChatGui.Print($"Job switch block active. Locked to Job={this.lastAllowedClassJob}, Gearset={this.lastAllowedGearsetId}");
        }

        // Moodle just became inactive:
        // unlock and resume tracking the player's current valid state.
        if (!blockingNow && this.wasBlockingActive)
        {
            this.wasBlockingActive = false;
            this.CaptureAllowedState();
            return;
        }

        // While not blocking, keep allowed state fresh.
        if (!blockingNow)
        {
            this.CaptureAllowedState();
            return;
        }

        var currentJob = this.GetCurrentClassJob();
        if (currentJob == 0)
            return;

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

    private void CaptureAllowedState()
    {
        _ = Plugin.Framework.RunOnFrameworkThread(() =>
        {
            var currentJob = this.GetCurrentClassJob();
            if (currentJob != 0)
                this.lastAllowedClassJob = currentJob;

            var module = RaptureGearsetModule.Instance();
            if (module == null)
                return;

            var currentGearset = module->CurrentGearsetIndex;

            if (currentGearset >= 0 && currentGearset < 100 && module->IsValidGearset(currentGearset))
                this.lastAllowedGearsetId = currentGearset;
        });

        
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
            // This matches the FFXIVClientStructs wrapper default.
            module->EquipGearset(this.lastAllowedGearsetId, 0);
        }
        finally
        {
            this.reverting = false;
        }
    }

    private bool ShouldBlock()
    {
        if (!this.Enabled)
            return false;

        return this.IsBlockMoodleActiveCached();
    }
    public bool IsBlockMoodleActiveCached(bool forceRefresh = false)
    {
        var moodles = plugin.Configuration.JobSwitchBlockMoodles;
        if (moodles == null || moodles.Count == 0)
            return false;

        var now = Environment.TickCount64;
        if (!forceRefresh && now < this.nextMoodleRefreshMs)
            return this.cachedMoodleActive;

        foreach (var moodle in moodles)
        {
            var id = moodle.Key;
            if (id == null || id == Guid.Empty)
                continue;

            this.cachedMoodleActive = plugin.MoodlesApi.IsStatusActive(id);
        }

        this.nextMoodleRefreshMs = now + 5000;
        return this.cachedMoodleActive;
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
        this.equipGearsetHook.Dispose();
    }
}
