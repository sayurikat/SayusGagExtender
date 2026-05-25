using Dalamud.Plugin;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace SayusGagExtender.API;

public sealed class XivMessengerApi : IDisposable
{
    private const int DefaultInputMaxLength = 15000;

    private readonly DalamudReflector reflector;
    private object? cachedPlugin;

    private readonly Dictionary<object, int> originalInputMaxLengths =
        new(ReferenceComparer.Instance);

    private static readonly BindingFlags Flags =
        BindingFlags.Instance |
        BindingFlags.Static |
        BindingFlags.Public |
        BindingFlags.NonPublic;

    public XivMessengerApi(Plugin plugin)
    {
        reflector = new DalamudReflector(
            Plugin.PluginInterface,
            Plugin.Framework,
            Plugin.Log);
    }

    public void Dispose()
    {
        ToggleTextInput(true);
        reflector.Dispose();
    }

    public bool IsWindowOpen()
    {
        var foundWindow = false;

        foreach (var chatWindow in GetChatWindows())
        {
            foundWindow = true;

            if (TryGetBool(chatWindow, "IsOpen", out var isOpen) && isOpen)
                return true;
        }

        if (!foundWindow)
            Plugin.Log.Verbose("[XivMessengerApi] IsWindowOpen: no chat windows found.");

        return false;
    }

    public bool CloseWindow()
    {
        var changed = false;
        var foundWindow = false;

        foreach (var chatWindow in GetChatWindows())
        {
            foundWindow = true;

            if (!TryGetBool(chatWindow, "IsOpen", out var isOpen))
            {
                Plugin.Log.Warning($"[XivMessengerApi] CloseWindow: window has no readable IsOpen. Type={chatWindow.GetType().FullName}");
                continue;
            }

            if (!isOpen)
                continue;

            if (TrySetBool(chatWindow, "IsOpen", false))
            {
                changed = true;
                Plugin.Log.Information("[XivMessengerApi] Closed XIM chat window.");
            }
            else
            {
                Plugin.Log.Warning($"[XivMessengerApi] CloseWindow: failed to set IsOpen=false. Type={chatWindow.GetType().FullName}");
            }
        }

        if (!foundWindow)
            Plugin.Log.Warning("[XivMessengerApi] CloseWindow: no chat windows found.");

        return changed;
    }

    public bool IsTextInputEnabled()
    {
        return TryIsTextInputEnabled(out var enabled) && enabled;
    }

    public bool TryIsTextInputEnabled(out bool enabled)
    {
        enabled = true;

        var foundInput = false;

        foreach (var input in GetInputs())
        {
            foundInput = true;

            if (TryGetInt(input, "MaxLength", out var maxLength))
            {
                if (maxLength <= 0)
                {
                    enabled = false;
                    return true;
                }

                continue;
            }

            Plugin.Log.Warning($"[XivMessengerApi] TryIsTextInputEnabled: input has no readable MaxLength. Type={input.GetType().FullName}");
        }

        return foundInput;
    }

    public bool ToggleTextInput(bool enabled)
    {
        var applied = false;
        var foundInput = false;

        foreach (var input in GetInputs())
        {
            foundInput = true;
            applied |= SetInputEnabled(input, enabled);
        }

        if (!foundInput)
            Plugin.Log.Warning($"[XivMessengerApi] ToggleTextInput({enabled}): no input objects found.");

        return applied;
    }

    public void DebugPrint()
    {
        if (!TryGetMessengerPlugin(out var messenger))
        {
            Plugin.ChatGui.PrintError("XIM debug: Messenger plugin not found.");
            return;
        }

        var assembly = messenger.GetType().Assembly;

        Plugin.ChatGui.Print($"XIM debug: plugin={messenger.GetType().FullName}");
        Plugin.ChatGui.Print($"XIM debug: asm={assembly.GetName().Name}");

        var serviceHolder =
            assembly.GetType("Messenger.S") ??
            FindTypeWithStaticMember(assembly, "MessageProcessor");

        Plugin.ChatGui.Print($"XIM debug: serviceHolder={serviceHolder?.FullName ?? "null"}");

        var messageProcessor = serviceHolder == null
            ? null
            : GetStaticFieldOrProperty(serviceHolder, "MessageProcessor");

        Plugin.ChatGui.Print($"XIM debug: messageProcessor={messageProcessor?.GetType().FullName ?? "null"}");

        var chats = messageProcessor == null
            ? null
            : GetFieldOrProperty(messageProcessor, "Chats");

        Plugin.ChatGui.Print($"XIM debug: chats={chats?.GetType().FullName ?? "null"}");

        if (chats != null)
        {
            var count = TryGetCount(chats, out var chatCount)
                ? chatCount.ToString()
                : "unknown";

            Plugin.ChatGui.Print($"XIM debug: chatsCount={count}");
        }

        var historyCount = 0;
        var windowCount = 0;
        var openCount = 0;
        var inputCount = 0;
        var maxLengthReadableCount = 0;

        foreach (var history in GetMessageHistories())
        {
            historyCount++;

            Plugin.ChatGui.Print($"XIM debug: history[{historyCount}]={history.GetType().FullName}");

            var window = GetFieldOrProperty(history, "ChatWindow");
            if (window == null)
            {
                Plugin.ChatGui.Print("XIM debug:   ChatWindow=null/missing");
                DebugPrintLikelyMembers(history, "Window");
                continue;
            }

            windowCount++;

            var isOpenText = TryGetBool(window, "IsOpen", out var isOpen)
                ? isOpen.ToString()
                : "unreadable";

            if (isOpen)
                openCount++;

            Plugin.ChatGui.Print($"XIM debug:   window={window.GetType().FullName}, IsOpen={isOpenText}");

            var input = GetFieldOrProperty(window, "Input");
            if (input == null)
            {
                Plugin.ChatGui.Print("XIM debug:   Input=null/missing");
                DebugPrintLikelyMembers(window, "Input");
                continue;
            }

            inputCount++;

            var maxLengthText = TryGetInt(input, "MaxLength", out var maxLength)
                ? maxLength.ToString()
                : "unreadable";

            if (TryGetInt(input, "MaxLength", out _))
                maxLengthReadableCount++;

            Plugin.ChatGui.Print($"XIM debug:   input={input.GetType().FullName}, MaxLength={maxLengthText}");

            DebugPrintLikelyMembers(input, "Text");
            DebugPrintLikelyMembers(input, "Length");
        }

        Plugin.ChatGui.Print(
            $"XIM debug: histories={historyCount}, windows={windowCount}, open={openCount}, inputs={inputCount}, maxLengthReadable={maxLengthReadableCount}");
    }

    public void DebugPrintAssemblySearch()
    {
        if (!TryGetMessengerPlugin(out var messenger))
        {
            Plugin.ChatGui.PrintError("XIM debug: Messenger plugin not found.");
            return;
        }

        var assembly = messenger.GetType().Assembly;

        Plugin.ChatGui.Print($"XIM debug search: asm={assembly.GetName().Name}");

        foreach (var type in assembly.GetTypes())
        {
            var name = type.FullName ?? type.Name;

            if (!name.Contains("Message", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Chat", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Window", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".S", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Plugin.ChatGui.Print($"XIM type: {name}");

            foreach (var field in type.GetFields(Flags))
            {
                if (LooksRelevant(field.Name) || LooksRelevant(field.FieldType.FullName ?? field.FieldType.Name))
                    Plugin.ChatGui.Print($"  field {field.FieldType.Name} {field.Name}");
            }

            foreach (var property in type.GetProperties(Flags))
            {
                if (property.GetIndexParameters().Length > 0)
                    continue;

                if (LooksRelevant(property.Name) || LooksRelevant(property.PropertyType.FullName ?? property.PropertyType.Name))
                    Plugin.ChatGui.Print($"  prop {property.PropertyType.Name} {property.Name}");
            }
        }
    }

    private bool SetInputEnabled(object input, bool enabled)
    {
        var maxLengthField = input.GetType().GetField("MaxLength", Flags);
        var maxLengthProperty = input.GetType().GetProperty("MaxLength", Flags);

        if (maxLengthField == null && maxLengthProperty == null)
        {
            Plugin.Log.Warning($"[XivMessengerApi] SetInputEnabled: MaxLength field/property missing. Type={input.GetType().FullName}");
            return false;
        }

        if (!originalInputMaxLengths.ContainsKey(input))
        {
            var original = TryGetInt(input, "MaxLength", out var current)
                ? current
                : DefaultInputMaxLength;

            if (original <= 0)
                original = DefaultInputMaxLength;

            originalInputMaxLengths[input] = original;
        }

        try
        {
            if (enabled)
            {
                var restore = originalInputMaxLengths.TryGetValue(input, out var original)
                    ? original
                    : DefaultInputMaxLength;

                if (TrySetInt(input, "MaxLength", restore))
                {
                    Plugin.Log.Verbose($"[XivMessengerApi] Restored XIM input MaxLength={restore}.");
                    return true;
                }

                return false;
            }

            ClearInputText(input);

            if (TrySetInt(input, "MaxLength", 0))
            {
                Plugin.Log.Verbose("[XivMessengerApi] Disabled XIM input MaxLength=0.");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[XivMessengerApi] Failed to toggle XIM input.");
            return false;
        }
    }

    private static void ClearInputText(object input)
    {
        foreach (var name in new[] { "Text", "CurrentText", "InputText", "Message" })
        {
            var field = input.GetType().GetField(name, Flags);
            if (field != null && field.FieldType == typeof(string))
            {
                field.SetValue(input, string.Empty);
                return;
            }

            var property = input.GetType().GetProperty(name, Flags);
            if (property != null &&
                property.PropertyType == typeof(string) &&
                property.SetMethod != null)
            {
                property.SetValue(input, string.Empty);
                return;
            }
        }
    }

    private IEnumerable<object> GetChatWindows()
    {
        foreach (var history in GetMessageHistories())
        {
            var chatWindow = GetFieldOrProperty(history, "ChatWindow");

            if (chatWindow != null)
            {
                yield return chatWindow;
                continue;
            }

            // Fallback: if the field name changed, find likely window object.
            foreach (var memberValue in GetObjectMemberValues(history))
            {
                var typeName = memberValue.GetType().FullName ?? memberValue.GetType().Name;

                if (!typeName.Contains("Window", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (HasBoolMember(memberValue, "IsOpen"))
                    yield return memberValue;
            }
        }
    }

    private IEnumerable<object> GetInputs()
    {
        foreach (var chatWindow in GetChatWindows())
        {
            var input = GetFieldOrProperty(chatWindow, "Input");

            if (input != null)
            {
                yield return input;
                continue;
            }

            // Fallback: if the field name changed, find likely input object.
            foreach (var memberValue in GetObjectMemberValues(chatWindow))
            {
                var typeName = memberValue.GetType().FullName ?? memberValue.GetType().Name;

                if (typeName.Contains("Input", StringComparison.OrdinalIgnoreCase) ||
                    HasIntMember(memberValue, "MaxLength"))
                {
                    yield return memberValue;
                }
            }
        }
    }

    private IEnumerable<object> GetMessageHistories()
    {
        if (!TryGetMessengerPlugin(out var messenger))
        {
            Plugin.Log.Warning("[XivMessengerApi] Messenger plugin not found.");
            yield break;
        }

        var assembly = messenger.GetType().Assembly;

        var serviceHolder =
            assembly.GetType("Messenger.S") ??
            FindTypeWithStaticMember(assembly, "MessageProcessor");

        if (serviceHolder == null)
        {
            Plugin.Log.Warning("[XivMessengerApi] Could not find XIM service holder type with MessageProcessor.");
            yield break;
        }

        var messageProcessor = GetStaticFieldOrProperty(serviceHolder, "MessageProcessor");

        if (messageProcessor == null)
        {
            Plugin.Log.Warning($"[XivMessengerApi] {serviceHolder.FullName}.MessageProcessor is null or missing.");
            yield break;
        }

        var chats = GetFieldOrProperty(messageProcessor, "Chats");

        if (chats == null)
        {
            Plugin.Log.Warning($"[XivMessengerApi] Could not find Chats on {messageProcessor.GetType().FullName}.");
            yield break;
        }

        foreach (var value in EnumerateValues(chats))
        {
            if (value != null)
                yield return value;
        }
    }

    private bool TryGetMessengerPlugin(out object plugin)
    {
        if (cachedPlugin != null)
        {
            plugin = cachedPlugin;
            return true;
        }

        foreach (var name in new[]
                 {
                     "Messenger",
                     "XIV Instant Messenger",
                     "XIVInstantMessenger",
                 })
        {
            if (reflector.TryGetDalamudPlugin(
                    name,
                    out IDalamudPlugin? instance,
                    suppressErrors: true,
                    ignoreCache: true) &&
                instance != null)
            {
                cachedPlugin = instance;
                plugin = instance;

                Plugin.Log.Information($"[XivMessengerApi] Found XIM plugin via '{name}', type={instance.GetType().FullName}.");
                return true;
            }
        }

        plugin = null!;
        return false;
    }

    private static IEnumerable<object?> EnumerateValues(object collectionLike)
    {
        if (collectionLike is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
                yield return entry.Value;

            yield break;
        }

        var values = GetFieldOrProperty(collectionLike, "Values") as IEnumerable;

        if (values != null)
        {
            foreach (var value in values)
                yield return value;

            yield break;
        }

        if (collectionLike is IEnumerable enumerable && collectionLike is not string)
        {
            foreach (var value in enumerable)
                yield return value;
        }
    }

    private static Type? FindTypeWithStaticMember(Assembly assembly, string memberName)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.GetField(memberName, Flags) != null)
                return type;

            if (type.GetProperty(memberName, Flags) != null)
                return type;
        }

        return null;
    }

    private static object? GetStaticFieldOrProperty(Type type, string name)
    {
        try
        {
            var field = type.GetField(name, Flags);
            if (field != null)
                return field.GetValue(null);

            var property = type.GetProperty(name, Flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(null);
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static object? GetFieldOrProperty(object obj, string name)
    {
        var type = obj.GetType();

        try
        {
            var field = type.GetField(name, Flags);
            if (field != null)
                return field.GetValue(obj);

            var property = type.GetProperty(name, Flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(obj);
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static IEnumerable<object> GetObjectMemberValues(object obj)
    {
        var type = obj.GetType();

        foreach (var field in type.GetFields(Flags))
        {
            if (field.FieldType.IsValueType || field.FieldType == typeof(string))
                continue;

            object? value;

            try
            {
                value = field.GetValue(obj);
            }
            catch
            {
                continue;
            }

            if (value != null)
                yield return value;
        }

        foreach (var property in type.GetProperties(Flags))
        {
            if (property.GetIndexParameters().Length > 0)
                continue;

            if (property.PropertyType.IsValueType || property.PropertyType == typeof(string))
                continue;

            object? value;

            try
            {
                value = property.GetValue(obj);
            }
            catch
            {
                continue;
            }

            if (value != null)
                yield return value;
        }
    }

    private static bool TryGetBool(object obj, string name, out bool value)
    {
        value = false;

        var raw = GetFieldOrProperty(obj, name);
        if (raw is not bool boolValue)
            return false;

        value = boolValue;
        return true;
    }

    private static bool TrySetBool(object obj, string name, bool value)
    {
        var type = obj.GetType();

        try
        {
            var field = type.GetField(name, Flags);
            if (field != null && field.FieldType == typeof(bool) && !field.IsInitOnly)
            {
                field.SetValue(obj, value);
                return true;
            }

            var property = type.GetProperty(name, Flags);
            if (property != null &&
                property.PropertyType == typeof(bool) &&
                property.SetMethod != null)
            {
                property.SetValue(obj, value);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryGetInt(object obj, string name, out int value)
    {
        value = 0;

        var raw = GetFieldOrProperty(obj, name);
        if (raw is not int intValue)
            return false;

        value = intValue;
        return true;
    }

    private static bool TrySetInt(object obj, string name, int value)
    {
        var type = obj.GetType();

        try
        {
            var field = type.GetField(name, Flags);
            if (field != null && field.FieldType == typeof(int) && !field.IsInitOnly)
            {
                field.SetValue(obj, value);
                return true;
            }

            var property = type.GetProperty(name, Flags);
            if (property != null &&
                property.PropertyType == typeof(int) &&
                property.SetMethod != null)
            {
                property.SetValue(obj, value);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool HasBoolMember(object obj, string name)
    {
        return TryGetBool(obj, name, out _);
    }

    private static bool HasIntMember(object obj, string name)
    {
        return TryGetInt(obj, name, out _);
    }

    private static bool TryGetCount(object obj, out int count)
    {
        count = 0;

        var raw = GetFieldOrProperty(obj, "Count");

        if (raw is int intCount)
        {
            count = intCount;
            return true;
        }

        if (obj is ICollection collection)
        {
            count = collection.Count;
            return true;
        }

        return false;
    }

    private static void DebugPrintLikelyMembers(object obj, string filter)
    {
        var type = obj.GetType();

        foreach (var field in type.GetFields(Flags))
        {
            if (field.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                field.FieldType.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (field.FieldType.FullName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                Plugin.ChatGui.Print($"XIM debug:     field {field.FieldType.Name} {field.Name}");
            }
        }

        foreach (var property in type.GetProperties(Flags))
        {
            if (property.GetIndexParameters().Length > 0)
                continue;

            if (property.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                property.PropertyType.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (property.PropertyType.FullName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                Plugin.ChatGui.Print($"XIM debug:     prop {property.PropertyType.Name} {property.Name}");
            }
        }
    }

    private static bool LooksRelevant(string text)
    {
        return text.Contains("Message", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Chat", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Window", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Input", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Length", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Text", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();

        public new bool Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
