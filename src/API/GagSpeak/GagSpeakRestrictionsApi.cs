using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace SayusGagExtender.API.GagSpeak
{
    public sealed class GagSpeakRestrictionsApi : IDisposable
    {
        public const int MaxRestrictionsLayers = 5;

        private readonly Plugin plugin;
        private readonly GagSpeakReflectionContext context;

        private static readonly TimeSpan OnUpdateCooldown = TimeSpan.FromSeconds(2);
        private DateTime onUpdateNextUTC = DateTime.MinValue;
        private Dictionary<int, string> wornRestrictions = new Dictionary<int, string>();
        public event Action<Dictionary<int, string>>? OnRestrictionsChanged;

        public bool DebugLog = false;

        public event Action<bool>? OnBlindfoldStateChanged;
        private bool? lastBlindfolded;

        public GagSpeakRestrictionsApi(Plugin plugin, GagSpeakReflectionContext context)
        {
            this.plugin = plugin;
            this.context = context;

            Plugin.Framework.Update += this.OnFrameworkUpdate;
            OnRestrictionsChanged += CheckBlindfoldState;
        }

        public void Dispose()
        {
            Plugin.Framework.Update -= this.OnFrameworkUpdate;
        }
        
        private void CheckBlindfoldState(Dictionary<int, string> restrictions)
        {
            var blindfolded = IsBlindfolded();
            if (lastBlindfolded == blindfolded)
                return;
            lastBlindfolded = blindfolded;
            OnBlindfoldStateChanged?.Invoke(blindfolded);
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

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (onUpdateNextUTC < DateTime.UtcNow)
            {
                onUpdateNextUTC = DateTime.UtcNow + OnUpdateCooldown;
                var restrictions = GetActiveRestrictions();


                bool changed = false;
                for (int i = 0; i < MaxRestrictionsLayers; i++)
                {
                    _ = wornRestrictions.TryGetValue(i, out var oldRestriction) ? oldRestriction : string.Empty;
                    _ = restrictions.TryGetValue(i, out var newRestriction) ? newRestriction : string.Empty;

                    //ChatPrint($"[{i}]: {oldRestriction} vs {newRestriction}");

                    if (oldRestriction == newRestriction)
                        continue;

                    changed = true;
                }


                if (changed)
                {
                    ChatPrint($"GagSpeak active restrictions changed:", force: true);
                    foreach (var restriction in restrictions)
                    {
                        ChatPrint($"[{restriction.Key}]: {restriction.Value}", force: true);
                    }

                    wornRestrictions = restrictions;
                    OnRestrictionsChanged?.Invoke(restrictions);
                }
            }


        }
        public Dictionary<Guid, string> GetAvailableRestrictions()
        {
            var result = new Dictionary<Guid, string>();

            try
            {
                if (!context.EnsureReady())
                {
                    ChatPrintError("GagSpeak context is not ready.");
                    return result;
                }

                var storage = context.GetPropertyValue(context.RestrictionManager, "Storage") as IEnumerable;
                if (storage == null)
                {
                    ChatPrintError("GagSpeak RestrictionManager.Storage was null or not enumerable.");
                    return result;
                }

                foreach (var restriction in storage)
                {
                    var idObj = context.GetPropertyValue(restriction, "Identifier");
                    if (idObj is not Guid id || id == Guid.Empty)
                        continue;

                    var label = context.GetPropertyValue(restriction, "Label") as string;
                    if (string.IsNullOrWhiteSpace(label))
                        continue;

                    result[id] = NormalizeName(label);
                }
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to get available GagSpeak restrictions: {ex}");
            }

            return result;
        }
        public bool IsAnyListedRestrictionsActive(Dictionary<Guid, string> handGuardBlockedItems)
        {
            try
            {
                if (handGuardBlockedItems == null || handGuardBlockedItems.Count == 0)
                    return false;

                if (!context.EnsureReady())
                    return false;

                foreach (var blockedItem in handGuardBlockedItems)
                {
                    var layer = FindActiveLayerForRestriction(blockedItem.Key);

                    if (layer >= 0)
                    {
                        ChatPrint($"HandGuard blocked GagSpeak restriction is active on layer {layer + 1}: {blockedItem.Value}");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to check HandGuard blocked GagSpeak restrictions: {ex}");
                return false;
            }
        }
        public bool IsRestrictionActive(string name)
        {
            try
            {
                if (!context.EnsureReady())
                    return false;

                var restriction = FindRestrictionByName(name);
                if (restriction == null)
                {
                    ChatPrintError($"GagSpeak restriction not found in Storage: {name}");
                    DumpRestrictionStorageNames();
                    return false;
                }

                var idObj = context.GetPropertyValue(restriction, "Identifier");
                if (idObj is not Guid id)
                {
                    ChatPrintError($"GagSpeak restriction found, but Identifier was not a Guid: {name}");
                    return false;
                }

                var layer = FindActiveLayerForRestriction(id);
                ChatPrint($"GagSpeak restriction \"{name}\" active layer: {(layer >= 0 ? layer + 1 : 0)}");

                return layer >= 0;
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to check GagSpeak restriction: {ex}");
                return false;
            }
        }

        public void ApplyRestriction(string name)
        {
            try
            {
                if (!context.EnsureReady())
                    return;

                var restriction = FindRestrictionByName(name);
                if (restriction == null)
                {
                    ChatPrintError($"GagSpeak restriction not found: {name}");
                    DumpRestrictionStorageNames();
                    return;
                }

                var idObj = context.GetPropertyValue(restriction, "Identifier");
                if (idObj is not Guid id)
                {
                    ChatPrintError("Found GagSpeak restriction, but Identifier was not a Guid.");
                    return;
                }

                if (FindActiveLayerForRestriction(id) >= 0)
                {
                    ChatPrint($"GagSpeak restriction already active: {name}");
                    return;
                }

                var freeLayer = FindFirstFreeLayer();
                if (freeLayer < 0)
                {
                    ChatPrintError("No free GagSpeak restriction layer found. Layers 1-5 are occupied.");
                    return;
                }

                ChatPrint($"Applying GagSpeak restriction \"{name}\" to layer {freeLayer + 1}.");
                PushActiveRestriction(id, freeLayer, "Applied");
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to apply GagSpeak restriction: {ex}");
            }
        }

        public void RemoveRestriction(string name)
        {
            try
            {
                if (!context.EnsureReady())
                    return;

                var restriction = FindRestrictionByName(name);
                if (restriction == null)
                {
                    ChatPrintError($"GagSpeak restriction not found: {name}");
                    DumpRestrictionStorageNames();
                    return;
                }

                var idObj = context.GetPropertyValue(restriction, "Identifier");
                if (idObj is not Guid id)
                {
                    ChatPrintError("Found GagSpeak restriction, but Identifier was not a Guid.");
                    return;
                }

                var layer = FindActiveLayerForRestriction(id);
                if (layer < 0)
                    return;

                ChatPrint($"Removing GagSpeak restriction \"{name}\" from layer {layer + 1}.");
                PushActiveRestriction(Guid.Empty, layer, "Removed");
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to remove GagSpeak restriction: {ex}");
            }
        }
        public Dictionary<int, string> GetActiveRestrictions()
        {
            var result = new Dictionary<int, string>();

            try
            {
                if (!context.EnsureReady())
                {
                    ChatPrintError("GagSpeak context is not ready.");
                    return result;
                }

                var serverData = context.GetPropertyValue(context.RestrictionManager, "ServerRestrictionData");
                if (serverData == null)
                {
                    ChatPrintError("GagSpeak ServerRestrictionData is null. GagSpeak may not be connected/synced yet.");
                    return result;
                }

                var restrictions = context.GetPropertyValue(serverData, "Restrictions") as IEnumerable;
                if (restrictions == null)
                {
                    ChatPrintError("GagSpeak ServerRestrictionData.Restrictions was null or not enumerable.");
                    return result;
                }

                var storage = context.GetPropertyValue(context.RestrictionManager, "Storage") as IEnumerable;
                if (storage == null)
                {
                    ChatPrintError("GagSpeak RestrictionManager.Storage was null or not enumerable.");
                    return result;
                }

                var storageById = new Dictionary<Guid, string>();

                foreach (var storedRestriction in storage)
                {
                    var storedIdObj = context.GetPropertyValue(storedRestriction, "Identifier");
                    if (storedIdObj is not Guid storedId || storedId == Guid.Empty)
                        continue;

                    var label = context.GetPropertyValue(storedRestriction, "Label") as string;
                    if (string.IsNullOrWhiteSpace(label))
                        continue;

                    storageById[storedId] = NormalizeName(label);
                }

                var layer = 0;

                foreach (var slot in restrictions)
                {
                    if (layer >= 5)
                        break;

                    var idObj = context.GetPropertyValue(slot, "Identifier");
                    if (idObj is Guid id && id != Guid.Empty)
                    {
                        if (storageById.TryGetValue(id, out var name))
                            result[layer] = name;
                        else
                            result[layer] = id.ToString();
                        //Plugin.ChatGui.Print($"Active GagSpeak restriction found in layer {layer + 1}: {result[layer]}");
                    }

                    layer++;
                }
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to get active GagSpeak restrictions: {ex}");
            }

            return result;
        }
        private object? GetCacheStateManager()
        {
            var cache = context.TryResolveServiceByTypeName("CacheStateManager");

            if (cache == null)
                ChatPrintError("Could not resolve GagSpeak CacheStateManager.");

            return cache;
        }

        private object? FindRestrictionByName(string name)
        {
            var storage = context.GetPropertyValue(context.RestrictionManager, "Storage") as IEnumerable;
            if (storage == null)
            {
                ChatPrintError("GagSpeak RestrictionManager.Storage was null or not enumerable.");
                return null;
            }

            var wanted = NormalizeName(name);

            foreach (var restriction in storage)
            {
                var label = context.GetPropertyValue(restriction, "Label") as string;
                if (label == null)
                    continue;

                if (string.Equals(NormalizeName(label), wanted, StringComparison.OrdinalIgnoreCase))
                    return restriction;
            }

            return null;
        }

        private int FindActiveLayerForRestriction(Guid id)
        {
            try
            {
                var map = context.GetPropertyValue(context.RestrictionManager, "IdToLayerMap");
                if (map != null)
                {
                    var mapType = map.GetType();

                    var tryGetValue = mapType.GetMethods()
                        .FirstOrDefault(m =>
                        {
                            if (m.Name != "TryGetValue")
                                return false;

                            var p = m.GetParameters();
                            return p.Length == 2 &&
                                   p[0].ParameterType == typeof(Guid) &&
                                   p[1].IsOut;
                        });

                    if (tryGetValue != null)
                    {
                        var args = new object?[] { id, null };
                        var ok = tryGetValue.Invoke(map, args);

                        if (ok is bool b && b && args[1] is int layer)
                        {
                            if (layer >= 0 && layer <= 4)
                                return layer;
                        }
                    }
                }

                var serverData = context.GetPropertyValue(context.RestrictionManager, "ServerRestrictionData");
                var restrictions = context.GetPropertyValue(serverData!, "Restrictions");

                if (restrictions is IEnumerable enumerable)
                {
                    var idx = 0;

                    foreach (var slot in enumerable)
                    {
                        if (idx >= 5)
                            break;

                        var slotIdObj = context.GetPropertyValue(slot, "Identifier");
                        if (slotIdObj is Guid slotId && slotId == id)
                            return idx;

                        idx++;
                    }
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
                var serverData = context.GetPropertyValue(context.RestrictionManager, "ServerRestrictionData");
                if (serverData == null)
                {
                    ChatPrintError("GagSpeak ServerRestrictionData is null. GagSpeak may not be connected/synced yet.");
                    return -1;
                }

                var restrictions = context.GetPropertyValue(serverData, "Restrictions");
                if (restrictions is not IEnumerable enumerable)
                {
                    ChatPrintError("GagSpeak ServerRestrictionData.Restrictions was null or not enumerable.");
                    return -1;
                }

                var idx = 0;

                foreach (var slot in enumerable)
                {
                    if (idx >= 5)
                        break;

                    var slotIdObj = context.GetPropertyValue(slot, "Identifier");

                    if (slotIdObj is Guid slotId && slotId == Guid.Empty)
                    {
                        if (CanApplyLayer(idx))
                            return idx;

                        ChatPrint($"Restriction layer {idx + 1} is empty but cannot be applied right now.");
                    }

                    idx++;
                }

                return -1;
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to find free restriction layer: {ex}");
                return -1;
            }
        }

        private bool CanApplyLayer(int layer)
        {
            try
            {
                var manager = context.RestrictionManager;
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

        private void PushActiveRestriction(Guid restrictionId, int layer, string updateTypeName)
        {
            if (!context.EnsureReady())
                return;

            var mainHub = context.MainHub;
            var mainHubType = mainHub.GetType();

            var method = mainHubType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m =>
                {
                    if (!string.Equals(m.Name, "UserPushActiveRestrictions", StringComparison.Ordinal))
                        return false;

                    var p = m.GetParameters();
                    return p.Length == 1 &&
                           string.Equals(p[0].ParameterType.Name, "PushClientActiveRestriction", StringComparison.Ordinal);
                });

            if (method == null)
            {
                ChatPrintError("MainHub.UserPushActiveRestrictions(PushClientActiveRestriction) was not found.");

                if (DebugLog)
                    context.DumpMethods(mainHubType, "UserPushActiveRestrictions");

                return;
            }

            var dtoType = method.GetParameters()[0].ParameterType;

            var dto = CreatePushClientActiveRestrictionDto(dtoType, restrictionId, layer, updateTypeName);
            if (dto == null)
            {
                if (DebugLog)
                    context.DumpDtoShape(dtoType);

                ChatPrintError("Could not construct PushClientActiveRestriction.");
                return;
            }

            object? hubResponse;

            try
            {
                hubResponse = InvokePossiblyAsync(method, mainHub, new object?[] { dto });
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak UserPushActiveRestrictions failed: {ex.InnerException ?? ex}");
                return;
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak UserPushActiveRestrictions invoke failed: {ex}");
                return;
            }

            DumpHubResponse(hubResponse);

            var activeData = ExtractActiveRestrictionPayload(hubResponse);
            if (activeData == null)
            {
                ChatPrintError("GagSpeak server call returned no ActiveRestriction payload.");
                return;
            }

            var enactor = GetMainHubUid();

            if (string.Equals(updateTypeName, "Removed", StringComparison.OrdinalIgnoreCase))
                InvokeLocalRemove(layer, enactor);
            else
                InvokeLocalApply(layer, activeData, enactor);
        }

        private object? CreatePushClientActiveRestrictionDto(
            Type dtoType,
            Guid restrictionId,
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
                ChatPrintError("Could not find PushClientActiveRestriction(List<UserData>, DataUpdateType) constructor.");

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
                ChatPrintError($"Could not construct PushClientActiveRestriction: {ex}");
                return null;
            }

            var wroteSomething = false;

            wroteSomething |= TrySetMemberValueCompatible(dto, "Layer", layer);
            wroteSomething |= TrySetMemberValueCompatible(dto, "LayerIdx", layer);
            wroteSomething |= TrySetMemberValueCompatible(dto, "LayerIndex", layer);
            wroteSomething |= TrySetMemberValueCompatible(dto, "Slot", layer);
            wroteSomething |= TrySetMemberValueCompatible(dto, "Idx", layer);
            wroteSomething |= TrySetMemberValueCompatible(dto, "Index", layer);

            wroteSomething |= TrySetMemberValueCompatible(dto, "Identifier", restrictionId);
            wroteSomething |= TrySetMemberValueCompatible(dto, "RestrictionId", restrictionId);
            wroteSomething |= TrySetMemberValueCompatible(dto, "RestrictionIdentifier", restrictionId);
            wroteSomething |= TrySetMemberValueCompatible(dto, "ActiveRestrictionId", restrictionId);

            TrySetMemberValueCompatible(dto, "Enabler", string.Empty);
            TrySetMemberValueCompatible(dto, "Password", string.Empty);
            TrySetMemberValueCompatible(dto, "Assigner", string.Empty);
            TrySetMemberValueCompatible(dto, "Timer", DateTimeOffset.MinValue);

            TrySetDefaultEnum(dto, "Padlock");

            if (!wroteSomething)
            {
                ChatPrintError("Constructed PushClientActiveRestriction, but no known layer/id properties were writable.");

                if (DebugLog)
                    context.DumpDtoShape(dtoType);

                return null;
            }

            return dto;
        }

        private void InvokeLocalApply(int layer, object activeData, string enactor)
        {
            try
            {
                var manager = context.RestrictionManager;
                var managerType = manager.GetType();

                var method = managerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "ApplyRestriction")
                            return false;

                        var p = m.GetParameters();

                        return p.Length == 4 &&
                               p[0].ParameterType == typeof(int) &&
                               p[1].ParameterType.Name == "ActiveRestriction" &&
                               p[2].ParameterType == typeof(string) &&
                               p[3].IsOut;
                    });

                if (method == null)
                {
                    ChatPrintError("RestrictionManager.ApplyRestriction(int, ActiveRestriction, string, out RestrictionItem) was not found.");

                    if (DebugLog)
                        context.DumpMethods(managerType, "ApplyRestriction");

                    return;
                }

                var expectedActiveType = method.GetParameters()[1].ParameterType;
                if (!expectedActiveType.IsInstanceOfType(activeData))
                {
                    ChatPrintError(
                        $"ActiveRestriction type mismatch. Got {activeData.GetType().AssemblyQualifiedName}, expected {expectedActiveType.AssemblyQualifiedName}.");
                    return;
                }

                var args = new object?[] { layer, activeData, enactor, null };
                var result = method.Invoke(manager, args);

                ChatPrint($"GagSpeak local ApplyRestriction returned: {result}");

                var item = args[3];
                if (item != null)
                {
                    var label = context.GetPropertyValue(item, "Label") as string ?? "<unknown>";
                    ChatPrint($"GagSpeak local applied restriction: {label}, layer {layer + 1}");

                    InvokeCacheAddRestrictionItemNoBlock(item, layer, enactor);
                }
                else
                {
                    ChatPrintError("ApplyRestriction succeeded but returned null RestrictionItem.");
                }
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak local ApplyRestriction failed: {ex.InnerException ?? ex}");
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak local ApplyRestriction invoke failed: {ex}");
            }
        }

        private void InvokeLocalRemove(int layer, string enactor)
        {
            try
            {
                var manager = context.RestrictionManager;
                var managerType = manager.GetType();

                var method = managerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "RemoveRestriction")
                            return false;

                        var p = m.GetParameters();

                        return p.Length == 3 &&
                               p[0].ParameterType == typeof(int) &&
                               p[1].ParameterType == typeof(string) &&
                               p[2].IsOut;
                    });

                if (method == null)
                {
                    ChatPrintError("RestrictionManager.RemoveRestriction(int, string, out RestrictionItem) was not found.");

                    if (DebugLog)
                        context.DumpMethods(managerType, "RemoveRestriction");

                    return;
                }

                var args = new object?[] { layer, enactor, null };
                var result = method.Invoke(manager, args);

                ChatPrint($"GagSpeak local RemoveRestriction returned: {result}");

                var item = args[2];
                if (item != null)
                {
                    var label = context.GetPropertyValue(item, "Label") as string ?? "<unknown>";
                    ChatPrint($"GagSpeak local removed restriction: {label}, layer {layer + 1}");

                    InvokeCacheRemoveRestrictionItemNoBlock(item, layer);
                }
                else
                {
                    ChatPrintError("RemoveRestriction succeeded but returned null RestrictionItem.");
                }
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak local RemoveRestriction failed: {ex.InnerException ?? ex}");
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak local RemoveRestriction invoke failed: {ex}");
            }
        }

        private void InvokeCacheAddRestrictionItem(object restrictionItem, int layer, string enabler)
        {
            try
            {
                var cache = GetCacheStateManager();
                if (cache == null)
                    return;

                var method = FindCacheMethod(cache, "AddRestrictionItem", 3);
                if (method == null)
                {
                    ChatPrintError("CacheStateManager.AddRestrictionItem(RestrictionItem, int, string) was not found.");
                    DumpCacheStateManagerMethods("AddRestrictionItem");
                    return;
                }

                var result = method.Invoke(cache, new object?[] { restrictionItem, layer, enabler });

                if (result is Task task)
                    task.GetAwaiter().GetResult();

                ChatPrint($"GagSpeak cache AddRestrictionItem completed for layer {layer + 1}.");
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak cache AddRestrictionItem failed: {ex.InnerException ?? ex}");
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak cache AddRestrictionItem invoke failed: {ex}");
            }
        }

        private void InvokeCacheRemoveRestrictionItem(object restrictionItem, int layer)
        {
            try
            {
                var cache = GetCacheStateManager();
                if (cache == null)
                    return;

                var method = FindCacheMethod(cache, "RemoveRestrictionItem", 2);
                if (method == null)
                {
                    ChatPrintError("CacheStateManager.RemoveRestrictionItem(RestrictionItem, int) was not found.");
                    DumpCacheStateManagerMethods("RemoveRestrictionItem");
                    return;
                }

                var result = method.Invoke(cache, new object?[] { restrictionItem, layer });

                if (result is Task task)
                    task.GetAwaiter().GetResult();

                ChatPrint($"GagSpeak cache RemoveRestrictionItem completed for layer {layer + 1}.");
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak cache RemoveRestrictionItem failed: {ex.InnerException ?? ex}");
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak cache RemoveRestrictionItem invoke failed: {ex}");
            }
        }

        private void InvokeCacheAddRestrictionItemNoBlock(object restrictionItem, int layer, string enabler)
        {
            try
            {
                var cache = GetCacheStateManager();
                if (cache == null)
                    return;

                var method = FindCacheMethod(cache, "AddRestrictionItem", 3);
                if (method == null)
                {
                    ChatPrintError("CacheStateManager.AddRestrictionItem(RestrictionItem, int, string) was not found.");
                    DumpCacheStateManagerMethods("AddRestrictionItem");
                    return;
                }

                var result = method.Invoke(cache, new object?[] { restrictionItem, layer, enabler });

                if (result is Task task)
                {
                    _ = task.ContinueWith(t =>
                    {
                        if (t.Exception != null)
                            ChatPrintError($"GagSpeak cache AddRestrictionItem failed: {t.Exception.GetBaseException()}");
                        else
                            ChatPrint($"GagSpeak cache AddRestrictionItem completed for layer {layer + 1}.");
                    });

                    return;
                }

                ChatPrint($"GagSpeak cache AddRestrictionItem invoked for layer {layer + 1}.");
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak cache AddRestrictionItem failed: {ex.InnerException ?? ex}");
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak cache AddRestrictionItem invoke failed: {ex}");
            }
        }

        private void InvokeCacheRemoveRestrictionItemNoBlock(object restrictionItem, int layer)
        {
            try
            {
                var cache = GetCacheStateManager();
                if (cache == null)
                    return;

                var method = FindCacheMethod(cache, "RemoveRestrictionItem", 2);
                if (method == null)
                {
                    ChatPrintError("CacheStateManager.RemoveRestrictionItem(RestrictionItem, int) was not found.");
                    DumpCacheStateManagerMethods("RemoveRestrictionItem");
                    return;
                }

                var result = method.Invoke(cache, new object?[] { restrictionItem, layer });

                if (result is Task task)
                {
                    _ = task.ContinueWith(t =>
                    {
                        if (t.Exception != null)
                            ChatPrintError($"GagSpeak cache RemoveRestrictionItem failed: {t.Exception.GetBaseException()}");
                        else
                            ChatPrint($"GagSpeak cache RemoveRestrictionItem completed for layer {layer + 1}.");
                    });

                    return;
                }

                ChatPrint($"GagSpeak cache RemoveRestrictionItem invoked for layer {layer + 1}.");
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak cache RemoveRestrictionItem failed: {ex.InnerException ?? ex}");
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak cache RemoveRestrictionItem invoke failed: {ex}");
            }
        }

        private MethodInfo? FindCacheMethod(object cache, string methodName, int parameterCount)
        {
            return cache.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m =>
                {
                    if (!string.Equals(m.Name, methodName, StringComparison.Ordinal))
                        return false;

                    var p = m.GetParameters();

                    if (p.Length != parameterCount)
                        return false;

                    if (parameterCount == 3)
                    {
                        return p[0].ParameterType.Name == "RestrictionItem" &&
                               p[1].ParameterType == typeof(int) &&
                               p[2].ParameterType == typeof(string);
                    }

                    if (parameterCount == 2)
                    {
                        return p[0].ParameterType.Name == "RestrictionItem" &&
                               p[1].ParameterType == typeof(int);
                    }

                    return false;
                });
        }

        private void DumpCacheStateManagerMethods(string methodName)
        {
            if (!DebugLog)
                return;

            var cache = GetCacheStateManager();
            if (cache == null)
                return;

            foreach (var method in cache.GetType().GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                var parameters = string.Join(
                    ", ",
                    method.GetParameters().Select(p =>
                        $"{(p.IsOut ? "out " : string.Empty)}{p.ParameterType.FullName} {p.Name}"));

                ChatPrintError($"Cache method: {method.Name}({parameters}) -> {method.ReturnType.FullName}");
            }
        }

        private object? ExtractActiveRestrictionPayload(object? response)
        {
            if (response == null)
                return null;

            var type = response.GetType();

            if (type.Name == "ActiveRestriction")
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

                if (value != null && value.GetType().Name == "ActiveRestriction")
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

                if (value != null && value.GetType().Name == "ActiveRestriction")
                    return value;
            }

            return null;
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

        private void DumpRestrictionStorageNames()
        {
            if (!DebugLog)
                return;

            try
            {
                var storage = context.GetPropertyValue(context.RestrictionManager, "Storage") as IEnumerable;
                if (storage == null)
                {
                    ChatPrintError("Could not dump GagSpeak restrictions: Storage was null.");
                    return;
                }

                var count = 0;

                foreach (var restriction in storage)
                {
                    if (count++ >= 50)
                    {
                        ChatPrint("GagSpeak restriction dump stopped after 50 entries.");
                        break;
                    }

                    var label = context.GetPropertyValue(restriction, "Label") as string ?? "<null label>";
                    var id = context.GetPropertyValue(restriction, "Identifier");

                    ChatPrint($"GagSpeak restriction: \"{label}\" / {id}");
                }

                if (count == 0)
                    ChatPrint("GagSpeak restriction storage is empty.");
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to dump GagSpeak restriction names: {ex}");
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

            var valueType = value.GetType();

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

        public bool IsBlindfolded()
        {
            try
            {
                if (!context.EnsureReady())
                    return false;

                foreach (var restriction in EnumerateActiveRestrictionItems())
                {
                    if (restriction == null)
                        continue;

                    if (IsBlindfoldRestrictionObject(restriction))
                        return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to check GagSpeak blindfold state: {ex}");
                return false;
            }
        }

        private IEnumerable<object?> EnumerateActiveRestrictionItems()
        {
            var activeItems = context.GetPropertyValue(context.RestrictionManager, "ActiveItems") as IEnumerable;
            if (activeItems != null)
            {
                foreach (var entry in activeItems)
                {
                    var item = UnwrapKeyValuePairValue(entry);
                    if (item != null)
                        yield return item;
                }

                yield break;
            }

            var serverData = context.GetPropertyValue(context.RestrictionManager, "ServerRestrictionData");
            var restrictions = context.GetPropertyValue(serverData!, "Restrictions") as IEnumerable;

            if (restrictions == null)
                yield break;

            var storage = context.GetPropertyValue(context.RestrictionManager, "Storage") as IEnumerable;
            if (storage == null)
                yield break;

            foreach (var slot in restrictions)
            {
                var idObj = context.GetPropertyValue(slot, "Identifier");
                if (idObj is not Guid id || id == Guid.Empty)
                    continue;

                foreach (var storedRestriction in storage)
                {
                    var storedIdObj = context.GetPropertyValue(storedRestriction, "Identifier");
                    if (storedIdObj is Guid storedId && storedId == id)
                    {
                        yield return storedRestriction;
                        break;
                    }
                }
            }
        }

        private static object? UnwrapKeyValuePairValue(object? value)
        {
            if (value == null)
                return null;

            var type = value.GetType();

            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
                return value;

            try
            {
                return type.GetProperty("Value")?.GetValue(value);
            }
            catch
            {
                return value;
            }
        }

        private bool IsBlindfoldRestrictionObject(object restriction)
        {
            var type = restriction.GetType();

            if (type.Name.Contains("BlindfoldRestriction", StringComparison.OrdinalIgnoreCase))
                return true;

            var restrictionType = context.GetPropertyValue(restriction, "Type");
            if (restrictionType != null &&
                restrictionType.ToString()?.Equals("Blindfold", StringComparison.OrdinalIgnoreCase) == true)
                return true;

            var label = context.GetPropertyValue(restriction, "Label") as string;
            return label?.Contains("Blindfold", StringComparison.OrdinalIgnoreCase) == true;
        }
    }
}
