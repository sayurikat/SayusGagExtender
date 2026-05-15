using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Text;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {
        private void DrawChat2Tab()
        {
            if (ImGui.Button("Save Chat2 position for Blindfold"))
            {
                if (plugin.Chat2Api.TryGetPositionAndSize(out var bounds))
                {
                    if (bounds != null)
                    {
                        configuration.Chat2Bounds = bounds;
                        configuration.Save();
                    }
                }
            }

            if (ImGui.Button("Apply Chat2 position for Blindfold"))
            {
                plugin.Chat2Api.SetPositionAndSize(configuration.Chat2Bounds);
            }

            ImGui.Spacing();

            ImGui.TextWrapped("Pick a tab Chat2 stays locked on while vanilla Chatbox is hidden by GagSpeak.");

            ImGui.SetNextItemWidth(160);
            var chat2HiddenTabName = configuration.Chat2HiddenTabName;
            if (ImGui.InputText("Hide Chat2 Tab Name", ref chat2HiddenTabName))
            {
                configuration.Chat2HiddenTabName = chat2HiddenTabName;
                configuration.Save();
            }

            if (ImGui.Button("Set current tab as Hide Chat2 tab"))
            {
                var activeTabName = plugin.Chat2Api.GetActiveTabName();
                if (activeTabName != null)
                {
                    configuration.Chat2HiddenTabName = activeTabName;
                    configuration.Save();
                }
            }

            /*
            if (ImGui.Button("Enable Chat2 Inputs"))
            {
                plugin.Chat2Api.EnableInputInAllTabs();
            }

            if (ImGui.Button("Disable Chat2 Inputs"))
            {
                plugin.Chat2Api.DisableInputInAllTabs();
            }

            if (ImGui.Button("Set Hidden tab as current"))
            {
                plugin.Chat2Api.SetActiveTab(configuration.Chat2HiddenTabName);
            }
            */
        }
    }
}
