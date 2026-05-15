using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace SayusGagExtender;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;




    public bool EmoteGuardEnabled { get; set; } = true;
    public bool HandGuardEnabled { get; set; } = true;
    public string ZapControllerName { get; set; }
    public bool AutoZapEnabled { get; set; } = true;
    public int AutoZapCount { get; set; } = 8;
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
    public string GagSpeakMasterName { get; set; }
    public string GagSpeakMasterWorld { get; set; }
    public string GagSpeakMasterID { get; set; }
    public string GagSpeakSlaveID { get; set; }

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
