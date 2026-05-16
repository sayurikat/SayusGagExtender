using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using System;

namespace SayusGagExtender
{
    public unsafe sealed class WeaponSheather : IDisposable
    {
        private readonly Plugin plugin;

        public bool wearsRestrictedItems { get; private set; } = false;

        private DateTime lastSheatheAttemptUtc = DateTime.MinValue;
        private DateTime lastItemCheckUtc = DateTime.MinValue;
        private DateTime lastSheatheErrorUtc = DateTime.MinValue;

        private const int RestrictionCheckMs = 5000;
        private const int SheatheAttemptMs = 125;
        private const int ErrorThrottleMs = 3000;

        public WeaponSheather(Plugin plugin)
        {
            this.plugin = plugin;
            Plugin.Framework.Update += this.OnFrameworkUpdate;

            this.CheckIfWearingRestrictiveItems();
        }

        public void Dispose()
        {
            Plugin.Framework.Update -= this.OnFrameworkUpdate;
        }

        public void Enable()
        {
            plugin.Configuration.HandGuardEnabled = true;
            this.CheckIfWearingRestrictiveItems();
        }

        public void ForceRefreshRestrictions()
        {
            this.CheckIfWearingRestrictiveItems();
            this.lastItemCheckUtc = DateTime.UtcNow;
        }

        public void ForceSheatheNow()
        {
            if (!plugin.Configuration.HandGuardEnabled)
                return;

            this.ForceRefreshRestrictions();

            if (!this.wearsRestrictedItems)
                return;

            this.SheatheWeapon();
            this.lastSheatheAttemptUtc = DateTime.UtcNow;
        }

        private void CheckIfWearingRestrictiveItems()
        {
            try
            {
                this.wearsRestrictedItems =
                    plugin.GagSpeakRestrictionsApi.IsAnyListedRestrictionsActive(
                        plugin.Configuration.HandGuardBlockedItems);
            }
            catch
            {
                this.wearsRestrictedItems = false;
            }
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (!plugin.Configuration.HandGuardEnabled)
                return;

            var now = DateTime.UtcNow;

            if ((now - this.lastItemCheckUtc).TotalMilliseconds >= RestrictionCheckMs)
            {
                this.CheckIfWearingRestrictiveItems();
                this.lastItemCheckUtc = now;
            }

            if (!this.wearsRestrictedItems)
                return;

            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            if (localPlayer == null)
                return;

            var weaponDrawn = localPlayer.StatusFlags.HasFlag(StatusFlags.WeaponOut);
            if (!weaponDrawn)
                return;

            if ((now - this.lastSheatheAttemptUtc).TotalMilliseconds < SheatheAttemptMs)
                return;

            this.lastSheatheAttemptUtc = now;
            this.SheatheWeapon();
        }

        private void SheatheWeapon()
        {
            try
            {
                plugin.EmoteGuard?.QueueGuardedEmote("/bm");

                // If you later decide guarded queue is too slow, test this instead:
                // Plugin.CommandManager.ProcessCommand("/bm");
                //
                // Or this native path:
                // ExecuteNativeCommand("/bm");
            }
            catch (Exception ex)
            {
                this.PrintSheatheErrorThrottled(ex);
            }
        }

        private void PrintSheatheErrorThrottled(Exception ex)
        {
            var now = DateTime.UtcNow;

            if ((now - this.lastSheatheErrorUtc).TotalMilliseconds < ErrorThrottleMs)
                return;

            this.lastSheatheErrorUtc = now;

            Plugin.ChatGui.PrintError($"Failed to sheathe weapon: {ex.Message}");
        }
    }
}
