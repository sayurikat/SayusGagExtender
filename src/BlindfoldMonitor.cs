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
        private bool forcePosition = false;

        public BlindfoldMonitor(Plugin plugin)
        {
            this.plugin = plugin;

            Plugin.Framework.Update += OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnBlindfoldStateChanged += BlindfoldStateChanged;
        }

        private void OnFrameworkUpdate(IFramework framework)
        {

            if (!forcePosition) return;

            // Only check every 5 seconds.
            var now = Environment.TickCount64;
            if (now < this.nextRefreshMs)
                return;

            this.nextRefreshMs = now + 5000;

            BlindfoldStateChanged(plugin.GagSpeakRestrictionsApi.IsBlindfolded());
        }

        private void BlindfoldStateChanged(bool enabled)
        {
            blindfolded = enabled;

            if (enabled)
            {
                // do things when blindfold starts
                plugin.Chat2Api.SetPositionAndSize(plugin.Configuration.Chat2Bounds);
            }
            else
            {
                // do things when blindfold ends
            }
        }

        public void Dispose()
        {
            Plugin.Framework.Update -= OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnBlindfoldStateChanged -= BlindfoldStateChanged;
        }
    }
}
