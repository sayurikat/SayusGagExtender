using Dalamud.Configuration;
using Dalamud.Game.Text;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using static SayusGagExtender.MoodleEnforcer;
using static SayusGagExtender.PenumbraEnforcer;
using static SayusGagExtender.RandomVibeSender;
using static SayusGagExtender.RandomZapSender;

namespace SayusGagExtender;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;




    public bool EmoteGuardEnabled { get; set; } = true;
    public bool HandGuardEnabled { get; set; } = true;

    public Dictionary<Guid, string> AutoZapRequiredRestrictions { get; set; } = new Dictionary<Guid, string>();
    public List<WeightedZapCommand> AutoZapCommands { get; set; } = new();
    public string ZapControllerName { get; set; }
    public bool AutoZapEnabled { get; set; } = true;
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
    public bool AutoVibeEnabled { get; set; } = true;
    public int AutoVibeCount { get; set; } = 8;
    public bool AutoVibeCountControllerLocked { get; set; } = false;
    public Guid AutoVibeEngagedMoodleId { get; set; } = Guid.Empty;
    public string AutoVibeEngagedMoodleName { get; set; } = string.Empty;
    public Guid AutoVibeControllerOnlineMoodleId { get; set; } = Guid.Empty;
    public string AutoVibeControllerOnlineMoodleName { get; set; } = string.Empty;
    public RandomVibeSender.OperateWhen AutoVibeWhen { get; set; } = RandomVibeSender.OperateWhen.Offline;


    public bool MountBlockFeature { get; set; } = true;
    public Dictionary<Guid, string> MountBlockMoodles { get; set; } = new();
    public bool MountQuotaEnabled { get; set; } = false;
    public int MountQuotaActions { get; set; } = -1;
    public QuotaWindow MountQuotaWindow { get; set; } = QuotaWindow.Hour;
    public List<DateTime> MountQuotaActionLogUtc { get; set; } = new();
    public Guid MountQuotaMoodleId { get; set; } = Guid.Empty;
    public Guid MountQuotaEmptyMoodleId { get; set; } = Guid.Empty;
    public string MountQuotaMoodleName { get; set; } = string.Empty;
    public string MountQuotaEmptyMoodleName { get; set; } = string.Empty;


    public bool TeleportBlockFeature { get; set; } = true;
    public Dictionary<Guid, string> TeleportBlockMoodles { get; set; } = new();
    public bool TeleportQuotaEnabled { get; set; } = false;
    public int TeleportQuotaActions { get; set; } = 0;
    public QuotaWindow TeleportQuotaWindow { get; set; } = QuotaWindow.Hour;
    public List<DateTime> TeleportQuotaActionLogUtc { get; set; } = new();
    public Guid TeleportQuotaMoodleId { get; set; } = Guid.Empty;
    public string TeleportQuotaMoodleName { get; set; } = string.Empty;
    public Guid TeleportQuotaEmptyMoodleId { get; set; } = Guid.Empty;
    public string TeleportQuotaEmptyMoodleName { get; set; } = string.Empty;


    public bool JobSwitchBlockFeature { get; set; } = true;
    public Dictionary<Guid, string> JobSwitchBlockMoodles { get; set; } = new();
    public bool JobSwitchQuotaEnabled { get; set; } = false;
    public int JobSwitchQuotaActions { get; set; } = 0;
    public QuotaWindow JobSwitchQuotaWindow { get; set; } = QuotaWindow.Hour;
    public List<DateTime> JobSwitchQuotaActionLogUtc { get; set; } = new();
    public Guid JobSwitchQuotaMoodleId { get; set; } = Guid.Empty;
    public string JobSwitchQuotaMoodleName { get; set; } = string.Empty;
    public Guid JobSwitchQuotaEmptyMoodleId { get; set; } = Guid.Empty;
    public string JobSwitchQuotaEmptyMoodleName { get; set; } = string.Empty;



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


    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
