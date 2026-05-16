using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using static FFXIVClientStructs.FFXIV.Client.Game.Character.CustomizeData.Delegates;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule;

namespace SayusGagExtender
{
    public unsafe sealed class EmoteGuard : IDisposable
    {
        private readonly Plugin plugin;

        private delegate byte ExecuteSlotDelegate(
            RaptureHotbarModule* hotbarModule,
            HotbarSlot* hotbarSlot);

        private delegate void ProcessChatBoxDelegate(
            IntPtr uiModule,
            IntPtr message,
            IntPtr unused,
            byte a4);

        private readonly Hook<ExecuteSlotDelegate> executeSlotHook;
        private readonly Hook<ProcessChatBoxDelegate> processChatBoxHook;

        private readonly HashSet<string> emoteCommands = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> emoteCommandAllowsMount = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, bool> emoteIdAllowsMount = new();


        private QueuedEmote? queued;
        private DateTime movementLockedUntil = DateTime.MinValue;
        private DateTime movementLockStartedAt = DateTime.MinValue;

        private bool replaying;
        private bool disposed;

        private Vector3 lastAutorunCheckPosition;
        private bool haveAutorunCheckPosition;
        private DateTime autorunMovementStartedAt = DateTime.MinValue;

        private static readonly TimeSpan MovementSettleDelay = TimeSpan.FromMilliseconds(25);
        private static readonly TimeSpan MovementLockDuration = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan QueueTimeout = TimeSpan.FromSeconds(30);

        private static readonly TimeSpan AutorunDetectionGrace = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan AutorunMovementRequired = TimeSpan.FromMilliseconds(500);

        private const float AutorunMoveDistanceSq = 0.000025f;

        private bool waitingForUserMovementStop;

        private Vector3 lastManualStopCheckPosition;
        private bool haveManualStopCheckPosition;
        private DateTime manualStoppedSince = DateTime.MinValue;
        private DateTime nextManualStopMessageAt = DateTime.MinValue;

        private static readonly TimeSpan ManualStopRequired = TimeSpan.FromMilliseconds(350);
        private static readonly TimeSpan ManualStopMessageCooldown = TimeSpan.FromSeconds(5);

        private const float ManualStopMoveDistanceSq = 0.000025f;

        private Vector3 lastSuppressedStopCheckPosition;
        private bool haveSuppressedStopCheckPosition;
        private DateTime suppressedStoppedSince = DateTime.MinValue;

        private static readonly TimeSpan SuppressedStopRequired = TimeSpan.FromMilliseconds(25);
        private const float SuppressedStopMoveDistanceSq = 0.000025f;

        private DateTime nextBlockedWarningAt = DateTime.MinValue;
        private DateTime nextDismountAttemptAt = DateTime.MinValue;
        private static readonly TimeSpan DismountGrace = TimeSpan.FromMilliseconds(500);
        //private bool dismountRequestedForCurrentQueue;

        private static readonly TimeSpan BlockedWarningCooldown = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DismountRetryCooldown = TimeSpan.FromMilliseconds(500);

        private DateTime combatSuppressUntil = DateTime.MinValue;
        private bool combatSuppressStarted;

        private static readonly TimeSpan CombatPostDelay = TimeSpan.FromSeconds(2);

        private DateTime combatKeySuppressUntil = DateTime.MinValue;
        private bool currentQueueHadCombatDelay;
        private bool executingCombatDelayedEmote;

        private static readonly TimeSpan CombatAfterEmoteSuppressDuration = TimeSpan.FromSeconds(3);

        private readonly HashSet<string> expressionEmoteCommands = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<uint> expressionEmoteIds = new();

        private DateTime nextEnforcerBlockWarningAt = DateTime.MinValue;
        private static readonly TimeSpan EnforcerBlockWarningCooldown = TimeSpan.FromSeconds(3);

        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;

        private const int VK_0 = 0x30;
        private const int VK_1 = 0x31;
        private const int VK_2 = 0x32;
        private const int VK_3 = 0x33;
        private const int VK_4 = 0x34;
        private const int VK_5 = 0x35;
        private const int VK_6 = 0x36;
        private const int VK_7 = 0x37;
        private const int VK_8 = 0x38;
        private const int VK_9 = 0x39;

        private const int VK_A = 0x41;
        private const int VK_B = 0x42;
        private const int VK_C = 0x43;
        private const int VK_D = 0x44;
        private const int VK_E = 0x45;
        private const int VK_F = 0x46;
        private const int VK_G = 0x47;
        private const int VK_H = 0x48;
        private const int VK_I = 0x49;
        private const int VK_J = 0x4A;
        private const int VK_K = 0x4B;
        private const int VK_L = 0x4C;
        private const int VK_M = 0x4D;
        private const int VK_N = 0x4E;
        private const int VK_O = 0x4F;
        private const int VK_P = 0x50;
        private const int VK_Q = 0x51;
        private const int VK_R = 0x52;
        private const int VK_S = 0x53;
        private const int VK_T = 0x54;
        private const int VK_U = 0x55;
        private const int VK_V = 0x56;
        private const int VK_W = 0x57;
        private const int VK_X = 0x58;
        private const int VK_Y = 0x59;
        private const int VK_Z = 0x5A;


        private const int VK_UP = 0x26;
        private const int VK_LEFT = 0x25;
        private const int VK_DOWN = 0x28;
        private const int VK_RIGHT = 0x27;

        private const int VK_SPACE = 0x20;

        private static readonly int[] MovementKeys =
        {
            VK_W,
            VK_A,
            VK_S,
            VK_D,
            VK_UP,
            VK_LEFT,
            VK_DOWN,
            VK_RIGHT,
            VK_SPACE,

            // Mouse inputs that can interfere with emotes/movement.
            VK_RBUTTON,
            VK_MBUTTON,
        };
        private static readonly int[] CombatKeys =
{
    VK_A, VK_B, VK_C, VK_D, VK_E, VK_F, VK_G, VK_H, VK_I, VK_J, VK_K, VK_L, VK_M,
    VK_N, VK_O, VK_P, VK_Q, VK_R, VK_S, VK_T, VK_U, VK_V, VK_W, VK_X, VK_Y, VK_Z,

    VK_0, VK_1, VK_2, VK_3, VK_4, VK_5, VK_6, VK_7, VK_8, VK_9,

    VK_UP, VK_LEFT, VK_DOWN, VK_RIGHT,
    VK_SPACE,
    VK_RBUTTON,
    VK_MBUTTON,
};

        public EmoteGuard(Plugin instance)
        {
            plugin = instance;

            BuildEmoteCommandSet();

            executeSlotHook = Plugin.GameInterop.HookFromAddress<ExecuteSlotDelegate>(
                (nint)RaptureHotbarModule.MemberFunctionPointers.ExecuteSlot,
                ExecuteSlotDetour);

            var processChatBoxPtr = Plugin.SigScanner.ScanText(
                "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 48 8B F2 48 8B F9 45 84 C9");

            processChatBoxHook = Plugin.GameInterop.HookFromAddress<ProcessChatBoxDelegate>(
                processChatBoxPtr,
                ProcessChatBoxDetour);

            executeSlotHook.Enable();
            processChatBoxHook.Enable();

            Plugin.Framework.Update += OnFrameworkUpdate;

        }
        private void BuildEmoteCommandSet()
        {
            emoteCommands.Clear();
            emoteCommandAllowsMount.Clear();
            emoteIdAllowsMount.Clear();

            expressionEmoteCommands.Clear();
            expressionEmoteIds.Clear();

            // Manual aliases / fallbacks / special cases.
            AddEmoteCommand("sit", false);
            AddEmoteCommand("groundsit", false);
            AddEmoteCommand("doze", true);
            AddEmoteCommand("pose", false);
            AddEmoteCommand("changepose", false);
            AddEmoteCommand("cpose", false);
            AddEmoteCommand("dpose", false);
            AddEmoteCommand("bstance", false);
            AddEmoteCommand("vpose", false);

            try
            {
                foreach (var emote in Plugin.DataManager.GetExcelSheet<Emote>())
                {
                    var textCommand = emote.TextCommand.Value;
                    var canUseMounted = EmoteCanBeUsedMounted(emote);

                    var command = textCommand.Command.ToString();
                    var shortCommand = textCommand.ShortCommand.ToString();

                    AddEmoteCommand(command, canUseMounted);
                    AddEmoteCommand(shortCommand, canUseMounted);

                    emoteIdAllowsMount[emote.RowId] = canUseMounted;

                    if (IsExpressionEmoteFromExcel(emote, command, shortCommand))
                    {
                        expressionEmoteIds.Add(emote.RowId);
                        AddExpressionEmoteCommand(command);
                        AddExpressionEmoteCommand(shortCommand);
                    }

                }
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"EmoteGuard failed to load emote commands: {ex.Message}");
            }

            //overrides for shock collar
            AddEmoteCommand("upset", false);
            AddEmoteCommand("shocked", false);


        }
        private void AddEmoteCommand(string? command, bool canUseMounted = false)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            command = command.Trim();

            if (command.StartsWith('/'))
                command = command[1..];

            var firstSpace = command.IndexOf(' ');
            if (firstSpace >= 0)
                command = command[..firstSpace];

            if (string.IsNullOrWhiteSpace(command))
                return;

            emoteCommands.Add(command);
            emoteCommandAllowsMount[command] = canUseMounted;
        }
        private void AddExpressionEmoteCommand(string? command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            command = command.Trim();

            if (command.StartsWith('/'))
                command = command[1..];

            var firstSpace = command.IndexOf(' ');
            if (firstSpace >= 0)
                command = command[..firstSpace];

            if (string.IsNullOrWhiteSpace(command))
                return;

            expressionEmoteCommands.Add(command);
        }
        private static bool IsExpressionEmoteFromExcel(
    Emote emote,
    string? command,
    string? shortCommand)
        {
            // Preferred: detect from Excel category/mode/name if your generated Lumina sheet exposes it.
            // This is intentionally reflection-based so minor generated field-name changes do not break compilation.
            try
            {
                var emoteType = emote.GetType();

                foreach (var propertyName in new[]
                         {
                     "EmoteMode",
                     "EmoteCategory",
                     "Category",
                     "Mode"
                 })
                {
                    var property = emoteType.GetProperty(propertyName);
                    if (property == null)
                        continue;

                    var value = property.GetValue(emote);
                    if (ValueLooksLikeExpression(value))
                        return true;

                    var innerValue = value?.GetType().GetProperty("Value")?.GetValue(value);
                    if (ValueLooksLikeExpression(innerValue))
                        return true;
                }
            }
            catch
            {
                // Fall through to command fallback below.
            }

            // Fallback: resolve known expression slash commands through Excel,
            // so IDs/aliases still come from the sheet instead of hardcoded RowIds.
            return CommandLooksLikeExpression(command)
                   || CommandLooksLikeExpression(shortCommand);
        }

        private static bool ValueLooksLikeExpression(object? value)
        {
            if (value == null)
                return false;

            var text = value.ToString();

            if (!string.IsNullOrWhiteSpace(text)
                && text.Contains("Expression", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var propertyName in new[] { "Name", "Text", "Description" })
            {
                var property = value.GetType().GetProperty(propertyName);
                if (property == null)
                    continue;

                var propertyText = property.GetValue(value)?.ToString();

                if (!string.IsNullOrWhiteSpace(propertyText)
                    && propertyText.Contains("Expression", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CommandLooksLikeExpression(string? command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            command = command.Trim();

            if (command.StartsWith('/'))
                command = command[1..];

            var firstSpace = command.IndexOf(' ');
            if (firstSpace >= 0)
                command = command[..firstSpace];

            return ExpressionCommandFallbacks.Contains(command);
        }

        private static readonly HashSet<string> ExpressionCommandFallbacks =
            new(StringComparer.OrdinalIgnoreCase)
            {
        "straightface",
        "smile",
        "grin",
        "smirk",
        "taunt",
        "shuteyes",
        "sad",
        "scared",
        "amazed",
        "ouch",
        "annoyed",
        "alert",
        "worried",
            };

        private bool ShouldBlockEmoteForEnforcer(uint emoteId)
        {
            if (!plugin.EmoteEnforcer.ShouldBlockUserEmotes)
                return false;

            return !expressionEmoteIds.Contains(emoteId);
        }

        private bool ShouldBlockEmoteForEnforcer(string normalizedCommand)
        {
            if (!plugin.EmoteEnforcer.ShouldBlockUserEmotes)
                return false;

            if (!TryGetCommandName(normalizedCommand, out var commandName))
                return false;

            return !expressionEmoteCommands.Contains(commandName);
        }

        private void MaybePrintEnforcerBlockedWarning()
        {
            var now = DateTime.UtcNow;

            if (now < nextEnforcerBlockWarningAt)
                return;

            nextEnforcerBlockWarningAt = now + EnforcerBlockWarningCooldown;

            Plugin.ChatGui.PrintError(
                "EmoteEnforcer: Emote blocked while an enforced emote is active.");
        }
        private static bool EmoteCanBeUsedMounted(Emote emote)
        {
            return emote.Unknown1;
        }
        private bool QueuedEmoteCanUseMounted(QueuedEmote emote)
        {
            switch (emote.Kind)
            {
                case QueuedEmoteKind.Hotbar:
                    return emoteIdAllowsMount.TryGetValue(emote.EmoteId, out var hotbarAllowed)
                           && hotbarAllowed;

                case QueuedEmoteKind.Command:
                    if (string.IsNullOrWhiteSpace(emote.Command))
                        return false;

                    if (!TryGetCommandName(emote.Command, out var commandName))
                        return false;

                    return emoteCommandAllowsMount.TryGetValue(commandName, out var commandAllowed)
                           && commandAllowed;

                default:
                    return false;
            }
        }

        private static bool TryGetCommandName(string raw, out string commandName)
        {
            commandName = string.Empty;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            raw = raw.Trim();

            if (!raw.StartsWith('/'))
                return false;

            var withoutSlash = raw[1..];
            var firstSpace = withoutSlash.IndexOf(' ');

            commandName = firstSpace >= 0
                ? withoutSlash[..firstSpace]
                : withoutSlash;

            return !string.IsNullOrWhiteSpace(commandName);
        }


        private byte ExecuteSlotDetour(RaptureHotbarModule* hotbarModule, HotbarSlot* hotbarSlot)
        {
            if (hotbarModule == null || hotbarSlot == null)
                return executeSlotHook.Original(hotbarModule, hotbarSlot);

            if (replaying)
                return executeSlotHook.Original(hotbarModule, hotbarSlot);

            var type = hotbarSlot->CommandType;
            var id = hotbarSlot->CommandId;

            if (type != HotbarSlotType.Emote)
                return executeSlotHook.Original(hotbarModule, hotbarSlot);

            // This is independent from EmoteGuardEnabled.
            if (ShouldBlockEmoteForEnforcer(id))
            {
                MaybePrintEnforcerBlockedWarning();
                return 0;
            }

            if (!plugin.Configuration.EmoteGuardEnabled)
                return executeSlotHook.Original(hotbarModule, hotbarSlot);

            QueueReplay(QueuedEmote.FromHotbar(id));

            return 0;
        }

        private void ProcessChatBoxDetour(
    IntPtr uiModule,
    IntPtr message,
    IntPtr unused,
    byte a4)
        {
            if (replaying)
            {
                processChatBoxHook.Original(uiModule, message, unused, a4);
                return;
            }

            var text = TryReadProcessChatBoxMessage(message);

            if (text == null || !TryNormalizeEmoteCommand(text, out var normalized))
            {
                processChatBoxHook.Original(uiModule, message, unused, a4);
                return;
            }

            // This is independent from EmoteGuardEnabled.
            if (ShouldBlockEmoteForEnforcer(normalized))
            {
                MaybePrintEnforcerBlockedWarning();
                return;
            }

            if (!plugin.Configuration.EmoteGuardEnabled)
            {
                processChatBoxHook.Original(uiModule, message, unused, a4);
                return;
            }

            QueueReplay(QueuedEmote.FromCommand(normalized));

            // Swallow original slash command.
        }

        private void OnFrameworkUpdate(IFramework _)
        {
            try
            {
                OnFrameworkUpdateInner();
            }
            catch (Exception ex)
            {
                queued = null;
                movementLockedUntil = DateTime.MinValue;
                waitingForUserMovementStop = false;
                //  dismountRequestedForCurrentQueue = false;
                combatSuppressUntil = DateTime.MinValue;
                combatSuppressStarted = false;
                currentQueueHadCombatDelay = false;

                ResetAutorunDetector();
                ResetManualStopDetector();
                ResetSuppressedStopDetector();

                Plugin.ChatGui.PrintError($"EmoteGuard update error: {ex.Message}");
            }
        }

        private void OnFrameworkUpdateInner()
        {
            if (combatKeySuppressUntil > DateTime.UtcNow)
                SuppressCombatInputs();

            if (queued is not { } q)
            {
                if (movementLockedUntil > DateTime.UtcNow)
                    SuppressMovementInputs();
                else
                    ResetAutorunDetector();

                return;
            }

            if (DateTime.UtcNow - q.QueuedAt > QueueTimeout)
            {
                queued = null;
                movementLockedUntil = DateTime.MinValue;
                waitingForUserMovementStop = false;
                //  dismountRequestedForCurrentQueue = false;
                combatSuppressUntil = DateTime.MinValue;
                combatSuppressStarted = false;
                currentQueueHadCombatDelay = false;

                ResetAutorunDetector();
                ResetManualStopDetector();
                ResetSuppressedStopDetector();
                return;
            }

            if (HandleCombatPreEmoteDelay())
                return;

            if (TryGetEmoteBlockReason(out var blockReason, out var canAutoDismount))
            {
                HandleQueuedEmoteBlocked(blockReason, canAutoDismount);
                return;
            }


            if (nextDismountAttemptAt + DismountGrace >= DateTime.UtcNow)
                return;

            if (waitingForUserMovementStop)
            {
                // Let the user control movement normally so they can disable autorun manually.
                movementLockedUntil = DateTime.MinValue;

                ResetAutorunDetector();
                ResetSuppressedStopDetector();

                if (!HasUserStoppedMoving())
                    return;

                waitingForUserMovementStop = false;
                ResetManualStopDetector();

                StartMovementLock();
                return;
            }

            if (movementLockedUntil <= DateTime.UtcNow)
            {
                StartMovementLock();
                return;
            }

            SuppressMovementInputs();

            if (DetectAutorunDuringMovementLock())
            {
                movementLockedUntil = DateTime.MinValue;
                waitingForUserMovementStop = true;
                combatSuppressUntil = DateTime.MinValue;
                combatSuppressStarted = false;
                currentQueueHadCombatDelay = false;

                ResetAutorunDetector();
                ResetSuppressedStopDetector();
                ResetManualStopDetector();

                if (DateTime.UtcNow >= nextManualStopMessageAt)
                {
                    nextManualStopMessageAt = DateTime.UtcNow + ManualStopMessageCooldown;

                    Plugin.ChatGui.PrintError(
                        "EmoteGuard: Auto-run or movement is still active. Toggle auto-run off / stop moving, then the queued emote will play.");
                }

                return;
            }

            // This is the important new part:
            // only replay once movement is actually stable while inputs are suppressed.
            if (!HasStoppedWhileSuppressed())
                return;

            var hadCombatDelay = currentQueueHadCombatDelay;

            queued = null;
            waitingForUserMovementStop = false;

            combatSuppressUntil = DateTime.MinValue;
            combatSuppressStarted = false;
            currentQueueHadCombatDelay = false;

            ResetAutorunDetector();
            ResetManualStopDetector();
            ResetSuppressedStopDetector();

            StartMovementLock();

            if (hadCombatDelay)
                combatKeySuppressUntil = DateTime.UtcNow + CombatAfterEmoteSuppressDuration;

            replaying = true;
            try
            {
                switch (q.Kind)
                {
                    case QueuedEmoteKind.Hotbar:
                        ExecuteHotbarEmote(q.EmoteId);
                        break;

                    case QueuedEmoteKind.Command:
                        if (!string.IsNullOrWhiteSpace(q.Command))
                            ExecuteNativeCommand(q.Command);
                        break;
                }
            }
            finally
            {
                replaying = false;
            }
        }
        private void HandleQueuedEmoteBlocked(string reason, bool canAutoDismount)
        {
            movementLockedUntil = DateTime.MinValue;
            waitingForUserMovementStop = false;

            ResetAutorunDetector();
            ResetManualStopDetector();
            ResetSuppressedStopDetector();

            var now = DateTime.UtcNow;

            if (canAutoDismount && now >= nextDismountAttemptAt)
            {
            //    dismountRequestedForCurrentQueue = true;
                nextDismountAttemptAt = now + DismountRetryCooldown;

                ExecuteNativeCommand("/mount");
            }

            if (now < nextBlockedWarningAt)
                return;

            nextBlockedWarningAt = now + BlockedWarningCooldown;

            if (canAutoDismount)
            {
                Plugin.ChatGui.PrintError(
                    $"EmoteGuard: Emote is queued but held because {reason}. Attempting to dismount; emote will play once ready.");
            }
            else
            {
                Plugin.ChatGui.PrintError(
                    $"EmoteGuard: Emote is queued but held because {reason}. It will play once ready.");
            }
        }
        private void QueueReplay(QueuedEmote emote)
        {
            if (queued == null)
            {
                waitingForUserMovementStop = false;
                nextDismountAttemptAt = DateTime.MinValue;

                combatSuppressUntil = DateTime.MinValue;
                combatSuppressStarted = false;
                currentQueueHadCombatDelay = false;
                executingCombatDelayedEmote = false;

                ResetAutorunDetector();
                ResetManualStopDetector();
                ResetSuppressedStopDetector();

                StartMovementLock();
            }

            queued = emote;
        }
        public void QueueGuardedEmote(string emote)
        {
            QueueReplay(QueuedEmote.FromCommand(emote.ToLower().Trim()));
        }
        private bool HandleCombatPreEmoteDelay()
        {
            if (!Plugin.Condition[ConditionFlag.InCombat])
                return false;

            SuppressCombatInputs();

            if (!combatSuppressStarted)
            {
                combatSuppressStarted = true;
                currentQueueHadCombatDelay = true;
                combatSuppressUntil = DateTime.UtcNow + CombatPostDelay;

                Plugin.ChatGui.PrintError(
                    "EmoteGuard: Emote queued during combat. Suppressing inputs briefly, then emote will play.");
            }

            return DateTime.UtcNow < combatSuppressUntil;
        }
        private bool HasStoppedWhileSuppressed()
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null)
            {
                ResetSuppressedStopDetector();
                return false;
            }

            var now = DateTime.UtcNow;

            // Always wait at least MovementSettleDelay before considering replay.
            if (now - movementLockStartedAt < MovementSettleDelay)
            {
                UpdateSuppressedStopCheckPosition(player.Position);
                return false;
            }

            var pos = player.Position;

            if (!haveSuppressedStopCheckPosition)
            {
                UpdateSuppressedStopCheckPosition(pos);
                suppressedStoppedSince = DateTime.MinValue;
                return false;
            }

            var dx = pos.X - lastSuppressedStopCheckPosition.X;
            var dz = pos.Z - lastSuppressedStopCheckPosition.Z;
            var movedSq = (dx * dx) + (dz * dz);

            UpdateSuppressedStopCheckPosition(pos);

            if (movedSq > SuppressedStopMoveDistanceSq)
            {
                suppressedStoppedSince = DateTime.MinValue;
                return false;
            }

            if (suppressedStoppedSince == DateTime.MinValue)
            {
                suppressedStoppedSince = now;
                return false;
            }

            return now - suppressedStoppedSince >= SuppressedStopRequired;
        }

        private void UpdateSuppressedStopCheckPosition(Vector3 position)
        {
            lastSuppressedStopCheckPosition = position;
            haveSuppressedStopCheckPosition = true;
        }

        private void ResetSuppressedStopDetector()
        {
            haveSuppressedStopCheckPosition = false;
            suppressedStoppedSince = DateTime.MinValue;
        }
        private void StartMovementLock()
        {
            movementLockedUntil = DateTime.UtcNow + MovementLockDuration;
            movementLockStartedAt = DateTime.UtcNow;

            SuppressMovementInputs();
        }

        private static void SuppressMovementInputs()
        {
            foreach (var key in MovementKeys)
            {
                if (Plugin.KeyState.IsVirtualKeyValid(key))
                    Plugin.KeyState[key] = false;
            }
        }
        private static void SuppressCombatInputs()
        {
            foreach (var key in CombatKeys)
            {
                if (Plugin.KeyState.IsVirtualKeyValid(key))
                    Plugin.KeyState[key] = false;
            }
        }
        private bool DetectAutorunDuringMovementLock()
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null)
            {
                ResetAutorunDetector();
                return false;
            }

            var now = DateTime.UtcNow;

            if (now - movementLockStartedAt < AutorunDetectionGrace)
            {
                UpdateAutorunCheckPosition(player.Position);
                return false;
            }

            var pos = player.Position;

            if (!haveAutorunCheckPosition)
            {
                UpdateAutorunCheckPosition(pos);
                return false;
            }

            var dx = pos.X - lastAutorunCheckPosition.X;
            var dz = pos.Z - lastAutorunCheckPosition.Z;
            var movedSq = (dx * dx) + (dz * dz);

            UpdateAutorunCheckPosition(pos);

            if (movedSq <= AutorunMoveDistanceSq)
            {
                autorunMovementStartedAt = DateTime.MinValue;
                return false;
            }

            if (autorunMovementStartedAt == DateTime.MinValue)
            {
                autorunMovementStartedAt = now;
                return false;
            }

            return now - autorunMovementStartedAt >= AutorunMovementRequired;
        }
        private bool HasUserStoppedMoving()
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null)
            {
                ResetManualStopDetector();
                return false;
            }

            var now = DateTime.UtcNow;
            var pos = player.Position;

            if (!haveManualStopCheckPosition)
            {
                lastManualStopCheckPosition = pos;
                haveManualStopCheckPosition = true;
                manualStoppedSince = DateTime.MinValue;
                return false;
            }

            var dx = pos.X - lastManualStopCheckPosition.X;
            var dz = pos.Z - lastManualStopCheckPosition.Z;
            var movedSq = (dx * dx) + (dz * dz);

            lastManualStopCheckPosition = pos;

            if (movedSq > ManualStopMoveDistanceSq)
            {
                manualStoppedSince = DateTime.MinValue;
                return false;
            }

            if (manualStoppedSince == DateTime.MinValue)
            {
                manualStoppedSince = now;
                return false;
            }

            return now - manualStoppedSince >= ManualStopRequired;
        }

        private void ResetManualStopDetector()
        {
            haveManualStopCheckPosition = false;
            manualStoppedSince = DateTime.MinValue;
        }

        private void UpdateAutorunCheckPosition(Vector3 position)
        {
            lastAutorunCheckPosition = position;
            haveAutorunCheckPosition = true;
        }

        private void ResetAutorunDetector()
        {
            haveAutorunCheckPosition = false;
            autorunMovementStartedAt = DateTime.MinValue;
        }

        private void ExecuteHotbarEmote(uint emoteId)
        {
            var hotbarModule = RaptureHotbarModule.Instance();
            if (hotbarModule == null)
                return;

            hotbarModule->ScratchSlot.Set(HotbarSlotType.Emote, emoteId);
            hotbarModule->ExecuteSlot(&hotbarModule->ScratchSlot);
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


        private bool TryGetEmoteBlockReason(out string reason, out bool canAutoDismount)
        {
            reason = string.Empty;
            canAutoDismount = false;

            if (Plugin.ObjectTable.LocalPlayer == null)
            {
                reason = "your character is not available";
                return true;
            }

            // Handle mount state separately so we can auto-dismount.
            if (Plugin.Condition[ConditionFlag.Mounted])
            {
                if (queued is { } q && QueuedEmoteCanUseMounted(q))
                    return false;

                reason = "you are mounted";
                canAutoDismount = true;
                return true;
            }

            if (Plugin.Condition[ConditionFlag.RidingPillion])
            {
                reason = "you are riding pillion";
                return true;
            }

            if (Plugin.Condition.Any(
                    ConditionFlag.Mounting,
                    ConditionFlag.Mounting71))
            {
                reason = "you are mounting or dismounting";
                return true;
            }

            //if (Plugin.Condition[ConditionFlag.InCombat])
            //{
            //    reason = "you are in combat";
            //    return true;
            //}

            if (Plugin.Condition.Any(
                    ConditionFlag.Occupied,
                    ConditionFlag.Occupied30,
                    ConditionFlag.OccupiedInEvent,
                    ConditionFlag.OccupiedInQuestEvent,
                    ConditionFlag.Occupied33,
                    ConditionFlag.OccupiedInCutSceneEvent))
            {
                reason = "you are occupied";
                return true;
            }

            if (Plugin.Condition[ConditionFlag.Casting])
            {
                reason = "you are casting";
                return true;
            }

            if (Plugin.Condition.Any(
                    ConditionFlag.Jumping,
                    ConditionFlag.Jumping61))
            {
                reason = "you are jumping";
                return true;
            }

            if (Plugin.Condition.Any(
                    ConditionFlag.BetweenAreas,
                    ConditionFlag.BetweenAreas51))
            {
                reason = "you are changing areas";
                return true;
            }

            if (Plugin.Condition.Any(
                    ConditionFlag.WatchingCutscene,
                    ConditionFlag.WatchingCutscene78))
            {
                reason = "you are watching a cutscene";
                return true;
            }

            if (Plugin.Condition[ConditionFlag.TradeOpen])
            {
                reason = "trade is open";
                return true;
            }

            if (Plugin.Condition[ConditionFlag.Crafting])
            {
                reason = "you are crafting";
                return true;
            }

            if (Plugin.Condition[ConditionFlag.Gathering])
            {
                reason = "you are gathering";
                return true;
            }

            if (Plugin.Condition[ConditionFlag.Fishing])
            {
                reason = "you are fishing";
                return true;
            }

            if (Plugin.Condition[ConditionFlag.Performing])
            {
                reason = "you are performing";
                return true;
            }

            return false;
        }

        private bool TryNormalizeEmoteCommand(string raw, out string normalized)
        {
            normalized = string.Empty;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            raw = raw.Trim();

            if (!raw.StartsWith('/'))
                return false;

            var fullCommand = raw;

            var withoutSlash = raw[1..];
            var firstSpace = withoutSlash.IndexOf(' ');

            var commandName = firstSpace >= 0
                ? withoutSlash[..firstSpace]
                : withoutSlash;

            if (!emoteCommands.Contains(commandName))
                return false;

            normalized = fullCommand;
            return true;
        }

        private static string? TryReadProcessChatBoxMessage(IntPtr message)
        {
            if (message == IntPtr.Zero)
                return null;

            try
            {
                var textPtr = Marshal.ReadIntPtr(message, 0x00);
                var length = Marshal.ReadInt64(message, 0x10);

                if (textPtr == IntPtr.Zero || length <= 0 || length > 500)
                    return null;

                var byteCount = (int)length;
                var bytes = new byte[byteCount];

                Marshal.Copy(textPtr, bytes, 0, byteCount);

                var nul = Array.IndexOf(bytes, (byte)0);
                if (nul >= 0)
                    byteCount = nul;

                if (byteCount <= 0)
                    return null;

                return Encoding.UTF8.GetString(bytes, 0, byteCount);
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            Plugin.Framework.Update -= OnFrameworkUpdate;

            executeSlotHook.Disable();
            executeSlotHook.Dispose();

            processChatBoxHook.Disable();
            processChatBoxHook.Dispose();
        }

        private enum QueuedEmoteKind
        {
            Hotbar,
            Command
        }

        private readonly record struct QueuedEmote(
            QueuedEmoteKind Kind,
            uint EmoteId,
            string? Command,
            DateTime QueuedAt)
        {
            public static QueuedEmote FromHotbar(uint emoteId)
                => new(QueuedEmoteKind.Hotbar, emoteId, null, DateTime.UtcNow);

            public static QueuedEmote FromCommand(string command)
                => new(QueuedEmoteKind.Command, 0, command, DateTime.UtcNow);
        }
    }
}
