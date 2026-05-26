using Dalamud.Configuration;
using Dalamud.Game.Text;
using System;
using System.Collections.Generic;
using static SayusGagExtender.CammyEnforcer;
using static SayusGagExtender.HonorificEnforcer;
using static SayusGagExtender.MoodleEnforcer;
using static SayusGagExtender.PenumbraEnforcer;
using static SayusGagExtender.RandomVibeSender;
using static SayusGagExtender.RandomZapSender;

namespace SayusGagExtender;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool OpenMainWindowOnStartup { get; set; } = false;
    public bool OpenConfigWindowOnStartup { get; set; } = false;
    public bool OpenMiniWindowOnStartup { get; set; } = false;


    public bool EmoteGuardEnabled { get; set; } = false;
    public bool HandGuardEnabled { get; set; } = false;

    public Dictionary<Guid, string> AutoZapRequiredRestrictions { get; set; } = new Dictionary<Guid, string>();
    public List<WeightedZapCommand> AutoZapCommands { get; set; } = new();
    public string ZapControllerName { get; set; }
    public bool AutoZapEnabled { get; set; } = false;
    public int AutoZapCount { get; set; } = 8;
    public bool AutoZapCountControllerLocked { get; set; } = false;
    public Guid AutoZapEngagedMoodleId { get; set; } = Guid.Empty;
    public string AutoZapEngagedMoodleName { get; set; } = string.Empty;
    public Guid AutoZapControllerOnlineMoodleId { get; set; } = Guid.Empty;
    public string AutoZapControllerOnlineMoodleName { get; set; } = string.Empty;
    public RandomZapSender.OperateWhen AutoZapWhen { get; set; } = RandomZapSender.OperateWhen.Offline;
    public Dictionary<Guid, string> AutoVibeRequiredRestrictions { get; set; } = new Dictionary<Guid, string>();
    public List<WeightedVibeCommand> AutoVibeCommands { get; set; } = new();
    public string VibeControllerName { get; set; }
    public bool AutoVibeEnabled { get; set; } = false;
    public int AutoVibeCount { get; set; } = 8;
    public bool AutoVibeCountControllerLocked { get; set; } = false;
    public Guid AutoVibeEngagedMoodleId { get; set; } = Guid.Empty;
    public string AutoVibeEngagedMoodleName { get; set; } = string.Empty;
    public Guid AutoVibeControllerOnlineMoodleId { get; set; } = Guid.Empty;
    public string AutoVibeControllerOnlineMoodleName { get; set; } = string.Empty;
    public RandomVibeSender.OperateWhen AutoVibeWhen { get; set; } = RandomVibeSender.OperateWhen.Offline;


    public bool MountBlockFeature { get; set; } = false;
    public Dictionary<Guid, string> MountBlockMoodles { get; set; } = new();
    public bool MountQuotaEnabled { get; set; } = false;
    public int MountQuotaActions { get; set; } = -1;
    public QuotaWindow MountQuotaWindow { get; set; } = QuotaWindow.Hour;
    public List<DateTime> MountQuotaActionLogUtc { get; set; } = new();
    public Guid MountQuotaMoodleId { get; set; } = Guid.Empty;
    public Guid MountQuotaEmptyMoodleId { get; set; } = Guid.Empty;
    public string MountQuotaMoodleName { get; set; } = string.Empty;
    public string MountQuotaEmptyMoodleName { get; set; } = string.Empty;


    public bool TeleportBlockFeature { get; set; } = false;
    public Dictionary<Guid, string> TeleportBlockMoodles { get; set; } = new();
    public bool TeleportQuotaEnabled { get; set; } = false;
    public int TeleportQuotaActions { get; set; } = 0;
    public QuotaWindow TeleportQuotaWindow { get; set; } = QuotaWindow.Hour;
    public List<DateTime> TeleportQuotaActionLogUtc { get; set; } = new();
    public Guid TeleportQuotaMoodleId { get; set; } = Guid.Empty;
    public string TeleportQuotaMoodleName { get; set; } = string.Empty;
    public Guid TeleportQuotaEmptyMoodleId { get; set; } = Guid.Empty;
    public string TeleportQuotaEmptyMoodleName { get; set; } = string.Empty;


    public bool JobSwitchBlockFeature { get; set; } = false;
    public Dictionary<Guid, string> JobSwitchBlockMoodles { get; set; } = new();
    public bool JobSwitchQuotaEnabled { get; set; } = false;
    public int JobSwitchQuotaActions { get; set; } = 0;
    public QuotaWindow JobSwitchQuotaWindow { get; set; } = QuotaWindow.Hour;
    public List<DateTime> JobSwitchQuotaActionLogUtc { get; set; } = new();
    public Guid JobSwitchQuotaMoodleId { get; set; } = Guid.Empty;
    public string JobSwitchQuotaMoodleName { get; set; } = string.Empty;
    public Guid JobSwitchQuotaEmptyMoodleId { get; set; } = Guid.Empty;
    public string JobSwitchQuotaEmptyMoodleName { get; set; } = string.Empty;

    public bool HonorificEnforcerEnabled { get; set; } = false;
    public List<HonorificEnforcerConfig> HonorificEnforcerTitles { get; set; } = new();

    public bool FatigueEnabled { get; set; } = false;

    // Runtime value, 0.0 = recovered, 1.0 = fully exhausted.
    public float FatigueCurrent { get; set; } = 0.0f;

    // User-facing thresholds.
    public float FatigueForcedWalkPercent { get; set; } = 75.0f;
    public float FatigueForcedStopPercent { get; set; } = 95.0f;
    public float FatigueForcedSitPercent { get; set; } = 100.0f;

    // User-friendly base calibration.
    // "How many normal unbuffed running steps from 0% fatigue to forced walk."
    public int FatigueBaseRunStepsUntilForcedWalk { get; set; } = 600;

    // 0.0 = no fatigue without configured active restraints.
    // 1.0 = normal fatigue even with no restraints.
    // 0.25 = light passive fatigue without restraints.
    public float FatigueUnrestrictedFactor { get; set; } = 0.0f;

    // Walking usually should be less exhausting than running.
    public float FatigueWalkRateMultiplier { get; set; } = 0.25f;

    // Non-linear speed impact.
    // 2.0 means 15% faster movement causes about 32% more fatigue.
    public float FatigueSpeedExponent { get; set; } = 2.0f;

    // Dev-calibrated normal run speed in yalms/second.
    // Use FatigueTracker speed recording commands to tune this.
    public float FatigueNormalRunSpeed { get; set; } = 6.0f;

    // Full recovery from 100% to 0%.
    public int FatigueFullRecoveryStandingSeconds { get; set; } = 300;
    public int FatigueFullRecoveryRestingSeconds { get; set; } = 120;

    // Raw 0.0 - 1.0 fatigue value.
    // 0.05 = 5 percentage points.
    // A force state turns on at its threshold, then only releases once fatigue
    // drops below threshold - this tolerance.
    public float FatigueReleaseTolerance { get; set; } = 0.05f;


    // Restriction list with stackable factors.
    public List<FatigueTracker.FatigueRestrictionConfig> FatigueRestrictions { get; set; } = new();
    public Guid FatigueEnabledMoodleId { get; set; } = Guid.Empty;
    public string FatigueEnabledMoodleName { get; set; } = string.Empty;

    public Guid FatigueRestrainedMoodleId { get; set; } = Guid.Empty;
    public string FatigueRestrainedMoodleName { get; set; } = string.Empty;

    public Guid FatigueStatusFreshMoodleId { get; set; } = Guid.Empty;
    public string FatigueStatusFreshMoodleName { get; set; } = string.Empty;

    public Guid FatigueStatusStrainingMoodleId { get; set; } = Guid.Empty;
    public string FatigueStatusStrainingMoodleName { get; set; } = string.Empty;

    public Guid FatigueStatusBurningMoodleId { get; set; } = Guid.Empty;
    public string FatigueStatusBurningMoodleName { get; set; } = string.Empty;

    public Guid FatigueStatusStalledMoodleId { get; set; } = Guid.Empty;
    public string FatigueStatusStalledMoodleName { get; set; } = string.Empty;

    public Guid FatigueStatusBrokenMoodleId { get; set; } = Guid.Empty;
    public string FatigueStatusBrokenMoodleName { get; set; } = string.Empty;
    public sealed class FatigueEffectConfig
    {
        public Guid MoodleId { get; set; } = Guid.Empty;
        public string MoodleName { get; set; } = string.Empty;

        public string HonorificTitle { get; set; } = string.Empty;
        public System.Numerics.Vector3 HonorificColor { get; set; } = new(1f, 1f, 1f);
        public System.Numerics.Vector3 HonorificGlow { get; set; } = new(0f, 0f, 0f);
        public int HonorificPriority { get; set; } = 0;
    }

    public FatigueEffectConfig FatigueEnabledEffect { get; set; } = new();
    public FatigueEffectConfig FatigueRestrainedEffect { get; set; } = new();

    public FatigueEffectConfig FatigueStatusFreshEffect { get; set; } = new();
    public FatigueEffectConfig FatigueStatusStrainingEffect { get; set; } = new();
    public FatigueEffectConfig FatigueStatusBurningEffect { get; set; } = new();
    public FatigueEffectConfig FatigueStatusStalledEffect { get; set; } = new();
    public FatigueEffectConfig FatigueStatusBrokenEffect { get; set; } = new();




    public string RemotePermanentHonorificTitleJson { get; set; } = string.Empty;



    public bool XivMessengerManagerEnabled { get; set; } = false;



    public bool CammyEnforcerEnabled { get; set; } = false;
    public List<CammyEnforcerConfig> CammyEnforcerPresets { get; set; } = new();
    public string CammyEnforcerDefaultPresetName { get; set; } = string.Empty;















    public bool JobRouletteEnabled { get; set; } = false;
    public bool JobRouletteLockManualChanges { get; set; } = true;
    public bool JobRouletteSwapEvenIfLockedOrOutOfQuota { get; set; } = true;
    public bool JobRouletteRemoteSet { get; set; } = true;
    public List<JobRouletteGearsetConfig> JobRouletteWhitelistedGearsets { get; set; } = new();
    public DateTime NextScheduledJobSwitch { get; set; } = DateTime.MinValue;
    public TimeSpan JobRouletteInterval { get; set; } = TimeSpan.FromHours(1);

    [Serializable]
    public sealed class JobRouletteGearsetConfig
    {
        public int GearsetId { get; set; } = -1;
        public string GearsetName { get; set; } = string.Empty;
        public byte ClassJobId { get; set; }
        public string JobName { get; set; } = string.Empty;
    }









    public API.Chat2Api.Chat2Bounds Chat2Bounds { get; set; } = new API.Chat2Api.Chat2Bounds();
    public string Chat2HiddenTabName { get; set; }
    public string ActiveRestraintSet { get; set; }
    public Dictionary<int, string> ActiveRestrictions { get; set; } = new Dictionary<int, string>();
    public Dictionary<int, string> ActiveGags { get; set; } = new Dictionary<int, string>();
    public bool GagSpeakRestraintCloner { get; set; }
    public bool GagSpeakEnforcedRestraintCloner { get; set; }
    public string GagSpeakMasterName { get; set; }
    public string GagSpeakMasterWorld { get; set; }
    public Dictionary<Guid, string> HandGuardBlockedItems { get; set; } = new Dictionary<Guid, string>();
    public bool MoodleEnforcerEnabled { get; set; } = false;
    public List<MoodleEnforcerMoodleConfig> MoodleEnforcerMoodles { get; set; } = new();
    public bool PenumbraEnforcerEnabled { get; set; }
    public List<PenumbraEnforcerConfig> PenumbraEnforcerMods { get; set; } = new();
    public bool EmoteEnforcerEnabled { get; set; } = false;
    public List<EmoteEnforcer.EmoteEnforcerEmoteConfig> EmoteEnforcerEmotes { get; set; } = new();
    public string EmoteEnforcerCancelCommand { get; set; } = "/sit /wait 0.5 /sit";
    public bool CustomizePlusEnforcerEnabled { get; set; }
    public List<CustomizePlusEnforcer.CustomizePlusEnforcerConfig> CustomizePlusEnforcerProfiles { get; set; } = new();
    public Guid CustomizePlusDefaultProfileId { get; set; } = Guid.Empty;
    public string CustomizePlusDefaultProfileName { get; set; } = string.Empty;
    public string CustomizePlusDefaultProfileVirtualPath { get; set; } = string.Empty;
    public bool Chat2BlindfoldFeatureEnable { get; set; } = false;
    public bool Chat2BlindfoldLocked { get; set; } = false;

    public bool RemoteChatCommandsEnabled { get; set; } = false;

    public string RemoteChatCommandPrefix { get; set; } = "sge";

    public string RemoteControllerName { get; set; } = string.Empty;
    public string RemoteControllerWorld { get; set; } = string.Empty;

    // Start narrow, but this supports all chat types.
    public List<XivChatType> RemoteAcceptedChannels { get; set; } =
    [
];
    public enum QuotaWindow
    {
        Hour = 0,
        Day = 1,
    }









    public bool ControllerWindowPreferred { get; set; } = false;
    public List<ControllerUserConfig> ControllerUsers { get; set; } = new();

    [Serializable]
    public sealed class ControllerUserConfig
    {
        public string Name { get; set; } = string.Empty;
        public string World { get; set; } = string.Empty;
        public DateTime LastStatusUtc { get; set; } = DateTime.MinValue;
        public bool AutoZapEnabled { get; set; } = false;
        public int AutoZapCount { get; set; } = 0;
        public string AutoZapWhen { get; set; } = string.Empty;
        public bool AutoZapLocked { get; set; } = false;
        public bool AutoVibeEnabled { get; set; } = false;
        public int AutoVibeCount { get; set; } = 0;
        public string AutoVibeWhen { get; set; } = string.Empty;
        public bool AutoVibeLocked { get; set; } = false;
        public bool MountQuotaEnabled { get; set; } = false;
        public int MountQuotaActions { get; set; } = -1;
        public int MountQuotaUsed { get; set; } = -1;
        public QuotaWindow MountQuotaWindow { get; set; } = QuotaWindow.Hour;
        public bool TeleportQuotaEnabled { get; set; } = false;
        public int TeleportQuotaActions { get; set; } = -1;
        public int TeleportQuotaUsed { get; set; } = -1;
        public QuotaWindow TeleportQuotaWindow { get; set; } = QuotaWindow.Hour;
        public bool JobQuotaEnabled { get; set; } = false;
        public int JobQuotaActions { get; set; } = -1;
        public int JobQuotaUsed { get; set; } = -1;
        public QuotaWindow JobQuotaWindow { get; set; } = QuotaWindow.Hour;
        public bool JobRouletteEnabled { get; set; } = false;
        public bool JobRouletteLocked { get; set; } = false;
        public TimeSpan JobRouletteInterval { get; set; } = TimeSpan.Zero;
        public DateTime NextScheduledJobSwitchUtc { get; set; } = DateTime.MinValue;
        public int JobRouletteWhitelistedGearsetCount { get; set; } = -1;
        //public int JobRouletteIntervalMinutes { get; set; } = 0;
        public string RemoteTitle { get; set; } = string.Empty;
        public ControllerHonorificTitleConfig HonorificTitle { get; set; } = new();
    }




    [Serializable]
    public sealed class ControllerHonorificTitleConfig
    {
        public string Json { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public System.Numerics.Vector3 Color { get; set; } = new(1f, 1f, 1f);
        public System.Numerics.Vector3 Glow { get; set; } = new(0f, 0f, 0f);
    }





    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
