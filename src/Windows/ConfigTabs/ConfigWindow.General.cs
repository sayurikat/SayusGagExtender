using Dalamud.Bindings.ImGui;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {
        private void DrawGeneralTab()
        {
            var emoteGuardEnabled = configuration.EmoteGuardEnabled;
            if (ImGui.Checkbox("Enable Emote Guard", ref emoteGuardEnabled))
            {
                configuration.EmoteGuardEnabled = emoteGuardEnabled;
                configuration.Save();
            }

            ImGui.TextWrapped("Makes sure emotes can be used when running, in combat or mounted.");
            ImGui.TextWrapped("Will block most actions until emote is executed.");
            ImGui.TextWrapped("Will also dismount for certain emotes.");

            ImGui.Spacing();

        }
    }
}
