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
            var enabled = configuration.Chat2BlindfoldFeatureEnable;
            if (ImGui.Checkbox("Blindfold feature", ref enabled))
            {
                configuration.Chat2BlindfoldFeatureEnable = enabled;
                configuration.Save();

            }
            ImGui.TextWrapped("Moves chatbox to specific position while any blindfold is active.");

            if (ImGui.Button("Save Chat2 size and position for Blindfold"))
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


            var locked = configuration.Chat2BlindfoldLocked;
            if (ImGui.Checkbox("Locked (prevents moving while blindfold is active)", ref locked))
            {
                configuration.Chat2BlindfoldLocked = locked;
                configuration.Save();

            }


            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextWrapped("For Chat2 compatibility with GagSpeaks chat restrictions, please assign an empty chat tab which will be enforced while GagSpeak Hide Chatbox is active.");

            ImGui.SetNextItemWidth(160);
            var chat2HiddenTabName = configuration.Chat2HiddenTabName;
            if (ImGui.InputText("Chat2 Tab Name", ref chat2HiddenTabName))
            {
                configuration.Chat2HiddenTabName = chat2HiddenTabName;
                configuration.Save();
            }

            if (ImGui.Button("Set current tab as enforced tab"))
            {
                var activeTabName = plugin.Chat2Api.GetActiveTabName();
                if (activeTabName != null)
                {
                    configuration.Chat2HiddenTabName = activeTabName;
                    configuration.Save();
                }
            }
            ImGui.Spacing();
            ImGui.TextWrapped("Chat inputs in Chat2 will be disabled regular chat input are disabled by GagSpeak.");

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
