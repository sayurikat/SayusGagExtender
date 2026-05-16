using Dalamud.Configuration;
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
    public Dictionary<Guid, string> AutoVibeRequiredRestrictions { get; set; } = new Dictionary<Guid, string>();
    public List<WeightedVibeCommand> AutoVibeCommands { get; set; } = new();
    public string VibeControllerName { get; set; }
    public bool AutoVibeEnabled { get; set; } = true;
    public int AutoVibeCount { get; set; } = 8;
    public bool TeleportBlockFeature { get; set; } = true;
    public string TeleportBlockMoodle { get; set; }
    public bool MountBlockFeature { get; set; } = true;
    public string MountBlockMoodle { get; set; }
    public bool JobSwitchBlockFeature { get; set; } = true;
    public string JobSwitchBlockMoodle { get; set; }
    public API.Chat2Api.Chat2Bounds Chat2Bounds { get; set; } = new API.Chat2Api.Chat2Bounds();
    public string Chat2HiddenTabName { get; set; }
    public string ActiveRestraintSet { get; set; }
    public Dictionary<int, string> ActiveRestrictions { get; set; } = new Dictionary<int, string>();
    public Dictionary<int, string> ActiveGags { get; set; } = new Dictionary<int, string>();
    public bool GagSpeakRestraintCloner { get; set; }
    public string GagSpeakMasterName { get; set; }
    public string GagSpeakMasterWorld { get; set; }
    public Dictionary<Guid, string> HandGuardBlockedItems { get; set; } = new Dictionary<Guid, string>();
    public bool MoodleEnforcerEnabled { get; set; } = false;
    public List<MoodleEnforcerMoodleConfig> MoodleEnforcerMoodles { get; set; } = new();
    public bool PenumbraEnforcerEnabled { get; set; }
    public List<PenumbraEnforcerConfig> PenumbraEnforcerMods { get; set; } = new();
    public bool EmoteEnforcerEnabled { get; set; } = false;
    public List<EmoteEnforcer.EmoteEnforcerEmoteConfig> EmoteEnforcerEmotes { get; set; } = new();
    public string EmoteEnforcerCancelCommand { get; set; } = "/quack eval /sit \"/wait 0.5\" /sit";
    public bool CustomizePlusEnforcerEnabled { get; set; }
    public List<CustomizePlusEnforcer.CustomizePlusEnforcerConfig> CustomizePlusEnforcerProfiles { get; set; } = new();
    public Guid CustomizePlusDefaultProfileId { get; set; } = Guid.Empty;
    public string CustomizePlusDefaultProfileName { get; set; } = string.Empty;
    public string CustomizePlusDefaultProfileVirtualPath { get; set; } = string.Empty;



    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
