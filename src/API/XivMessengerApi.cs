using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SayusGagExtender.API;

public sealed class XivMessengerApi : IDisposable
{
    private const string InternalName = "Messenger";

    private readonly DalamudReflector reflector;

    private object? cachedPlugin;

    private static readonly BindingFlags Flags =
        BindingFlags.Instance |
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
        reflector.Dispose();
    }

    public bool IsWindowOpen()
    {
        if (!TryGetMessengerPlugin(out var messenger))
            return false;

        foreach (var entry in WalkObjectGraph(messenger, maxDepth: 6))
        {
            if (TryReadWindowOpen(entry.Value, entry.Path, out var isOpen) && isOpen)
                return true;
        }

        return false;
    }

    public bool CloseWindow()
    {
        if (!TryGetMessengerPlugin(out var messenger))
            return false;

        var changed = false;

        foreach (var entry in WalkObjectGraph(messenger, maxDepth: 6))
            changed |= TryCloseWindow(entry.Value, entry.Path);

        return changed;
    }

    public bool IsTextInputEnabled()
    {
        return TryIsTextInputEnabled(out var enabled) && enabled;
    }

    public bool TryIsTextInputEnabled(out bool enabled)
    {
        enabled = false;

        if (!TryGetMessengerPlugin(out var messenger))
            return false;

        foreach (var entry in WalkObjectGraph(messenger, maxDepth: 6))
        {
            if (TryReadTextInputEnabled(entry.Value, entry.Path, out enabled))
                return true;
        }

        return false;
    }

    public bool ToggleTextInput(bool enabled)
    {
        if (!TryGetMessengerPlugin(out var messenger))
            return false;

        if (TryInvokeTextInputSetter(messenger, enabled))
            return true;

        foreach (var entry in WalkObjectGraph(messenger, maxDepth: 6))
        {
            if (TrySetTextInputEnabled(entry.Value, entry.Path, enabled))
            {
                TrySaveConfig(messenger);
                return true;
            }
        }

        return false;
    }

    private bool TryGetMessengerPlugin(out object plugin)
    {
        if (cachedPlugin != null)
        {
            plugin = cachedPlugin;
            return true;
        }

        if (reflector.TryGetDalamudPlugin(
                InternalName,
                out IDalamudPlugin? instance,
                suppressErrors: true,
                ignoreCache: false) &&
            instance != null)
        {
            cachedPlugin = instance;
            plugin = instance;
            return true;
        }

        plugin = null!;
        return false;
    }

    private static bool TryReadWindowOpen(object obj, string path, out bool isOpen)
    {
        isOpen = false;

        if (obj is Window window)
        {
            isOpen = window.IsOpen;
            return true;
        }

        var type = obj.GetType();

        if (!LooksLikeWindow(type, path))
            return false;

        foreach (var member in GetBoolMembers(type))
        {
            if (!string.Equals(member.Name, "IsOpen", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(member.Name, "Open", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(member.Name, "Visible", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(member.Name, "IsVisible", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryGetBool(member, obj, out isOpen))
                return true;
        }

        return false;
    }

    private static bool TryCloseWindow(object obj, string path)
    {
        if (obj is Window window)
        {
            if (!window.IsOpen)
                return false;

            window.IsOpen = false;
            return true;
        }

        var type = obj.GetType();

        if (!LooksLikeWindow(type, path))
            return false;

        var changed = false;

        foreach (var member in GetBoolMembers(type))
        {
            if (!CanSet(member))
                continue;

            if (!string.Equals(member.Name, "IsOpen", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(member.Name, "Open", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(member.Name, "Visible", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(member.Name, "IsVisible", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryGetBool(member, obj, out var current) && current)
            {
                changed |= TrySetBool(member, obj, false);
            }
        }

        TryInvokeNoArg(obj, "Close");
        TryInvokeNoArg(obj, "Hide");

        return changed;
    }

    private static bool TryReadTextInputEnabled(object obj, string path, out bool enabled)
    {
        enabled = false;

        foreach (var member in GetBoolMembers(obj.GetType()))
        {
            if (!LooksLikeTextInputMember(member.Name, obj.GetType(), path))
                continue;

            if (!TryGetBool(member, obj, out var raw))
                continue;

            enabled = IsNegativeName(member.Name) ? !raw : raw;
            return true;
        }

        return false;
    }

    private static bool TrySetTextInputEnabled(object obj, string path, bool enabled)
    {
        foreach (var member in GetBoolMembers(obj.GetType()))
        {
            if (!CanSet(member))
                continue;

            if (!LooksLikeTextInputMember(member.Name, obj.GetType(), path))
                continue;

            var raw = IsNegativeName(member.Name) ? !enabled : enabled;

            if (TrySetBool(member, obj, raw))
                return true;
        }

        return false;
    }

    private static bool TryInvokeTextInputSetter(object obj, bool enabled)
    {
        foreach (var methodName in new[]
                 {
                     "SetTextInputEnabled",
                     "SetInputEnabled",
                     "ToggleTextInput",
                     "ToggleInput",
                     "SetTextBoxEnabled",
                 })
        {
            var method = obj.GetType().GetMethod(methodName, Flags);

            if (method == null)
                continue;

            var parameters = method.GetParameters();

            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(bool))
                continue;

            try
            {
                method.Invoke(obj, [enabled]);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static void TrySaveConfig(object obj)
    {
        foreach (var methodName in new[]
                 {
                     "Save",
                     "SaveConfig",
                     "SaveConfiguration",
                 })
        {
            if (TryInvokeNoArg(obj, methodName))
                return;
        }

        foreach (var entry in WalkObjectGraph(obj, maxDepth: 3))
        {
            if (entry.Value.GetType().Name.Contains("Config", StringComparison.OrdinalIgnoreCase) &&
                TryInvokeNoArg(entry.Value, "Save"))
            {
                return;
            }
        }
    }

    private static bool TryInvokeNoArg(object obj, string methodName)
    {
        var method = obj.GetType().GetMethod(methodName, Flags);

        if (method == null || method.GetParameters().Length != 0)
            return false;

        try
        {
            method.Invoke(obj, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeWindow(Type type, string path)
    {
        return type.Name.Contains("Window", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("Window", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("Windows", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTextInputMember(string memberName, Type ownerType, string path)
    {
        if (memberName.Contains("TextInput", StringComparison.OrdinalIgnoreCase) ||
            memberName.Contains("TextBox", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!memberName.Contains("Input", StringComparison.OrdinalIgnoreCase))
            return false;

        return ownerType.Name.Contains("Config", StringComparison.OrdinalIgnoreCase) ||
               ownerType.Name.Contains("Setting", StringComparison.OrdinalIgnoreCase) ||
               ownerType.Name.Contains("Messenger", StringComparison.OrdinalIgnoreCase) ||
               ownerType.Name.Contains("Window", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("Config", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("Setting", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNegativeName(string name)
    {
        return name.Contains("Disable", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Disabled", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Block", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Blocked", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Lock", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Locked", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<MemberInfo> GetBoolMembers(Type type)
    {
        foreach (var property in type.GetProperties(Flags))
        {
            if (property.GetIndexParameters().Length == 0 &&
                property.PropertyType == typeof(bool))
            {
                yield return property;
            }
        }

        foreach (var field in type.GetFields(Flags))
        {
            if (field.FieldType == typeof(bool))
                yield return field;
        }
    }

    private static bool CanSet(MemberInfo member)
    {
        return member switch
        {
            PropertyInfo property => property.SetMethod != null,
            FieldInfo field => !field.IsInitOnly,
            _ => false,
        };
    }

    private static bool TryGetBool(MemberInfo member, object obj, out bool value)
    {
        value = false;

        try
        {
            switch (member)
            {
                case PropertyInfo property:
                    value = (bool)property.GetValue(obj)!;
                    return true;

                case FieldInfo field:
                    value = (bool)field.GetValue(obj)!;
                    return true;

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetBool(MemberInfo member, object obj, bool value)
    {
        try
        {
            switch (member)
            {
                case PropertyInfo property:
                    property.SetValue(obj, value);
                    return true;

                case FieldInfo field:
                    field.SetValue(obj, value);
                    return true;

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<(object Value, string Path)> WalkObjectGraph(object root, int maxDepth)
    {
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        var queue = new Queue<(object Value, string Path, int Depth)>();

        queue.Enqueue((root, root.GetType().Name, 0));
        visited.Add(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return (current.Value, current.Path);

            if (current.Depth >= maxDepth)
                continue;

            var type = current.Value.GetType();

            if (ShouldSkipType(type))
                continue;

            if (current.Value is IEnumerable enumerable && current.Value is not string)
            {
                var count = 0;

                foreach (var item in enumerable)
                {
                    if (item == null)
                        continue;

                    if (++count > 200)
                        break;

                    if (ShouldSkipType(item.GetType()))
                        continue;

                    if (visited.Add(item))
                        queue.Enqueue((item, $"{current.Path}[]", current.Depth + 1));
                }
            }

            foreach (var member in GetObjectMembers(type))
            {
                object? value;

                try
                {
                    value = member switch
                    {
                        PropertyInfo property => property.GetValue(current.Value),
                        FieldInfo field => field.GetValue(current.Value),
                        _ => null,
                    };
                }
                catch
                {
                    continue;
                }

                if (value == null)
                    continue;

                if (ShouldSkipType(value.GetType()))
                    continue;

                if (visited.Add(value))
                    queue.Enqueue((value, $"{current.Path}.{member.Name}", current.Depth + 1));
            }
        }
    }

    private static IEnumerable<MemberInfo> GetObjectMembers(Type type)
    {
        foreach (var property in type.GetProperties(Flags))
        {
            if (property.GetIndexParameters().Length == 0 &&
                property.PropertyType != typeof(string) &&
                !property.PropertyType.IsValueType)
            {
                yield return property;
            }
        }

        foreach (var field in type.GetFields(Flags))
        {
            if (field.FieldType != typeof(string) &&
                !field.FieldType.IsValueType)
            {
                yield return field;
            }
        }
    }

    private static bool ShouldSkipType(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            return true;

        var ns = type.Namespace ?? string.Empty;

        return ns.StartsWith("System.Reflection", StringComparison.OrdinalIgnoreCase) ||
               ns.StartsWith("System.Runtime", StringComparison.OrdinalIgnoreCase) ||
               ns.StartsWith("System.Threading", StringComparison.OrdinalIgnoreCase);
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
