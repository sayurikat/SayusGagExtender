using Dalamud.Plugin.Services;
using System;
using System.Reflection;

namespace SayusGagExtender.API.GagSpeak
{
    public enum GagSpeakChatMonitorKind
    {
        ChatboxHidden,
        ChatInputHidden,
        ChatInputDisabled,
    }

    public sealed class GagSpeakChatMonitorApi : IDisposable
    {
        private readonly Plugin plugin;
        private readonly GagSpeakReflectionContext ctx;

        private object? chatboxController;
        private Type? chatboxControllerType;

        private FieldInfo? hideChatBoxesField;
        private FieldInfo? hideChatInputField;
        private FieldInfo? blockInputField;

        private bool? lastChatboxHidden;
        private bool? lastChatInputHidden;
        private bool? lastChatInputDisabled;

        private bool disposed;

        /// <summary>
        /// When true, debug messages are printed to chat.
        /// When false, ChatGui.Print and ChatGui.PrintError calls in this class are suppressed.
        /// </summary>
        public bool DebugLog = false;

        public event Action<bool>? OnChatboxHiddenChanged;
        public event Action<bool>? OnChatInputHiddenChanged;
        public event Action<bool>? OnChatInputDisabledChanged;

        public GagSpeakChatMonitorApi(
            Plugin plugin,
            GagSpeakReflectionContext ctx,
            bool debugLog = false)
        {
            this.plugin = plugin;
            this.ctx = ctx;
            DebugLog = debugLog;

            Plugin.Framework.Update += this.OnFrameworkUpdate;
        }

        public bool IsChatboxHidden()
            => TryGetChatboxControllerState(out var value, out _, out _) && value;

        public bool IsChatInputHidden()
            => TryGetChatboxControllerState(out _, out var value, out _) && value;

        public bool IsChatInputDisabled()
            => TryGetChatboxControllerState(out _, out _, out var value) && value;

        public bool IsMonitorActive(GagSpeakChatMonitorKind kind)
        {
            return kind switch
            {
                GagSpeakChatMonitorKind.ChatboxHidden => IsChatboxHidden(),
                GagSpeakChatMonitorKind.ChatInputHidden => IsChatInputHidden(),
                GagSpeakChatMonitorKind.ChatInputDisabled => IsChatInputDisabled(),
                _ => false,
            };
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (disposed)
                return;

            if (!TryGetChatboxControllerState(
                    out var chatboxHidden,
                    out var chatInputHidden,
                    out var chatInputDisabled))
            {
                return;
            }

            if (lastChatboxHidden != chatboxHidden)
            {
                lastChatboxHidden = chatboxHidden;
                OnChatboxHiddenChanged?.Invoke(chatboxHidden);
            }

            if (lastChatInputHidden != chatInputHidden)
            {
                lastChatInputHidden = chatInputHidden;
                OnChatInputHiddenChanged?.Invoke(chatInputHidden);
            }

            if (lastChatInputDisabled != chatInputDisabled)
            {
                lastChatInputDisabled = chatInputDisabled;
                OnChatInputDisabledChanged?.Invoke(chatInputDisabled);
            }
        }

        private bool TryResolveChatboxController()
        {
            if (!ctx.EnsureReady())
            {
                ClearCachedChatboxController();
                return false;
            }

            if (CachedChatboxControllerStillUsable())
                return true;

            ClearCachedChatboxController();

            chatboxController = ctx.TryResolveServiceByTypeName("ChatboxController");

            if (chatboxController == null)
            {
                DebugPrintError("Could not resolve GagSpeak ChatboxController.");
                return false;
            }

            chatboxControllerType = chatboxController.GetType();

            hideChatBoxesField = FindBoolField(
                chatboxControllerType,
                "_hideChatBoxes",
                "hideChatBoxes",
                "HideChatBoxes");

            hideChatInputField = FindBoolField(
                chatboxControllerType,
                "_hideChatInput",
                "hideChatInput",
                "HideChatInput");

            blockInputField = FindBoolField(
                chatboxControllerType,
                "_blockInput",
                "blockInput",
                "BlockInput");

            if (hideChatBoxesField == null)
            {
                DebugPrintError("GagSpeak ChatboxController hide-chatboxes field was not found.");
                DumpChatboxControllerFields(chatboxControllerType);
                ClearCachedChatboxController();
                return false;
            }

            if (hideChatInputField == null)
            {
                DebugPrintError("GagSpeak ChatboxController hide-chat-input field was not found.");
                DumpChatboxControllerFields(chatboxControllerType);
                ClearCachedChatboxController();
                return false;
            }

            if (blockInputField == null)
            {
                DebugPrintError("GagSpeak ChatboxController block-input field was not found.");
                DumpChatboxControllerFields(chatboxControllerType);
                ClearCachedChatboxController();
                return false;
            }

            DebugPrint($"Resolved GagSpeak ChatboxController: {chatboxControllerType.FullName}");
            return true;
        }

        private bool CachedChatboxControllerStillUsable()
        {
            if (chatboxController == null ||
                chatboxControllerType == null ||
                hideChatBoxesField == null ||
                hideChatInputField == null ||
                blockInputField == null)
            {
                return false;
            }

            try
            {
                // Force-read the three values. If the cached controller came from an old
                // disposed GagSpeak host, this is where we want to invalidate it.
                _ = hideChatBoxesField.GetValue(chatboxController);
                _ = hideChatInputField.GetValue(chatboxController);
                _ = blockInputField.GetValue(chatboxController);

                // Extra guard: make sure the cached controller type still belongs to the
                // same live GagSpeak assembly context as the resolved service would.
                var fresh = ctx.TryResolveServiceByTypeName("ChatboxController");
                if (fresh == null)
                    return false;

                return ReferenceEquals(chatboxController, fresh);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (TargetInvocationException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetChatboxControllerState(
            out bool chatboxHidden,
            out bool chatInputHidden,
            out bool chatInputDisabled)
        {
            chatboxHidden = false;
            chatInputHidden = false;
            chatInputDisabled = false;

            if (!TryResolveChatboxController())
                return false;

            try
            {
                chatboxHidden = ReadBoolField(hideChatBoxesField!, chatboxController);
                chatInputHidden = ReadBoolField(hideChatInputField!, chatboxController);
                chatInputDisabled = ReadBoolField(blockInputField!, chatboxController);
                return true;
            }
            catch (ObjectDisposedException)
            {
                ClearCachedChatboxController();
                return false;
            }
            catch (TargetInvocationException)
            {
                ClearCachedChatboxController();
                return false;
            }
            catch
            {
                ClearCachedChatboxController();
                return false;
            }
        }

        private static bool ReadBoolField(FieldInfo field, object? instance)
        {
            var value = field.GetValue(instance);

            if (value is bool b)
                return b;

            if (value is int i)
                return i != 0;

            if (value is byte by)
                return by != 0;

            return false;
        }

        private static FieldInfo? FindBoolField(Type type, params string[] names)
        {
            foreach (var name in names)
            {
                var field = type.GetField(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (field == null)
                    continue;

                if (field.FieldType == typeof(bool) ||
                    field.FieldType == typeof(int) ||
                    field.FieldType == typeof(byte))
                {
                    return field;
                }
            }

            return null;
        }

        private void DumpChatboxControllerFields(Type type)
        {
            if (!DebugLog)
                return;

            try
            {
                DebugPrintError($"Fields on {type.FullName}:");

                foreach (var field in type.GetFields(
                             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    DebugPrintError($"- {field.FieldType.FullName} {field.Name}");
                }
            }
            catch
            {
                // ignored
            }
        }

        private void DebugPrint(string message)
        {
            if (!DebugLog)
                return;

            Plugin.ChatGui.Print(message);
        }

        private void DebugPrintError(string message)
        {
            if (!DebugLog)
                return;

            Plugin.ChatGui.PrintError(message);
        }

        private void ClearCachedChatboxController()
        {
            chatboxController = null;
            chatboxControllerType = null;

            hideChatBoxesField = null;
            hideChatInputField = null;
            blockInputField = null;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            Plugin.Framework.Update -= this.OnFrameworkUpdate;

            OnChatboxHiddenChanged = null;
            OnChatInputHiddenChanged = null;
            OnChatInputDisabledChanged = null;

            ClearCachedChatboxController();
        }
    }
}
