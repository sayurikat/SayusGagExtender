using Dalamud.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;

namespace SayusGagExtender
{
    public sealed unsafe class AutoAttackKiller : IDisposable
    {
        private readonly Plugin plugin;
        private readonly AutoAttackState* autoAttackState;

        private bool wasAutoAttacking;
        private DateTime lastStopAttemptUtc = DateTime.MinValue;

        public AutoAttackKiller(Plugin plugin)
        {
            this.plugin = plugin;

            this.autoAttackState = this.ResolveAutoAttackState();

            if (this.autoAttackState == null)
            {
                Plugin.ChatGui.PrintError("Failed to resolve AutoAttackState.");
                return;
            }

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
        private void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
        {
            if (!plugin.Configuration.HandGuardEnabled)
                return;

            if (this.autoAttackState == null)
                return;

            var isAutoAttacking = this.autoAttackState->Get();

            if (isAutoAttacking && !this.wasAutoAttacking)
                this.StopAutoAttack();

            this.wasAutoAttacking = isAutoAttacking;
        }

        private void StopAutoAttack()
        {
            var now = DateTime.UtcNow;
            if ((now - this.lastStopAttemptUtc).TotalMilliseconds < 100)
                return;

            this.lastStopAttemptUtc = now;
            Plugin.ChatGui.Print($"Is auto attacking");
            try
            {
                // First try the safer wrapper.
                var result = this.autoAttackState->Set(false);
                Plugin.ChatGui.Print($"Stopping auto attack");

                // If Set(false) returns false or does not actually stop it,
                // try SetImpl instead.
                if (!result)
                    this.autoAttackState->SetImpl(false, sendPacket: true, isInstant: true);
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to stop auto-attack: {ex.Message}");
            }
        }

        private AutoAttackState* ResolveAutoAttackState()
        {
            const string sig =
                "E8 ?? ?? ?? ?? B0 ?? EB ?? 48 8D 0D ?? ?? ?? ??";

            if (!Plugin.SigScanner.TryGetStaticAddressFromSig(sig, out var addr, offset: 9))
            {
                Plugin.ChatGui.PrintError("Failed to resolve AutoAttackState via Set signature.");
                return null;
            }

            if (addr == IntPtr.Zero)
            {
                Plugin.ChatGui.PrintError("Resolved AutoAttackState address was zero.");
                return null;
            }

            //Plugin.ChatGui.Print($"AutoAttackState resolved: 0x{addr:X}");

            return (AutoAttackState*)addr;
        }
    }
}
