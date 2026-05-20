using Dalamud.Plugin.Services;
using Microsoft.Extensions.Configuration;
using System;

namespace SayusGagExtender
{
    public sealed class BlindfoldMonitor : IDisposable
    {
        private readonly Plugin plugin;
        public bool blindfolded { get; private set; } = false;
        private long nextRefreshMs;
        private bool forcePosition => plugin.Configuration.Chat2BlindfoldLocked;
        public bool IsActive => plugin.Configuration.Chat2BlindfoldFeatureEnable && forcePosition && blindfolded;

        public BlindfoldMonitor(Plugin plugin)
        {
            this.plugin = plugin;

            Plugin.Framework.Update += OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnBlindfoldStateChanged += BlindfoldStateChanged;
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (!plugin.Configuration.Chat2BlindfoldFeatureEnable)
                return;
            if (!blindfolded)
                return;
            if (!forcePosition)
                return;

            var now = Environment.TickCount64;
            if (now < this.nextRefreshMs)
                return;

            this.nextRefreshMs = now + 100;

            MoveChat2(blindfolded);
        }

        private void BlindfoldStateChanged(bool enabled)
        {
            blindfolded = enabled;
            if (!plugin.Configuration.Chat2BlindfoldFeatureEnable)
                return;
            MoveChat2(blindfolded);

            /*
             * Appears to be a race condition with GagSpeak when blindfold is applied.
             * RefreshVisuals may in some cases run before the blindfold is registered fully, making it skip forced first person.
             * It seems to happen more frequent with this plugin running, as it could add additional delays.
             * Once blindfold state is confirmed in this plugin, we call a 2nd refresh visual to ensure 1st person gets triggered.
             * 
             */
            plugin.GagSpeakContext.RefreshGagSpeakVisualsAsync(redraw: true);
        }
        private void MoveChat2(bool blindfolded)
        {
            if (blindfolded)
            {
                Plugin.ChatGui.Print("moving chat");
                plugin.Chat2Api.SetPositionAndSize(plugin.Configuration.Chat2Bounds);

            }
        }

        public void Dispose()
        {
            
            Plugin.Framework.Update -= OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnBlindfoldStateChanged -= BlindfoldStateChanged;
        }
    }
}
