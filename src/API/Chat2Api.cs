using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace SayusGagExtender.API
{
    

    public sealed class Chat2Api
    {
        private readonly Plugin plugin;

        private Type? chatTwoPluginType;
        private Type? chatLogWindowType;
        private Type? configurationType;
        private Type? tabType;

        private object? chatTwoPlugin;
        private object? chatLogWindow;
        private object? config;

        private PropertyInfo? chatLogWindowProperty;
        private FieldInfo? configField;
        private PropertyInfo? lastTabProperty;
        private PropertyInfo? wantedTabProperty;
        private MethodInfo? saveConfigMethod;
        private MethodInfo? changeTabMethod;

        private PropertyInfo? lastWindowPosProperty;
        private PropertyInfo? lastWindowSizeProperty;
        private PropertyInfo? positionProperty;
        private PropertyInfo? sizeProperty;
        private PropertyInfo? positionConditionProperty;
        private PropertyInfo? sizeConditionProperty;

        private FieldInfo? tabsField;
        private FieldInfo? inputDisabledField;
        private FieldInfo? tabNameField;
        public sealed class Chat2Bounds
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }

            public Chat2Bounds()
            {
                X = 0f;
                Y = 0f;
                Width = 0f;
                Height = 0f;
            }

            public Chat2Bounds(float x, float y, float width, float height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }
        }

        public Chat2Api(Plugin plugin)
        {
            this.plugin = plugin;
        }

        public bool TryGetPositionAndSize(out Chat2Bounds? bounds)
        {
            bounds = null;

            try
            {
                Chat2Bounds? localBounds = null;

                var success = WithRefresh(() =>
                {
                    if (chatLogWindow == null)
                        return false;

                    var pos = ReadVector2(lastWindowPosProperty, chatLogWindow)
                              ?? ReadVector2(positionProperty, chatLogWindow)
                              ?? Vector2.Zero;

                    var size = ReadVector2(lastWindowSizeProperty, chatLogWindow)
                               ?? ReadVector2(sizeProperty, chatLogWindow)
                               ?? Vector2.Zero;

                    localBounds = new Chat2Bounds(pos.X, pos.Y, size.X, size.Y);
                    return IsValidBounds(localBounds);
                });

                if (success)
                    bounds = localBounds;

                return success;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Chat2Api.TryGetPositionAndSize failed: {ex}");
                ClearCache();
                return false;
            }
        }

        public bool SetPositionAndSize(Chat2Bounds bounds)
        {
            if (bounds == null)
            {
                Plugin.ChatGui.PrintError("Chat2Api: Refusing null Chat2 bounds.");
                return false;
            }

            if (!IsValidBounds(bounds))
            {
                Plugin.ChatGui.PrintError(
                    $"Chat2Api: Refusing invalid Chat2 bounds: " +
                    $"{bounds.X}, {bounds.Y}, {bounds.Width}, {bounds.Height}");

                return false;
            }

            if (bounds.X == 0f && bounds.Y == 0f && bounds.Width == 0f && bounds.Height == 0f)
            {
                Plugin.ChatGui.PrintError("No Chat2 bounds saved yet.");
                return false;
            }

            return SetPositionAndSize(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        public bool SetPositionAndSize(float x, float y, float width, float height)
        {
            var bounds = new Chat2Bounds(x, y, width, height);

            if (!IsValidBounds(bounds))
            {
                Plugin.ChatGui.PrintError(
                    $"Chat2Api: Refusing invalid Chat2 bounds: " +
                    $"{x}, {y}, {width}, {height}");

                return false;
            }

            try
            {
                return WithRefresh(() =>
                {
                    if (positionProperty == null ||
                        sizeProperty == null ||
                        chatLogWindow == null)
                        return false;

                    WriteVector2(positionProperty, chatLogWindow, new Vector2(x, y));
                    WriteVector2(sizeProperty, chatLogWindow, new Vector2(width, height));

                    // Force Chat2/Dalamud to apply it once.
                    // Important: do NOT leave these as Always, or the window becomes locked.
                    SetImGuiCond(positionConditionProperty, chatLogWindow, "Always");
                    SetImGuiCond(sizeConditionProperty, chatLogWindow, "Always");

                    ClearTemporaryPositionAndSizeSoon(chatLogWindow);

                    return true;
                });
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Chat2Api.SetPositionAndSize failed: {ex}");
                ClearCache();
                return false;
            }
        }

        public bool EnableInputInAllTabs()
            => SetInputDisabledForAllTabs(false);

        public bool DisableInputInAllTabs()
            => SetInputDisabledForAllTabs(true);

        public int GetActiveTab()
        {
            try
            {
                if (!RefreshReflection())
                    return -1;

                return lastTabProperty?.GetValue(chatTwoPlugin) is int index ? index : -1;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Chat2Api.GetActiveTab failed: {ex}");
                ClearCache();
                return -1;
            }
        }

        public string? GetActiveTabName()
        {
            try
            {
                if (!RefreshReflection())
                    return null;

                var index = GetActiveTab();
                var tabs = GetTabs();

                if (index < 0 || index >= tabs.Count)
                    return null;

                return tabNameField?.GetValue(tabs[index]) as string;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Chat2Api.GetActiveTabName failed: {ex}");
                ClearCache();
                return null;
            }
        }

        public bool SetActiveTab(int index)
        {
            try
            {
                return WithRefresh(() =>
                {
                    var tabs = GetTabs();

                    if (index < 0 || index >= tabs.Count)
                        return false;

                    if (changeTabMethod != null && chatLogWindow != null)
                    {
                        changeTabMethod.Invoke(chatLogWindow, new object[] { index });
                        return true;
                    }

                    wantedTabProperty?.SetValue(chatTwoPlugin, index);
                    return true;
                });
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Chat2Api.SetActiveTab failed: {ex}");
                ClearCache();
                return false;
            }
        }

        public bool SetActiveTab(string name)
        {
            try
            {
                if (!RefreshReflection())
                    return false;

                var tabs = GetTabs();

                for (var i = 0; i < tabs.Count; i++)
                {
                    var tabName = tabNameField?.GetValue(tabs[i]) as string;

                    if (string.Equals(tabName, name, StringComparison.OrdinalIgnoreCase))
                        return SetActiveTab(i);
                }

                return false;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Chat2Api.SetActiveTab(name) failed: {ex}");
                ClearCache();
                return false;
            }
        }

        private bool SetInputDisabledForAllTabs(bool disabled)
        {
            try
            {
                return WithRefresh(() =>
                {
                    var tabs = GetTabs();

                    if (inputDisabledField == null)
                        return false;

                    foreach (var tab in tabs)
                        inputDisabledField.SetValue(tab, disabled);

                    saveConfigMethod?.Invoke(chatTwoPlugin, null);
                    return true;
                });
            }
            catch (Exception ex)
            {
                //Plugin.ChatGui.PrintError($"Chat2Api.SetInputDisabledForAllTabs failed: {ex}");
                ClearCache();
                return false;
            }
        }

        private void ClearTemporaryPositionAndSizeSoon(object targetWindow)
        {
            _ = Task.Run(async () =>
            {
                // Give Chat2/Dalamud time to draw once with the forced position/size.
                await Task.Delay(250);

                try
                {
                    _ = Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        try
                        {
                            if (chatLogWindow == null)
                                return;

                            if (!ReferenceEquals(chatLogWindow, targetWindow))
                                return;

                            // Critical part:
                            // Clearing the condition alone is not enough.
                            // ImGuiCond.None still means "apply unconditionally".
                            // We must clear the actual nullable Position/Size values.
                            ClearVector2Property(positionProperty, chatLogWindow);
                            ClearVector2Property(sizeProperty, chatLogWindow);

                            // Optional cleanup. These do not matter once Position/Size are null,
                            // but reset them to Once instead of None to avoid accidental hard-locking.
                            SetImGuiCond(positionConditionProperty, chatLogWindow, "Once");
                            SetImGuiCond(sizeConditionProperty, chatLogWindow, "Once");
                        }
                        catch
                        {
                            // ignored
                        }
                    });
                }
                catch
                {
                    // ignored
                }
            });
        }
        private static void ClearVector2Property(PropertyInfo? property, object instance)
        {
            if (property == null)
                return;

            var type = property.PropertyType;
            var nullableType = Nullable.GetUnderlyingType(type);

            try
            {
                if (nullableType == typeof(Vector2))
                {
                    property.SetValue(instance, null);
                    return;
                }

                // If Dalamud ever exposes these as non-nullable Vector2,
                // we cannot safely clear them. Setting Vector2.Zero would be worse.
            }
            catch
            {
                // ignored
            }
        }

        private bool WithRefresh(Func<bool> action)
        {
            // Always refresh first.
            // Chat2 can restart independently from us, and old cached objects may still look "valid".
            if (!RefreshReflection())
                return false;

            if (action())
                return true;

            // Retry once in case Chat2 restarted mid-call.
            if (!RefreshReflection())
                return false;

            return action();
        }
        private bool RefreshReflection()
        {
            ClearCache();
            return TryCacheReflection();
        }

        private bool TryCacheReflection()
        {
            ClearCache();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var candidatePluginType = asm.GetType("ChatTwo.Plugin", throwOnError: false);

                if (candidatePluginType == null)
                    continue;

                var candidateChatLogType = asm.GetType("ChatTwo.Ui.ChatLogWindow", throwOnError: false);
                var candidateConfigType = asm.GetType("ChatTwo.Configuration", throwOnError: false);
                var candidateTabType = asm.GetType("ChatTwo.Tab", throwOnError: false);

                if (candidateChatLogType == null ||
                    candidateConfigType == null ||
                    candidateTabType == null)
                    continue;

                var candidateConfigField = candidatePluginType.GetField(
                    "Config",
                    BindingFlags.Public | BindingFlags.Static);

                if (candidateConfigField == null)
                    continue;

                var candidateConfig = candidateConfigField.GetValue(null);

                if (candidateConfig == null)
                    continue;

                var candidatePlugin = TryFindLiveChatTwoPlugin(candidatePluginType);

                if (candidatePlugin == null)
                    continue;

                var candidateChatLogProperty = candidatePluginType.GetProperty(
                    "ChatLogWindow",
                    BindingFlags.Public | BindingFlags.Instance);

                var candidateChatLog = candidateChatLogProperty?.GetValue(candidatePlugin);

                if (candidateChatLog == null)
                    continue;

                var candidateTabsField = candidateConfigType.GetField(
                    "Tabs",
                    BindingFlags.Public | BindingFlags.Instance);

                var candidateInputDisabledField = candidateTabType.GetField(
                    "InputDisabled",
                    BindingFlags.Public | BindingFlags.Instance);

                var candidateTabNameField = candidateTabType.GetField(
                    "Name",
                    BindingFlags.Public | BindingFlags.Instance);

                if (candidateTabsField == null ||
                    candidateInputDisabledField == null ||
                    candidateTabNameField == null)
                    continue;

                chatTwoPluginType = candidatePluginType;
                chatLogWindowType = candidateChatLogType;
                configurationType = candidateConfigType;
                tabType = candidateTabType;

                chatTwoPlugin = candidatePlugin;
                chatLogWindow = candidateChatLog;
                config = candidateConfig;

                chatLogWindowProperty = candidateChatLogProperty;
                configField = candidateConfigField;

                lastTabProperty = candidatePluginType.GetProperty(
                    "LastTab",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                wantedTabProperty = candidatePluginType.GetProperty(
                    "WantedTab",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                saveConfigMethod = candidatePluginType.GetMethod(
                    "SaveConfig",
                    BindingFlags.Public | BindingFlags.Instance);

                changeTabMethod = candidateChatLogType.GetMethod(
                    "ChangeTab",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                lastWindowPosProperty = candidateChatLogType.GetProperty(
                    "LastWindowPos",
                    BindingFlags.Public | BindingFlags.Instance);

                lastWindowSizeProperty = candidateChatLogType.GetProperty(
                    "LastWindowSize",
                    BindingFlags.Public | BindingFlags.Instance);

                positionProperty = candidateChatLogType.GetProperty(
                    "Position",
                    BindingFlags.Public | BindingFlags.Instance);

                sizeProperty = candidateChatLogType.GetProperty(
                    "Size",
                    BindingFlags.Public | BindingFlags.Instance);

                positionConditionProperty = candidateChatLogType.GetProperty(
                    "PositionCondition",
                    BindingFlags.Public | BindingFlags.Instance);

                sizeConditionProperty = candidateChatLogType.GetProperty(
                    "SizeCondition",
                    BindingFlags.Public | BindingFlags.Instance);

                tabsField = candidateTabsField;
                inputDisabledField = candidateInputDisabledField;
                tabNameField = candidateTabNameField;

                return true;
            }

            //Plugin.ChatGui.PrintError("Chat2Api: No live Chat2 instance found.");
            return false;
        }


        private void ClearCache()
        {
            chatTwoPluginType = null;
            chatLogWindowType = null;
            configurationType = null;
            tabType = null;

            chatTwoPlugin = null;
            chatLogWindow = null;
            config = null;

            chatLogWindowProperty = null;
            configField = null;
            lastTabProperty = null;
            wantedTabProperty = null;
            saveConfigMethod = null;
            changeTabMethod = null;

            lastWindowPosProperty = null;
            lastWindowSizeProperty = null;
            positionProperty = null;
            sizeProperty = null;
            positionConditionProperty = null;
            sizeConditionProperty = null;

            tabsField = null;
            inputDisabledField = null;
            tabNameField = null;
        }

        private IList GetTabs()
        {
            if (tabsField?.GetValue(config) is IList tabs)
                return tabs;

            throw new InvalidOperationException("Could not read Chat2 Config.Tabs.");
        }

        private object? TryFindLiveChatTwoPlugin(Type candidatePluginType)
        {
            foreach (var field in candidatePluginType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (!candidatePluginType.IsAssignableFrom(field.FieldType))
                    continue;

                var value = SafeGetField(field, null);

                if (value != null)
                    return value;
            }

            foreach (var property in candidatePluginType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;

                if (!candidatePluginType.IsAssignableFrom(property.PropertyType))
                    continue;

                var value = SafeGetProperty(property, null);

                if (value != null)
                    return value;
            }

            var pluginInterface = GetDalamudPluginInterfaceFromYourPlugin();

            if (pluginInterface == null)
            {
                Plugin.ChatGui.PrintError("Chat2Api: Could not find IDalamudPluginInterface on your Plugin.");
                return null;
            }

            var installedPluginsProperty = pluginInterface.GetType().GetProperty(
                "InstalledPlugins",
                BindingFlags.Public | BindingFlags.Instance);

            if (installedPluginsProperty?.GetValue(pluginInterface) is not IEnumerable installedPlugins)
            {
                Plugin.ChatGui.PrintError("Chat2Api: Could not read PluginInterface.InstalledPlugins.");
                return null;
            }

            foreach (var installedPluginEntry in installedPlugins)
            {
                if (!IsProbablyChatTwoPluginEntry(installedPluginEntry))
                    continue;

                var instance = TryGetPluginInstanceFromInstalledPluginEntry(installedPluginEntry, candidatePluginType);

                if (instance != null)
                    return instance;

                //Plugin.ChatGui.PrintError($"Chat2Api: Found Chat2 entry, but could not read live instance. Entry type: {installedPluginEntry.GetType().FullName}");
            }

            return null;
        }

        private object? GetDalamudPluginInterfaceFromYourPlugin()
        {
            var pluginType = plugin.GetType();

            foreach (var field in pluginType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var value = SafeGetField(field, plugin);

                if (LooksLikePluginInterface(value))
                    return value;
            }

            foreach (var property in pluginType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;

                var value = SafeGetProperty(property, plugin);

                if (LooksLikePluginInterface(value))
                    return value;
            }

            foreach (var field in pluginType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                var value = SafeGetField(field, null);

                if (LooksLikePluginInterface(value))
                    return value;
            }

            foreach (var property in pluginType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;

                var value = SafeGetProperty(property, null);

                if (LooksLikePluginInterface(value))
                    return value;
            }

            return null;
        }

        private static bool LooksLikePluginInterface(object? value)
        {
            if (value == null)
                return false;

            return value.GetType().GetProperty(
                "InstalledPlugins",
                BindingFlags.Public | BindingFlags.Instance) != null;
        }

        private static bool IsProbablyChatTwoPluginEntry(object installedPluginEntry)
        {
            if (EntryHasChatTwoName(installedPluginEntry))
                return true;

            var manifest = GetNamedMemberValue(installedPluginEntry, "Manifest");

            return manifest != null && EntryHasChatTwoName(manifest);
        }

        private static bool EntryHasChatTwoName(object value)
        {
            foreach (var name in new[]
            {
                "InternalName",
                "Name",
                "EffectiveName",
                "PluginInternalName"
            })
            {
                var memberValue = GetNamedMemberValue(value, name) as string;

                if (IsChatTwoName(memberValue))
                    return true;
            }

            return false;
        }

        private static bool IsChatTwoName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Equals("ChatTwo", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("Chat 2", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("Chat2", StringComparison.OrdinalIgnoreCase)
                   || value.Contains("ChatTwo", StringComparison.OrdinalIgnoreCase);
        }

        private static object? TryGetPluginInstanceFromInstalledPluginEntry(
            object installedPluginEntry,
            Type wantedPluginType)
        {
            foreach (var name in new[]
            {
                "Instance",
                "Plugin",
                "PluginInstance",
                "LoadedPlugin",
                "LocalPlugin",
                "PluginObject",
                "DalamudPlugin",
                "PluginBase",
                "Loader"
            })
            {
                var value = GetNamedMemberValue(installedPluginEntry, name);

                if (value == null)
                    continue;

                if (wantedPluginType.IsInstanceOfType(value))
                    return value;

                var nested = TryGetDirectPluginInstance(value, wantedPluginType);

                if (nested != null)
                    return nested;
            }

            var direct = TryGetDirectPluginInstance(installedPluginEntry, wantedPluginType);

            if (direct != null)
                return direct;

            return FieldOnlyFindInstance(installedPluginEntry, wantedPluginType, maxDepth: 8, maxNodes: 2048);
        }

        private static object? FieldOnlyFindInstance(
            object root,
            Type wantedPluginType,
            int maxDepth,
            int maxNodes)
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var queue = new Queue<(object Obj, int Depth)>();

            queue.Enqueue((root, 0));

            var visitedNodes = 0;

            while (queue.Count > 0)
            {
                if (++visitedNodes > maxNodes)
                    return null;

                var (current, depth) = queue.Dequeue();

                if (wantedPluginType.IsInstanceOfType(current))
                    return current;

                if (depth >= maxDepth)
                    continue;

                if (!seen.Add(current))
                    continue;

                var currentType = current.GetType();

                if (ShouldSkipFieldScanType(currentType))
                    continue;

                FieldInfo[] fields;

                try
                {
                    fields = currentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                catch
                {
                    continue;
                }

                foreach (var field in fields)
                {
                    if (ShouldSkipFieldScanType(field.FieldType))
                        continue;

                    object? value;

                    try
                    {
                        value = field.GetValue(current);
                    }
                    catch
                    {
                        continue;
                    }

                    if (value == null)
                        continue;

                    if (wantedPluginType.IsInstanceOfType(value))
                        return value;

                    var valueType = value.GetType();

                    if (ShouldSkipFieldScanType(valueType))
                        continue;

                    queue.Enqueue((value, depth + 1));
                }
            }

            return null;
        }

        private static bool ShouldSkipFieldScanType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type.IsPrimitive ||
                type.IsEnum ||
                type == typeof(string) ||
                type == typeof(decimal) ||
                type == typeof(DateTime) ||
                type == typeof(TimeSpan) ||
                type == typeof(Guid) ||
                type == typeof(IntPtr) ||
                type == typeof(UIntPtr))
                return true;

            if (typeof(Delegate).IsAssignableFrom(type))
                return true;

            if (typeof(Assembly).IsAssignableFrom(type))
                return true;

            if (typeof(MemberInfo).IsAssignableFrom(type))
                return true;

            if (typeof(Type).IsAssignableFrom(type))
                return true;

            if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(byte[]))
                return true;

            return false;
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();

            private ReferenceEqualityComparer()
            {
            }

            public new bool Equals(object? x, object? y)
                => ReferenceEquals(x, y);

            public int GetHashCode(object obj)
                => RuntimeHelpers.GetHashCode(obj);
        }

        private static object? TryGetDirectPluginInstance(object wrapper, Type wantedPluginType)
        {
            var type = wrapper.GetType();

            foreach (var name in new[]
            {
                "Instance",
                "Plugin",
                "PluginInstance",
                "LoadedPlugin",
                "PluginObject",
                "DalamudPlugin"
            })
            {
                var value = GetNamedMemberValue(wrapper, name);

                if (value != null && wantedPluginType.IsInstanceOfType(value))
                    return value;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;

                if (!wantedPluginType.IsAssignableFrom(property.PropertyType))
                    continue;

                var value = SafeGetProperty(property, wrapper);

                if (value != null)
                    return value;
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!wantedPluginType.IsAssignableFrom(field.FieldType))
                    continue;

                var value = SafeGetField(field, wrapper);

                if (value != null)
                    return value;
            }

            return null;
        }

        private static object? GetNamedMemberValue(object instance, string name)
        {
            var type = instance.GetType();

            var property = type.GetProperty(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (property != null && property.GetIndexParameters().Length == 0)
                return SafeGetProperty(property, instance);

            var field = type.GetField(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (field != null)
                return SafeGetField(field, instance);

            return null;
        }

        private static bool IsValidBounds(Chat2Bounds? bounds)
        {
            if (bounds == null)
                return false;

            if (!float.IsFinite(bounds.X) ||
                !float.IsFinite(bounds.Y) ||
                !float.IsFinite(bounds.Width) ||
                !float.IsFinite(bounds.Height))
                return false;

            if (bounds.Width < 100 || bounds.Height < 80)
                return false;

            if (bounds.Width > 10000 || bounds.Height > 10000)
                return false;

            return true;
        }

        private static Vector2? ReadVector2(PropertyInfo? property, object? instance)
        {
            if (property == null || instance == null)
                return null;

            var value = property.GetValue(instance);

            return value switch
            {
                Vector2 vec => vec,
                _ => null,
            };
        }

        private static void WriteVector2(PropertyInfo property, object instance, Vector2 value)
        {
            var type = property.PropertyType;
            var nullableType = Nullable.GetUnderlyingType(type);

            if (type == typeof(Vector2) || nullableType == typeof(Vector2))
                property.SetValue(instance, value);
        }

        private static void SetImGuiCond(PropertyInfo? property, object instance, string valueName)
        {
            if (property == null)
                return;

            var enumType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (!enumType.IsEnum)
                return;

            try
            {
                object value;

                if (Enum.IsDefined(enumType, valueName))
                {
                    value = Enum.Parse(enumType, valueName);
                }
                else if (valueName == "None")
                {
                    value = Enum.ToObject(enumType, 0);
                }
                else
                {
                    return;
                }

                property.SetValue(instance, value);
            }
            catch
            {
                // Different Dalamud/ImGui.NET versions may expose this differently.
            }
        }

        private static object? SafeGetField(FieldInfo field, object? instance)
        {
            try
            {
                return field.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

        private static object? SafeGetProperty(PropertyInfo property, object? instance)
        {
            try
            {
                return property.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }
    }
}
