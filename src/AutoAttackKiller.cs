using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;

namespace SayusGagExtender
{
    public sealed unsafe class AutoAttackKiller : IDisposable
    {
        private readonly Plugin plugin;
        private readonly AutoAttackState* autoAttackState;

        private Hook<AutoAttackSetLikeDelegate>? autoAttackSetA;
        private Hook<AutoAttackSetLikeDelegate>? autoAttackSetB;

        private DateTime lastStopAttemptUtc = DateTime.MinValue;
        private DateTime lastBlockedChatUtc = DateTime.MinValue;

        private const int StopThrottleMs = 25;
        private const int BlockedChatThrottleMs = 3000;

        // Candidate A was confirmed useful.
        // Candidate B is kept because the retrieved working class hooked both.
        private const bool HookCandidateB = true;

        private delegate bool AutoAttackSetLikeDelegate(
            AutoAttackState* self,
            bool value,
            bool sendPacket,
            bool isInstant);

        public AutoAttackKiller(Plugin plugin)
        {
            this.plugin = plugin;

            this.autoAttackState = this.ResolveAutoAttackState();

            if (this.autoAttackState == null)
            {
                Plugin.ChatGui.PrintError("Failed to resolve AutoAttackState.");
                return;
            }

            this.EnableSetLikeHooks();

            Plugin.Framework.Update += this.OnFrameworkUpdate;
        }

        public void Dispose()
        {
            Plugin.Framework.Update -= this.OnFrameworkUpdate;

            this.autoAttackSetA?.Disable();
            this.autoAttackSetA?.Dispose();
            this.autoAttackSetA = null;

            this.autoAttackSetB?.Disable();
            this.autoAttackSetB?.Dispose();
            this.autoAttackSetB = null;
        }

        public void Enable()
        {
            plugin.Configuration.HandGuardEnabled = true;
        }

        public void ForceStopNow()
        {
            this.StopAutoAttack(force: true);
        }

        private void EnableSetLikeHooks()
        {
            try
            {
                var (setA, setB) = this.ResolveSetLikeCandidateAddresses();

                if (setA == 0 && setB == 0)
                {
                    Plugin.ChatGui.PrintError("Failed to resolve auto-attack hooks.");
                    return;
                }

                if (setA != 0)
                {
                    this.autoAttackSetA =
                        Plugin.GameInterop.HookFromAddress<AutoAttackSetLikeDelegate>(
                            setA,
                            this.AutoAttackSetADetour);
                
                    this.autoAttackSetA.Enable();
                }

                if (HookCandidateB && setB != 0 && setB != setA)
                {
                    this.autoAttackSetB =
                        Plugin.GameInterop.HookFromAddress<AutoAttackSetLikeDelegate>(
                            setB,
                            this.AutoAttackSetBDetour);

                    this.autoAttackSetB.Enable();
                }

                //Plugin.ChatGui.Print("Auto-attack guard enabled.");
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to hook auto-attack guard: {ex.Message}");
            }
        }

        private bool AutoAttackSetADetour(
    AutoAttackState* self,
    bool value,
    bool sendPacket,
    bool isInstant)
        {
            if (this.ShouldBlockNativeAutoAttack(value))
            {
                //this.PrintBlockedAutoAttackMessage(sendPacket, isInstant);

                return this.autoAttackSetA!.Original(
                    self,
                    false,
                    sendPacket: true,
                    isInstant: true);
            }

            return this.autoAttackSetA!.Original(self, value, sendPacket, isInstant);
        }

        private bool AutoAttackSetBDetour(
            AutoAttackState* self,
            bool value,
            bool sendPacket,
            bool isInstant)
        {
            if (this.ShouldBlockNativeAutoAttack(value))
            {
                this.PrintBlockedAutoAttackMessage(sendPacket, isInstant);

                return this.autoAttackSetB!.Original(
                    self,
                    false,
                    sendPacket: true,
                    isInstant: true);
            }

            return this.autoAttackSetB!.Original(self, value, sendPacket, isInstant);
        }

        private bool ShouldBlockNativeAutoAttack(bool value)
        {
            if (!value)
                return false;

            if (!plugin.Configuration.HandGuardEnabled)
                return false;

            if (!plugin.WeaponSheather.wearsRestrictedItems)
                return false;

            return true;
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (!plugin.Configuration.HandGuardEnabled)
                return;

            if (this.autoAttackState == null)
                return;

            if (!plugin.WeaponSheather.wearsRestrictedItems)
                return;

            // Do not rely on transition detection. If restricted, keep it stopped.
            this.StopAutoAttack(force: false);
        }

        private void StopAutoAttack(bool force)
        {
            if (this.autoAttackState == null)
                return;

            var now = DateTime.UtcNow;

            if (!force && (now - this.lastStopAttemptUtc).TotalMilliseconds < StopThrottleMs)
                return;

            this.lastStopAttemptUtc = now;

            try
            {
                this.autoAttackState->Set(false);

                // The working retrieved class always called SetImpl.
                // Keep it here, but throttled by StopThrottleMs.
                this.autoAttackState->SetImpl(false, sendPacket: true, isInstant: true);
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to stop auto-attack: {ex.Message}");
            }
        }

        private void PrintBlockedAutoAttackMessage(bool originalSendPacket, bool isInstant)
        {
            // Targeting/focus changes can hit the native path with value=true.
            // Only show chat when the original call intended to send an autoattack packet.
            if (!originalSendPacket || isInstant)
                return;

            var now = DateTime.UtcNow;

            if ((now - this.lastBlockedChatUtc).TotalMilliseconds < BlockedChatThrottleMs)
                return;

            this.lastBlockedChatUtc = now;

            Plugin.ChatGui.PrintError("Auto-attack blocked while your hands are restricted.");
        }

        private AutoAttackState* ResolveAutoAttackState()
        {
            const string sig =
                "E8 ?? ?? ?? ?? B0 ?? EB ?? 48 8D 0D ?? ?? ?? ??";

            if (!Plugin.SigScanner.TryGetStaticAddressFromSig(sig, out var addr, offset: 9))
            {
                Plugin.ChatGui.PrintError("Failed to resolve AutoAttackState.");
                return null;
            }

            if (addr == IntPtr.Zero)
            {
                Plugin.ChatGui.PrintError("Resolved AutoAttackState address was zero.");
                return null;
            }

            return (AutoAttackState*)addr;
        }

        private (nint setA, nint setB) ResolveSetLikeCandidateAddresses()
        {
            var setB = this.ResolveRawSigFirstCallTarget();

            if (setB == 0)
                return (0, 0);

            var setA = setB - 0x200;

            return (setA, setB);
        }

        private nint ResolveRawSigFirstCallTarget()
        {
            const string sig =
                "E8 ?? ?? ?? ?? B0 ?? EB ?? 48 8D 0D ?? ?? ?? ??";

            try
            {
                var match = RawScanFirst(sig);

                if (match == 0)
                    return 0;

                var p = (byte*)match;

                if (p[0] != 0xE8)
                    return 0;

                var rel = *(int*)(p + 1);
                return (nint)(p + 5 + rel);
            }
            catch
            {
                return 0;
            }
        }

        private static nint RawScanFirst(string signature)
        {
            var pattern = ParseSignature(signature);

            var moduleBase = (byte*)Plugin.SigScanner.Module.BaseAddress;
            var moduleSize = Plugin.SigScanner.Module.ModuleMemorySize;

            var start = moduleBase;
            var end = start + moduleSize - pattern.Length;

            for (var p = start; p < end; p++)
            {
                var matched = true;

                for (var i = 0; i < pattern.Length; i++)
                {
                    var expected = pattern[i];

                    if (expected.HasValue && p[i] != expected.Value)
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                    return (nint)p;
            }

            return 0;
        }

        private static byte?[] ParseSignature(string signature)
        {
            var parts = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = new byte?[parts.Length];

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];

                if (part == "??" || part == "?")
                {
                    result[i] = null;
                    continue;
                }

                result[i] = Convert.ToByte(part, 16);
            }

            return result;
        }
    }
}
