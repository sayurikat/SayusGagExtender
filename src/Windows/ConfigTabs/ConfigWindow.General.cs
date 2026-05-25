using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using ECommons;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {
        
        private void DrawGeneralTab()
        {
            var openMainWindowOnStartup = configuration.OpenMainWindowOnStartup;
            if (ImGui.Checkbox("Open automatically on startup", ref openMainWindowOnStartup))
            {
                configuration.OpenMainWindowOnStartup = openMainWindowOnStartup;
                configuration.Save();
            }

            var openMiniWindowOnStartup = configuration.OpenMiniWindowOnStartup;
            if (ImGui.Checkbox("Open Mini automatically on startup", ref openMiniWindowOnStartup))
            {
                configuration.OpenMiniWindowOnStartup = openMiniWindowOnStartup;
                configuration.Save();
            }

            var openConfigWindowOnStartup = configuration.OpenConfigWindowOnStartup;
            if (ImGui.Checkbox("Open settings automatically on startup", ref openConfigWindowOnStartup))
            {
                configuration.OpenConfigWindowOnStartup = openConfigWindowOnStartup;
                configuration.Save();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

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
            ImGui.Separator();
            ImGui.Spacing();

        }

    }
}
