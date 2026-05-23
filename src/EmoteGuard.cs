using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using Lumina.Excel.Sheets;
//using Lumina.Excel.Sheets.Experimental;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using static FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.DynamicEvent.Delegates;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule;

namespace SayusGagExtender;

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

    private readonly EmoteRegistry emotes = new();
    private readonly StopDetector suppressedStopDetector = new(SuppressedStopMoveDistanceSq, SuppressedStopRequired);

    private QueuedEmote? queued;

    private DateTime movementLockedUntil = DateTime.MinValue;
    private DateTime movementLockStartedAt = DateTime.MinValue;
    private bool movementBlockRequested;

    private DateTime nextBlockedWarningAt = DateTime.MinValue;
    private DateTime nextDismountAttemptAt = DateTime.MinValue;
    private DateTime nextEnforcerBlockWarningAt = DateTime.MinValue;

    private DateTime combatSuppressUntil = DateTime.MinValue;
    private DateTime combatActionBlockUntil = DateTime.MinValue;
    private bool combatSuppressStarted;
    private bool currentQueueHadCombatDelay;
    private bool combatActionBlockRequested;

    private bool replaying;
    private bool disposed;

    private static readonly TimeSpan MovementSettleDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan MovementLockDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan QueueTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan SuppressedStopRequired = TimeSpan.FromMilliseconds(25);
    private const float SuppressedStopMoveDistanceSq = 0.000025f;

    private static readonly TimeSpan DismountGrace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DismountRetryCooldown = TimeSpan.FromMilliseconds(200);

    private static readonly TimeSpan BlockedWarningCooldown = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EnforcerBlockWarningCooldown = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan CombatPostDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CombatAfterEmoteSuppressDuration = TimeSpan.FromSeconds(3);

    private readonly Queue<QueuedEmoteStep> queuedSequence = new();
    private DateTime nextSequenceStepAt = DateTime.MinValue;

    private static readonly TimeSpan DefaultSequenceDelay = TimeSpan.FromSeconds(2);

    public bool IsActive = false;

    private enum QueuedEmoteStepKind
    {
        Command,
        Wait,
        EmoteId,
        Hotbar,
    }

    private readonly record struct QueuedEmoteStep(
    QueuedEmoteStepKind Kind,
    string? Command,
    TimeSpan Duration,
    uint EmoteId)
    {
        public static QueuedEmoteStep CommandStep(string command)
            => new(QueuedEmoteStepKind.Command, command, TimeSpan.Zero, 0);

        public static QueuedEmoteStep WaitStep(TimeSpan duration)
            => new(QueuedEmoteStepKind.Wait, null, duration, 0);

        public static QueuedEmoteStep EmoteIdStep(uint emoteId)
            => new(QueuedEmoteStepKind.EmoteId, null, TimeSpan.Zero, emoteId);

        public static QueuedEmoteStep HotbarStep(uint emoteId)
            => new(QueuedEmoteStepKind.Hotbar, null, TimeSpan.Zero, emoteId);
    }

    public EmoteGuard(Plugin instance)
    {
        plugin = instance;

        BuildEmoteRegistry();

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

    //public void QueueGuardedEmote(string emote)
    //{
    //    QueueReplay(QueuedEmote.FromCommand(emote.Trim().ToLowerInvariant()));
    //}
    public void QueueGuardedEmote(string emote)
    {
        foreach (var step in CommandParser.ParseQueuedEmoteSequence(emote))
            queuedSequence.Enqueue(step);

        TryStartNextQueuedSequenceStep(DateTime.UtcNow);
    }
    

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        Plugin.Framework.Update -= OnFrameworkUpdate;
        ClearMovementBlock();
        ClearCombatActionBlock();

        executeSlotHook.Disable();
        executeSlotHook.Dispose();

        processChatBoxHook.Disable();
        processChatBoxHook.Dispose();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        try
        {
            OnFrameworkUpdateInner();
        }
        catch (Exception ex)
        {
            ResetQueueState();
            Plugin.ChatGui.PrintError($"EmoteGuard update error: {ex.Message}");
        }
    }

    private void OnFrameworkUpdateInner()
    {
        var now = DateTime.UtcNow;
        IsActive = true;

        UpdateCombatActionBlock(now);

        if (queued is not { } current)
        {
            TryStartNextQueuedSequenceStep(now);

            if (queued is not { } nextCurrent)
            {
                PostEmoteQueue(now);
                return;
            }

            current = nextCurrent;
        }

        if (now - current.QueuedAt > QueueTimeout)
        {
            ResetQueueState();
            return;
        }

        if (HandleCombatPreEmoteDelay(now))
            return;

        if (TryGetEmoteBlockReason(out var blockReason, out var canAutoDismount))
        {
            HandleQueuedEmoteBlocked(blockReason, canAutoDismount, now);
            return;
        }

        if (nextDismountAttemptAt + DismountGrace >= now)
            return;

        if (!EnsureMovementLockActive(now))
            return;

        if (!HasStoppedWhileSuppressed(now))
            return;

        ReplayQueuedEmote(current);
    }
    private void TryStartNextQueuedSequenceStep(DateTime now)
    {
        if (queued != null)
            return;

        if (nextSequenceStepAt > now)
        {
            RequestMovementBlock();
            return;
        }

        while (queuedSequence.Count > 0)
        {
            var step = queuedSequence.Dequeue();

            if (step.Kind == QueuedEmoteStepKind.Wait)
            {
                nextSequenceStepAt = now + step.Duration;

                if (movementLockedUntil < nextSequenceStepAt)
                    movementLockedUntil = nextSequenceStepAt;

                RequestMovementBlock();

                return;
            }
            //Plugin.ChatGui.Print($"TryStartNextQueuedSequenceStep {step.Kind.ToString()} {step.EmoteId.ToString()} {step.Command?.ToString()}");
            if (step.Kind == QueuedEmoteStepKind.EmoteId && step.EmoteId > 0)
            {
                nextSequenceStepAt = DateTime.MinValue;
                QueueReplay(QueuedEmote.FromApiEmoteId(step.EmoteId));
                return;
            }

            if (step.Kind == QueuedEmoteStepKind.Hotbar && step.EmoteId > 0)
            {
                nextSequenceStepAt = DateTime.MinValue;
                QueueReplay(QueuedEmote.FromHotbar(step.EmoteId));
                return;
            }

            if (!string.IsNullOrWhiteSpace(step.Command))
            {
                nextSequenceStepAt = DateTime.MinValue;
                QueueReplay(QueuedEmote.FromCommand(step.Command));
                return;
            }
        }

        nextSequenceStepAt = DateTime.MinValue;
    }

    private bool IsQueuedSequenceBusy(DateTime now)
    {
        return queuedSequence.Count > 0 || nextSequenceStepAt > now;
    }

    private void QueueUserEmoteStep(QueuedEmoteStep step)
    {
        queuedSequence.Enqueue(step);
        RequestMovementBlock();
    }

    private void BuildEmoteRegistry()
    {
        emotes.Clear();

        // Manual aliases / fallbacks / special cases.
        emotes.AddCommand("sit", false);
        emotes.AddCommand("groundsit", false);
        emotes.AddCommand("doze", true);
        emotes.AddCommand("pose", false);
        emotes.AddCommand("changepose", false);
        emotes.AddCommand("cpose", false);
        emotes.AddCommand("dpose", false);
        emotes.AddCommand("bstance", false);
        emotes.AddCommand("vpose", false);

        try
        {
            foreach (var emote in Plugin.DataManager.GetExcelSheet<Emote>())
            {
                var textCommand = emote.TextCommand.Value;
                var canUseMounted = EmoteCanBeUsedMounted(emote);

                var command = textCommand.Command.ToString();
                var shortCommand = textCommand.ShortCommand.ToString();

                emotes.AddCommand(command, canUseMounted);
                emotes.AddCommand(shortCommand, canUseMounted);
                emotes.SetMountAllowedForId(emote.RowId, canUseMounted);

                if (!IsExpressionEmote(emote, command, shortCommand))
                    continue;

                emotes.AddExpressionId(emote.RowId);
                emotes.AddExpressionCommand(command);
                emotes.AddExpressionCommand(shortCommand);
            }
        }
        catch (Exception ex)
        {
            Plugin.ChatGui.PrintError($"EmoteGuard failed to load emote commands: {ex.Message}");
        }

        // Shock-collar overrides.
        emotes.AddCommand("upset", false);
        emotes.AddCommand("shocked", false);
        emotes.AddCommand("standup", false);
    }

    private byte ExecuteSlotDetour(RaptureHotbarModule* hotbarModule, HotbarSlot* hotbarSlot)
    {
        if (hotbarModule == null || hotbarSlot == null || replaying)
            return executeSlotHook.Original(hotbarModule, hotbarSlot);

        if (hotbarSlot->CommandType != HotbarSlotType.Emote)
            return executeSlotHook.Original(hotbarModule, hotbarSlot);

        var emoteId = hotbarSlot->CommandId;

        // EmoteEnforcer is intentionally independent from EmoteGuardEnabled.
        if (ShouldBlockEmoteForEnforcer(emoteId))
        {
            MaybePrintEnforcerBlockedWarning();
            return 0;
        }

        if (!plugin.Configuration.EmoteGuardEnabled)
            return executeSlotHook.Original(hotbarModule, hotbarSlot);

        var now = DateTime.UtcNow;

        if (IsQueuedSequenceBusy(now))
        {
            QueueUserEmoteStep(QueuedEmoteStep.HotbarStep(emoteId));
            return 0;
        }

        QueueReplay(QueuedEmote.FromHotbar(emoteId));
        return 0;
    }

    private void ProcessChatBoxDetour(IntPtr uiModule, IntPtr message, IntPtr unused, byte a4)
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

        // EmoteEnforcer is intentionally independent from EmoteGuardEnabled.
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

        var now = DateTime.UtcNow;

        if (IsQueuedSequenceBusy(now))
        {
            QueueUserEmoteStep(QueuedEmoteStep.CommandStep(normalized));
            return;
        }

        QueueReplay(QueuedEmote.FromCommand(normalized));
        // Swallow the original slash command. It will be replayed once safe.
    }



    private bool HandleCombatPreEmoteDelay(DateTime now)
    {
        if (!Plugin.Condition[ConditionFlag.InCombat])
            return false;

        RequestCombatActionBlock();

        if (!combatSuppressStarted)
        {
            combatSuppressStarted = true;
            currentQueueHadCombatDelay = true;
            combatSuppressUntil = now + CombatPostDelay;

            Plugin.ChatGui.PrintError(
                "EmoteGuard: Emote queued during combat. Suppressing combat actions briefly, then emote will play.");
        }

        return now < combatSuppressUntil;
    }

    private void HandleQueuedEmoteBlocked(string reason, bool canAutoDismount, DateTime now)
    {
        suppressedStopDetector.Reset();

        if (canAutoDismount && now >= nextDismountAttemptAt)
        {
            nextDismountAttemptAt = now + DismountRetryCooldown;
            ExecuteNativeCommand("/mount");
        }

        if (now < nextBlockedWarningAt)
            return;

        nextBlockedWarningAt = now + BlockedWarningCooldown;

        var action = canAutoDismount
            ? "Attempting to dismount; emote will play once ready."
            : "It will play once ready.";

        Plugin.ChatGui.PrintError(
            $"EmoteGuard: Emote is queued but held because {reason}. {action}");
    }

    private void QueueReplay(QueuedEmote emote)
    {
        if (queued == null)
            ResetForNewQueue();

        //Plugin.ChatGui.Print($"QueueReplay {emote.Kind.ToString()} {emote.EmoteId.ToString()} {emote.Command?.ToString()}");
        queued = emote;
    }

    private void ResetForNewQueue()
    {
        nextDismountAttemptAt = DateTime.MinValue;
        combatSuppressUntil = DateTime.MinValue;
        combatActionBlockUntil = DateTime.MinValue;
        combatSuppressStarted = false;
        currentQueueHadCombatDelay = false;
        ClearCombatActionBlock();
        suppressedStopDetector.Reset();
    }

    private void ResetQueueState()
    {
        queued = null;
        combatSuppressUntil = DateTime.MinValue;
        combatActionBlockUntil = DateTime.MinValue;
        combatSuppressStarted = false;
        currentQueueHadCombatDelay = false;
        ClearCombatActionBlock();
        suppressedStopDetector.Reset();
    }

    private void PostEmoteQueue(DateTime now)
    {
        if (movementLockedUntil > now)
        {
            RequestMovementBlock();
            return;
        }

        IsActive = false;
        ClearMovementBlock();
    }

    private bool EnsureMovementLockActive(DateTime now)
    {
        if (movementLockedUntil <= now)
        {
            StartMovementLock(now);
            return false;
        }

        RequestMovementBlock();
        return true;
    }

    private void StartMovementLock(DateTime now)
    {
        movementLockedUntil = now + MovementLockDuration;
        movementLockStartedAt = now;
        suppressedStopDetector.Reset();

        RequestMovementBlock();
    }

    private void RequestMovementBlock()
    {
        if (movementBlockRequested || plugin.MovementBlocker == null)
            return;

        movementBlockRequested = true;
        plugin.MovementBlocker.RequestBlock(nameof(EmoteGuard));
    }

    private void ClearMovementBlock()
    {
        if (!movementBlockRequested || plugin.MovementBlocker == null)
            return;

        movementBlockRequested = false;
        plugin.MovementBlocker.ClearBlock(nameof(EmoteGuard));
    }

    private void UpdateCombatActionBlock(DateTime now)
    {
        if (combatActionBlockUntil > now)
        {
            RequestCombatActionBlock();
            return;
        }

        if (queued != null && combatSuppressStarted && Plugin.Condition[ConditionFlag.InCombat])
        {
            RequestCombatActionBlock();
            return;
        }

        ClearCombatActionBlock();
    }

    private void RequestCombatActionBlock()
    {
        if (combatActionBlockRequested || plugin.ActionBlocker == null)
            return;

        combatActionBlockRequested = true;
        plugin.ActionBlocker.RequestBlock(nameof(EmoteGuard));
    }

    private void ClearCombatActionBlock()
    {
        if (!combatActionBlockRequested || plugin.ActionBlocker == null)
            return;

        combatActionBlockRequested = false;
        plugin.ActionBlocker.ClearBlock(nameof(EmoteGuard));
    }

    private bool HasStoppedWhileSuppressed(DateTime now)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
        {
            suppressedStopDetector.Reset();
            return false;
        }

        if (now - movementLockStartedAt < MovementSettleDelay)
        {
            suppressedStopDetector.Update(player.Position, now);
            return false;
        }

        return suppressedStopDetector.Update(player.Position, now);
    }

    private void ReplayQueuedEmote(QueuedEmote emote)
    {
        var hadCombatDelay = currentQueueHadCombatDelay;

        ResetQueueState();
        StartMovementLock(DateTime.UtcNow);

        if (hadCombatDelay)
        {
            combatActionBlockUntil = DateTime.UtcNow + CombatAfterEmoteSuppressDuration;
            RequestCombatActionBlock();
        }
        //Plugin.ChatGui.Print($"ReplayQueuedEmote {emote.Kind.ToString()} {emote.EmoteId.ToString()} {emote.Command?.ToString()}");
        replaying = true;
        try
        {
            switch (emote.Kind)
            {
                case QueuedEmoteKind.Hotbar:
                    ExecuteHotbarEmote(emote.EmoteId);
                    break;

                case QueuedEmoteKind.ApiEmoteId:
                    plugin.EmoteApi.ExecuteEmote(emote.EmoteId);
                    break;

                case QueuedEmoteKind.Command when !string.IsNullOrWhiteSpace(emote.Command):
                    ExecuteNativeCommand(emote.Command);
                    break;
            }
        }
        finally
        {
            replaying = false;
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

        if (Plugin.Condition[ConditionFlag.Mounted])
        {
            if (queued is { } q && emotes.CanUseMounted(q))
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

        if (Plugin.Condition.Any(ConditionFlag.Mounting, ConditionFlag.Mounting71))
        {
            reason = "you are mounting or dismounting";
            return true;
        }

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

        if (Plugin.Condition.Any(ConditionFlag.Jumping, ConditionFlag.Jumping61))
        {
            reason = "you are jumping";
            return true;
        }

        if (Plugin.Condition.Any(ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51))
        {
            reason = "you are changing areas";
            return true;
        }

        if (Plugin.Condition.Any(ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78))
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

        if (!CommandParser.TryNormalizeSlashCommand(raw, out var commandName, out var fullCommand))
            return false;

        if (!emotes.ContainsCommand(commandName))
            return false;

        normalized = fullCommand;
        return true;
    }

    private bool ShouldBlockEmoteForEnforcer(uint emoteId)
    {
        return plugin.EmoteEnforcer.ShouldBlockUserEmotes
               && !emotes.IsExpressionEmote(emoteId);
    }

    private bool ShouldBlockEmoteForEnforcer(string normalizedCommand)
    {
        return plugin.EmoteEnforcer.ShouldBlockUserEmotes
               && CommandParser.TryGetCommandName(normalizedCommand, out var commandName)
               && !emotes.IsExpressionEmote(commandName);
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

    private static bool IsExpressionEmote(Emote emote, string? command, string? shortCommand)
    {
        // Preferred: detect from Excel category/mode/name if the generated Lumina sheet exposes it.
        // Reflection keeps this resilient against minor generated field-name changes.
        try
        {
            var emoteType = emote.GetType();

            foreach (var propertyName in new[] { "EmoteMode", "EmoteCategory", "Category", "Mode" })
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

        return ExpressionCommandFallbacks.Contains(CommandParser.NormalizeCommandName(command))
               || ExpressionCommandFallbacks.Contains(CommandParser.NormalizeCommandName(shortCommand));
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

    private static readonly HashSet<string> ExpressionCommandFallbacks = new(StringComparer.OrdinalIgnoreCase)
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
        }
        finally
        {
            cmd.Dtor(true);
        }
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

    private enum QueuedEmoteKind
    {
        Hotbar,
        Command,
        ApiEmoteId,
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

        public static QueuedEmote FromApiEmoteId(uint emoteId)
            => new(QueuedEmoteKind.ApiEmoteId, emoteId, null, DateTime.UtcNow);
    }

    private sealed class EmoteRegistry
    {
        private readonly HashSet<string> commands = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> commandAllowsMount = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, bool> idAllowsMount = new();

        private readonly HashSet<string> expressionCommands = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<uint> expressionIds = new();

        public void Clear()
        {
            commands.Clear();
            commandAllowsMount.Clear();
            idAllowsMount.Clear();
            expressionCommands.Clear();
            expressionIds.Clear();
        }

        public void AddCommand(string? command, bool canUseMounted)
        {
            var normalized = CommandParser.NormalizeCommandName(command);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            commands.Add(normalized);
            commandAllowsMount[normalized] = canUseMounted;
        }

        public bool ContainsCommand(string commandName)
        {
            return commands.Contains(commandName);
        }

        public void SetMountAllowedForId(uint emoteId, bool canUseMounted)
        {
            idAllowsMount[emoteId] = canUseMounted;
        }

        public bool CanUseMounted(QueuedEmote emote)
        {
            return emote.Kind switch
            {
                QueuedEmoteKind.Hotbar => idAllowsMount.TryGetValue(emote.EmoteId, out var allowed) && allowed,
                QueuedEmoteKind.ApiEmoteId => idAllowsMount.TryGetValue(emote.EmoteId, out var allowed) && allowed,
                QueuedEmoteKind.Command => CanCommandUseMounted(emote.Command),
                _ => false,
            };
        }

        public void AddExpressionCommand(string? command)
        {
            var normalized = CommandParser.NormalizeCommandName(command);
            if (!string.IsNullOrWhiteSpace(normalized))
                expressionCommands.Add(normalized);
        }

        public void AddExpressionId(uint emoteId)
        {
            expressionIds.Add(emoteId);
        }

        public bool IsExpressionEmote(uint emoteId)
        {
            return expressionIds.Contains(emoteId);
        }

        public bool IsExpressionEmote(string commandName)
        {
            return expressionCommands.Contains(commandName);
        }

        private bool CanCommandUseMounted(string? command)
        {
            if (!CommandParser.TryGetCommandName(command, out var commandName))
                return false;

            return commandAllowsMount.TryGetValue(commandName, out var allowed) && allowed;
        }
    }

    private sealed class StopDetector
    {
        private readonly float moveDistanceSq;
        private readonly TimeSpan stoppedRequired;

        private Vector3 lastPosition;
        private bool hasPosition;
        private DateTime stoppedSince = DateTime.MinValue;

        public StopDetector(float moveDistanceSq, TimeSpan stoppedRequired)
        {
            this.moveDistanceSq = moveDistanceSq;
            this.stoppedRequired = stoppedRequired;
        }

        public bool Update(Vector3 position, DateTime now)
        {
            if (!hasPosition)
            {
                lastPosition = position;
                hasPosition = true;
                stoppedSince = DateTime.MinValue;
                return false;
            }

            var dx = position.X - lastPosition.X;
            var dz = position.Z - lastPosition.Z;
            var movedSq = (dx * dx) + (dz * dz);

            lastPosition = position;

            if (movedSq > moveDistanceSq)
            {
                stoppedSince = DateTime.MinValue;
                return false;
            }

            if (stoppedSince == DateTime.MinValue)
            {
                stoppedSince = now;
                return false;
            }

            return now - stoppedSince >= stoppedRequired;
        }

        public void Reset()
        {
            hasPosition = false;
            stoppedSince = DateTime.MinValue;
        }
    }

    private static class CommandParser
    {
        public static bool TryNormalizeSlashCommand(
            string? raw,
            out string commandName,
            out string fullCommand)
        {
            commandName = string.Empty;
            fullCommand = string.Empty;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            fullCommand = raw.Trim();
            if (!fullCommand.StartsWith('/'))
                return false;

            return TryGetCommandName(fullCommand, out commandName);
        }

        public static bool TryGetCommandName(string? raw, out string commandName)
        {
            commandName = NormalizeCommandName(raw);
            return !string.IsNullOrWhiteSpace(commandName);
        }

        public static string NormalizeCommandName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var text = raw.Trim();

            if (text.StartsWith('/'))
                text = text[1..];

            var firstSpace = text.IndexOf(' ');
            if (firstSpace >= 0)
                text = text[..firstSpace];

            return text.Trim();
        }
        public static IEnumerable<QueuedEmoteStep> ParseQueuedEmoteSequence(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            var commands = SplitSlashCommands(raw)
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => x.StartsWith('/'))
                .ToList();

            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];

                if (TryParseWaitCommand(command, out var waitDuration))
                {
                    yield return QueuedEmoteStep.WaitStep(waitDuration);
                    continue;
                }

                if (TryParseEmoteIdCommand(command, out var emoteId))
                {
                    yield return QueuedEmoteStep.EmoteIdStep(emoteId);
                }
                else
                {
                    yield return QueuedEmoteStep.CommandStep(command);
                }

                var nextIsExplicitWait =
                    i + 1 < commands.Count &&
                    TryParseWaitCommand(commands[i + 1], out _);

                var hasAnotherCommandAfterThis =
                    commands
                        .Skip(i + 1)
                        .Any(x => !TryParseWaitCommand(x, out _));

                if (!nextIsExplicitWait && hasAnotherCommandAfterThis)
                    yield return QueuedEmoteStep.WaitStep(DefaultSequenceDelay);
            }
        }

        private static IEnumerable<string> SplitSlashCommands(string raw)
        {
            var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
                yield return "/" + part.Trim();
        }

        private static bool TryParseWaitCommand(string command, out TimeSpan duration)
        {
            duration = TimeSpan.Zero;

            if (string.IsNullOrWhiteSpace(command))
                return false;

            var parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
                return false;

            if (!string.Equals(parts[0], "/wait", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!double.TryParse(
                    parts[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var seconds))
            {
                return false;
            }
            
            if (seconds < 0)
                seconds = 0;

            duration = TimeSpan.FromSeconds(seconds);
            return true;
        }
        private static bool TryParseEmoteIdCommand(string command, out uint emoteId)
        {
            emoteId = 0;

            if (string.IsNullOrWhiteSpace(command))
                return false;

            command = command.Trim();

            if (!command.StartsWith("/emoteid ", StringComparison.OrdinalIgnoreCase))
                return false;

            var rawId = command.Substring("/emoteid ".Length).Trim();

            if (!uint.TryParse(rawId, out emoteId))
                return false;

            return emoteId > 0;
        }
    }
}
