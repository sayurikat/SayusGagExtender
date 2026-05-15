using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Common.Lua;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace SayusGagExtender.API.GagSpeak
{
    public sealed class GagSpeakGagsApi : IDisposable
    {
        public const int MaxGagLayers = 3;

        private readonly Plugin plugin;
        private readonly GagSpeakReflectionContext context;

        private static readonly TimeSpan OnUpdateCooldown = TimeSpan.FromSeconds(2);
        private DateTime onUpdateNextUTC = DateTime.MinValue;
        private Dictionary<int, string> wornGags = new Dictionary<int, string>();
        public event Action<Dictionary<int, string>>? OnGagsChanged;

        public bool DebugLog = false;

        public GagSpeakGagsApi(Plugin plugin, GagSpeakReflectionContext context)
        {
            this.plugin = plugin;
            this.context = context;

            Plugin.Framework.Update += this.OnFrameworkUpdate;
        }

        public void Dispose()
        {
            Plugin.Framework.Update -= this.OnFrameworkUpdate;
        }
        private void OnFrameworkUpdate(IFramework framework)
        {
            if (onUpdateNextUTC < DateTime.UtcNow)
            {
                onUpdateNextUTC = DateTime.UtcNow + OnUpdateCooldown;
                var gags = GetActiveGags();


                bool changed = false;
                for (int i = 0; i < MaxGagLayers; i++)
                {
                    _ = wornGags.TryGetValue(i, out var oldGag) ? oldGag : string.Empty;
                    _ = gags.TryGetValue(i, out var newGag) ? newGag : string.Empty;

                    //ChatPrint($"[{i}]: {oldGag} vs {newGag}");

                    if (oldGag == newGag)
                        continue;

                    changed = true;
                }


                if (changed)
                {
                    ChatPrint($"GagSpeak active gags changed:");
                    foreach (var gag in gags)
                    {
                        ChatPrint($"[{gag.Key}]: {gag.Value}");
                    }

                    wornGags = gags;
                    OnGagsChanged?.Invoke(gags);
                }
            }

        }

        private void ChatPrint(string message, bool force = false)
        {
            if (DebugLog || force)
                Plugin.ChatGui.Print(message);
        }

        private void ChatPrintError(string message, bool force = false)
        {
            if (DebugLog || force)
                Plugin.ChatGui.PrintError(message);
        }

        public bool IsGagActive(string name)
        {
            try
            {
                if (!context.EnsureReady())
                    return false;

                var gag = FindGagByName(name, out var gagType);
                if (gag == null || gagType == null)
                {
                    ChatPrintError($"GagSpeak gag not found in Storage: {name}");
                    DumpGagStorageNames();
                    return false;
                }

                var layer = FindActiveLayerForGag(gagType);
                ChatPrint($"GagSpeak gag \"{name}\" active layer: {(layer >= 0 ? layer + 1 : 0)}");

                return layer >= 0;
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to check GagSpeak gag: {ex}");
                return false;
            }
        }

        public void ApplyGag(string name)
        {
            try
            {
                if (!context.EnsureReady())
                    return;

                var gag = FindGagByName(name, out var gagType);
                if (gag == null || gagType == null)
                {
                    ChatPrintError($"GagSpeak gag not found: {name}");
                    DumpGagStorageNames();
                    return;
                }

                if (FindActiveLayerForGag(gagType) >= 0)
                {
                    ChatPrint($"GagSpeak gag already active: {name}");
                    return;
                }

                var freeLayer = FindFirstFreeLayer();
                if (freeLayer < 0)
                {
                    ChatPrintError("No free GagSpeak gag layer found. Layers 1-3 are occupied.");
                    return;
                }

                ChatPrint($"Applying GagSpeak gag \"{name}\" to layer {freeLayer + 1}.");
                PushActiveGag(gagType, freeLayer, "Applied");
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to apply GagSpeak gag: {ex}");
            }
        }

        public void RemoveGag(string name)
        {
            try
            {
                if (!context.EnsureReady())
                    return;

                var gag = FindGagByName(name, out var gagType);
                if (gag == null || gagType == null)
                {
                    ChatPrintError($"GagSpeak gag not found: {name}");
                    DumpGagStorageNames();
                    return;
                }

                var layer = FindActiveLayerForGag(gagType);
                if (layer < 0)
                    return;

                ChatPrint($"Removing GagSpeak gag \"{name}\" from layer {layer + 1}.");

                var noneGag = GetNoneGagTypeValue(gagType.GetType());
                PushActiveGag(noneGag ?? gagType, layer, "Removed");
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to remove GagSpeak gag: {ex}");
            }
        }

        public Dictionary<int, string> GetActiveGags()
        {
            var result = new Dictionary<int, string>();

            try
            {
                if (!context.EnsureReady())
                {
                    ChatPrintError("GagSpeak context is not ready.");
                    return result;
                }

                var serverData = context.GetPropertyValue(context.GagManager, "ServerGagData");
                if (serverData == null)
                {
                    ChatPrintError("GagSpeak ServerGagData is null. GagSpeak may not be connected/synced yet.");
                    return result;
                }

                var gagSlots = context.GetPropertyValue(serverData, "GagSlots") as IEnumerable;
                if (gagSlots == null)
                {
                    ChatPrintError("GagSpeak ServerGagData.GagSlots was null or not enumerable.");
                    DumpObjectShape(serverData, "ServerGagData");
                    return result;
                }

                var storageByGagType = BuildStorageNameMap();

                var layer = 0;

                foreach (var slot in gagSlots)
                {
                    if (layer >= MaxGagLayers)
                        break;

                    var gagType = context.GetPropertyValue(slot, "GagItem");
                    if (gagType != null && !IsNoneGagType(gagType))
                    {
                        var key = GagTypeKey(gagType);

                        if (storageByGagType.TryGetValue(key, out var name))
                            result[layer] = name;
                        else
                            result[layer] = gagType.ToString() ?? "<unknown>";
                    }

                    layer++;
                }
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to get active GagSpeak gags: {ex}");
            }

            return result;
        }

        // Compatibility alias for your originally requested casing.
        public Dictionary<int, string> GetActivegags()
            => GetActiveGags();

        private object? FindGagByName(string name, out object? gagType)
        {
            gagType = null;

            var storage = context.GetPropertyValue(context.GagManager, "Storage") as IEnumerable;
            if (storage == null)
            {
                ChatPrintError("GagSpeak GagRestrictionManager.Storage was null or not enumerable.");
                return null;
            }

            var wanted = NormalizeName(name);

            foreach (var entry in storage)
            {
                var key = UnwrapKeyValuePairPart(entry, "Key");
                var value = UnwrapKeyValuePairPart(entry, "Value") ?? entry;

                if (value == null)
                    continue;

                var itemGagType = context.GetPropertyValue(value, "GagType") ?? key;
                if (itemGagType == null || IsNoneGagType(itemGagType))
                    continue;

                var label =
                    context.GetPropertyValue(value, "Label") as string ??
                    context.GetPropertyValue(value, "Name") as string ??
                    itemGagType.ToString();

                if (label == null)
                    continue;

                if (string.Equals(NormalizeName(label), wanted, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(NormalizeName(itemGagType.ToString() ?? string.Empty), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    gagType = itemGagType;
                    return value;
                }
            }

            return null;
        }

        private Dictionary<string, string> BuildStorageNameMap()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var storage = context.GetPropertyValue(context.GagManager, "Storage") as IEnumerable;
                if (storage == null)
                    return result;

                foreach (var entry in storage)
                {
                    var key = UnwrapKeyValuePairPart(entry, "Key");
                    var value = UnwrapKeyValuePairPart(entry, "Value") ?? entry;

                    if (value == null)
                        continue;

                    var gagType = context.GetPropertyValue(value, "GagType") ?? key;
                    if (gagType == null || IsNoneGagType(gagType))
                        continue;

                    var label =
                        context.GetPropertyValue(value, "Label") as string ??
                        context.GetPropertyValue(value, "Name") as string ??
                        gagType.ToString();

                    if (string.IsNullOrWhiteSpace(label))
                        continue;

                    result[GagTypeKey(gagType)] = NormalizeName(label);
                }
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to build GagSpeak gag storage map: {ex}");
            }

            return result;
        }

        private int FindActiveLayerForGag(object gagType)
        {
            try
            {
                var serverData = context.GetPropertyValue(context.GagManager, "ServerGagData");
                if (serverData == null)
                    return -1;

                var gagSlots = context.GetPropertyValue(serverData, "GagSlots") as IEnumerable;
                if (gagSlots == null)
                    return -1;

                var wanted = GagTypeKey(gagType);
                var idx = 0;

                foreach (var slot in gagSlots)
                {
                    if (idx >= MaxGagLayers)
                        break;

                    var slotGagType = context.GetPropertyValue(slot, "GagItem");
                    if (slotGagType != null &&
                        !IsNoneGagType(slotGagType) &&
                        string.Equals(GagTypeKey(slotGagType), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return idx;
                    }

                    idx++;
                }
            }
            catch
            {
                // ignored
            }

            return -1;
        }

        private int FindFirstFreeLayer()
        {
            try
            {
                var serverData = context.GetPropertyValue(context.GagManager, "ServerGagData");
                if (serverData == null)
                {
                    ChatPrintError("GagSpeak ServerGagData is null. GagSpeak may not be connected/synced yet.");
                    return -1;
                }

                var gagSlots = context.GetPropertyValue(serverData, "GagSlots") as IEnumerable;
                if (gagSlots == null)
                {
                    ChatPrintError("GagSpeak ServerGagData.GagSlots was null or not enumerable.");
                    DumpObjectShape(serverData, "ServerGagData");
                    return -1;
                }

                var idx = 0;

                foreach (var slot in gagSlots)
                {
                    if (idx >= MaxGagLayers)
                        break;

                    var gagType = context.GetPropertyValue(slot, "GagItem");

                    if (gagType == null || IsNoneGagType(gagType))
                    {
                        if (CanApplyLayer(idx))
                            return idx;

                        ChatPrint($"Gag layer {idx + 1} is empty but cannot be applied right now.");
                    }

                    idx++;
                }

                return -1;
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to find free gag layer: {ex}");
                return -1;
            }
        }

        private bool CanApplyLayer(int layer)
        {
            try
            {
                var manager = context.GagManager;
                var managerType = manager.GetType();

                var method = managerType.GetMethod(
                    "CanApply",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    binder: null,
                    types: new[] { typeof(int) },
                    modifiers: null);

                if (method == null)
                    return true;

                var result = method.Invoke(manager, new object[] { layer });
                return result is not bool b || b;
            }
            catch
            {
                return true;
            }
        }

        private void PushActiveGag(object gagType, int layer, string updateTypeName)
        {
            if (!context.EnsureReady())
                return;

            var mainHub = context.MainHub;
            var mainHubType = mainHub.GetType();

            var method = mainHubType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m =>
                {
                    if (!string.Equals(m.Name, "UserPushActiveGags", StringComparison.Ordinal))
                        return false;

                    var p = m.GetParameters();
                    return p.Length == 1 &&
                           string.Equals(p[0].ParameterType.Name, "PushClientActiveGagSlot", StringComparison.Ordinal);
                });

            if (method == null)
            {
                ChatPrintError("MainHub.UserPushActiveGags(PushClientActiveGagSlot) was not found.");

                if (DebugLog)
                    context.DumpMethods(mainHubType, "UserPushActiveGags");

                return;
            }

            var dtoType = method.GetParameters()[0].ParameterType;

            var dto = CreatePushClientActiveGagSlotDto(dtoType, gagType, layer, updateTypeName);
            if (dto == null)
            {
                if (DebugLog)
                    context.DumpDtoShape(dtoType);

                ChatPrintError("Could not construct PushClientActiveGagSlot.");
                return;
            }

            object? hubResponse;

            try
            {
                hubResponse = InvokePossiblyAsync(method, mainHub, new object?[] { dto });
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak UserPushActiveGags failed: {ex.InnerException ?? ex}");
                return;
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak UserPushActiveGags invoke failed: {ex}");
                return;
            }

            DumpHubResponse(hubResponse);

            var activeGagSlot = ExtractActiveGagSlotPayload(hubResponse);
            if (activeGagSlot == null)
            {
                ChatPrintError("GagSpeak server call returned no ActiveGagSlot payload.");
                return;
            }

            var enactor = GetMainHubUid();

            if (string.Equals(updateTypeName, "Removed", StringComparison.OrdinalIgnoreCase))
            {
                InvokeLocalRemove(layer, enactor);
            }
            else
            {
                var appliedGagType = context.GetPropertyValue(activeGagSlot, "GagItem") ?? gagType;
                InvokeLocalApply(layer, appliedGagType, enactor);
            }
        }

        private object? CreatePushClientActiveGagSlotDto(
            Type dtoType,
            object gagType,
            int layer,
            string updateTypeName)
        {
            var ctor = dtoType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(c =>
                {
                    var p = c.GetParameters();

                    return p.Length == 2 &&
                           p[0].ParameterType.IsGenericType &&
                           p[0].ParameterType.GetGenericTypeDefinition() == typeof(List<>) &&
                           p[1].ParameterType.IsEnum;
                });

            if (ctor == null)
            {
                ChatPrintError("Could not find PushClientActiveGagSlot(List<UserData>, DataUpdateType) constructor.");

                if (DebugLog)
                    context.DumpDtoShape(dtoType);

                return null;
            }

            var parameters = ctor.GetParameters();
            var recipientsType = parameters[0].ParameterType;
            var dataUpdateTypeType = parameters[1].ParameterType;

            object recipients;

            try
            {
                recipients = Activator.CreateInstance(recipientsType)!;
            }
            catch (Exception ex)
            {
                ChatPrintError($"Could not create recipients list: {ex.Message}");
                return null;
            }

            object dataUpdateType;

            try
            {
                dataUpdateType = Enum.Parse(dataUpdateTypeType, updateTypeName);
            }
            catch
            {
                ChatPrintError($"Could not parse DataUpdateType.{updateTypeName}");

                if (DebugLog)
                    context.DumpEnumValues(dataUpdateTypeType);

                return null;
            }

            object dto;

            try
            {
                dto = ctor.Invoke(new[] { recipients, dataUpdateType });
            }
            catch (Exception ex)
            {
                ChatPrintError($"Could not construct PushClientActiveGagSlot: {ex}");
                return null;
            }

            var wroteSomething = false;

            wroteSomething |= TrySetMemberValueCompatible(dto, "Layer", layer);
            wroteSomething |= TrySetMemberValueCompatible(dto, "LayerIdx", layer);
            wroteSomething |= TrySetMemberValueCompatible(dto, "LayerIndex", layer);
            wroteSomething |= TrySetMemberValueCompatible(dto, "Slot", layer);
            wroteSomething |= TrySetMemberValueCompatible(dto, "SlotIdx", layer);
            wroteSomething |= TrySetMemberValueCompatible(dto, "SlotIndex", layer);
            wroteSomething |= TrySetMemberValueCompatible(dto, "Idx", layer);
            wroteSomething |= TrySetMemberValueCompatible(dto, "Index", layer);

            wroteSomething |= TrySetMemberValueCompatible(dto, "GagItem", gagType);
            wroteSomething |= TrySetMemberValueCompatible(dto, "GagType", gagType);
            wroteSomething |= TrySetMemberValueCompatible(dto, "Gag", gagType);
            wroteSomething |= TrySetMemberValueCompatible(dto, "NewGag", gagType);
            wroteSomething |= TrySetMemberValueCompatible(dto, "NewGagType", gagType);

            TrySetMemberValueCompatible(dto, "Enabler", string.Empty);
            TrySetMemberValueCompatible(dto, "Password", string.Empty);
            TrySetMemberValueCompatible(dto, "Assigner", string.Empty);
            TrySetMemberValueCompatible(dto, "PadlockAssigner", string.Empty);
            TrySetMemberValueCompatible(dto, "Timer", DateTimeOffset.MinValue);

            TrySetDefaultEnum(dto, "Padlock");

            if (!wroteSomething)
            {
                ChatPrintError("Constructed PushClientActiveGagSlot, but no known layer/gag properties were writable.");

                if (DebugLog)
                    context.DumpDtoShape(dtoType);

                return null;
            }

            return dto;
        }

        private void InvokeLocalApply(int layer, object gagType, string enactor)
        {
            try
            {
                var manager = context.GagManager;
                var managerType = manager.GetType();

                var method = managerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "ApplyGag")
                            return false;

                        var p = m.GetParameters();

                        return p.Length == 4 &&
                               p[0].ParameterType == typeof(int) &&
                               p[1].ParameterType.IsEnum &&
                               p[1].ParameterType.Name == "GagType" &&
                               p[2].ParameterType == typeof(string) &&
                               p[3].IsOut;
                    });

                if (method == null)
                {
                    ChatPrintError("GagRestrictionManager.ApplyGag(int, GagType, string, out GarblerRestriction) was not found.");

                    if (DebugLog)
                        context.DumpMethods(managerType, "ApplyGag");

                    return;
                }

                var expectedGagType = method.GetParameters()[1].ParameterType;
                if (!expectedGagType.IsInstanceOfType(gagType))
                {
                    ChatPrintError(
                        $"GagType mismatch. Got {gagType.GetType().AssemblyQualifiedName}, expected {expectedGagType.AssemblyQualifiedName}.");
                    return;
                }

                var args = new object?[] { layer, gagType, enactor, null };
                var result = method.Invoke(manager, args);

                ChatPrint($"GagSpeak local ApplyGag returned: {result}");

                var item = args[3];
                if (item != null)
                {
                    var label = context.GetPropertyValue(item, "Label") as string ?? gagType.ToString() ?? "<unknown>";
                    ChatPrint($"GagSpeak local applied gag: {label}, layer {layer + 1}");
                }
                else
                {
                    ChatPrint("ApplyGag returned no GarblerRestriction item. This may be normal if the storage item is missing or disabled.");
                }
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak local ApplyGag failed: {ex.InnerException ?? ex}");
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak local ApplyGag invoke failed: {ex}");
            }
        }

        private void InvokeLocalRemove(int layer, string enactor)
        {
            try
            {
                var manager = context.GagManager;
                var managerType = manager.GetType();

                var method = managerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "RemoveGag")
                            return false;

                        var p = m.GetParameters();

                        return p.Length == 3 &&
                               p[0].ParameterType == typeof(int) &&
                               p[1].ParameterType == typeof(string) &&
                               p[2].IsOut;
                    });

                if (method == null)
                {
                    ChatPrintError("GagRestrictionManager.RemoveGag(int, string, out GarblerRestriction) was not found.");

                    if (DebugLog)
                        context.DumpMethods(managerType, "RemoveGag");

                    return;
                }

                var args = new object?[] { layer, enactor, null };
                var result = method.Invoke(manager, args);

                ChatPrint($"GagSpeak local RemoveGag returned: {result}");

                var item = args[2];
                if (item != null)
                {
                    var label = context.GetPropertyValue(item, "Label") as string ?? "<unknown>";
                    ChatPrint($"GagSpeak local removed gag: {label}, layer {layer + 1}");
                }
                else
                {
                    ChatPrint("RemoveGag returned no GarblerRestriction item.");
                }
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak local RemoveGag failed: {ex.InnerException ?? ex}");
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak local RemoveGag invoke failed: {ex}");
            }
        }

        private object? ExtractActiveGagSlotPayload(object? response)
        {
            if (response == null)
                return null;

            var type = response.GetType();

            if (type.Name == "ActiveGagSlot")
                return response;

            foreach (var propName in new[]
                     {
                         "Value",
                         "Data",
                         "Payload",
                         "Result",
                         "Response",
                         "Content",
                         "Object"
                     })
            {
                var prop = type.GetProperty(
                    propName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (prop == null || prop.GetIndexParameters().Length != 0)
                    continue;

                object? value = null;

                try
                {
                    value = prop.GetValue(response);
                }
                catch
                {
                    continue;
                }

                if (value != null && value.GetType().Name == "ActiveGagSlot")
                    return value;
            }

            foreach (var fieldName in new[]
                     {
                         "Value",
                         "Data",
                         "Payload",
                         "Result",
                         "Response",
                         "Content",
                         "Object",
                         "<Value>k__BackingField",
                         "<Data>k__BackingField",
                         "<Payload>k__BackingField",
                         "<Result>k__BackingField",
                         "<Response>k__BackingField"
                     })
            {
                var field = type.GetField(
                    fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (field == null)
                    continue;

                object? value = null;

                try
                {
                    value = field.GetValue(response);
                }
                catch
                {
                    continue;
                }

                if (value != null && value.GetType().Name == "ActiveGagSlot")
                    return value;
            }

            return null;
        }

        private static object? UnwrapKeyValuePairPart(object? value, string partName)
        {
            if (value == null)
                return null;

            var type = value.GetType();

            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
                return null;

            try
            {
                return type.GetProperty(partName)?.GetValue(value);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsNoneGagType(object gagType)
        {
            if (gagType == null)
                return true;

            try
            {
                if (gagType.GetType().IsEnum)
                {
                    if (string.Equals(gagType.ToString(), "None", StringComparison.OrdinalIgnoreCase))
                        return true;

                    return Convert.ToUInt64(gagType) == 0;
                }
            }
            catch
            {
                // ignored
            }

            return string.Equals(gagType.ToString(), "None", StringComparison.OrdinalIgnoreCase);
        }

        private static object? GetNoneGagTypeValue(Type gagTypeType)
        {
            try
            {
                if (!gagTypeType.IsEnum)
                    return null;

                try
                {
                    return Enum.Parse(gagTypeType, "None");
                }
                catch
                {
                    foreach (var value in Enum.GetValues(gagTypeType))
                    {
                        if (Convert.ToUInt64(value) == 0)
                            return value;
                    }
                }
            }
            catch
            {
                // ignored
            }

            return null;
        }

        private static string GagTypeKey(object gagType)
        {
            try
            {
                if (gagType.GetType().IsEnum)
                    return Convert.ToUInt64(gagType).ToString();
            }
            catch
            {
                // ignored
            }

            return gagType.ToString() ?? string.Empty;
        }

        private string GetMainHubUid()
        {
            var mainHub = context.MainHub;
            var mainHubType = mainHub.GetType();

            foreach (var memberName in new[] { "UID", "Uid", "UserId", "UserUID" })
            {
                var prop = mainHubType.GetProperty(
                    memberName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

                try
                {
                    if (prop != null)
                    {
                        var value = prop.GetValue(prop.GetMethod?.IsStatic == true ? null : mainHub);
                        if (value is string s)
                            return s;
                    }
                }
                catch
                {
                    // ignored
                }

                var field = mainHubType.GetField(
                    memberName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

                try
                {
                    if (field != null)
                    {
                        var value = field.GetValue(field.IsStatic ? null : mainHub);
                        if (value is string s)
                            return s;
                    }
                }
                catch
                {
                    // ignored
                }
            }

            return string.Empty;
        }

        private void DumpGagStorageNames()
        {
            if (!DebugLog)
                return;

            try
            {
                var storage = context.GetPropertyValue(context.GagManager, "Storage") as IEnumerable;
                if (storage == null)
                {
                    ChatPrintError("Could not dump GagSpeak gags: Storage was null.");
                    return;
                }

                var count = 0;

                foreach (var entry in storage)
                {
                    if (count++ >= 80)
                    {
                        ChatPrint("GagSpeak gag dump stopped after 80 entries.");
                        break;
                    }

                    var key = UnwrapKeyValuePairPart(entry, "Key");
                    var value = UnwrapKeyValuePairPart(entry, "Value") ?? entry;

                    var gagType = value != null ? context.GetPropertyValue(value, "GagType") ?? key : key;
                    var label = value != null ? context.GetPropertyValue(value, "Label") as string : null;

                    ChatPrint($"GagSpeak gag: \"{label ?? "<null label>"}\" / Type={gagType ?? "<null type>"}");
                }

                if (count == 0)
                    ChatPrint("GagSpeak gag storage is empty.");
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to dump GagSpeak gag names: {ex}");
            }
        }

        private void DumpHubResponse(object? response)
        {
            if (!DebugLog)
                return;

            if (response == null)
            {
                ChatPrintError("GagSpeak hub response was null.");
                return;
            }

            var type = response.GetType();

            ChatPrint($"GagSpeak hub response type: {type.FullName}");

            foreach (var prop in type.GetProperties(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length != 0)
                    continue;

                object? value = null;

                try
                {
                    value = prop.GetValue(response);
                }
                catch
                {
                    continue;
                }

                ChatPrint($"HubResponse prop {prop.Name}: {value ?? "null"}");
            }
        }

        private void DumpObjectShape(object obj, string label)
        {
            if (!DebugLog)
                return;

            try
            {
                var type = obj.GetType();

                ChatPrintError($"{label} type: {type.FullName}");

                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (prop.GetIndexParameters().Length != 0)
                        continue;

                    ChatPrintError($"Prop: {prop.PropertyType.FullName} {prop.Name}");
                }

                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    ChatPrintError($"Field: {field.FieldType.FullName} {field.Name}");
                }
            }
            catch
            {
                // ignored
            }
        }

        private static object? InvokePossiblyAsync(MethodInfo method, object target, object?[] args)
        {
            var result = method.Invoke(target, args);

            if (result is not Task task)
                return result;

            task.GetAwaiter().GetResult();

            var resultProp = task.GetType().GetProperty("Result");
            return resultProp?.GetValue(task);
        }

        private static bool TrySetMemberValueCompatible(object obj, string propertyOrFieldName, object? value)
        {
            var type = obj.GetType();

            var prop = type.GetProperty(
                propertyOrFieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (prop != null && prop.CanWrite)
            {
                if (TryCoerceValue(value, prop.PropertyType, out var coerced))
                {
                    try
                    {
                        prop.SetValue(obj, coerced);
                        return true;
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }

            var field = type.GetField(
                propertyOrFieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (field != null && !field.IsInitOnly)
            {
                if (TryCoerceValue(value, field.FieldType, out var coerced))
                {
                    try
                    {
                        field.SetValue(obj, coerced);
                        return true;
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }

            return false;
        }

        private static bool TryCoerceValue(object? value, Type targetType, out object? coerced)
        {
            coerced = null;

            var nullableTarget = Nullable.GetUnderlyingType(targetType);
            var effectiveTarget = nullableTarget ?? targetType;

            if (value == null)
            {
                if (!effectiveTarget.IsValueType || nullableTarget != null)
                {
                    coerced = null;
                    return true;
                }

                return false;
            }

            if (effectiveTarget.IsInstanceOfType(value))
            {
                coerced = value;
                return true;
            }

            try
            {
                if (effectiveTarget.IsEnum)
                {
                    if (value is string s)
                    {
                        coerced = Enum.Parse(effectiveTarget, s);
                        return true;
                    }

                    coerced = Enum.ToObject(effectiveTarget, value);
                    return true;
                }

                if (effectiveTarget == typeof(Guid))
                {
                    if (value is Guid guid)
                    {
                        coerced = guid;
                        return true;
                    }

                    if (value is string s && Guid.TryParse(s, out var parsed))
                    {
                        coerced = parsed;
                        return true;
                    }

                    return false;
                }

                if (effectiveTarget == typeof(DateTimeOffset))
                {
                    if (value is DateTimeOffset dto)
                    {
                        coerced = dto;
                        return true;
                    }

                    if (value is DateTime dt)
                    {
                        coerced = new DateTimeOffset(dt);
                        return true;
                    }

                    return false;
                }

                if (effectiveTarget == typeof(DateTime))
                {
                    if (value is DateTime dt)
                    {
                        coerced = dt;
                        return true;
                    }

                    if (value is DateTimeOffset dto)
                    {
                        coerced = dto.DateTime;
                        return true;
                    }

                    return false;
                }

                coerced = Convert.ChangeType(value, effectiveTarget);
                return true;
            }
            catch
            {
                coerced = null;
                return false;
            }
        }

        private void TrySetDefaultEnum(object obj, string propertyOrFieldName)
        {
            var type = obj.GetType();

            var prop = type.GetProperty(
                propertyOrFieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (prop != null && prop.CanWrite && prop.PropertyType.IsEnum)
            {
                try
                {
                    var value = Enum.GetValues(prop.PropertyType).GetValue(0);
                    prop.SetValue(obj, value);
                }
                catch
                {
                    // ignored
                }

                return;
            }

            var field = type.GetField(
                propertyOrFieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (field != null && !field.IsInitOnly && field.FieldType.IsEnum)
            {
                try
                {
                    var value = Enum.GetValues(field.FieldType).GetValue(0);
                    field.SetValue(obj, value);
                }
                catch
                {
                    // ignored
                }
            }
        }

        private static string NormalizeName(string value)
        {
            return value
                .Replace("\uE000", string.Empty)
                .Replace("\uE001", string.Empty)
                .Replace("\uE002", string.Empty)
                .Replace("\uE003", string.Empty)
                .Trim();
        }
    }
}
