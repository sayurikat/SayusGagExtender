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
        public bool wearsRestrictedItems { get; private set;  } = false;

        private DateTime lastSheatheAttemptUtc = DateTime.MinValue;
        private DateTime lastItemChekUtc = DateTime.MinValue;

        public WeaponSheather(Plugin plugin)
        {
            this.plugin = plugin;
            Plugin.Framework.Update += this.OnFrameworkUpdate;
        }

        public void Dispose()
        {
            Plugin.Framework.Update -= this.OnFrameworkUpdate;
        }
        public void Enable()
        {
            plugin.Configuration.HandGuardEnabled = true;
        }
        private void CheckIfWearingRestrictiveItems()
        {
            wearsRestrictedItems = plugin.GagSpeakRestrictionsApi.IsAnyListedRestrictionsActive(plugin.Configuration.HandGuardBlockedItems);
        }
        private void OnFrameworkUpdate(IFramework framework)
        {
            if (!plugin.Configuration.HandGuardEnabled)
                return;


            var now = DateTime.UtcNow;

            if ((now - this.lastItemChekUtc).TotalMilliseconds > 5000)
            {
                CheckIfWearingRestrictiveItems();
                this.lastItemChekUtc = now;
            }

            if (!wearsRestrictedItems)
                return;
            


            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            if (localPlayer == null)
                return;

            var weaponDrawn = localPlayer.StatusFlags.HasFlag(StatusFlags.WeaponOut);

            if (!weaponDrawn)
                return;

            if ((now - this.lastSheatheAttemptUtc).TotalMilliseconds < 500)
                return;
            Plugin.ChatGui.Print($"Weapon drawn");
            this.lastSheatheAttemptUtc = now;

            this.SheatheWeapon();
        }

        private void SheatheWeapon()
        {
            try
            {
                //Plugin.CommandManager.ProcessCommand("/bm");
                //ExecuteNativeCommand("/bm");
                plugin.EmoteGuard?.QueueGuardedEmote("/bm");
                Plugin.ChatGui.Print($"Sending /bm");
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to sheathe weapon: {ex.Message}");
            }
        }
        private static void ExecuteNativeCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            command = command.Trim();

            if (!command.StartsWith('/'))
                return;

            var shellModule = RaptureShellModule.Instance();
            var uiModule = UIModule.Instance();

            if (shellModule == null || uiModule == null)
                return;

            Utf8String cmd = default;

            try
            {
                cmd.SetString(command);

                cmd.SanitizeString(
                    AllowedEntities.Unknown9 |
                    AllowedEntities.Payloads |
                    AllowedEntities.OtherCharacters |
                    AllowedEntities.SpecialCharacters |
                    AllowedEntities.Numbers |
                    AllowedEntities.LowercaseLetters |
                    AllowedEntities.UppercaseLetters);

                if (cmd.Length > 500)
                    return;

                shellModule->ExecuteCommandInner(&cmd, uiModule);

                // If your ClientStructs build uses the overload with a bool:
                // shellModule->ExecuteCommandInner(&cmd, uiModule, false);
            }
            finally
            {
                cmd.Dtor(true);
            }
        }
    }
}
