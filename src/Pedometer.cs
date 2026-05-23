using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using System;
using System.Numerics;

namespace SayusGagExtender;

public sealed class Pedometer : IDisposable
{
    private readonly Plugin plugin;

    private Vector3? lastPosition;
    private DateTime lastUpdateUtc = DateTime.MinValue;

    private bool speedRecording;
    private Vector3? speedRecordLastPosition;
    private DateTime speedRecordLastUtc = DateTime.MinValue;
    private float speedRecordDistance;
    private double speedRecordSeconds;
    private double speedRecordMaxSpeed;

    private long nextSaveMs;

    // Development/debug values.
    public double LastSpeed { get; private set; }
    public double AverageRecordedSpeed => speedRecordSeconds > 0
        ? speedRecordDistance / speedRecordSeconds
        : 0;

    public bool IsSpeedRecording => speedRecording;

    // Total movement distance.
    public float WalkDistance { get; private set; }
    public float RunDistance { get; private set; }
    public float SprintDistance { get; private set; }
    public float PelotonDistance { get; private set; }

    // "Step" estimates. Tune these however you like.
    public int WalkSteps => (int)(WalkDistance / WalkStepLength);
    public int RunSteps => (int)(RunDistance / RunStepLength);
    public int TotalSteps => WalkSteps + RunSteps;

    public float TotalDistance =>
        WalkDistance +
        RunDistance +
        SprintDistance +
        PelotonDistance;

    private const float WalkStepLength = 0.75f;
    private const float RunStepLength = 1.05f;

    // Ignore tiny jitters and huge snaps.
    private const float MinDelta = 0.01f;
    private const float MaxDeltaPerFrame = 5.0f;

    // FFXIV status ids. Verify if needed, but these are the expected common ones.
    private const uint SprintStatusId = 50;
    private const uint PelotonStatusId = 1199;

    public Pedometer(Plugin plugin)
    {
        this.plugin = plugin;

        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            OnFrameworkUpdateInner();
        }
        catch (Exception ex)
        {
            Plugin.ChatGui.PrintError($"Pedometer error: {ex.Message}");
            ResetTrackingPosition();
        }
    }

    private void OnFrameworkUpdateInner()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
        {
            ResetTrackingPosition();
            return;
        }

        if (IsInvalidMovementState())
        {
            ResetTrackingPosition();
            return;
        }

        var now = DateTime.UtcNow;
        var pos = player.Position;

        if (lastPosition == null)
        {
            lastPosition = pos;
            lastUpdateUtc = now;
            return;
        }

        var seconds = Math.Max(0.001, (now - lastUpdateUtc).TotalSeconds);
        lastUpdateUtc = now;

        var delta = Vector3.Distance(pos, lastPosition.Value);
        lastPosition = pos;

        if (delta < MinDelta)
        {
            LastSpeed = 0;
            return;
        }

        if (delta > MaxDeltaPerFrame)
        {
            LastSpeed = 0;
            ResetTrackingPosition();
            return;
        }

        var speed = delta / seconds;
        LastSpeed = speed;

        UpdateSpeedRecorder(pos, delta, seconds, speed);

        var movementKind = GetCurrentMovementKind();

        switch (movementKind)
        {
            case MovementKind.Walk:
                WalkDistance += delta;
                break;

            case MovementKind.Sprint:
                SprintDistance += delta;
                break;

            case MovementKind.Peloton:
                PelotonDistance += delta;
                break;

            case MovementKind.Run:
            default:
                RunDistance += delta;
                break;
        }

        // Optional, only useful if you add config fields later.
        // Save occasionally, not every frame.
        MaybePersistToConfig();
    }

    private MovementKind GetCurrentMovementKind()
    {
        // Walk should win over everything else because later you may force-walk
        // from another class and still want the pedometer to count it as walk.
        if (IsWalkEnabled())
            return MovementKind.Walk;

        if (HasStatus(SprintStatusId))
            return MovementKind.Sprint;

        if (HasStatus(PelotonStatusId))
            return MovementKind.Peloton;

        return MovementKind.Run;
    }

    private bool IsInvalidMovementState()
    {
        return Plugin.Condition.Any(
            ConditionFlag.Mounted,
            ConditionFlag.RidingPillion,
            ConditionFlag.Mounting,
            ConditionFlag.Mounting71,
            ConditionFlag.BetweenAreas,
            ConditionFlag.BetweenAreas51,
            ConditionFlag.WatchingCutscene,
            ConditionFlag.WatchingCutscene78,
            ConditionFlag.OccupiedInCutSceneEvent);
    }

    private bool IsWalkEnabled()
    {
        // Avoid direct ConditionFlag.Walking reference in case your Dalamud enum
        // version does not expose it. This compiles either way.
        if (!Enum.TryParse<ConditionFlag>("Walking", out var walkingFlag))
            return false;

        return Plugin.Condition[walkingFlag];
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

    private void UpdateSpeedRecorder(
        Vector3 currentPosition,
        float delta,
        double seconds,
        double speed)
    {
        if (!speedRecording)
            return;

        if (speedRecordLastPosition == null)
        {
            speedRecordLastPosition = currentPosition;
            speedRecordLastUtc = DateTime.UtcNow;
            return;
        }

        speedRecordDistance += delta;
        speedRecordSeconds += seconds;
        speedRecordMaxSpeed = Math.Max(speedRecordMaxSpeed, speed);
        speedRecordLastPosition = currentPosition;
        speedRecordLastUtc = DateTime.UtcNow;
    }

    public void StartSpeedRecord()
    {
        var player = Plugin.ObjectTable.LocalPlayer;

        speedRecording = true;
        speedRecordLastPosition = player?.Position;
        speedRecordLastUtc = DateTime.UtcNow;
        speedRecordDistance = 0;
        speedRecordSeconds = 0;
        speedRecordMaxSpeed = 0;

        Plugin.ChatGui.Print("Pedometer speed recording started.");
    }

    public void StopSpeedRecordAndPrint()
    {
        speedRecording = false;

        var avg = AverageRecordedSpeed;

        Plugin.ChatGui.Print(
            $"Pedometer speed recording stopped. " +
            $"Avg={avg:F2} y/s, " +
            $"Max={speedRecordMaxSpeed:F2} y/s, " +
            $"Distance={speedRecordDistance:F2} y, " +
            $"Time={speedRecordSeconds:F1}s");
    }

    public void PrintCurrentSpeed()
    {
        Plugin.ChatGui.Print(
            $"Pedometer speed now: {LastSpeed:F2} y/s. " +
            $"Walk={IsWalkEnabled()}, Sprint={HasStatus(SprintStatusId)}, Peloton={HasStatus(PelotonStatusId)}");
    }

    public void PrintTotals()
    {
        Plugin.ChatGui.Print(
            $"Pedometer totals: " +
            $"Walk={WalkDistance:F1}y/{WalkSteps} steps, " +
            $"Run={RunDistance:F1}y/{RunSteps} steps, " +
            $"Sprint={SprintDistance:F1}y, " +
            $"Peloton={PelotonDistance:F1}y, " +
            $"Total={TotalDistance:F1}y/{TotalSteps} steps");
    }

    public void ResetTotals()
    {
        WalkDistance = 0;
        RunDistance = 0;
        SprintDistance = 0;
        PelotonDistance = 0;

        ResetTrackingPosition();

        Plugin.ChatGui.Print("Pedometer totals reset.");
    }

    private void ResetTrackingPosition()
    {
        lastPosition = null;
        lastUpdateUtc = DateTime.MinValue;
        LastSpeed = 0;
    }

    private void MaybePersistToConfig()
    {
        // Add config fields first if you want persistent totals.
        // Example fields:
        //
        // public float PedometerWalkDistance { get; set; }
        // public float PedometerRunDistance { get; set; }
        // public float PedometerSprintDistance { get; set; }
        // public float PedometerPelotonDistance { get; set; }
        //
        // Then uncomment this method body.

        /*
        var now = Environment.TickCount64;
        if (now < nextSaveMs)
            return;

        nextSaveMs = now + 30000;

        plugin.Configuration.PedometerWalkDistance = WalkDistance;
        plugin.Configuration.PedometerRunDistance = RunDistance;
        plugin.Configuration.PedometerSprintDistance = SprintDistance;
        plugin.Configuration.PedometerPelotonDistance = PelotonDistance;
        plugin.Configuration.Save();
        */
    }

    private enum MovementKind
    {
        Walk,
        Run,
        Sprint,
        Peloton,
    }
}
