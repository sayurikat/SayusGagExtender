using Dalamud.Plugin.Services;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace SayusGagExtender
{
    public unsafe sealed class ChatMonitor : IDisposable
    {
        private readonly Plugin plugin;
        public bool chatboxHidden { get; private set; } = false;
        public bool chatboxInputHidden { get; private set; } = false;
        public bool chatboxInputDisabled { get; private set; } = false;
        private long nextRefreshMs;
        public ChatMonitor(Plugin plugin)
        {
            this.plugin = plugin;


            Plugin.Framework.Update += this.OnFrameworkUpdate;
            plugin.GagSpeakChatMonitorApi.OnChatboxHiddenChanged += ChatboxHiddenChanged;
            plugin.GagSpeakChatMonitorApi.OnChatInputHiddenChanged += ChatInputHiddenChanged;
            plugin.GagSpeakChatMonitorApi.OnChatInputDisabledChanged += ChatInputDisabledChanged;

            //Plugin.ChatGui.Print($"ChatboxHidden: {plugin.GagSpeakChatMonitorApi.IsChatboxHidden()}");
            
        }
        private void OnFrameworkUpdate(IFramework framework)
        {
            if (chatboxHidden)
            {
                if (plugin.Configuration.Chat2HiddenTabName.Length > 0 && plugin.Chat2Api.GetActiveTabName() != plugin.Configuration.Chat2HiddenTabName)
                {
                    plugin.Chat2Api.SetActiveTab(plugin.Configuration.Chat2HiddenTabName);
                }
            }


            // Only check every 5 seconds.
            var now = Environment.TickCount64;
            if (now < this.nextRefreshMs)
                return;

            this.nextRefreshMs = now + 5000;

            ChatboxHiddenChanged(plugin.GagSpeakChatMonitorApi.IsChatboxHidden());
            ChatInputHiddenChanged(plugin.GagSpeakChatMonitorApi.IsChatInputHidden());
            ChatInputDisabledChanged(plugin.GagSpeakChatMonitorApi.IsChatInputDisabled());
        }
        private void ChatboxHiddenChanged(bool enabled)
        {
            if (chatboxHidden != enabled && !enabled)
            {
                // switch back to general tab so user know chat has been revealed again
                plugin.Chat2Api.SetActiveTab(0);
            }
            chatboxHidden = enabled;
        }
        private void ChatInputHiddenChanged(bool enabled)
        {
            chatboxInputHidden = enabled;
            InputChanged();
        }
        private void ChatInputDisabledChanged(bool enabled)
        {
            chatboxInputDisabled = enabled;
            InputChanged();
        }
        private void InputChanged()
        {
            //when called on load, this will fail, same if we're not yet logged in.
            if (chatboxInputHidden || chatboxInputDisabled)
            {
                plugin.Chat2Api.DisableInputInAllTabs();
            }
            else
            {
                plugin.Chat2Api.EnableInputInAllTabs();
            }
        }
        public void Dispose()
        {
            Plugin.Framework.Update -= this.OnFrameworkUpdate;
            plugin.GagSpeakChatMonitorApi.OnChatboxHiddenChanged -= ChatboxHiddenChanged;
            plugin.GagSpeakChatMonitorApi.OnChatInputHiddenChanged -= ChatInputHiddenChanged;
            plugin.GagSpeakChatMonitorApi.OnChatInputDisabledChanged -= ChatInputDisabledChanged;
        }
    }
}
