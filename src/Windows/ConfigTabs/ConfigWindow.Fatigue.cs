using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SayusGagExtender.Windows;

public partial class ConfigWindow
{
    private List<KeyValuePair<Guid, string>> availableFatigueRestrictions = new();
    private string fatigueRestrictionSearch = string.Empty;
    private Guid? selectedFatigueRestrictionToAdd;
    private string selectedFatigueRestrictionFactor = "1.0";
    private readonly Dictionary<string, Guid> stagedFatigueMoodleSelections = new();
    private readonly Dictionary<string, string> fatigueMoodleSearchText = new();

    private void DrawFatigueTab()
    {
        RefreshMoodleBlockOptionsIfNeeded();

        var enabled = configuration.FatigueEnabled;
        if (ImGui.Checkbox("Enable Fatigue Tracker", ref enabled))
        {
            configuration.FatigueEnabled = enabled;
            configuration.Save();
        }

        ImGui.TextWrapped("Tracks fatigue from walking/running. Configured restraints can increase fatigue rate. Later this can be used by a separate class to force walking or stopping.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawFatigueCurrentStatus();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawFatigueGeneralSettings();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawFatigueRestrictionList();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawFatigueAddRestriction();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawFatigueMoodles();
    }

    private void DrawFatigueCurrentStatus()
    {
        ImGui.Text("Current Fatigue");

        var fatiguePercent = configuration.FatigueCurrent * 100.0f;
        //ImGui.ProgressBar(configuration.FatigueCurrent, new Vector2(300, 0), $"{fatiguePercent:F1}%");
        var statusColor = GetFatigueStatusColor(plugin.FatigueTracker.CurrentFatigueStatus);

        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, statusColor);
        ImGui.ProgressBar(configuration.FatigueCurrent, new Vector2(300, 0), $"{fatiguePercent:F1}%");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.TextDisabled(GetFatigueStatusLabel(plugin.FatigueTracker.CurrentFatigueStatus));

        ImGui.TextDisabled(
            $"Force Walk: {plugin.FatigueTracker.ShouldForceWalk} | " +
            $"Force Stop: {plugin.FatigueTracker.ShouldForceStop} | " +
            $"Speed: {plugin.FatigueTracker.LastSpeed:F2} y/s | " +
            $"Factor: {plugin.FatigueTracker.LastRestrictionFactor:F2}x");

        if (plugin.FatigueTracker.IsMoving) ImGui.Text("IsMoving"); else ImGui.TextDisabled("IsMoving"); ImGui.SameLine();
        if (plugin.FatigueTracker.IsWalking) ImGui.Text("IsWalking"); else ImGui.TextDisabled("IsWalking"); ImGui.SameLine();
        if (plugin.FatigueTracker.IsRunning) ImGui.Text("IsRunning"); else ImGui.TextDisabled("IsRunning"); ImGui.SameLine();
        if (plugin.FatigueTracker.IsSprinting) ImGui.Text("IsSprinting"); else ImGui.TextDisabled("IsSprinting"); ImGui.SameLine();
        if (plugin.FatigueTracker.HasPeloton) ImGui.Text("HasPeloton"); else ImGui.TextDisabled("HasPeloton"); ImGui.SameLine();
        if (plugin.FatigueTracker.IsJogging) ImGui.Text("IsJogging"); else ImGui.TextDisabled("IsJogging"); ImGui.SameLine();
        if (plugin.FatigueTracker.IsMounted) ImGui.Text("IsMounted"); else ImGui.TextDisabled("IsMounted"); ImGui.SameLine();
        if (plugin.FatigueTracker.IsResting) ImGui.Text("IsResting"); else ImGui.TextDisabled("IsResting"); ImGui.SameLine();









        if (ImGui.Button("Print Status##Fatigue"))
            plugin.FatigueTracker.PrintStatus();

        ImGui.SameLine();

        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        if (!ctrlHeld)
            ImGui.BeginDisabled();

        if (ImGui.Button("Reset Fatigue##Fatigue"))
            plugin.FatigueTracker.ResetFatigue();

        if (!ctrlHeld)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(ctrlHeld ? "Reset fatigue to 0%" : "Hold CTRL to reset fatigue");

        ImGui.Spacing();

        if (!plugin.FatigueTracker.IsSpeedRecording)
        {
            if (ImGui.Button("Start Speed Record##Fatigue"))
                plugin.FatigueTracker.StartSpeedRecord();
        }
        else
        {
            if (ImGui.Button("Stop Speed Record##Fatigue"))
                plugin.FatigueTracker.StopSpeedRecordAndPrint();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Dev tool for calibrating normal run, sprint, Peloton, etc.");
    }

    private void DrawFatigueGeneralSettings()
    {
        ImGui.Text("General Settings");

        var forcedWalkPercent = configuration.FatigueForcedWalkPercent;
        if (DrawFloatInput(
            "Forced Walk at % fatigue",
            ref forcedWalkPercent,
            0.0f,
            100.0f,
            "When fatigue reaches this percent, the later enforcer can force walk."))
        {
            configuration.FatigueForcedWalkPercent = forcedWalkPercent;
            configuration.Save();
        }

        SameLineDisabledText(
            $"≈ {StepText(EstimateRunStepsToFatigue(GetForcedWalkThreshold01(), 1.0f))} normal run");

        var forcedStopPercent = configuration.FatigueForcedStopPercent;
        if (DrawFloatInput(
            "Forced Stop at % fatigue",
            ref forcedStopPercent,
            0.0f,
            100.0f,
            "When fatigue reaches this percent, the later enforcer can stop movement."))
        {
            configuration.FatigueForcedStopPercent = forcedStopPercent;
            configuration.Save();
        }

        SameLineDisabledText(
            $"≈ {StepText(EstimateRunStepsToFatigue(GetForcedStopThreshold01(), 1.0f))} normal run");

        var forcedSitPercent = configuration.FatigueForcedSitPercent;
        if (DrawFloatInput(
            "Forced Sit at % fatigue",
            ref forcedSitPercent,
            0.0f,
            100.0f,
            "When fatigue reaches this percent, the later enforcer can stop movement."))
        {
            configuration.FatigueForcedSitPercent = forcedSitPercent;
            configuration.Save();
        }

        SameLineDisabledText(
            $"≈ {StepText(EstimateRunStepsToFatigue(GetForcedSitThreshold01(), 1.0f))} normal run");


        var releaseTolerancePercent = configuration.FatigueReleaseTolerance * 100.0f;
        if (DrawFloatInput(
            "Force release tolerance %",
            ref releaseTolerancePercent,
            0.0f,
            100.0f,
            "How far below each force threshold fatigue must recover before release. 5% means threshold 60% releases at 55%."))
        {
            configuration.FatigueReleaseTolerance = Math.Clamp(releaseTolerancePercent, 0.0f, 100.0f) / 100.0f;
            configuration.Save();
        }

        SameLineDisabledText(
            $"release at threshold - {configuration.FatigueReleaseTolerance * 100.0f:F1}%");


        var baseRunStepsUntilForcedWalk = configuration.FatigueBaseRunStepsUntilForcedWalk;
        if (DrawIntInput(
            "Normal running steps until forced walk",
            ref baseRunStepsUntilForcedWalk,
            1,
            100000,
            "User-friendly base calibration. Assumes no active restraint factor and straight-line unbuffed running."))
        {
            configuration.FatigueBaseRunStepsUntilForcedWalk = baseRunStepsUntilForcedWalk;
            configuration.Save();
        }

        //SameLineDisabledText($"≈ {StepText(EstimateRunStepsToFatigue(GetForcedWalkThreshold01(), 1.0f))} to forced walk, ");
        SameLineDisabledText($"≈ {TimeText(EstimateRunTimeToFatigue(GetForcedWalkThreshold01(), 1.0f))} to forced walk, ");

        var unrestrictedFactor = configuration.FatigueUnrestrictedFactor;
        if (DrawFloatInput(
            "Unrestricted fatigue factor",
            ref unrestrictedFactor,
            0.0f,
            10.0f,
            "0 = no fatigue unless configured restraints are active. 1 = normal fatigue even with no restraints."))
        {
            configuration.FatigueUnrestrictedFactor = unrestrictedFactor;
            configuration.Save();
        }

        if (configuration.FatigueUnrestrictedFactor <= 0.0001f)
        {
            SameLineDisabledText("no unrestricted fatigue");
        }
        else
        {
            SameLineDisabledText(
                $"≈ {StepText(EstimateRunStepsToFatigue(GetForcedWalkThreshold01(), configuration.FatigueUnrestrictedFactor))}, " +
                $"/ {TimeText(EstimateRunTimeToFatigue(GetForcedWalkThreshold01(), configuration.FatigueUnrestrictedFactor))} to forced walk, ");
                //$"{StepText(EstimateRunStepsToFatigue(GetForcedStopThreshold01(), configuration.FatigueUnrestrictedFactor))} to stop");
        }

        var walkRateMultiplier = configuration.FatigueWalkRateMultiplier;
        if (DrawFloatInput(
            "Walking fatigue multiplier",
            ref walkRateMultiplier,
            0.0f,
            10.0f,
            "0.25 means walking causes 25% of normal running fatigue. Speed exponent does not reduce walking further."))
        {
            configuration.FatigueWalkRateMultiplier = walkRateMultiplier;
            configuration.Save();
        }

        //GetForcedWalkThreshold01()

        ;

        var restrictedWalkSteps = EstimateWalkStepsFromFatigueToStop(GetForcedWalkThreshold01(), GetForcedStopThreshold01(), 1.0f);
        var unrestrictedWalkSteps = configuration.FatigueUnrestrictedFactor <= 0.0001f
            ? 0
            : EstimateWalkStepsFromFatigueToStop(GetForcedWalkThreshold01(), GetForcedStopThreshold01(), configuration.FatigueUnrestrictedFactor);

        var restrictedWalkTime = EstimateWalkTimeFromFatigueToStop(GetForcedWalkThreshold01(), GetForcedStopThreshold01(), 1.0f);
        var unrestrictedWalkTime = configuration.FatigueUnrestrictedFactor <= 0.0001f
            ? 0
            : EstimateWalkTimeFromFatigueToStop(GetForcedWalkThreshold01(), GetForcedStopThreshold01(), configuration.FatigueUnrestrictedFactor);

        var unrestrictedWalkText = configuration.FatigueUnrestrictedFactor <= 0.0001f
                ? $" unrestricted"
                : $"≈ {StepText(unrestrictedWalkSteps)} / {TimeText(unrestrictedWalkTime)} unrestricted";

        var walkText = configuration.FatigueWalkRateMultiplier <= 0.0001f
                ? $"no walking fatigue"
                : $"≈ {StepText(restrictedWalkSteps)} / {TimeText(unrestrictedWalkTime)} from forced walk to stop, " + unrestrictedWalkText;

        //var restrictedWalkSteps = EstimateWalkStepsToFatigue(GetForcedWalkThreshold01(), 1.0f);
        //var unrestrictedWalkSteps = configuration.FatigueUnrestrictedFactor <= 0.0001f
        //    ? 0
        //    : EstimateWalkStepsToFatigue(GetForcedWalkThreshold01(), configuration.FatigueUnrestrictedFactor);
        //
        //var restrictedWalkTime = EstimateWalkTimeToFatigue(GetForcedWalkThreshold01(), 1.0f);
        //var unrestrictedWalkTime = configuration.FatigueUnrestrictedFactor <= 0.0001f
        //    ? 0
        //    : EstimateWalkTimeToFatigue(GetForcedWalkThreshold01(), configuration.FatigueUnrestrictedFactor);
        //
        //var unrestrictedWalkText = configuration.FatigueUnrestrictedFactor <= 0.0001f
        //        ? $"no unrestricted walking fatigue"
        //        : $"≈ {StepText(unrestrictedWalkSteps)} / {TimeText(unrestrictedWalkTime)} to forced walk unrestricted";
        //
        //var walkText = configuration.FatigueWalkRateMultiplier <= 0.0001f
        //        ? $"no walking fatigue"
        //        : $"≈ {StepText(restrictedWalkSteps)} / {TimeText(unrestrictedWalkTime)} to forced walk, " + unrestrictedWalkText;

        SameLineDisabledText(walkText);




        var speedExponent = configuration.FatigueSpeedExponent;
        if (DrawFloatInput(
            "Speed exponent",
            ref speedExponent,
            0.1f,
            5.0f,
            "Non-linear speed impact above normal run speed. 2.0 means speed impact is squared."))
        {
            configuration.FatigueSpeedExponent = speedExponent;
            configuration.Save();
        }

        var sprintSpeedMultiplier = GetSpeedMultiplierForReference(FatigueUiSprintReferenceMultiplier);
        var sprintWalkSteps = EstimateRunStepsToFatigue(GetForcedWalkThreshold01(), 1.0f, sprintSpeedMultiplier);
        var sprintWalkTime = EstimateRunTimeToFatigue(GetForcedWalkThreshold01(), 1.0f, sprintSpeedMultiplier);
        var sprintStopSteps = EstimateRunStepsToFatigue(GetForcedStopThreshold01(), 1.0f, sprintSpeedMultiplier);

        SameLineDisabledText(
            $"Sprint ref {FatigueUiSprintReferenceMultiplier:0.##}x speed => {sprintSpeedMultiplier:0.##}x fatigue, " +
            $"≈ {StepText(sprintWalkSteps)} / {TimeText(sprintWalkTime)} to forced walk");

        //var normalRunSpeed = configuration.FatigueNormalRunSpeed;
        //if (DrawFloatInput(
        //    "Normal run speed y/s",
        //    ref normalRunSpeed,
        //    0.1f,
        //    30.0f,
        //    "Use the speed recorder to calibrate this for normal unbuffed running."))
        //{
        //    configuration.FatigueNormalRunSpeed = normalRunSpeed;
        //    configuration.Save();
        //}
        //
        //SameLineDisabledText("baseline for sprint / speed buffs");

        var fullRecoveryStandingSeconds = configuration.FatigueFullRecoveryStandingSeconds;
        if (DrawIntInput(
            "Full recovery standing seconds",
            ref fullRecoveryStandingSeconds,
            1,
            86400,
            "How long it takes to recover from 100% to 0% while standing still."))
        {
            configuration.FatigueFullRecoveryStandingSeconds = fullRecoveryStandingSeconds;
            configuration.Save();
        }

        SameLineDisabledText(
            $"100% → 0% in {TimeText(configuration.FatigueFullRecoveryStandingSeconds)} standing");

        var fullRecoveryRestingSeconds = configuration.FatigueFullRecoveryRestingSeconds;
        if (DrawIntInput(
            "Full recovery resting seconds",
            ref fullRecoveryRestingSeconds,
            1,
            86400,
            "How long it takes to recover from 100% to 0% while sitting, lying, mounted, etc."))
        {
            configuration.FatigueFullRecoveryRestingSeconds = fullRecoveryRestingSeconds;
            configuration.Save();
        }

        SameLineDisabledText(
            $"100% → 0% in {TimeText(configuration.FatigueFullRecoveryRestingSeconds)} resting");


        if (ImGui.Button("Set Default##Fatigue"))
        {
            configuration.FatigueForcedWalkPercent = 60;
            configuration.FatigueForcedStopPercent = 80;
            configuration.FatigueForcedSitPercent = 90;
            configuration.FatigueReleaseTolerance = 0.05f;
            configuration.FatigueBaseRunStepsUntilForcedWalk = 600;
            configuration.FatigueUnrestrictedFactor = 1;
            configuration.FatigueWalkRateMultiplier = 0.25f;
            configuration.FatigueSpeedExponent = 2;
            configuration.FatigueFullRecoveryStandingSeconds = 300;
            configuration.FatigueFullRecoveryRestingSeconds = 120;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Set Alt2##Fatigue"))
        {
            configuration.FatigueForcedWalkPercent = 60;
            configuration.FatigueForcedStopPercent = 80;
            configuration.FatigueForcedSitPercent = 90;
            configuration.FatigueReleaseTolerance = 0.05f;
            configuration.FatigueBaseRunStepsUntilForcedWalk = 2500;
            configuration.FatigueUnrestrictedFactor = 1;
            configuration.FatigueWalkRateMultiplier = 0.25f;
            configuration.FatigueSpeedExponent = 2;
            configuration.FatigueFullRecoveryStandingSeconds = 3600;
            configuration.FatigueFullRecoveryRestingSeconds = 900;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Set Athlete##Fatigue"))
        {
            configuration.FatigueForcedWalkPercent = 60;
            configuration.FatigueForcedStopPercent = 80;
            configuration.FatigueForcedSitPercent = 90;
            configuration.FatigueReleaseTolerance = 0.05f;
            configuration.FatigueBaseRunStepsUntilForcedWalk = 4500;
            configuration.FatigueUnrestrictedFactor = 1;
            configuration.FatigueWalkRateMultiplier = 0.37f;
            configuration.FatigueSpeedExponent = 2;
            configuration.FatigueFullRecoveryStandingSeconds = 360;
            configuration.FatigueFullRecoveryRestingSeconds = 180;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Set Average##Fatigue"))
        {
            configuration.FatigueForcedWalkPercent = 60;
            configuration.FatigueForcedStopPercent = 80;
            configuration.FatigueForcedSitPercent = 90;
            configuration.FatigueReleaseTolerance = 0.05f;
            configuration.FatigueBaseRunStepsUntilForcedWalk = 1800;
            configuration.FatigueUnrestrictedFactor = 1;
            configuration.FatigueWalkRateMultiplier = 0.25f;
            configuration.FatigueSpeedExponent = 2;
            configuration.FatigueFullRecoveryStandingSeconds = 600;
            configuration.FatigueFullRecoveryRestingSeconds = 300;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Set Base Dweller##Fatigue"))
        {
            configuration.FatigueForcedWalkPercent = 60;
            configuration.FatigueForcedStopPercent = 80;
            configuration.FatigueForcedSitPercent = 90;
            configuration.FatigueReleaseTolerance = 0.05f;
            configuration.FatigueBaseRunStepsUntilForcedWalk = 600;
            configuration.FatigueUnrestrictedFactor = 1;
            configuration.FatigueWalkRateMultiplier = 0.25f;
            configuration.FatigueSpeedExponent = 2;
            configuration.FatigueFullRecoveryStandingSeconds = 900;
            configuration.FatigueFullRecoveryRestingSeconds = 480;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Set Overweight##Fatigue"))
        {
            configuration.FatigueForcedWalkPercent = 60;
            configuration.FatigueForcedStopPercent = 80;
            configuration.FatigueForcedSitPercent = 90;
            configuration.FatigueReleaseTolerance = 0.05f;
            configuration.FatigueBaseRunStepsUntilForcedWalk = 1000;
            configuration.FatigueUnrestrictedFactor = 1;
            configuration.FatigueWalkRateMultiplier = 0.28f;
            configuration.FatigueSpeedExponent = 2;
            configuration.FatigueFullRecoveryStandingSeconds = 900;
            configuration.FatigueFullRecoveryRestingSeconds = 480;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Set Sedentary##Fatigue"))
        {
            configuration.FatigueForcedWalkPercent = 60;
            configuration.FatigueForcedStopPercent = 80;
            configuration.FatigueForcedSitPercent = 90;
            configuration.FatigueReleaseTolerance = 0.05f;
            configuration.FatigueBaseRunStepsUntilForcedWalk = 350;
            configuration.FatigueUnrestrictedFactor = 1;
            configuration.FatigueWalkRateMultiplier = 0.23f;
            configuration.FatigueSpeedExponent = 2;
            configuration.FatigueFullRecoveryStandingSeconds = 1200;
            configuration.FatigueFullRecoveryRestingSeconds = 720;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Set Injured/Sick##Fatigue"))
        {
            configuration.FatigueForcedWalkPercent = 60;
            configuration.FatigueForcedStopPercent = 80;
            configuration.FatigueForcedSitPercent = 90;
            configuration.FatigueReleaseTolerance = 0.05f;
            configuration.FatigueBaseRunStepsUntilForcedWalk = 150;
            configuration.FatigueUnrestrictedFactor = 1;
            configuration.FatigueWalkRateMultiplier = 0.20f;
            configuration.FatigueSpeedExponent = 2;
            configuration.FatigueFullRecoveryStandingSeconds = 1800;
            configuration.FatigueFullRecoveryRestingSeconds = 900;
            configuration.Save();
        }
        //Athlete:        4500 run steps, 4500 walk steps, 360 standing, 180 sitting
        //Average:        1800 run steps, 2000 walk steps, 600 standing, 300 sitting
        //Base dweller:    600 run steps,  800 walk steps, 900 standing, 480 sitting
        //Overweight:     1000 run steps, 1200 walk steps, 900 standing, 480 sitting
        //Sedentary:       350 run steps,  500 walk steps, 1200 standing, 720 sitting
        //Injured/Sick:    150 run steps,  250 walk steps, 1800 standing, 900 sitting
    }

    private void DrawFatigueRestrictionList()
    {
        ImGui.Text("Fatiguing Restraints");

        var items = configuration.FatigueRestrictions
            .Where(x => x.RestrictionId != Guid.Empty)
            .OrderBy(x => x.RestrictionName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (items.Count == 0)
        {
            ImGui.TextDisabled("No fatiguing restraints added.");
            return;
        }

        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        var selectedRowWidth = 300f;

        //ImGui.TextDisabled("Scroll sideways for standing-pain settings.");

        //var childHeight = Math.Min(360f, 28f + items.Count * ImGui.GetFrameHeightWithSpacing());

        //if (ImGui.BeginChild(
        //        "FatigueRestrictionListScroll",
        //        new Vector2(0, childHeight),
        //        false,
        //        ImGuiWindowFlags.HorizontalScrollbar))
        //{
            ImGui.Indent();

            foreach (var item in items)
            {
                ImGui.PushID($"FatigueRestriction-{item.RestrictionId}");

                var displayName = string.IsNullOrWhiteSpace(item.RestrictionName)
                    ? item.RestrictionId.ToString()
                    : item.RestrictionName;

                if (DrawGagSpeakItem(displayName, selectedRowWidth, ctrlHeld))
                {
                    configuration.FatigueRestrictions.RemoveAll(x => x.RestrictionId == item.RestrictionId);
                    configuration.Save();

                    ImGui.PopID();
                    continue;
                }

                ImGui.SameLine();

                ImGui.SetNextItemWidth(70);
                var factorText = item.FatigueFactor.ToString("0.###");

                if (ImGui.InputText("Factor##FatigueFactor", ref factorText, 16, ImGuiInputTextFlags.CharsDecimal))
                {
                    if (float.TryParse(factorText.Trim(), out var factor))
                    {
                        item.FatigueFactor = Math.Clamp(factor, 0.0f, 100.0f);
                        configuration.Save();
                    }
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("1.0 = normal, 2.0 = twice as exhausting, 0.5 = half as exhausting.");

                var forcedWalkSteps = EstimateRunStepsToFatigue(
                    GetForcedWalkThreshold01(),
                    item.FatigueFactor);

                var forcedStopSteps = EstimateRunStepsToFatigue(
                    GetForcedStopThreshold01(),
                    item.FatigueFactor);

                ImGui.SameLine();
                ImGui.TextDisabled(
                    //$"≈ {StepText(forcedWalkSteps)} to forced walk, {StepText(forcedStopSteps)} to stop");
                    $"≈ {StepText(forcedWalkSteps)} to forced walk");

                ImGui.SameLine();

                ImGui.SetNextItemWidth(80);
                var standingText = item.StandingSecondsUntilForcedSit <= 0
                    ? ""
                    : item.StandingSecondsUntilForcedSit.ToString();

                if (ImGui.InputTextWithHint(
                        "Stand s##StandingSecondsUntilForcedSit",
                        "off",
                        ref standingText,
                        16,
                        ImGuiInputTextFlags.CharsDecimal))
                {
                    if (string.IsNullOrWhiteSpace(standingText))
                    {
                        item.StandingSecondsUntilForcedSit = 0;
                        configuration.Save();
                    }
                    else if (int.TryParse(standingText.Trim(), out var seconds))
                    {
                        item.StandingSecondsUntilForcedSit = Math.Clamp(seconds, 0, 86400);
                        configuration.Save();
                    }
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Seconds standing still from 0% fatigue until forced sit/stop. Empty or 0 disables standing fatigue.");

                ImGui.SameLine();

                ImGui.TextDisabled(
                    StandingSitText(EstimateStandingSecondsToForcedSit(item)));

                ImGui.PopID();
            }

            ImGui.Unindent();
        //}
        //
        //ImGui.EndChild();
    }

    private void DrawFatigueAddRestriction()
    {
        ImGui.Text("Add fatiguing restraint");

        var availableItems = availableFatigueRestrictions;

        if (selectedFatigueRestrictionToAdd != null &&
            !availableItems.Any(x => x.Key == selectedFatigueRestrictionToAdd.Value))
        {
            selectedFatigueRestrictionToAdd = null;
        }

        var selectedName = selectedFatigueRestrictionToAdd != null
            ? availableItems.FirstOrDefault(x => x.Key == selectedFatigueRestrictionToAdd.Value).Value
            : "Select restriction...";

        ImGui.SetNextItemWidth(300);

        if (ImGui.BeginCombo("##FatigueAvailableRestrictions", selectedName))
        {
            ImGui.SetNextItemWidth(-1);

            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();

            ImGui.InputTextWithHint(
                "##FatigueRestrictionSearch",
                "Search...",
                ref fatigueRestrictionSearch,
                128);

            ImGui.Separator();

            var filteredItems = string.IsNullOrWhiteSpace(fatigueRestrictionSearch)
                ? availableItems
                : availableItems
                    .Where(x => x.Value.Contains(
                        fatigueRestrictionSearch,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (filteredItems.Count == 0)
            {
                ImGui.TextDisabled("No matches.");
            }
            else
            {
                foreach (var (guid, name) in filteredItems)
                {
                    var isSelected = selectedFatigueRestrictionToAdd == guid;

                    if (ImGui.Selectable($"{name}##AvailableFatigueRestriction{guid}", isSelected))
                    {
                        selectedFatigueRestrictionToAdd = guid;
                        fatigueRestrictionSearch = string.Empty;
                        ImGui.CloseCurrentPopup();
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        ImGui.SetNextItemWidth(70);
        ImGui.InputTextWithHint(
            "##NewFatigueRestrictionFactor",
            "Factor",
            ref selectedFatigueRestrictionFactor,
            16,
            ImGuiInputTextFlags.CharsDecimal);

        ImGui.SameLine();

        float parsedFactor = 1.0f;
        var canAdd =
            selectedFatigueRestrictionToAdd != null &&
            availableItems.Any(x => x.Key == selectedFatigueRestrictionToAdd.Value) &&
            float.TryParse(selectedFatigueRestrictionFactor.Trim(), out parsedFactor);

        if (!canAdd)
            ImGui.BeginDisabled();

        if (ImGui.Button("Add restraint##Fatigue"))
        {
            var selected = availableItems
                .Cast<KeyValuePair<Guid, string>?>()
                .FirstOrDefault(x => x?.Key == selectedFatigueRestrictionToAdd.Value);

            if (selected != null)
            {
                configuration.FatigueRestrictions.Add(new FatigueTracker.FatigueRestrictionConfig
                {
                    RestrictionId = selected.Value.Key,
                    RestrictionName = selected.Value.Value,
                    FatigueFactor = Math.Clamp(parsedFactor, 0.0f, 100.0f),
                    StandingSecondsUntilForcedSit = 0,
                });

                configuration.Save();

                availableFatigueRestrictions.RemoveAll(x => x.Key == selected.Value.Key);

                selectedFatigueRestrictionToAdd = null;
                selectedFatigueRestrictionFactor = "1.0";
                fatigueRestrictionSearch = string.Empty;
            }
        }

        if (!canAdd)
            ImGui.EndDisabled();

        if (ImGui.Button("Reload restrictions from GagSpeak##Fatigue"))
        {
            RefreshFatigueAvailableRestrictions();
            fatigueRestrictionSearch = string.Empty;
        }
    }

    private void RefreshFatigueAvailableRestrictions()
    {
        var configuredIds = configuration.FatigueRestrictions
            .Select(x => x.RestrictionId)
            .ToHashSet();

        var availableRestrictions = plugin.GagSpeakRestrictionsApi.GetAvailableRestrictions();

        availableFatigueRestrictions = availableRestrictions?
            .Where(x => !configuredIds.Contains(x.Key))
            .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<KeyValuePair<Guid, string>>();

        selectedFatigueRestrictionToAdd = null;
    }

    private bool DrawFloatInput(
    string label,
    ref float value,
    float min,
    float max,
    string tooltip)
    {
        ImGui.SetNextItemWidth(90);

        var text = value.ToString("0.###");

        if (ImGui.InputText(label, ref text, 32, ImGuiInputTextFlags.CharsDecimal))
        {
            if (float.TryParse(text.Trim(), out var parsed))
            {
                value = Math.Clamp(parsed, min, max);

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(tooltip);

                return true;
            }
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);

        return false;
    }

    private bool DrawIntInput(
    string label,
    ref int value,
    int min,
    int max,
    string tooltip)
    {
        ImGui.SetNextItemWidth(90);

        var text = value.ToString();

        if (ImGui.InputText(label, ref text, 32, ImGuiInputTextFlags.CharsDecimal))
        {
            if (int.TryParse(text.Trim(), out var parsed))
            {
                value = Math.Clamp(parsed, min, max);

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(tooltip);

                return true;
            }
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);

        return false;
    }

    private const float FatigueUiSprintReferenceMultiplier = 1.30f;

    private float GetForcedWalkThreshold01()
    {
        return Math.Clamp(configuration.FatigueForcedWalkPercent / 100.0f, 0.01f, 1.0f);
    }

    private float GetForcedStopThreshold01()
    {
        return Math.Clamp(configuration.FatigueForcedStopPercent / 100.0f, 0.01f, 1.0f);
    }

    private float GetForcedSitThreshold01()
    {
        return Math.Clamp(configuration.FatigueForcedSitPercent / 100.0f, 0.01f, 1.0f);
    }

    private int GetBaseRunStepsToForcedWalk()
    {
        return Math.Max(1, configuration.FatigueBaseRunStepsUntilForcedWalk);
    }

    private float GetSpeedMultiplierForReference(float speedRatio)
    {
        var exponent = Math.Clamp(configuration.FatigueSpeedExponent, 0.1f, 5.0f);

        if (speedRatio <= 1.0f)
            return 1.0f;

        return MathF.Pow(speedRatio, exponent);
    }

    private int EstimateRunStepsToFatigue(float targetFatigue01, float fatigueFactor, float speedMultiplier = 1.0f)
    {
        var forcedWalk = GetForcedWalkThreshold01();
        var baseSteps = GetBaseRunStepsToForcedWalk();

        fatigueFactor = Math.Max(0.0001f, fatigueFactor);
        speedMultiplier = Math.Max(0.0001f, speedMultiplier);

        // baseSteps reaches forcedWalk at factor 1.0.
        // So reaching any target is scaled by target / forcedWalk.
        var steps = baseSteps * (targetFatigue01 / forcedWalk) / fatigueFactor / speedMultiplier;

        return Math.Max(1, (int)MathF.Round(steps));
    }

    private int EstimateWalkStepsToFatigue(float targetFatigue01, float fatigueFactor)
    {
        var walkMultiplier = Math.Max(0.0001f, configuration.FatigueWalkRateMultiplier);
        return EstimateRunStepsToFatigue(targetFatigue01, fatigueFactor * walkMultiplier);
    }
    private int EstimateWalkStepsFromFatigueToStop(float targetFatigue01, float targetStop01, float fatigueFactor)
    {
        var targetFatigue = targetStop01 - targetFatigue01;
        var walkMultiplier = Math.Max(0.0001f, configuration.FatigueWalkRateMultiplier);
        return EstimateRunStepsToFatigue(targetFatigue, fatigueFactor * walkMultiplier);
    }

    private int EstimateRunTimeToFatigue(float targetFatigue01, float fatigueFactor, float speedMultiplier = 1.0f)
    {
        var forcedWalk = GetForcedWalkThreshold01();
        var baseSteps = GetBaseRunStepsToForcedWalk();

        fatigueFactor = Math.Max(0.0001f, fatigueFactor);
        speedMultiplier = Math.Max(0.0001f, speedMultiplier);

        // baseSteps reaches forcedWalk at factor 1.0.
        // So reaching any target is scaled by target / forcedWalk.
        var steps = baseSteps * (targetFatigue01 / forcedWalk) / fatigueFactor / speedMultiplier;

        var distance = steps * FatigueTracker.RunStepLength;

        var time = distance / FatigueTracker.RunSpeed;

        return Math.Max(1, (int)MathF.Round(time));
    }
    private int EstimateWalkTimeToFatigue(float targetFatigue01, float fatigueFactor)
    {
        var walkMultiplier = Math.Max(0.0001f, configuration.FatigueWalkRateMultiplier);
        var steps = EstimateRunStepsToFatigue(targetFatigue01, fatigueFactor * walkMultiplier);

        var distance = steps * FatigueTracker.WalkStepLength;

        var time = distance / FatigueTracker.WalkSpeed;

        return Math.Max(1, (int)MathF.Round(time));
    }
    private int EstimateWalkTimeFromFatigueToStop(float targetFatigue01, float targetStop01, float fatigueFactor)
    {
        var targetFatigue = targetStop01 - targetFatigue01;
        var walkMultiplier = Math.Max(0.0001f, configuration.FatigueWalkRateMultiplier);
        var steps = EstimateRunStepsToFatigue(targetFatigue, fatigueFactor * walkMultiplier);

        var distance = steps * FatigueTracker.WalkStepLength;

        var time = distance / FatigueTracker.WalkSpeed;

        return Math.Max(1, (int)MathF.Round(time));
    }

    private string StepText(int steps)
    {
        return $"{steps:n0} steps";
    }
    private string TimeText(int seconds)
    {
        if (seconds < 0) seconds = 0;

        int hours = seconds / 3600;
        int minutes = (seconds % 3600) / 60;
        int secs = seconds % 60;

        if (hours > 0)
            return secs > 0 ? $"{hours}h{minutes}m{secs}s" : minutes > 0 ? $"{hours}h{minutes}m" : $"{hours}h";

        if (minutes > 0)
            return secs > 0 ? $"{minutes}m{secs}s" : $"{minutes}m";

        return $"{secs}s";
    }

    private void SameLineDisabledText(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled(text);
    }

    private int EstimateStandingSecondsToForcedSit(FatigueTracker.FatigueRestrictionConfig item)
    {
        if (item.StandingSecondsUntilForcedSit <= 0)
            return 0;

        return Math.Max(1, item.StandingSecondsUntilForcedSit);
    }

    private string StandingSitText(int seconds)
    {
        return seconds <= 0
            ? "standing fatigue off"
            : $"≈ {seconds:n0}s standing to forced sit";
    }
    public static Vector4 GetFatigueStatusColor(FatigueTracker.FatigueStatusLevel status)
    {
        return status switch
        {
            FatigueTracker.FatigueStatusLevel.Fresh => new Vector4(0.20f, 0.85f, 0.25f, 1.0f),
            FatigueTracker.FatigueStatusLevel.Straining => new Vector4(0.75f, 0.85f, 0.25f, 1.0f),
            FatigueTracker.FatigueStatusLevel.Burning => new Vector4(1.00f, 0.65f, 0.20f, 1.0f),
            FatigueTracker.FatigueStatusLevel.Stalled => new Vector4(1.00f, 0.35f, 0.20f, 1.0f),
            FatigueTracker.FatigueStatusLevel.Broken => new Vector4(0.75f, 0.20f, 1.00f, 1.0f),
            _ => new Vector4(0.45f, 0.45f, 0.45f, 1.0f),
        };
    }

    public static string GetFatigueStatusLabel(FatigueTracker.FatigueStatusLevel status)
    {
        return status switch
        {
            FatigueTracker.FatigueStatusLevel.Fresh => "OK",
            FatigueTracker.FatigueStatusLevel.Straining => "Slightly tired",
            FatigueTracker.FatigueStatusLevel.Burning => "Exhausted",
            FatigueTracker.FatigueStatusLevel.Stalled => "Drained",
            FatigueTracker.FatigueStatusLevel.Broken => "Worn out",
            _ => "Inactive",
        };
    }
    private void DrawFatigueMoodles()
    {
        ImGui.Text("Fatigue Moodles");
        ImGui.TextWrapped("All Moodles are optional. Status Moodles are exclusive: only one fatigue status Moodle is enforced at a time.");
        ImGui.TextWrapped("Status changes use the same release tolerance as force walk / stop / sit.");

        ImGui.Spacing();

        DrawSingleFatigueMoodlePicker(
            label: "Fatigue enabled",
            controlId: "fatigue-enabled-moodle",
            currentId: configuration.FatigueEnabledMoodleId,
            currentName: configuration.FatigueEnabledMoodleName,
            onChanged: (id, name) =>
            {
                configuration.FatigueEnabledMoodleId = id;
                configuration.FatigueEnabledMoodleName = name;
                configuration.Save();
            });

        DrawSingleFatigueMoodlePicker(
            label: "Wearing fatigue restraint + enabled",
            controlId: "fatigue-restrained-moodle",
            currentId: configuration.FatigueRestrainedMoodleId,
            currentName: configuration.FatigueRestrainedMoodleName,
            onChanged: (id, name) =>
            {
                configuration.FatigueRestrainedMoodleId = id;
                configuration.FatigueRestrainedMoodleName = name;
                configuration.Save();
            });

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Status Moodles");

        DrawSingleFatigueMoodlePicker(
            label: "Fresh",
            controlId: "fatigue-status-ok-moodle",
            currentId: configuration.FatigueStatusFreshMoodleId,
            currentName: configuration.FatigueStatusFreshMoodleName,
            onChanged: (id, name) =>
            {
                configuration.FatigueStatusFreshMoodleId = id;
                configuration.FatigueStatusFreshMoodleName = name;
                configuration.Save();
            });

        DrawSingleFatigueMoodlePicker(
            label: "Straining",
            controlId: "fatigue-status-slightly-tired-moodle",
            currentId: configuration.FatigueStatusStrainingMoodleId,
            currentName: configuration.FatigueStatusStrainingMoodleName,
            onChanged: (id, name) =>
            {
                configuration.FatigueStatusStrainingMoodleId = id;
                configuration.FatigueStatusStrainingMoodleName = name;
                configuration.Save();
            });

        DrawSingleFatigueMoodlePicker(
            label: "Burning",
            controlId: "fatigue-status-exhausted-moodle",
            currentId: configuration.FatigueStatusBurningMoodleId,
            currentName: configuration.FatigueStatusBurningMoodleName,
            onChanged: (id, name) =>
            {
                configuration.FatigueStatusBurningMoodleId = id;
                configuration.FatigueStatusBurningMoodleName = name;
                configuration.Save();
            });

        DrawSingleFatigueMoodlePicker(
            label: "Stalled",
            controlId: "fatigue-status-drained-moodle",
            currentId: configuration.FatigueStatusStalledMoodleId,
            currentName: configuration.FatigueStatusStalledMoodleName,
            onChanged: (id, name) =>
            {
                configuration.FatigueStatusStalledMoodleId = id;
                configuration.FatigueStatusStalledMoodleName = name;
                configuration.Save();
            });

        DrawSingleFatigueMoodlePicker(
            label: "Broken",
            controlId: "fatigue-status-worn-out-moodle",
            currentId: configuration.FatigueStatusBrokenMoodleId,
            currentName: configuration.FatigueStatusBrokenMoodleName,
            onChanged: (id, name) =>
            {
                configuration.FatigueStatusBrokenMoodleId = id;
                configuration.FatigueStatusBrokenMoodleName = name;
                configuration.Save();
            });

        ImGui.Spacing();

        if (ImGui.Button("Refresh Moodle list##fatigue"))
            _ = RefreshMoodleBlockOptionsAsync(force: true);

        if (moodleBlockOptionsLoading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Loading Moodles...");
        }
    }
    private void DrawSingleFatigueMoodlePicker(
    string label,
    string controlId,
    Guid currentId,
    string currentName,
    Action<Guid, string> onChanged)
    {
        ImGui.TextUnformatted(label);

        var displayName = GetCurrentFatigueMoodleDisplayName(currentId, currentName);

        stagedFatigueMoodleSelections.TryGetValue(controlId, out var stagedId);

        var stagedPreview = stagedId != Guid.Empty && moodleBlockOptions.TryGetValue(stagedId, out var stagedName)
            ? stagedName
            : displayName;

        const float selectedRowWidth = 300f;

        if (currentId != Guid.Empty)
        {
            var ctrlHeld = ImGui.GetIO().KeyCtrl;

            ImGui.Indent();

            ImGui.PushID($"{controlId}-selected-{currentId}");

            if (DrawGagSpeakItem(displayName, selectedRowWidth, ctrlHeld))
            {
                onChanged(Guid.Empty, string.Empty);

                stagedFatigueMoodleSelections.Remove(controlId);
                fatigueMoodleSearchText[controlId] = string.Empty;
            }

            ImGui.PopID();

            ImGui.Unindent();
        }
        else
        {
            ImGui.TextDisabled("No Moodle selected.");
        }

        ImGui.SetNextItemWidth(300);

        if (ImGui.BeginCombo($"##{controlId}-combo", stagedPreview))
        {
            if (!fatigueMoodleSearchText.TryGetValue(controlId, out var searchText))
                searchText = string.Empty;

            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText($"##{controlId}-search", ref searchText, 128))
                fatigueMoodleSearchText[controlId] = searchText;

            ImGui.Separator();

            if (ImGui.Selectable($"None##{controlId}-none", currentId == Guid.Empty))
            {
                stagedFatigueMoodleSelections[controlId] = Guid.Empty;
                fatigueMoodleSearchText[controlId] = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.Separator();

            var filteredMoodles = moodleBlockOptions
                .Where(x => string.IsNullOrWhiteSpace(searchText) ||
                            x.Value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var moodle in filteredMoodles)
            {
                var isSelected = moodle.Key == currentId;
                var isStaged = moodle.Key == stagedId;

                if (ImGui.Selectable($"{moodle.Value}##{controlId}-{moodle.Key}", isSelected || isStaged))
                {
                    stagedFatigueMoodleSelections[controlId] = moodle.Key;
                    fatigueMoodleSearchText[controlId] = string.Empty;
                    ImGui.CloseCurrentPopup();
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            if (moodleBlockOptions.Count == 0)
                ImGui.TextDisabled(moodleBlockOptionsLoading ? "Loading Moodles..." : "No Moodles found. Click Refresh Moodle list.");
            else if (filteredMoodles.Length == 0)
                ImGui.TextDisabled("No matching Moodles.");

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        var stagedValid =
            stagedId == Guid.Empty ||
            moodleBlockOptions.ContainsKey(stagedId);

        if (!stagedFatigueMoodleSelections.ContainsKey(controlId) || !stagedValid)
            ImGui.BeginDisabled();

        if (ImGui.Button($"Set##{controlId}-set"))
        {
            if (stagedId == Guid.Empty)
            {
                onChanged(Guid.Empty, string.Empty);
            }
            else if (moodleBlockOptions.TryGetValue(stagedId, out var selectedName))
            {
                onChanged(stagedId, selectedName);
            }

            stagedFatigueMoodleSelections.Remove(controlId);
            fatigueMoodleSearchText[controlId] = string.Empty;
        }

        if (!stagedFatigueMoodleSelections.ContainsKey(controlId) || !stagedValid)
            ImGui.EndDisabled();

        ImGui.Spacing();
    }

    private string GetCurrentFatigueMoodleDisplayName(Guid id, string savedName)
    {
        if (id == Guid.Empty)
            return "None";

        if (moodleBlockOptions.TryGetValue(id, out var currentName))
            return currentName;

        if (!string.IsNullOrWhiteSpace(savedName))
            return $"{savedName} (missing)";

        return $"{id} (missing)";
    }
}
