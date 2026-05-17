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

        private const BindingFlags AllInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private const BindingFlags AllStatic =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private Type? chatTwoPluginType;
        private Type? chatLogWindowType;
        private Type? configurationType;
        private Type? tabType;

        private object? chatTwoPlugin;
        private object? chatLogWindow;
        private object? config;

        private MemberInfo? chatLogWindowMember;
        private MemberInfo? configMember;
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

        private MemberInfo? tabsMember;
        private MemberInfo? inputDisabledMember;
        private MemberInfo? tabNameMember;

        private string? lastFailure;

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

            public override string ToString()
                => $"{X}, {Y}, {Width}, {Height}";
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

                var success = WithRefresh("TryGetPositionAndSize", () =>
                {
                    if (chatLogWindow == null)
                    {
                        lastFailure = "ChatLog window object was not found.";
                        return false;
                    }

                    var pos = ReadVector2(lastWindowPosProperty, chatLogWindow)
                              ?? ReadVector2(positionProperty, chatLogWindow)
                              ?? Vector2.Zero;

                    var size = ReadVector2(lastWindowSizeProperty, chatLogWindow)
                               ?? ReadVector2(sizeProperty, chatLogWindow)
                               ?? Vector2.Zero;

                    localBounds = new Chat2Bounds(pos.X, pos.Y, size.X, size.Y);

                    if (!IsValidBounds(localBounds))
                    {
                        lastFailure = $"Read invalid Chat2 bounds: {localBounds}";
                        return false;
                    }

                    return true;
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
                Plugin.ChatGui.PrintError($"Chat2Api: Refusing invalid Chat2 bounds: {bounds}");
                return false;
            }

            if (bounds.X == 0f && bounds.Y == 0f && bounds.Width == 0f && bounds.Height == 0f)
            {
                Plugin.ChatGui.PrintError("Chat2Api: No Chat2 bounds saved yet.");
                return false;
            }

            return SetPositionAndSize(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        public bool SetPositionAndSize(float x, float y, float width, float height)
        {
            var bounds = new Chat2Bounds(x, y, width, height);

            if (!IsValidBounds(bounds))
            {
                Plugin.ChatGui.PrintError($"Chat2Api: Refusing invalid Chat2 bounds: {bounds}");
                return false;
            }

            try
            {
                return WithRefresh("SetPositionAndSize", () =>
                {
                    if (chatLogWindow == null)
                    {
                        lastFailure = "ChatLog window object was not found.";
                        return false;
                    }

                    if (positionProperty == null)
                    {
                        lastFailure = "ChatLog Position property was not found.";
                        return false;
                    }

                    if (sizeProperty == null)
                    {
                        lastFailure = "ChatLog Size property was not found.";
                        return false;
                    }

                    if (!WriteVector2(positionProperty, chatLogWindow, new Vector2(x, y)))
                    {
                        lastFailure = $"Could not write Position. Property type: {positionProperty.PropertyType.FullName}";
                        return false;
                    }

                    if (!WriteVector2(sizeProperty, chatLogWindow, new Vector2(width, height)))
                    {
                        lastFailure = $"Could not write Size. Property type: {sizeProperty.PropertyType.FullName}";
                        return false;
                    }

                    // Force ChatTwo/Dalamud to apply these on the next draw.
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
                {
                    PrintLastFailure("GetActiveTab");
                    return -1;
                }

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
                {
                    PrintLastFailure("GetActiveTabName");
                    return null;
                }

                var index = GetActiveTab();
                var tabs = GetTabs();

                if (index < 0 || index >= tabs.Count)
                    return null;

                return GetMemberValue(tabNameMember, tabs[index]) as string;
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
                return WithRefresh("SetActiveTab", () =>
                {
                    var tabs = GetTabs();

                    if (index < 0 || index >= tabs.Count)
                    {
                        lastFailure = $"Tab index {index} is out of range. Tab count: {tabs.Count}";
                        return false;
                    }

                    if (changeTabMethod != null && chatLogWindow != null)
                    {
                        changeTabMethod.Invoke(chatLogWindow, new object[] { index });
                        return true;
                    }

                    if (wantedTabProperty != null && chatTwoPlugin != null)
                    {
                        wantedTabProperty.SetValue(chatTwoPlugin, index);
                        return true;
                    }

                    lastFailure = "Neither ChatLog.ChangeTab nor Plugin.WantedTab was available.";
                    return false;
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
                {
                    PrintLastFailure("SetActiveTab(name)");
                    return false;
                }

                var tabs = GetTabs();

                for (var i = 0; i < tabs.Count; i++)
                {
                    var tabName = GetMemberValue(tabNameMember, tabs[i]) as string;

                    if (string.Equals(tabName, name, StringComparison.OrdinalIgnoreCase))
                        return SetActiveTab(i);
                }

                Plugin.ChatGui.PrintError($"Chat2Api.SetActiveTab: No Chat2 tab named \"{name}\" was found.");
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
                return WithRefresh("SetInputDisabledForAllTabs", () =>
                {
                    var tabs = GetTabs();

                    if (inputDisabledMember == null)
                    {
                        lastFailure = "Tab.InputDisabled member was not found.";
                        return false;
                    }

                    foreach (var tab in tabs)
                        SetMemberValue(inputDisabledMember, tab, disabled);

                    saveConfigMethod?.Invoke(chatTwoPlugin, null);
                    return true;
                });
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Chat2Api.SetInputDisabledForAllTabs failed: {ex}");
                ClearCache();
                return false;
            }
        }

        private void ClearTemporaryPositionAndSizeSoon(object targetWindow)
        {
            _ = Task.Run(async () =>
            {
                // Let ChatTwo/Dalamud draw once with the forced position/size.
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

                            // ChatTwo currently clears Position itself during DrawChatLog,
                            // but doing this here too keeps the API safe across versions.
                            ClearVector2Property(positionProperty, chatLogWindow);
                            ClearVector2Property(sizeProperty, chatLogWindow);

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

        private bool WithRefresh(string operation, Func<bool> action)
        {
            lastFailure = null;

            if (!RefreshReflection())
            {
                PrintLastFailure(operation);
                return false;
            }

            if (action())
                return true;

            var firstFailure = lastFailure;

            // Retry once in case ChatTwo restarted mid-call.
            if (!RefreshReflection())
            {
                if (lastFailure == null)
                    lastFailure = firstFailure;

                PrintLastFailure(operation);
                return false;
            }

            if (action())
                return true;

            if (lastFailure == null)
                lastFailure = firstFailure ?? "Action returned false.";

            PrintLastFailure(operation);
            return false;
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

                var candidateChatLogType =
                    asm.GetType("ChatTwo.Ui.ChatLog.ChatLog", throwOnError: false)
                    ?? asm.GetType("ChatTwo.Ui.ChatLogWindow", throwOnError: false);

                var candidateConfigType = asm.GetType("ChatTwo.Configuration", throwOnError: false);
                var candidateTabType = asm.GetType("ChatTwo.Tab", throwOnError: false);

                if (candidateChatLogType == null)
                {
                    lastFailure = "Found ChatTwo.Plugin, but could not find ChatTwo.Ui.ChatLog.ChatLog or old ChatTwo.Ui.ChatLogWindow.";
                    continue;
                }

                if (candidateConfigType == null)
                {
                    lastFailure = "Found ChatTwo.Plugin, but could not find ChatTwo.Configuration.";
                    continue;
                }

                if (candidateTabType == null)
                {
                    lastFailure = "Found ChatTwo.Plugin, but could not find ChatTwo.Tab.";
                    continue;
                }

                var candidateConfigMember =
                    FindMember(candidatePluginType, "Config", AllStatic)
                    ?? FindMember(candidatePluginType, "Configuration", AllStatic);

                if (candidateConfigMember == null)
                {
                    lastFailure = "Found ChatTwo.Plugin, but could not find static Config member.";
                    continue;
                }

                var candidateConfig = GetMemberValue(candidateConfigMember, null);
                if (candidateConfig == null)
                {
                    lastFailure = "Found ChatTwo.Config member, but its value was null.";
                    continue;
                }

                var candidatePlugin = TryFindLiveChatTwoPlugin(candidatePluginType);
                var candidateChatLogMember =
                    FindMember(candidatePluginType, "ChatLog", AllInstance)
                    ?? FindMember(candidatePluginType, "ChatLogWindow", AllInstance);

                object? candidateChatLog = null;

                if (candidatePlugin != null && candidateChatLogMember != null)
                    candidateChatLog = GetMemberValue(candidateChatLogMember, candidatePlugin);

                // Fallback: if the plugin instance cannot be found, try to find the live
                // ChatLog object directly inside Dalamud's installed plugin entry graph.
                candidateChatLog ??= TryFindLiveObjectFromInstalledPlugins(
                    candidateChatLogType,
                    installedPluginEntry => IsProbablyChatTwoPluginEntry(installedPluginEntry));

                if (candidateChatLog == null)
                {
                    lastFailure = "Found ChatTwo types and config, but could not find the live ChatLog object.";
                    continue;
                }

                var candidateTabsMember =
                    FindMember(candidateConfigType, "Tabs", AllInstance);

                var candidateInputDisabledMember =
                    FindMember(candidateTabType, "InputDisabled", AllInstance);

                var candidateTabNameMember =
                    FindMember(candidateTabType, "Name", AllInstance);

                if (candidateTabsMember == null)
                {
                    lastFailure = "Found ChatTwo config, but could not find Config.Tabs.";
                    continue;
                }

                if (candidateInputDisabledMember == null)
                {
                    lastFailure = "Found ChatTwo.Tab, but could not find InputDisabled.";
                    continue;
                }

                if (candidateTabNameMember == null)
                {
                    lastFailure = "Found ChatTwo.Tab, but could not find Name.";
                    continue;
                }

                chatTwoPluginType = candidatePluginType;
                chatLogWindowType = candidateChatLogType;
                configurationType = candidateConfigType;
                tabType = candidateTabType;

                chatTwoPlugin = candidatePlugin;
                chatLogWindow = candidateChatLog;
                config = candidateConfig;

                chatLogWindowMember = candidateChatLogMember;
                configMember = candidateConfigMember;

                lastTabProperty = candidatePluginType.GetProperty("LastTab", AllInstance);
                wantedTabProperty = candidatePluginType.GetProperty("WantedTab", AllInstance);

                saveConfigMethod = candidatePluginType.GetMethod("SaveConfig", AllInstance);
                changeTabMethod = candidateChatLogType.GetMethod("ChangeTab", AllInstance);

                lastWindowPosProperty = candidateChatLogType.GetProperty("LastWindowPos", AllInstance);
                lastWindowSizeProperty = candidateChatLogType.GetProperty("LastWindowSize", AllInstance);

                positionProperty = candidateChatLogType.GetProperty("Position", AllInstance);
                sizeProperty = candidateChatLogType.GetProperty("Size", AllInstance);

                positionConditionProperty = candidateChatLogType.GetProperty("PositionCondition", AllInstance);
                sizeConditionProperty = candidateChatLogType.GetProperty("SizeCondition", AllInstance);

                tabsMember = candidateTabsMember;
                inputDisabledMember = candidateInputDisabledMember;
                tabNameMember = candidateTabNameMember;

                lastFailure = null;
                return true;
            }

            lastFailure ??= "No ChatTwo assembly with ChatTwo.Plugin was found.";
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

            chatLogWindowMember = null;
            configMember = null;
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

            tabsMember = null;
            inputDisabledMember = null;
            tabNameMember = null;
        }

        private IList GetTabs()
        {
            if (GetMemberValue(tabsMember, config) is IList tabs)
                return tabs;

            throw new InvalidOperationException("Could not read Chat2 Config.Tabs.");
        }

        private object? TryFindLiveChatTwoPlugin(Type candidatePluginType)
        {
            foreach (var field in candidatePluginType.GetFields(AllStatic))
            {
                if (!candidatePluginType.IsAssignableFrom(field.FieldType))
                    continue;

                var value = SafeGetField(field, null);

                if (value != null)
                    return value;
            }

            foreach (var property in candidatePluginType.GetProperties(AllStatic))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;

                if (!candidatePluginType.IsAssignableFrom(property.PropertyType))
                    continue;

                var value = SafeGetProperty(property, null);

                if (value != null)
                    return value;
            }

            return TryFindLiveObjectFromInstalledPlugins(
                candidatePluginType,
                installedPluginEntry => IsProbablyChatTwoPluginEntry(installedPluginEntry));
        }

        private object? TryFindLiveObjectFromInstalledPlugins(
            Type wantedType,
            Func<object, bool>? entryFilter = null)
        {
            var pluginInterface = GetDalamudPluginInterfaceFromYourPlugin();

            if (pluginInterface == null)
            {
                lastFailure = "Could not find IDalamudPluginInterface on your Plugin.";
                return null;
            }

            var installedPluginsProperty = pluginInterface.GetType().GetProperty(
                "InstalledPlugins",
                BindingFlags.Public | BindingFlags.Instance);

            if (installedPluginsProperty?.GetValue(pluginInterface) is not IEnumerable installedPlugins)
            {
                lastFailure = "Could not read PluginInterface.InstalledPlugins.";
                return null;
            }

            foreach (var installedPluginEntry in installedPlugins)
            {
                if (entryFilter != null && !entryFilter(installedPluginEntry))
                    continue;

                if (wantedType.IsInstanceOfType(installedPluginEntry))
                    return installedPluginEntry;

                var direct = TryGetDirectInstance(installedPluginEntry, wantedType);
                if (direct != null)
                    return direct;

                var nested = FieldOnlyFindInstance(installedPluginEntry, wantedType, maxDepth: 10, maxNodes: 4096);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private object? GetDalamudPluginInterfaceFromYourPlugin()
        {
            var pluginType = plugin.GetType();

            foreach (var field in pluginType.GetFields(AllInstance))
            {
                var value = SafeGetField(field, plugin);

                if (LooksLikePluginInterface(value))
                    return value;
            }

            foreach (var property in pluginType.GetProperties(AllInstance))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;

                var value = SafeGetProperty(property, plugin);

                if (LooksLikePluginInterface(value))
                    return value;
            }

            foreach (var field in pluginType.GetFields(AllStatic))
            {
                var value = SafeGetField(field, null);

                if (LooksLikePluginInterface(value))
                    return value;
            }

            foreach (var property in pluginType.GetProperties(AllStatic))
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

        private static object? TryGetDirectInstance(object wrapper, Type wantedType)
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
                "Loader",
                "ChatLog",
                "ChatLogWindow"
            })
            {
                var value = GetNamedMemberValue(wrapper, name);

                if (value == null)
                    continue;

                if (wantedType.IsInstanceOfType(value))
                    return value;

                var nested = TryGetDirectInstanceOneLevel(value, wantedType);

                if (nested != null)
                    return nested;
            }

            return TryGetDirectInstanceOneLevel(wrapper, wantedType);
        }

        private static object? TryGetDirectInstanceOneLevel(object wrapper, Type wantedType)
        {
            var type = wrapper.GetType();

            foreach (var property in type.GetProperties(AllInstance))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;

                if (!wantedType.IsAssignableFrom(property.PropertyType))
                    continue;

                var value = SafeGetProperty(property, wrapper);

                if (value != null)
                    return value;
            }

            foreach (var field in type.GetFields(AllInstance))
            {
                if (!wantedType.IsAssignableFrom(field.FieldType))
                    continue;

                var value = SafeGetField(field, wrapper);

                if (value != null)
                    return value;
            }

            return null;
        }

        private static object? FieldOnlyFindInstance(
            object root,
            Type wantedType,
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

                if (wantedType.IsInstanceOfType(current))
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
                    fields = currentType.GetFields(AllInstance);
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

                    if (wantedType.IsInstanceOfType(value))
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

        private static MemberInfo? FindMember(Type type, string name, BindingFlags flags)
        {
            var property = type.GetProperty(name, flags);

            if (property != null && property.GetIndexParameters().Length == 0)
                return property;

            var field = type.GetField(name, flags);

            if (field != null)
                return field;

            return null;
        }

        private static object? GetNamedMemberValue(object instance, string name)
        {
            var type = instance.GetType();

            var property = type.GetProperty(name, AllInstance);

            if (property != null && property.GetIndexParameters().Length == 0)
                return SafeGetProperty(property, instance);

            var field = type.GetField(name, AllInstance);

            if (field != null)
                return SafeGetField(field, instance);

            return null;
        }

        private static object? GetMemberValue(MemberInfo? member, object? instance)
        {
            try
            {
                return member switch
                {
                    FieldInfo field => field.GetValue(instance),
                    PropertyInfo property when property.GetIndexParameters().Length == 0 => property.GetValue(instance),
                    _ => null,
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool SetMemberValue(MemberInfo? member, object? instance, object? value)
        {
            try
            {
                switch (member)
                {
                    case FieldInfo field:
                        field.SetValue(instance, value);
                        return true;

                    case PropertyInfo property when property.GetIndexParameters().Length == 0 && property.CanWrite:
                        property.SetValue(instance, value);
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

            try
            {
                var value = property.GetValue(instance);

                return value switch
                {
                    Vector2 vec => vec,
                    _ => null,
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool WriteVector2(PropertyInfo property, object instance, Vector2 value)
        {
            try
            {
                var type = property.PropertyType;
                var nullableType = Nullable.GetUnderlyingType(type);

                if (type == typeof(Vector2) || nullableType == typeof(Vector2))
                {
                    property.SetValue(instance, value);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
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
                    property.SetValue(instance, null);
            }
            catch
            {
                // ignored
            }
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

        private void PrintLastFailure(string operation)
        {
            Plugin.ChatGui.PrintError($"Chat2Api.{operation} failed: {lastFailure ?? "Unknown reflection failure."}");
        }
    }
}
