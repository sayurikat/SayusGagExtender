using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Lumina.Excel.Sheets;

namespace SayusGagExtender;

public sealed class FatigueTracker : IDisposable
{
    private readonly Plugin plugin;

    private Vector3? lastPosition;
    private DateTime lastUpdateUtc = DateTime.MinValue;

    private DateTime lastRestrictionCheckUtc = DateTime.MinValue;
    private float cachedRestrictionFactor = 1.0f;
    private int cachedActiveRestrictionCount;

    private DateTime nextSaveUtc = DateTime.MinValue;
    private DateTime nextDebugPrintUtc = DateTime.MinValue;

    private bool speedRecording;
    private float speedRecordDistance;
    private double speedRecordSeconds;
    private double speedRecordMaxSpeed;

    private const float MinDelta = 0.01f;
    private const float MaxDeltaPerFrame = 5.0f;

    public const float WalkStepLength = 1.18f;
    public const float RunStepLength = 1.9f;

    public const float WalkSpeed = 2.4f;
    public const float RunSpeed = 6.0f;

    private const float SprintModifier = 1.3f;
    private const float PelatonModifier = 1.2f;
    private const float JogModifier = 1.2f;

    private static readonly TimeSpan RestrictionCheckCooldown = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SaveCooldown = TimeSpan.FromSeconds(30);

    private const uint SprintStatusId = 50;
    private const uint PelotonStatusId = 1199;
    private const uint JogStatusId = 4209;

    public float Fatigue => Clamp01(plugin.Configuration.FatigueCurrent);

    public float FatiguePercent => Fatigue * 100.0f;

    public float ForcedWalkThreshold => ClampPercent(plugin.Configuration.FatigueForcedWalkPercent) / 100.0f;

    public float ForcedStopThreshold => ClampPercent(plugin.Configuration.FatigueForcedStopPercent) / 100.0f;

    public float ForcedSitThreshold => ClampPercent(plugin.Configuration.FatigueForcedSitPercent) / 100.0f;

    private bool shouldForceWalkLatched;
    private bool shouldForceStopLatched;
    private bool shouldForceSitLatched;

    public float FatigueReleaseTolerance => Math.Clamp(
        plugin.Configuration.FatigueReleaseTolerance,
        0.0f,
        1.0f);

    public float ForcedWalkReleaseThreshold => GetReleaseThreshold(ForcedWalkThreshold);

    public float ForcedStopReleaseThreshold => GetReleaseThreshold(ForcedStopThreshold);

    public float ForcedSitReleaseThreshold => GetReleaseThreshold(ForcedSitThreshold);

    public bool ShouldForceWalk =>
        plugin.Configuration.FatigueEnabled &&
        GetLatchedThresholdState(ref shouldForceWalkLatched, ForcedWalkThreshold);

    public bool ShouldForceStop =>
        plugin.Configuration.FatigueEnabled &&
        GetLatchedThresholdState(ref shouldForceStopLatched, ForcedStopThreshold);

    public bool ShouldForceSit =>
        plugin.Configuration.FatigueEnabled &&
        GetLatchedThresholdState(ref shouldForceSitLatched, ForcedSitThreshold);

    public bool IsActive => plugin.Configuration.FatigueEnabled && Fatigue > 0.001f;

    public bool IsMoving { get; private set; }
    public bool IsMounted { get; private set; }
    public bool IsWalking { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool HasPeloton { get; private set; }
    public bool IsJogging { get; private set; }
    public bool IsResting { get; private set; }
    public double LastSpeed { get; private set; }

    public float LastRestrictionFactor => cachedRestrictionFactor;

    public int LastActiveRestrictionCount => cachedActiveRestrictionCount;

    public bool IsSpeedRecording => speedRecording;
    public FatigueStatusLevel CurrentFatigueStatus => currentFatigueStatus;

    private float cachedStandingFatiguePerSecond;
    private int cachedActiveStandingPainCount;

    private Guid activeFatigueEnabledMoodleId = Guid.Empty;
    private Guid activeFatigueRestrainedMoodleId = Guid.Empty;
    private Guid activeFatigueStatusMoodleId = Guid.Empty;

    private FatigueStatusLevel currentFatigueStatus = FatigueStatusLevel.None;

    private bool hasSubmittedHonorificRequest;
    private string lastSubmittedHonorificJson = string.Empty;
    private int lastSubmittedHonorificPriority = -1;

    public sealed class FatigueRestrictionConfig
    {
        public Guid RestrictionId { get; set; } = Guid.Empty;
        public string RestrictionName { get; set; } = string.Empty;

        // 1.0 = normal.
        // 2.0 = twice as exhausting.
        // 0.5 = half as exhausting.
        public float FatigueFactor { get; set; } = 1.0f;

        // 0 = disabled.
        // If > 0, standing still with this active restraint increases fatigue.
        // Value means seconds from 0% fatigue to forced sit/stop threshold.
        public int StandingSecondsUntilForcedSit { get; set; } = 0;
    }
    private enum MovementKind
    {
        None,           //walk  run     +%      steps/60ylm  step lenght    time
        Walk,           //---   2.4             71,          1.18m          29.6s
        Run,            //6.0   ---             114,         1.9m           19.1s
        Sprint,         //7.80  3.12    30%     115.         1.9m           14.8s
        Peloton,        //7.20  2.88    20%
        Jog,            //7.20  2.88    20%
        Mount,
    }
    public enum FatigueStatusLevel
    {
        None = 0,
        Fresh = 1,
        Straining = 2,
        Burning = 3,
        Stalled = 4,
        Broken = 5,
    }

    public FatigueTracker(Plugin plugin)
    {
        this.plugin = plugin;

        plugin.Configuration.FatigueCurrent = Clamp01(plugin.Configuration.FatigueCurrent);


        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;
        ClearFatigueEffects();
        SaveNow();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            OnFrameworkUpdateInner();
        }
        catch (Exception ex)
        {
            Plugin.ChatGui.PrintError($"FatigueTracker error: {ex.Message}");
            ResetTrackingPosition();
        }
    }

    private void OnFrameworkUpdateInner()
    {
        if (!plugin.Configuration.FatigueEnabled)
        {
            ResetRuntimeFlags();
            ResetTrackingPosition();
            ClearFatigueEffects();
            currentFatigueStatus = FatigueStatusLevel.None;
            return;
        }

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
        {
            ResetRuntimeFlags();
            ResetTrackingPosition();
            return;
        }

        UpdateFatigueEffects();

        if (IsInvalidTrackingState())
        {
            ResetRuntimeFlags();
            ResetTrackingPosition();

            // Mounted can count as resting/recovery if wanted.
            if (Plugin.Condition[ConditionFlag.Mounted] || Plugin.Condition[ConditionFlag.RidingPillion])
                Recover(GetDeltaSecondsFallback(), resting: true);

            MaybeSave();
            return;
        }

        RefreshRestrictionFactorIfNeeded();

        var now = DateTime.UtcNow;
        var pos = player.Position;

        if (lastPosition == null)
        {
            lastPosition = pos;
            lastUpdateUtc = now;
            ResetRuntimeFlags();
            return;
        }

        var seconds = Math.Max(0.001, (now - lastUpdateUtc).TotalSeconds);
        lastUpdateUtc = now;

        var delta = Vector3.Distance(pos, lastPosition.Value);
        lastPosition = pos;

        if (delta > MaxDeltaPerFrame)
        {
            ResetRuntimeFlags();
            ResetTrackingPosition();
            MaybeSave();
            return;
        }

        LastSpeed = delta / seconds;

        IsResting = IsRestingState();

        if (delta < MinDelta)
        {
            ResetRuntimeFlags();
            LastSpeed = 0;

            var resting = IsRestingState();
            IsResting = resting;

            if (resting)
            {
                Recover(seconds, resting: true);
            }
            else if (cachedStandingFatiguePerSecond > 0)
            {
                ApplyUprightFatigue(seconds);
            }
            else
            {
                Recover(seconds, resting: false);
            }

            MaybeSave();
            return;
        }

        UpdateSpeedRecorder(delta, seconds, LastSpeed);

        var movementKind = GetCurrentMovementKind();

        IsMoving = true;
        IsWalking = movementKind == MovementKind.Walk;
        IsRunning = movementKind == MovementKind.Run;
        IsSprinting = movementKind == MovementKind.Sprint;
        HasPeloton = movementKind == MovementKind.Peloton;
        IsJogging = movementKind == MovementKind.Jog;
        IsMounted = movementKind == MovementKind.Mount;
        //IsResting = false;

        ApplyMovementFatigue(delta, movementKind, LastSpeed);

        // Standing-pain restraints should keep hurting while upright,
        // even if the player is also walking/running.
        if (!IsRestingState() && cachedStandingFatiguePerSecond > 0)
            ApplyUprightFatigue(seconds);



        MaybeSave();
    }
    private void ApplyUprightFatigue(double seconds)
    {
        if (cachedStandingFatiguePerSecond <= 0)
            return;

        var deltaFatigue = cachedStandingFatiguePerSecond * (float)seconds;

        if (deltaFatigue <= 0)
            return;

        SetFatigue(Fatigue + deltaFatigue);
    }
    private void ApplyMovementFatigue(float distance, MovementKind movementKind, double speed)
    {
        if (movementKind == MovementKind.None || movementKind == MovementKind.Mount)
            return;

        var forcedWalkThreshold = Math.Max(0.01f, ForcedWalkThreshold);
        var baseStepsUntilForcedWalk = Math.Max(1, plugin.Configuration.FatigueBaseRunStepsUntilForcedWalk);

        // Reverse user-facing input:
        // "X normal running steps until forced walk"
        // into fatigue-per-run-step.
        var baseFatiguePerRunStep = forcedWalkThreshold / baseStepsUntilForcedWalk;

        var stepLength = movementKind == MovementKind.Walk
            ? WalkStepLength
            : RunStepLength;

        var stepEquivalent = distance / stepLength;

        var movementMultiplier = movementKind switch
        {
            MovementKind.Walk => Math.Max(0.0f, plugin.Configuration.FatigueWalkRateMultiplier),
            MovementKind.Run => 1.0f,
            MovementKind.Sprint => 1.0f,
            MovementKind.Peloton => 1.0f,
            MovementKind.Jog => 1.0f,
            _ => 0.0f,
        };

        var speedMultiplier = CalculateSpeedMultiplier(speed);

        var restrictionFactor = GetEffectiveFatigueFactor();

        var deltaFatigue =
            baseFatiguePerRunStep *
            stepEquivalent *
            movementMultiplier *
            speedMultiplier *
            restrictionFactor;

        if (deltaFatigue <= 0)
            return;

        SetFatigue(Fatigue + deltaFatigue);
    }

    private float GetEffectiveFatigueFactor()
    {
        // If active configured restraints exist, they are the main stack.
        if (cachedActiveRestrictionCount > 0)
            return cachedRestrictionFactor;

        // Otherwise use the unrestricted/base factor.
        // 0 = no fatigue without restrictions.
        // 1 = full normal fatigue without restrictions.
        return Math.Max(0.0f, plugin.Configuration.FatigueUnrestrictedFactor);
    }

    private float CalculateSpeedMultiplier(double speed)
    {
        var normalRunSpeed = Math.Max(0.1f, RunSpeed);
        var exponent = Math.Max(0.1f, plugin.Configuration.FatigueSpeedExponent);

        var ratio = Math.Max(0.1, speed / normalRunSpeed);

        // Speed only makes fatigue worse when above normal run speed.
        // Slower movement is handled by walking multiplier / movement type instead.
        if (ratio <= 1.0)
            return 1.0f;

        ratio = Math.Clamp(ratio, 1.0, 3.0);

        return MathF.Pow((float)ratio, exponent);
    }

    private void Recover(double seconds, bool resting)
    {
        if (Fatigue <= 0)
        {
            SetFatigue(0);
            return;
        }

        var fullRecoverySeconds = resting
            ? plugin.Configuration.FatigueFullRecoveryRestingSeconds
            : plugin.Configuration.FatigueFullRecoveryStandingSeconds;

        fullRecoverySeconds = Math.Max(1, fullRecoverySeconds);

        var recoveryPerSecond = 1.0f / fullRecoverySeconds;
        var recovery = recoveryPerSecond * (float)seconds;

        SetFatigue(Fatigue - recovery);
    }

    private MovementKind GetCurrentMovementKind()
    {
        if (IsPlayerMounted())
            return MovementKind.Mount;

        if (plugin.MovementBlocker?.ForceWalkEnabled == true)
            return MovementKind.Walk;

        if (HasStatus(SprintStatusId))
            return MovementKind.Sprint;

        if (HasStatus(PelotonStatusId))
            return MovementKind.Peloton;

        if (HasStatus(JogStatusId))
            return MovementKind.Jog;

        if (LastSpeed > 4)
            return MovementKind.Run;

        return MovementKind.Walk;
    }
    private bool IsPlayerMounted()
    {
        return Plugin.Condition.Any(
            ConditionFlag.Mounted,
            ConditionFlag.RidingPillion);
    }
    private bool IsInvalidTrackingState()
    {
        return Plugin.Condition.Any(
            ConditionFlag.BetweenAreas,
            ConditionFlag.BetweenAreas51,
            ConditionFlag.WatchingCutscene,
            ConditionFlag.WatchingCutscene78,
            ConditionFlag.OccupiedInCutSceneEvent,
            ConditionFlag.Mounting,
            ConditionFlag.Mounting71);
    }

    private bool IsRestingState()
    {
        if (Plugin.Condition.Any(
                ConditionFlag.Mounted,
                ConditionFlag.RidingPillion))
            return true;

        if (IsRestingByEmote())
            return true;

        if (Plugin.Condition.Any(
                ConditionFlag.Performing,
                ConditionFlag.Crafting,
                ConditionFlag.Gathering,
                ConditionFlag.Fishing))
            return true;

        return false;
    }



    private bool HasStatus(uint statusId)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return false;

        foreach (var status in player.StatusList)
        {
            if (status.StatusId == statusId)
                return true;
        }

        return false;
    }

    private void RefreshRestrictionFactorIfNeeded()
    {
        var now = DateTime.UtcNow;
        if (now < lastRestrictionCheckUtc + RestrictionCheckCooldown)
            return;

        lastRestrictionCheckUtc = now;

        cachedRestrictionFactor = CalculateActiveRestrictionFactor(
            out cachedActiveRestrictionCount,
            out cachedStandingFatiguePerSecond,
            out cachedActiveStandingPainCount);
    }

    private float CalculateActiveRestrictionFactor(
    out int activeCount,
    out float standingFatiguePerSecond,
    out int activeStandingPainCount)
    {
        activeCount = 0;
        standingFatiguePerSecond = 0;
        activeStandingPainCount = 0;

        var configured = plugin.Configuration.FatigueRestrictions;
        if (configured == null || configured.Count == 0)
            return 1.0f;

        Dictionary<Guid, string> activeRestrictions;

        try
        {
            activeRestrictions = plugin.GagSpeakRestrictionsApi.GetActiveRestrictionsWithId();
        }
        catch
        {
            return 1.0f;
        }

        if (activeRestrictions == null || activeRestrictions.Count == 0)
            return 1.0f;

        var factor = 1.0f;

        foreach (var item in configured)
        {
            if (item.RestrictionId == Guid.Empty)
                continue;

            if (!activeRestrictions.ContainsKey(item.RestrictionId))
                continue;

            activeCount++;

            var itemFactor = Math.Clamp(item.FatigueFactor, 0.0f, 10.0f);
            factor *= itemFactor;

            if (item.StandingSecondsUntilForcedSit > 0)
            {
                activeStandingPainCount++;

                var seconds = Math.Max(1, item.StandingSecondsUntilForcedSit);

                // If standing from 0% fatigue, this item alone reaches forced stop/sit
                // threshold after StandingSecondsUntilForcedSit seconds.
                standingFatiguePerSecond += ForcedSitThreshold / seconds;
            }
        }

        standingFatiguePerSecond = Math.Clamp(standingFatiguePerSecond, 0.0f, 1.0f);

        return Math.Clamp(factor, 0.0f, 25.0f);
    }

    private void SetFatigue(float value)
    {
        plugin.Configuration.FatigueCurrent = Clamp01(value);
    }

    public void ResetFatigue()
    {
        SetFatigue(0);
        ResetForceLatches();
        currentFatigueStatus = FatigueStatusLevel.Fresh;
        UpdateFatigueEffects();
        ResetTrackingPosition();
        SaveNow();

        Plugin.ChatGui.Print("Fatigue reset.");
    }
    private void ResetForceLatches()
    {
        shouldForceWalkLatched = false;
        shouldForceStopLatched = false;
        shouldForceSitLatched = false;
    }
    public void SetFatiguePercent(float percent)
    {
        SetFatigue(ClampPercent(percent) / 100.0f);

        if (Fatigue <= 0.0f)
            ResetForceLatches();

        SaveNow();

        Plugin.ChatGui.Print($"Fatigue set to {FatiguePercent:F1}%.");
    }

    public void PrintStatus()
    {
        Plugin.ChatGui.Print(
            $"Fatigue: {FatiguePercent:F1}% | " +
            $"ForceWalk={ShouldForceWalk} | " +
            $"ForceStop={ShouldForceStop} | " +
            $"ForceSit={ShouldForceSit} | " +
            $"ReleaseTolerance={FatigueReleaseTolerance * 100.0f:F1}% | " +
            $"Speed={LastSpeed:F2}y/s | " +
            $"ActiveFactor={cachedRestrictionFactor:F2}x | " +
            $"ActiveRestrictions={cachedActiveRestrictionCount} | " +
            $"StandingPain={cachedActiveStandingPainCount}");
    }

    public void StartSpeedRecord()
    {
        speedRecording = true;
        speedRecordDistance = 0;
        speedRecordSeconds = 0;
        speedRecordMaxSpeed = 0;

        Plugin.ChatGui.Print("Fatigue speed recording started.");
    }

    public void StopSpeedRecordAndPrint()
    {
        speedRecording = false;

        var avg = speedRecordSeconds > 0
            ? speedRecordDistance / speedRecordSeconds
            : 0;

        Plugin.ChatGui.Print(
            $"Fatigue speed recording stopped. " +
            $"Avg={avg:F2} y/s, " +
            $"Max={speedRecordMaxSpeed:F2} y/s, " +
            $"Distance={speedRecordDistance:F2} y, " +
            $"Time={speedRecordSeconds:F1}s");
    }

    private void UpdateSpeedRecorder(float delta, double seconds, double speed)
    {
        if (!speedRecording)
            return;

        speedRecordDistance += delta;
        speedRecordSeconds += seconds;
        speedRecordMaxSpeed = Math.Max(speedRecordMaxSpeed, speed);
    }

    private void ResetRuntimeFlags()
    {
        IsMoving = false;
        IsWalking = false;
        IsRunning = false;
        IsSprinting = false;
        HasPeloton = false;
        IsResting = false;
        LastSpeed = 0;
    }

    private void ResetTrackingPosition()
    {
        lastPosition = null;
        lastUpdateUtc = DateTime.MinValue;
        LastSpeed = 0;
    }

    private double GetDeltaSecondsFallback()
    {
        if (lastUpdateUtc == DateTime.MinValue)
            return 0.1;

        return Math.Max(0.001, (DateTime.UtcNow - lastUpdateUtc).TotalSeconds);
    }

    private void MaybeSave()
    {
        var now = DateTime.UtcNow;
        if (now < nextSaveUtc)
            return;

        nextSaveUtc = now + SaveCooldown;
        plugin.Configuration.Save();
    }

    private void SaveNow()
    {
        plugin.Configuration.Save();
        nextSaveUtc = DateTime.UtcNow + SaveCooldown;
    }

    private static float Clamp01(float value)
    {
        return Math.Clamp(value, 0.0f, 1.0f);
    }

    private static float ClampPercent(float value)
    {
        return Math.Clamp(value, 0.0f, 100.0f);
    }

    private float GetReleaseThreshold(float threshold)
    {
        return Math.Max(0.0f, threshold - FatigueReleaseTolerance);
    }

    private bool GetLatchedThresholdState(ref bool latched, float threshold)
    {
        var fatigue = Fatigue;

        if (fatigue <= 0.0f)
        {
            latched = false;
            return false;
        }

        if (!latched)
        {
            if (fatigue >= threshold)
                latched = true;

            return latched;
        }

        var releaseThreshold = GetReleaseThreshold(threshold);

        if (fatigue <= releaseThreshold)
            latched = false;

        return latched;
    }



    private bool IsRestingByEmote()
    {
        var currentEmoteId = plugin.EmoteApi.GetCurrentLocalPlayerEmoteId();

        if (currentEmoteId == 0)
            return false;

        return plugin.EmoteApi.IsAnySitOrSleep(currentEmoteId) || plugin.EmoteApi.IsThisThatEmote(currentEmoteId, "/playdead");
    }
    private void UpdateFatigueEffects()
    {
        if (!plugin.Configuration.FatigueEnabled)
        {
            ClearFatigueEffects();
            currentFatigueStatus = FatigueStatusLevel.None;
            return;
        }

        var status = GetLatchedFatigueStatus();
        currentFatigueStatus = status;

        var enabledEffect = plugin.Configuration.FatigueEnabledEffect;
        var restrainedEffect = plugin.Configuration.FatigueRestrainedEffect;
        var statusEffect = GetEffectConfigForFatigueStatus(status);

        EnsureFatigueMoodleState(
            ref activeFatigueEnabledMoodleId,
            enabledEffect.MoodleId,
            shouldBeActive: plugin.Configuration.FatigueEnabled);

        EnsureFatigueMoodleState(
            ref activeFatigueRestrainedMoodleId,
            restrainedEffect.MoodleId,
            shouldBeActive: plugin.Configuration.FatigueEnabled && cachedActiveRestrictionCount > 0);

        EnsureFatigueMoodleState(
            ref activeFatigueStatusMoodleId,
            statusEffect.MoodleId,
            shouldBeActive: status != FatigueStatusLevel.None && statusEffect.MoodleId != Guid.Empty);

        UpdateFatigueHonorificWinner(enabledEffect, restrainedEffect, statusEffect);
    }

    private void ClearFatigueEffects()
    {
        ClearFatigueMoodles();
        RecallFatigueHonorificRequest();
    }

    private void ClearFatigueMoodles()
    {
        ClearFatigueMoodle(ref activeFatigueEnabledMoodleId);
        ClearFatigueMoodle(ref activeFatigueRestrainedMoodleId);
        ClearFatigueMoodle(ref activeFatigueStatusMoodleId);
    }

    private void EnsureFatigueMoodleState(ref Guid activeId, Guid wantedId, bool shouldBeActive)
    {
        if (!shouldBeActive || wantedId == Guid.Empty)
        {
            ClearFatigueMoodle(ref activeId);
            return;
        }

        if (activeId == wantedId)
            return;

        ClearFatigueMoodle(ref activeId);

        plugin.MoodleEnforcer.AddEnforcedMoodle(wantedId, this);
        activeId = wantedId;
    }

    private void ClearFatigueMoodle(ref Guid activeId)
    {
        if (activeId == Guid.Empty)
            return;

        plugin.MoodleEnforcer.RemoveEnforcedMoodle(activeId, this);
        activeId = Guid.Empty;
    }

    private void UpdateFatigueHonorificWinner(
        Configuration.FatigueEffectConfig enabledEffect,
        Configuration.FatigueEffectConfig restrainedEffect,
        Configuration.FatigueEffectConfig statusEffect)
    {
        Configuration.FatigueEffectConfig? winner = null;

        if (plugin.Configuration.FatigueEnabled)
            winner = PickBetterHonorificEffect(winner, enabledEffect);

        if (plugin.Configuration.FatigueEnabled && cachedActiveRestrictionCount > 0)
            winner = PickBetterHonorificEffect(winner, restrainedEffect);

        if (currentFatigueStatus != FatigueStatusLevel.None)
            winner = PickBetterHonorificEffect(winner, statusEffect);

        if (winner == null)
        {
            RecallFatigueHonorificRequest();
            return;
        }

        var json = plugin.HonorificManager.BuildTitleJson(
            winner.HonorificTitle,
            winner.HonorificColor,
            winner.HonorificGlow);

        if (string.IsNullOrWhiteSpace(json))
        {
            RecallFatigueHonorificRequest();
            return;
        }

        if (hasSubmittedHonorificRequest &&
            lastSubmittedHonorificPriority == winner.HonorificPriority &&
            string.Equals(lastSubmittedHonorificJson, json, StringComparison.Ordinal))
        {
            return;
        }

        plugin.HonorificManager.SetTitle(
            json,
            winner.HonorificPriority,
            this);

        hasSubmittedHonorificRequest = true;
        lastSubmittedHonorificJson = json;
        lastSubmittedHonorificPriority = winner.HonorificPriority;
    }

    private static Configuration.FatigueEffectConfig? PickBetterHonorificEffect(
        Configuration.FatigueEffectConfig? currentBest,
        Configuration.FatigueEffectConfig candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.HonorificTitle))
            return currentBest;

        if (currentBest == null)
            return candidate;

        if (candidate.HonorificPriority > currentBest.HonorificPriority)
            return candidate;

        return currentBest;
    }

    private void RecallFatigueHonorificRequest()
    {
        if (!hasSubmittedHonorificRequest)
            return;

        plugin.HonorificManager.RecallTitle(this);

        hasSubmittedHonorificRequest = false;
        lastSubmittedHonorificJson = string.Empty;
        lastSubmittedHonorificPriority = -1;
    }

    private FatigueStatusLevel GetLatchedFatigueStatus()
    {
        if (!plugin.Configuration.FatigueEnabled)
            return FatigueStatusLevel.None;

        var rawStatus = GetRawFatigueStatus();

        if (currentFatigueStatus == FatigueStatusLevel.None)
            return rawStatus;

        // Going up should happen immediately.
        if (rawStatus > currentFatigueStatus)
            return rawStatus;

        // Same status, keep it.
        if (rawStatus == currentFatigueStatus)
            return currentFatigueStatus;

        // Going down requires dropping below the current status lower bound
        // by the configured tolerance.
        var lowerBound = GetLowerBoundForStatus(currentFatigueStatus);
        var releaseAt = Math.Max(0.0f, lowerBound - FatigueReleaseTolerance);

        if (Fatigue > releaseAt)
            return currentFatigueStatus;

        return rawStatus;
    }

    private FatigueStatusLevel GetRawFatigueStatus()
    {
        var fatigue = Fatigue;

        var halfWalk = ForcedWalkThreshold * 0.5f;
        var walk = ForcedWalkThreshold;
        var stop = ForcedStopThreshold;
        var sit = ForcedSitThreshold;

        if (fatigue >= sit)
            return FatigueStatusLevel.Broken;

        if (fatigue >= stop)
            return FatigueStatusLevel.Stalled;

        if (fatigue >= walk)
            return FatigueStatusLevel.Burning;

        if (fatigue >= halfWalk)
            return FatigueStatusLevel.Straining;

        return FatigueStatusLevel.Fresh;
    }

    private float GetLowerBoundForStatus(FatigueStatusLevel status)
    {
        return status switch
        {
            FatigueStatusLevel.Broken => ForcedSitThreshold,
            FatigueStatusLevel.Stalled => ForcedStopThreshold,
            FatigueStatusLevel.Burning => ForcedWalkThreshold,
            FatigueStatusLevel.Straining => ForcedWalkThreshold * 0.5f,
            FatigueStatusLevel.Fresh => 0.0f,
            _ => 0.0f,
        };
    }

    private Configuration.FatigueEffectConfig GetEffectConfigForFatigueStatus(FatigueStatusLevel status)
    {
        return status switch
        {
            FatigueStatusLevel.Fresh => plugin.Configuration.FatigueStatusFreshEffect,
            FatigueStatusLevel.Straining => plugin.Configuration.FatigueStatusStrainingEffect,
            FatigueStatusLevel.Burning => plugin.Configuration.FatigueStatusBurningEffect,
            FatigueStatusLevel.Stalled => plugin.Configuration.FatigueStatusStalledEffect,
            FatigueStatusLevel.Broken => plugin.Configuration.FatigueStatusBrokenEffect,
            _ => new Configuration.FatigueEffectConfig(),
        };
    }
}
