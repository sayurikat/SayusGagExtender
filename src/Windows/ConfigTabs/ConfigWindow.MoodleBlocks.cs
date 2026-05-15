using Dalamud.Bindings.ImGui;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {
        private void DrawMoodleBlocksTab()
        {
            DrawMoodleBlockSetting(
                "Enable Teleport Block Feature",
                "Teleport Block Moodle ID",
                configuration.TeleportBlockFeature,
                configuration.TeleportBlockMoodle,
                enabled =>
                {
                    configuration.TeleportBlockFeature = enabled;
                    configuration.Save();
                },
                moodle =>
                {
                    configuration.TeleportBlockMoodle = moodle;
                    configuration.Save();
                });

            ImGui.Spacing();

            DrawMoodleBlockSetting(
                "Enable Mount Block Feature",
                "Mount Block Moodle ID",
                configuration.MountBlockFeature,
                configuration.MountBlockMoodle,
                enabled =>
                {
                    configuration.MountBlockFeature = enabled;
                    configuration.Save();
                },
                moodle =>
                {
                    configuration.MountBlockMoodle = moodle;
                    configuration.Save();
                });

            ImGui.Spacing();

            DrawMoodleBlockSetting(
                "Enable Job Switch Block Feature",
                "Job Switch Block Moodle ID",
                configuration.JobSwitchBlockFeature,
                configuration.JobSwitchBlockMoodle,
                enabled =>
                {
                    configuration.JobSwitchBlockFeature = enabled;
                    configuration.Save();
                },
                moodle =>
                {
                    configuration.JobSwitchBlockMoodle = moodle;
                    configuration.Save();
                });
        }
        private static void DrawMoodleBlockSetting(
        string checkboxLabel,
        string inputLabel,
        bool currentEnabled,
        string currentMoodle,
        Action<bool> onEnabledChanged,
        Action<string> onMoodleChanged)
        {
            var enabled = currentEnabled;
            if (ImGui.Checkbox(checkboxLabel, ref enabled))
            {
                onEnabledChanged(enabled);
            }

            ImGui.SetNextItemWidth(300);
            var moodle = currentMoodle;
            if (ImGui.InputText(inputLabel, ref moodle))
            {
                onMoodleChanged(moodle);
            }
        }
    }
}
