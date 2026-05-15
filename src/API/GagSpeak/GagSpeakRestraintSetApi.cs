using Dalamud.Plugin.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace SayusGagExtender.API.GagSpeak
{
    public sealed class GagSpeakRestraintSetApi : IDisposable
    {
        private readonly Plugin plugin;
        private readonly GagSpeakReflectionContext context;

        private static readonly TimeSpan OnUpdateCooldown = TimeSpan.FromSeconds(2);
        private DateTime onUpdateNextUTC = DateTime.MinValue;
        private string wornRestraintSet = string.Empty;
        public event Action<string>? OnRestraintSetChanged;

        public bool DebugLog = false;

        public GagSpeakRestraintSetApi(Plugin plugin, GagSpeakReflectionContext context)
        {
            this.plugin = plugin;
            this.context = context;
            Plugin.Framework.Update += OnFrameworkUpdate;
        }
        public void Dispose()
        {
            Plugin.Framework.Update -= OnFrameworkUpdate;
        }
        private void OnFrameworkUpdate(IFramework framework)
        {
            if (onUpdateNextUTC < DateTime.UtcNow)
            {
                onUpdateNextUTC = DateTime.UtcNow + OnUpdateCooldown;
                var restraintSet = GetActiveRestraintSet();
                if (restraintSet != wornRestraintSet)
                {
                    ChatPrint($"GagSpeak active restraint set changed: {wornRestraintSet} -> {restraintSet}", force: true);
                    wornRestraintSet = restraintSet;
                    OnRestraintSetChanged?.Invoke(restraintSet);
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

        public bool IsRestraintActive(string name)
        {
            try
            {
                if (!context.EnsureReady())
                    return false;

                var restraint = FindRestraintByName(name);
                if (restraint == null)
                {
                    ChatPrintError($"GagSpeak restraint set not found in Storage: {name}");
                    DumpRestraintStorageNames();
                    return false;
                }

                var restraintIdObj = context.GetPropertyValue(restraint, "Identifier");
                if (restraintIdObj is not Guid restraintId)
                {
                    ChatPrintError($"GagSpeak restraint set found, but Identifier was not a Guid: {name}");
                    return false;
                }

                var serverData = context.GetPropertyValue(context.RestraintManager, "ServerData");
                if (serverData == null)
                {
                    ChatPrintError("GagSpeak ServerData is null. GagSpeak may not be connected/synced yet.");
                    return false;
                }

                var activeIdObj = context.GetPropertyValue(serverData, "Identifier");
                if (activeIdObj is not Guid activeId)
                {
                    ChatPrintError("GagSpeak ServerData.Identifier was not a Guid.");
                    return false;
                }

                ChatPrint($"GagSpeak active restraint id: {activeId}, wanted id: {restraintId}");

                return activeId != Guid.Empty && activeId == restraintId;
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to check GagSpeak restraint set: {ex}");
                return false;
            }
        }
        public string GetActiveRestraintSet()
        {
            try
            {
                if (!context.EnsureReady())
                    return string.Empty;

                var serverData = context.GetPropertyValue(context.RestraintManager, "ServerData");
                if (serverData == null)
                {
                    ChatPrintError("GagSpeak ServerData is null. GagSpeak may not be connected/synced yet.");
                    return string.Empty;
                }

                var activeIdObj = context.GetPropertyValue(serverData, "Identifier");
                if (activeIdObj is not Guid activeId)
                {
                    ChatPrintError("GagSpeak ServerData.Identifier was not a Guid.");
                    return string.Empty;
                }

                if (activeId == Guid.Empty)
                    return string.Empty;

                var storage = context.GetPropertyValue(context.RestraintManager, "Storage") as IEnumerable;
                if (storage == null)
                {
                    ChatPrintError("GagSpeak RestraintManager.Storage was null or not enumerable.");
                    return string.Empty;
                }

                foreach (var restraint in storage)
                {
                    var idObj = context.GetPropertyValue(restraint, "Identifier");
                    if (idObj is not Guid id || id != activeId)
                        continue;

                    var label = context.GetPropertyValue(restraint, "Label") as string;
                    if (!string.IsNullOrWhiteSpace(label))
                        return NormalizeName(label);

                    return activeId.ToString();
                }

                ChatPrintError($"Active GagSpeak restraint set id was not found in Storage: {activeId}");
                return activeId.ToString();
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to get active GagSpeak restraint set: {ex}");
                return string.Empty;
            }
        }
        public void ApplyRestraintSet(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            try
            {
                if (!context.EnsureReady())
                    return;

                var restraint = FindRestraintByName(name);
                if (restraint == null)
                {
                    ChatPrintError($"GagSpeak restraint set not found: {name}");
                    DumpRestraintStorageNames();
                    return;
                }

                var identifierObj = context.GetPropertyValue(restraint, "Identifier");
                if (identifierObj is not Guid identifier)
                {
                    ChatPrintError("Found GagSpeak restraint set, but Identifier was not a Guid.");
                    return;
                }

                PushActiveRestraintSet(identifier, "Applied");
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to apply GagSpeak restraint set: {ex}");
            }
        }

        public void RemoveRestraintSet(string name)
        {
            try
            {
                if (!context.EnsureReady())
                    return;

                if (!IsRestraintActive(name))
                    return;

                PushActiveRestraintSet(Guid.Empty, "Removed");
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to remove GagSpeak restraint set: {ex}");
            }
        }

        private object? FindRestraintByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var storage = context.GetPropertyValue(context.RestraintManager, "Storage") as IEnumerable;
            if (storage == null)
            {
                ChatPrintError("GagSpeak RestraintManager.Storage was null or not enumerable.");
                return null;
            }

            var wanted = NormalizeName(name);

            foreach (var restraint in storage)
            {
                var label = context.GetPropertyValue(restraint, "Label") as string;
                if (label == null)
                    continue;

                if (string.Equals(NormalizeName(label), wanted, StringComparison.OrdinalIgnoreCase))
                    return restraint;
            }

            return null;
        }

        private void DumpRestraintStorageNames()
        {
            if (!DebugLog)
                return;

            try
            {
                var storage = context.GetPropertyValue(context.RestraintManager, "Storage") as IEnumerable;
                if (storage == null)
                {
                    ChatPrintError("Could not dump GagSpeak restraints: Storage was null.");
                    return;
                }

                var count = 0;

                foreach (var restraint in storage)
                {
                    if (count++ >= 30)
                    {
                        ChatPrint("GagSpeak restraint dump stopped after 30 entries.");
                        break;
                    }

                    var label = context.GetPropertyValue(restraint, "Label") as string ?? "<null label>";
                    var id = context.GetPropertyValue(restraint, "Identifier");

                    ChatPrint($"GagSpeak restraint set: \"{label}\" / {id}");
                }

                if (count == 0)
                    ChatPrint("GagSpeak restraint storage is empty.");
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to dump GagSpeak restraint names: {ex}");
            }
        }

        private void PushActiveRestraintSet(Guid activeSetId, string updateTypeName)
        {
            if (!context.EnsureReady())
                return;

            var mainHub = context.MainHub;
            var mainHubType = mainHub.GetType();

            var method = mainHubType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m =>
                {
                    if (!string.Equals(m.Name, "UserPushActiveRestraint", StringComparison.Ordinal))
                        return false;

                    var p = m.GetParameters();
                    return p.Length == 1 &&
                           string.Equals(p[0].ParameterType.Name, "PushClientActiveRestraint", StringComparison.Ordinal);
                });

            if (method == null)
            {
                ChatPrintError("MainHub.UserPushActiveRestraint(PushClientActiveRestraint) was not found.");
                context.DumpMethods(mainHubType, "UserPushActiveRestraint");
                return;
            }

            var dtoType = method.GetParameters()[0].ParameterType;

            var dto = CreatePushClientActiveRestraintDto(dtoType, activeSetId, updateTypeName);
            if (dto == null)
            {
                context.DumpDtoShape(dtoType);
                ChatPrintError("Could not construct PushClientActiveRestraint.");
                return;
            }

            try
            {
                var result = method.Invoke(mainHub, new[] { dto });

                if (result is Task task)
                {
                    _ = task.ContinueWith(t =>
                    {
                        try
                        {
                            if (t.Exception != null)
                            {
                                ChatPrintError($"GagSpeak UserPushActiveRestraint failed: {t.Exception.GetBaseException()}");
                                return;
                            }

                            var resultProp = t.GetType().GetProperty("Result");
                            var hubResponse = resultProp?.GetValue(t);

                            ProcessHubResponse(hubResponse, updateTypeName);
                        }
                        catch (Exception ex)
                        {
                            ChatPrintError($"GagSpeak restraint continuation failed: {ex}");
                        }
                    });

                    return;
                }

                ProcessHubResponse(result, updateTypeName);
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak UserPushActiveRestraint failed: {ex.InnerException ?? ex}");
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak UserPushActiveRestraint invoke failed: {ex}");
            }
        }

        private void ProcessHubResponse(object? hubResponse, string updateTypeName)
        {
            DumpHubResponse(hubResponse);

            var activeData = ExtractHubResponsePayload(hubResponse);
            if (activeData == null)
            {
                ChatPrintError("GagSpeak server call returned no CharaActiveRestraint payload.");
                return;
            }

            var enactor = GetMainHubUid();

            if (string.Equals(updateTypeName, "Removed", StringComparison.OrdinalIgnoreCase))
                InvokeLocalRemove(enactor);
            else
                InvokeLocalApply(activeData, enactor);
        }

        private object? CreatePushClientActiveRestraintDto(Type dtoType, Guid activeSetId, string updateTypeName)
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
                ChatPrintError("Could not find PushClientActiveRestraint(List<UserData>, DataUpdateType) constructor.");
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
                ChatPrintError($"Could not construct PushClientActiveRestraint: {ex}");
                return null;
            }

            var wrote = false;

            wrote |= TrySetMemberValueCompatible(dto, "ActiveSetId", activeSetId);
            wrote |= TrySetMemberValueCompatible(dto, "Identifier", activeSetId);
            wrote |= TrySetMemberValueCompatible(dto, "RestraintId", activeSetId);
            wrote |= TrySetMemberValueCompatible(dto, "RestraintIdentifier", activeSetId);
            wrote |= TrySetMemberValueCompatible(dto, "ActiveRestraintId", activeSetId);

            var activeLayersType = GetMemberType(dtoType, "ActiveLayers");
            if (activeLayersType != null)
            {
                var noneLayer = CreateNoneLayerValue(activeLayersType);
                if (noneLayer != null)
                    TrySetMemberValueCompatible(dto, "ActiveLayers", noneLayer);
            }

            TrySetMemberValueCompatible(dto, "Enabler", string.Empty);
            TrySetMemberValueCompatible(dto, "Password", string.Empty);
            TrySetMemberValueCompatible(dto, "Assigner", string.Empty);
            TrySetMemberValueCompatible(dto, "Timer", DateTimeOffset.MinValue);

            TrySetDefaultEnum(dto, "Padlock");

            if (!wrote)
            {
                ChatPrintError("Constructed PushClientActiveRestraint, but could not set ActiveSetId/Identifier.");
                context.DumpDtoShape(dtoType);
                return null;
            }

            return dto;
        }

        private object? ExtractHubResponsePayload(object? response)
        {
            if (response == null)
                return null;

            var type = response.GetType();

            if (type.Name == "CharaActiveRestraint")
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

                if (value != null && value.GetType().Name == "CharaActiveRestraint")
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

                if (value != null && value.GetType().Name == "CharaActiveRestraint")
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

        private void InvokeLocalApply(object activeData, string enactor)
        {
            try
            {
                var manager = context.RestraintManager;
                var managerType = manager.GetType();

                var method = managerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "Apply")
                            return false;

                        var p = m.GetParameters();

                        return p.Length == 3 &&
                               p[0].ParameterType.Name == "CharaActiveRestraint" &&
                               p[1].ParameterType == typeof(string) &&
                               p[2].IsOut;
                    });

                if (method == null)
                {
                    ChatPrintError("RestraintManager.Apply(CharaActiveRestraint, string, out RestraintSet) was not found.");
                    context.DumpMethods(managerType, "Apply");
                    return;
                }

                var expectedActiveType = method.GetParameters()[0].ParameterType;
                if (!expectedActiveType.IsInstanceOfType(activeData))
                {
                    ChatPrintError(
                        $"CharaActiveRestraint type mismatch. Got {activeData.GetType().AssemblyQualifiedName}, expected {expectedActiveType.AssemblyQualifiedName}.");
                    return;
                }

                var args = new object?[] { activeData, enactor, null };
                var result = method.Invoke(manager, args);

                ChatPrint($"GagSpeak local Apply returned: {result}");

                var visualSet = args[2];
                if (visualSet != null)
                {
                    var label = context.GetPropertyValue(visualSet, "Label") as string ?? "<unknown>";
                    ChatPrint($"GagSpeak local applied visual set: {label}");

                    var activeLayers = context.GetPropertyValue(activeData, "ActiveLayers")
                        ?? CreateNoneLayerValue(GetOutParameterElementType(method.GetParameters()[2]));

                    InvokeCacheApplyRestraintSetNoBlock(visualSet, activeLayers, enactor);
                }
                else
                {
                    ChatPrintError("RestraintManager.Apply returned null visual set.");
                }
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak local Apply failed: {ex.InnerException ?? ex}");
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak local Apply invoke failed: {ex}");
            }
        }

        private void InvokeLocalRemove(string enactor)
        {
            try
            {
                var manager = context.RestraintManager;
                var managerType = manager.GetType();

                var method = managerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "Remove")
                            return false;

                        var p = m.GetParameters();

                        return p.Length == 3 &&
                               p[0].ParameterType == typeof(string) &&
                               p[1].IsOut &&
                               p[2].IsOut;
                    });

                if (method == null)
                {
                    ChatPrintError("RestraintManager.Remove(string, out RestraintSet, out RestraintLayer) was not found.");
                    context.DumpMethods(managerType, "Remove");
                    return;
                }

                var args = new object?[] { enactor, null, null };
                var result = method.Invoke(manager, args);

                ChatPrint($"GagSpeak local Remove returned: {result}");

                var visualSet = args[1];
                var removedLayers = args[2] ?? CreateNoneLayerValue(GetOutParameterElementType(method.GetParameters()[2]));

                if (visualSet != null)
                {
                    var label = context.GetPropertyValue(visualSet, "Label") as string ?? "<unknown>";
                    ChatPrint($"GagSpeak local removed visual set: {label}");

                    InvokeCacheRemoveRestraintSetNoBlock(visualSet, removedLayers);
                }
                else
                {
                    ChatPrintError("RestraintManager.Remove returned null visual set.");
                }
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak local Remove failed: {ex.InnerException ?? ex}");
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak local Remove invoke failed: {ex}");
            }
        }

        private object? GetCacheStateManager()
        {
            var cache = context.TryResolveServiceByTypeName("CacheStateManager");

            if (cache == null)
                ChatPrintError("Could not resolve GagSpeak CacheStateManager.");

            return cache;
        }

        private void InvokeCacheApplyRestraintSetNoBlock(object restraintSet, object? activeLayers, string enactor)
        {
            try
            {
                var cache = GetCacheStateManager();
                if (cache == null)
                    return;

                var method = FindCacheRestraintMethod(cache, isApply: true);
                if (method == null)
                {
                    ChatPrintError("No CacheStateManager apply/add restraint-set method was found.");
                    DumpCacheRestraintMethods(cache);
                    return;
                }

                var args = BuildCacheMethodArgs(method, restraintSet, activeLayers, enactor);
                if (args == null)
                {
                    ChatPrintError($"Could not build args for cache method {method.Name}.");
                    DumpCacheMethod(method);
                    return;
                }

                var result = method.Invoke(cache, args);

                if (result is Task task)
                {
                    _ = task.ContinueWith(t =>
                    {
                        if (t.Exception != null)
                            ChatPrintError($"GagSpeak cache {method.Name} failed: {t.Exception.GetBaseException()}");
                        else
                            ChatPrint($"GagSpeak cache {method.Name} completed for restraint set.");
                    });

                    return;
                }

                ChatPrint($"GagSpeak cache {method.Name} invoked for restraint set.");
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak cache restraint apply failed: {ex.InnerException ?? ex}");
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak cache restraint apply invoke failed: {ex}");
            }
        }

        private void InvokeCacheRemoveRestraintSetNoBlock(object restraintSet, object? removedLayers)
        {
            try
            {
                var cache = GetCacheStateManager();
                if (cache == null)
                    return;

                var method = FindCacheRestraintMethod(cache, isApply: false);
                if (method == null)
                {
                    ChatPrintError("No CacheStateManager remove restraint-set method was found.");
                    DumpCacheRestraintMethods(cache);
                    return;
                }

                var args = BuildCacheMethodArgs(method, restraintSet, removedLayers, string.Empty);
                if (args == null)
                {
                    ChatPrintError($"Could not build args for cache method {method.Name}.");
                    DumpCacheMethod(method);
                    return;
                }

                var result = method.Invoke(cache, args);

                if (result is Task task)
                {
                    _ = task.ContinueWith(t =>
                    {
                        if (t.Exception != null)
                            ChatPrintError($"GagSpeak cache {method.Name} failed: {t.Exception.GetBaseException()}");
                        else
                            ChatPrint($"GagSpeak cache {method.Name} completed for restraint set.");
                    });

                    return;
                }

                ChatPrint($"GagSpeak cache {method.Name} invoked for restraint set.");
            }
            catch (TargetInvocationException ex)
            {
                ChatPrintError($"GagSpeak cache restraint remove failed: {ex.InnerException ?? ex}");
            }
            catch (Exception ex)
            {
                ChatPrintError($"GagSpeak cache restraint remove invoke failed: {ex}");
            }
        }

        private static MethodInfo? FindCacheRestraintMethod(object cache, bool isApply)
        {
            var methods = cache.GetType().GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            return methods.FirstOrDefault(m =>
            {
                var name = m.Name;

                if (!name.Contains("Restraint", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (name.Contains("Restriction", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (isApply)
                {
                    return name.Contains("Add", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("Apply", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("Update", StringComparison.OrdinalIgnoreCase);
                }

                return name.Contains("Remove", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Rem", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Clear", StringComparison.OrdinalIgnoreCase);
            });
        }

        private object?[]? BuildCacheMethodArgs(MethodInfo method, object restraintSet, object? layers, string enactor)
        {
            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var t = p.ParameterType;

                if (t.IsByRef)
                    t = t.GetElementType()!;

                if (t.Name == "RestraintSet" || t.IsInstanceOfType(restraintSet))
                {
                    args[i] = restraintSet;
                    continue;
                }

                if (layers != null)
                {
                    if (t.IsInstanceOfType(layers))
                    {
                        args[i] = layers;
                        continue;
                    }

                    if (t.IsEnum && layers.GetType().IsEnum && string.Equals(t.Name, layers.GetType().Name, StringComparison.Ordinal))
                    {
                        args[i] = Enum.ToObject(t, Convert.ToUInt64(layers));
                        continue;
                    }

                    if (t.Name == "RestraintLayer")
                    {
                        var converted = CreateLayerValue(t, layers);
                        if (converted != null)
                        {
                            args[i] = converted;
                            continue;
                        }
                    }
                }

                if (t == typeof(string))
                {
                    args[i] = enactor;
                    continue;
                }

                if (t == typeof(int))
                {
                    args[i] = 0;
                    continue;
                }

                if (t == typeof(bool))
                {
                    args[i] = false;
                    continue;
                }

                if (t.IsEnum)
                {
                    args[i] = Enum.GetValues(t).GetValue(0);
                    continue;
                }

                if (t.IsValueType)
                {
                    args[i] = Activator.CreateInstance(t);
                    continue;
                }

                args[i] = null;
            }

            return args;
        }

        private void DumpCacheRestraintMethods(object cache)
        {
            if (!DebugLog)
                return;

            foreach (var method in cache.GetType().GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!method.Name.Contains("Restraint", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (method.Name.Contains("Restriction", StringComparison.OrdinalIgnoreCase))
                    continue;

                DumpCacheMethod(method);
            }
        }

        private void DumpCacheMethod(MethodInfo method)
        {
            if (!DebugLog)
                return;

            var parameters = string.Join(
                ", ",
                method.GetParameters().Select(p =>
                    $"{(p.IsOut ? "out " : string.Empty)}{p.ParameterType.FullName} {p.Name}"));

            ChatPrintError($"Cache method: {method.Name}({parameters}) -> {method.ReturnType.FullName}");
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

        private static Type? GetMemberType(Type type, string propertyOrFieldName)
        {
            var prop = type.GetProperty(
                propertyOrFieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (prop != null)
                return prop.PropertyType;

            var field = type.GetField(
                propertyOrFieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            return field?.FieldType;
        }

        private static Type? GetOutParameterElementType(ParameterInfo parameter)
        {
            if (!parameter.ParameterType.IsByRef)
                return parameter.ParameterType;

            return parameter.ParameterType.GetElementType();
        }

        private static object? CreateNoneLayerValue(Type? layerType)
        {
            if (layerType == null)
                return null;

            if (!layerType.IsEnum)
                return null;

            try
            {
                if (Enum.GetNames(layerType).Any(n => string.Equals(n, "None", StringComparison.Ordinal)))
                    return Enum.Parse(layerType, "None");
            }
            catch
            {
                // ignored
            }

            try
            {
                return Enum.ToObject(layerType, 0);
            }
            catch
            {
                return null;
            }
        }

        private static object? CreateLayerValue(Type targetLayerType, object sourceLayer)
        {
            if (!targetLayerType.IsEnum)
                return null;

            try
            {
                if (sourceLayer.GetType().IsEnum)
                    return Enum.ToObject(targetLayerType, Convert.ToUInt64(sourceLayer));

                return Enum.ToObject(targetLayerType, sourceLayer);
            }
            catch
            {
                return CreateNoneLayerValue(targetLayerType);
            }
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
