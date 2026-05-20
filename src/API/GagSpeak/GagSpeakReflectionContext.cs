using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace SayusGagExtender.API.GagSpeak
{
    public sealed class GagSpeakReflectionContext
    {
        private readonly Plugin plugin;

        private Type? gagSpeakType;
        private FieldInfo? hostField;
        private object? gagSpeakInstance;

        private Type? restraintManagerType;
        private Type? mainHubType;
        private Type? restraintLayerType;
        private Type? pushClientActiveRestraintType;

        private object? restraintManager;
        private object? mainHub;

        private Type? restrictionManagerType;
        private Type? pushClientActiveRestrictionType;

        private object? restrictionManager;

        private Type? gagRestrictionManagerType;
        private Type? pushClientActiveGagSlotType;

        private object? gagRestrictionManager;

        private bool debugLogEnabled = false;

        private static readonly TimeSpan MissingGagSpeakRetryCooldown = TimeSpan.FromSeconds(10);
        private DateTime nextMissingGagSpeakCheckUtc = DateTime.MinValue;

        private void ChatPrint(string message)
        {
            if (debugLogEnabled)
                Plugin.ChatGui.Print(message);
        }

        private void ChatPrintError(string message)
        {
            if (debugLogEnabled)
                Plugin.ChatGui.PrintError(message);
        }
        public class GagSpeakItem
        {
            public Guid Id { get; set; } = Guid.Empty;
            public string Name { get; set; } = string.Empty;
        }
        private sealed class GagSpeakTypeSet
        {
            public required Assembly Assembly { get; init; }
            public required Type GagSpeakType { get; init; }
            public required FieldInfo HostField { get; init; }

            public required Type RestraintManagerType { get; init; }
            public required Type RestrictionManagerType { get; init; }
            public required Type GagRestrictionManagerType { get; init; }

            public required Type MainHubType { get; init; }
            public required Type RestraintLayerType { get; init; }

            public required Type PushClientActiveRestraintType { get; init; }
            public required Type PushClientActiveRestrictionType { get; init; }
            public required Type PushClientActiveGagSlotType { get; init; }
        }

        public GagSpeakReflectionContext(Plugin plugin)
        {
            this.plugin = plugin;
        }

        public Type GagSpeakType => gagSpeakType!;

        public Type RestraintManagerType => restraintManagerType!;
        public Type MainHubType => mainHubType!;
        public Type RestraintLayerType => restraintLayerType!;
        public Type PushClientActiveRestraintType => pushClientActiveRestraintType!;

        public object GagSpeakInstance => gagSpeakInstance!;
        public object RestraintManager => restraintManager!;
        public object MainHub => mainHub!;

        public Type RestrictionManagerType => restrictionManagerType!;
        public Type PushClientActiveRestrictionType => pushClientActiveRestrictionType!;
        public object RestrictionManager => restrictionManager!;

        public Type GagRestrictionManagerType => gagRestrictionManagerType!;
        public Type PushClientActiveGagSlotType => pushClientActiveGagSlotType!;
        public object GagRestrictionManager => gagRestrictionManager!;

        // Compatibility aliases for existing API code using context.GagManager / PushClientActiveGagType.
        public Type GagManagerType => gagRestrictionManagerType!;
        public object GagManager => gagRestrictionManager!;
        public Type PushClientActiveGagType => pushClientActiveGagSlotType!;

        public bool EnsureReady()
        {
            ChatPrint("Ensuring GagSpeakReflectionContext is ready...");

            if (CachedServicesStillUsable())
                return true;

            ChatPrint("Cached GagSpeak services are not usable.");

            if (DateTime.UtcNow < nextMissingGagSpeakCheckUtc)
                return false;

            ChatPrint("Attempting to find and cache live GagSpeak services...");
            return TryFindAndCacheLiveGagSpeakServices();
        }

        public void ResetCachedServices()
        {
            gagSpeakInstance = null;
            restraintManager = null;
            restrictionManager = null;
            gagRestrictionManager = null;
            mainHub = null;
        }

        private void ResetCachedTypes()
        {
            gagSpeakType = null;
            hostField = null;

            restraintManagerType = null;
            mainHubType = null;
            restraintLayerType = null;
            pushClientActiveRestraintType = null;

            restrictionManagerType = null;
            pushClientActiveRestrictionType = null;

            gagRestrictionManagerType = null;
            pushClientActiveGagSlotType = null;
        }

        private bool CachedServicesStillUsable()
        {
            if (gagSpeakInstance == null ||
                hostField == null ||
                restraintManager == null ||
                restrictionManager == null ||
                gagRestrictionManager == null ||
                mainHub == null ||
                restraintManagerType == null ||
                restrictionManagerType == null ||
                gagRestrictionManagerType == null ||
                mainHubType == null)
            {
                return false;
            }

            try
            {
                var host = hostField.GetValue(gagSpeakInstance);
                if (host == null)
                    return false;

                return TryResolveServicesFromHost(
                           host,
                           restraintManagerType,
                           restrictionManagerType,
                           gagRestrictionManagerType,
                           mainHubType,
                           out var currentRestraintManager,
                           out var currentRestrictionManager,
                           out var currentGagRestrictionManager,
                           out var currentMainHub)
                       && ReferenceEquals(restraintManager, currentRestraintManager)
                       && ReferenceEquals(restrictionManager, currentRestrictionManager)
                       && ReferenceEquals(gagRestrictionManager, currentGagRestrictionManager)
                       && ReferenceEquals(mainHub, currentMainHub);
            }
            catch
            {
                return false;
            }
        }

        private bool TryFindAndCacheLiveGagSpeakServices()
        {
            ResetCachedServices();

            var sawCandidate = false;

            foreach (var candidate in EnumeratePossibleGagSpeakInstances())
            {
                if (candidate == null)
                    continue;

                var candidateType = candidate.GetType();

                if (!string.Equals(candidateType.FullName, "GagSpeak.GagSpeak", StringComparison.Ordinal))
                    continue;

                sawCandidate = true;

                var typeSet = TryCreateTypeSetFromAssembly(candidateType.Assembly);
                if (typeSet == null)
                {
                    ChatPrintError($"Could not build GagSpeak type set from {candidateType.Assembly.GetName().Name}.");
                    continue;
                }

                object? host = null;

                try
                {
                    host = typeSet.HostField.GetValue(candidate);
                }
                catch
                {
                    continue;
                }

                ChatPrint(
                    $"GagSpeak candidate: {candidateType.FullName}, asm: {candidateType.Assembly.GetName().Name}, _host null: {host == null}, host type: {host?.GetType().FullName ?? "null"}");

                if (host == null)
                    continue;

                if (!TryResolveServicesFromHost(
                        host,
                        typeSet.RestraintManagerType,
                        typeSet.RestrictionManagerType,
                        typeSet.GagRestrictionManagerType,
                        typeSet.MainHubType,
                        out var foundRestraintManager,
                        out var foundRestrictionManager,
                        out var foundGagRestrictionManager,
                        out var foundMainHub))
                {
                    ChatPrint("Skipping GagSpeak candidate because its host/services are not usable.");
                    continue;
                }

                gagSpeakType = typeSet.GagSpeakType;
                hostField = typeSet.HostField;

                restraintManagerType = typeSet.RestraintManagerType;
                restrictionManagerType = typeSet.RestrictionManagerType;
                gagRestrictionManagerType = typeSet.GagRestrictionManagerType;
                mainHubType = typeSet.MainHubType;

                restraintLayerType = typeSet.RestraintLayerType;
                pushClientActiveRestraintType = typeSet.PushClientActiveRestraintType;
                pushClientActiveRestrictionType = typeSet.PushClientActiveRestrictionType;
                pushClientActiveGagSlotType = typeSet.PushClientActiveGagSlotType;

                gagSpeakInstance = candidate;
                restraintManager = foundRestraintManager;
                restrictionManager = foundRestrictionManager;
                gagRestrictionManager = foundGagRestrictionManager;
                mainHub = foundMainHub;

                nextMissingGagSpeakCheckUtc = DateTime.MinValue;

                ChatPrint("Hooked live GagSpeak instance.");
                return true;
            }

            nextMissingGagSpeakCheckUtc = DateTime.UtcNow + MissingGagSpeakRetryCooldown;

            ChatPrintError(
                sawCandidate
                    ? "Found GagSpeak.GagSpeak instance(s), but none had usable live services. Will retry in 10 seconds."
                    : "Could not find any GagSpeak.GagSpeak instance. Will retry in 10 seconds.");

            return false;
        }

        private GagSpeakTypeSet? TryCreateTypeSetFromAssembly(Assembly asm)
        {
            try
            {
                var gsType = asm.GetType("GagSpeak.GagSpeak", throwOnError: false);
                if (gsType == null)
                    return null;

                var host = gsType.GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance);
                if (host == null)
                    return null;

                var projectTypes = SafeGetTypes(asm);

                var restraintMgr =
                    asm.GetType("GagSpeak.State.Managers.RestraintManager", throwOnError: false) ??
                    projectTypes.FirstOrDefault(t => t.Name == "RestraintManager");

                var restrictionMgr =
                    asm.GetType("GagSpeak.State.Managers.RestrictionManager", throwOnError: false) ??
                    projectTypes.FirstOrDefault(t => t.Name == "RestrictionManager");

                var gagRestrictionMgr =
                    asm.GetType("GagSpeak.State.Managers.GagRestrictionManager", throwOnError: false) ??
                    projectTypes.FirstOrDefault(t => t.Name == "GagRestrictionManager");

                var mainHubSvc =
                    asm.GetType("GagSpeak.WebAPI.MainHub", throwOnError: false) ??
                    projectTypes.FirstOrDefault(t => t.Name == "MainHub");


                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a =>
                    {
                        var name = a.GetName().Name ?? string.Empty;
                        return name.Contains("GagSpeak", StringComparison.OrdinalIgnoreCase) ||
                               name.Contains("GagspeakAPI", StringComparison.OrdinalIgnoreCase) ||
                               name.Contains("ProjectGagSpeak", StringComparison.OrdinalIgnoreCase);
                    })
                    .SelectMany(SafeGetTypes)
                    .ToArray();

                var layer =
                    FindTypeByNames(
                        allTypes,
                        mustBeEnum: true,
                        "GagspeakAPI.Attributes.RestraintLayer",
                        "GagspeakAPI.Data.RestraintLayer",
                        "GagspeakAPI.Dto.Permissions.RestraintLayer",
                        "GagSpeak.API.Data.RestraintLayer",
                        "GagSpeak.API.Attributes.RestraintLayer",
                        "RestraintLayer");

                var pushRestraint =
                    FindTypeByNames(
                        allTypes,
                        mustBeEnum: false,
                        "GagspeakAPI.Network.PushClientActiveRestraint",
                        "GagspeakAPI.Data.PushClientActiveRestraint",
                        "GagspeakAPI.Dto.PushClientActiveRestraint",
                        "GagspeakAPI.Dto.Restraints.PushClientActiveRestraint",
                        "PushClientActiveRestraint");

                var pushRestriction =
                    FindTypeByNames(
                        allTypes,
                        mustBeEnum: false,
                        "GagspeakAPI.Network.PushClientActiveRestriction",
                        "GagspeakAPI.Data.PushClientActiveRestriction",
                        "GagspeakAPI.Dto.PushClientActiveRestriction",
                        "GagspeakAPI.Dto.Restrictions.PushClientActiveRestriction",
                        "PushClientActiveRestriction");

                var pushGagSlot =
                    FindTypeByNames(
                        allTypes,
                        mustBeEnum: false,
                        "GagspeakAPI.Network.PushClientActiveGagSlot",
                        "GagspeakAPI.Data.PushClientActiveGagSlot",
                        "GagspeakAPI.Dto.PushClientActiveGagSlot",
                        "GagspeakAPI.Dto.Gags.PushClientActiveGagSlot",
                        "PushClientActiveGagSlot");

                if (restraintMgr == null ||
                    restrictionMgr == null ||
                    gagRestrictionMgr == null ||
                    mainHubSvc == null ||
                    layer == null ||
                    pushRestraint == null ||
                    pushRestriction == null ||
                    pushGagSlot == null)
                {
                    ChatPrintError(
                        $"Missing types: " +
                        $"RestraintManager={restraintMgr != null}, " +
                        $"RestrictionManager={restrictionMgr != null}, " +
                        $"GagRestrictionManager={gagRestrictionMgr != null}, " +
                        $"MainHub={mainHubSvc != null}, " +
                        $"RestraintLayer={layer != null}, " +
                        $"PushClientActiveRestraint={pushRestraint != null}, " +
                        $"PushClientActiveRestriction={pushRestriction != null}, " +
                        $"PushClientActiveGagSlot={pushGagSlot != null}");

                    DumpInterestingGagSpeakApiTypes(allTypes);
                    return null;
                }

                ChatPrint(
                    $"Type set OK: " +
                    $"Layer={layer.FullName}, " +
                    $"PushRestraint={pushRestraint.FullName}, " +
                    $"PushRestriction={pushRestriction.FullName}, " +
                    $"PushGagSlot={pushGagSlot.FullName}, " +
                    $"GagRestrictionManager={gagRestrictionMgr.FullName}");

                return new GagSpeakTypeSet
                {
                    Assembly = asm,
                    GagSpeakType = gsType,
                    HostField = host,

                    RestraintManagerType = restraintMgr,
                    RestrictionManagerType = restrictionMgr,
                    GagRestrictionManagerType = gagRestrictionMgr,

                    MainHubType = mainHubSvc,
                    RestraintLayerType = layer,

                    PushClientActiveRestraintType = pushRestraint,
                    PushClientActiveRestrictionType = pushRestriction,
                    PushClientActiveGagSlotType = pushGagSlot
                };
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to create GagSpeak type set from {asm.GetName().Name}: {ex.Message}");
                return null;
            }
        }

        private static Type? FindTypeByNames(Type[] types, bool mustBeEnum, params string[] names)
        {
            foreach (var name in names)
            {
                var exact = types.FirstOrDefault(t =>
                    string.Equals(t.FullName, name, StringComparison.Ordinal) ||
                    string.Equals(t.Name, name, StringComparison.Ordinal));

                if (exact != null && (!mustBeEnum || exact.IsEnum))
                    return exact;
            }

            return null;
        }

        private void DumpInterestingGagSpeakApiTypes(Type[] types)
        {
            try
            {
                ChatPrintError("Interesting GagSpeak/API types:");

                foreach (var type in types
                             .Where(t =>
                             {
                                 var n = t.FullName ?? t.Name;
                                 return n.Contains("RestraintLayer", StringComparison.OrdinalIgnoreCase) ||
                                        n.Contains("PushClient", StringComparison.OrdinalIgnoreCase) ||
                                        n.Contains("ActiveRestraint", StringComparison.OrdinalIgnoreCase) ||
                                        n.Contains("ActiveRestriction", StringComparison.OrdinalIgnoreCase) ||
                                        n.Contains("ActiveGag", StringComparison.OrdinalIgnoreCase) ||
                                        n.Contains("GagRestrictionManager", StringComparison.OrdinalIgnoreCase) ||
                                        n.Contains("GagRestrictionsManager", StringComparison.OrdinalIgnoreCase) ||
                                        n.Contains("GagManager", StringComparison.OrdinalIgnoreCase) ||
                                        n.Contains("GagItem", StringComparison.OrdinalIgnoreCase) ||
                                        n.Contains("GagSlot", StringComparison.OrdinalIgnoreCase) ||
                                        n.Contains("GagType", StringComparison.OrdinalIgnoreCase) ||
                                        n.Contains("GarblerRestriction", StringComparison.OrdinalIgnoreCase);
                             })
                             .Take(120))
                {
                    ChatPrintError($"- {type.FullName}");
                }
            }
            catch
            {
                // ignored
            }
        }

        private void DumpInterestingTypesFromAssembly(Assembly asm)
        {
            try
            {
                ChatPrintError($"Interesting types in {asm.GetName().Name}:");

                foreach (var type in SafeGetTypes(asm)
                             .Where(t =>
                                 t.FullName?.Contains("Restraint", StringComparison.OrdinalIgnoreCase) == true ||
                                 t.FullName?.Contains("Restriction", StringComparison.OrdinalIgnoreCase) == true ||
                                 t.FullName?.Contains("Gag", StringComparison.OrdinalIgnoreCase) == true ||
                                 t.FullName?.Contains("Garbler", StringComparison.OrdinalIgnoreCase) == true ||
                                 t.FullName?.Contains("MainHub", StringComparison.OrdinalIgnoreCase) == true ||
                                 t.FullName?.Contains("PushClient", StringComparison.OrdinalIgnoreCase) == true)
                             .Take(80))
                {
                    ChatPrintError($"- {type.FullName}");
                }
            }
            catch
            {
                // ignored
            }
        }

        private IEnumerable<GagSpeakTypeSet> EnumerateGagSpeakTypeSets()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var asmName = asm.GetName().Name ?? string.Empty;

                if (!asmName.Contains("GagSpeak", StringComparison.OrdinalIgnoreCase) &&
                    !asmName.Contains("ProjectGagSpeak", StringComparison.OrdinalIgnoreCase) &&
                    !asmName.Contains("GagspeakAPI", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Type? gsType;
                FieldInfo? host;
                Type? restraintMgr;
                Type? restrictionMgr;
                Type? gagRestrictionMgr;
                Type? mainHubSvc;
                Type? layer;
                Type? pushRestraint;
                Type? pushRestriction;
                Type? pushGagSlot;

                try
                {
                    gsType = asm.GetType("GagSpeak.GagSpeak", throwOnError: false);
                    if (gsType == null)
                        continue;

                    host = gsType.GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (host == null)
                        continue;

                    var types = AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a =>
                        {
                            var name = a.GetName().Name ?? string.Empty;
                            return name.Contains("GagSpeak", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("ProjectGagSpeak", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("GagspeakAPI", StringComparison.OrdinalIgnoreCase);
                        })
                        .SelectMany(SafeGetTypes)
                        .ToArray();

                    restraintMgr =
                        asm.GetType("GagSpeak.State.Managers.RestraintManager", throwOnError: false) ??
                        types.FirstOrDefault(t => t.Name == "RestraintManager");

                    restrictionMgr =
                        asm.GetType("GagSpeak.State.Managers.RestrictionManager", throwOnError: false) ??
                        types.FirstOrDefault(t => t.Name == "RestrictionManager");

                    gagRestrictionMgr =
                        asm.GetType("GagSpeak.State.Managers.GagRestrictionManager", throwOnError: false) ??
                        types.FirstOrDefault(t => t.Name == "GagRestrictionManager");

                    mainHubSvc =
                        asm.GetType("GagSpeak.WebAPI.MainHub", throwOnError: false) ??
                        types.FirstOrDefault(t => t.Name == "MainHub");

                    layer =
                        asm.GetType("GagspeakAPI.Attributes.RestraintLayer", throwOnError: false) ??
                        asm.GetType("GagspeakAPI.Data.RestraintLayer", throwOnError: false) ??
                        types.FirstOrDefault(t => t.Name == "RestraintLayer" && t.IsEnum);

                    pushRestraint = types.FirstOrDefault(t => t.Name == "PushClientActiveRestraint");
                    pushRestriction = types.FirstOrDefault(t => t.Name == "PushClientActiveRestriction");
                    pushGagSlot = types.FirstOrDefault(t => t.Name == "PushClientActiveGagSlot");
                }
                catch
                {
                    continue;
                }

                if (restraintMgr == null ||
                    restrictionMgr == null ||
                    gagRestrictionMgr == null ||
                    mainHubSvc == null ||
                    layer == null ||
                    pushRestraint == null ||
                    pushRestriction == null ||
                    pushGagSlot == null)
                {
                    continue;
                }

                yield return new GagSpeakTypeSet
                {
                    Assembly = asm,
                    GagSpeakType = gsType,
                    HostField = host,

                    RestraintManagerType = restraintMgr,
                    RestrictionManagerType = restrictionMgr,
                    GagRestrictionManagerType = gagRestrictionMgr,

                    MainHubType = mainHubSvc,
                    RestraintLayerType = layer,

                    PushClientActiveRestraintType = pushRestraint,
                    PushClientActiveRestrictionType = pushRestriction,
                    PushClientActiveGagSlotType = pushGagSlot
                };
            }
        }

        private bool TryResolveServicesFromHost(
            object host,
            Type restraintMgrType,
            Type restrictionMgrType,
            Type gagRestrictionMgrType,
            Type mainHubSvcType,
            out object? foundRestraintManager,
            out object? foundRestrictionManager,
            out object? foundGagRestrictionManager,
            out object? foundMainHub)
        {
            foundRestraintManager = null;
            foundRestrictionManager = null;
            foundGagRestrictionManager = null;
            foundMainHub = null;

            IServiceProvider? services;

            try
            {
                if (host is IHost typedHost)
                {
                    services = typedHost.Services;
                }
                else
                {
                    var servicesProp = host.GetType().GetProperty(
                        "Services",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    services = servicesProp?.GetValue(host) as IServiceProvider;
                }
            }
            catch (ObjectDisposedException)
            {
                ChatPrint("Skipping disposed GagSpeak host while reading Services.");
                return false;
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to read GagSpeak host services: {ex.Message}");
                return false;
            }

            if (services == null)
            {
                ChatPrintError($"Could not get Services from GagSpeak host. Host type: {host.GetType().FullName}");
                return false;
            }

            try
            {
                foundRestraintManager = services.GetService(restraintMgrType);
                foundRestrictionManager = services.GetService(restrictionMgrType);
                foundGagRestrictionManager = services.GetService(gagRestrictionMgrType);
                foundMainHub = services.GetService(mainHubSvcType);
            }
            catch (ObjectDisposedException)
            {
                ChatPrint("Skipping disposed GagSpeak IServiceProvider.");
                return false;
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to resolve GagSpeak services: {ex.Message}");
                return false;
            }

            if (foundRestraintManager == null)
            {
                ChatPrint("Candidate host did not contain RestraintManager.");
                return false;
            }

            if (foundRestrictionManager == null)
            {
                ChatPrint("Candidate host did not contain RestrictionManager.");
                return false;
            }

            if (foundGagRestrictionManager == null)
            {
                ChatPrint("Candidate host did not contain GagRestrictionManager.");
                return false;
            }

            if (foundMainHub == null)
            {
                ChatPrint("Candidate host did not contain MainHub.");
                return false;
            }

            return true;
        }

        private IEnumerable<object?> EnumeratePossibleGagSpeakInstances()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? gsType = null;

                try
                {
                    gsType = asm.GetType("GagSpeak.GagSpeak", throwOnError: false);
                }
                catch
                {
                    // ignored
                }

                if (gsType == null)
                    continue;

                foreach (var field in gsType.GetFields(
                             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    object? value = null;

                    try
                    {
                        value = field.GetValue(null);
                    }
                    catch
                    {
                        // ignored
                    }

                    if (value != null)
                        yield return value;
                }
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? ckHostType = null;

                try
                {
                    ckHostType =
                        asm.GetType("CkCommons.CkCommonsHost", throwOnError: false) ??
                        asm.GetType("CkCommonsHost", throwOnError: false);
                }
                catch
                {
                    // ignored
                }

                if (ckHostType == null)
                    continue;

                foreach (var value in WalkStaticFieldGraphForGagSpeak(ckHostType, maxDepth: 5))
                    yield return value;
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var asmName = asm.GetName().Name ?? string.Empty;

                if (!asmName.Contains("GagSpeak", StringComparison.OrdinalIgnoreCase) &&
                    !asmName.Contains("CkCommons", StringComparison.OrdinalIgnoreCase) &&
                    !asmName.Contains("ProjectGagSpeak", StringComparison.OrdinalIgnoreCase) &&
                    !asmName.Contains("GagspeakAPI", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var type in SafeGetTypes(asm))
                {
                    foreach (var value in WalkStaticFieldGraphForGagSpeak(type, maxDepth: 4))
                        yield return value;
                }
            }
        }

        private IEnumerable<object?> WalkStaticFieldGraphForGagSpeak(Type rootType, int maxDepth)
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

            foreach (var field in rootType.GetFields(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                object? value = null;

                try
                {
                    value = field.GetValue(null);
                }
                catch
                {
                    // ignored
                }

                foreach (var found in WalkObjectFieldsOnly(value, maxDepth, seen))
                    yield return found;
            }
        }

        private IEnumerable<object?> WalkObjectFieldsOnly(object? obj, int depth, HashSet<object> seen)
        {
            if (obj == null || depth < 0)
                yield break;

            var type = obj.GetType();

            if (!type.IsValueType && !seen.Add(obj))
                yield break;

            if (string.Equals(type.FullName, "GagSpeak.GagSpeak", StringComparison.Ordinal))
            {
                yield return obj;
                yield break;
            }

            if (obj is string)
                yield break;

            if (type.IsPrimitive || type.IsEnum)
                yield break;

            if (obj is Delegate)
                yield break;

            var ns = type.Namespace ?? string.Empty;

            if (ns.StartsWith("System", StringComparison.Ordinal) ||
                ns.StartsWith("Microsoft", StringComparison.Ordinal) ||
                ns.StartsWith("Dalamud", StringComparison.Ordinal) ||
                ns.StartsWith("FFXIVClientStructs", StringComparison.Ordinal) ||
                ns.StartsWith("Lumina", StringComparison.Ordinal) ||
                ns.StartsWith("CkCommons.RichText", StringComparison.Ordinal))
            {
                yield break;
            }

            foreach (var field in type.GetFields(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.FieldType.IsPointer || field.FieldType.IsByRef)
                    continue;

                object? value = null;

                try
                {
                    value = field.GetValue(obj);
                }
                catch
                {
                    // ignored
                }

                foreach (var found in WalkObjectFieldsOnly(value, depth - 1, seen))
                    yield return found;
            }
        }
        public async Task<bool> RefreshGagSpeakVisualsAsync(bool redraw = true)
        {
            if (!EnsureReady())
                return false;

            var cacheStateManager =
                TryResolveServiceByTypeName("CacheStateManager") ??
                TryResolveServiceByTypeName("Cache", "State", "Manager");

            if (cacheStateManager == null)
                return false;

            var anyInvoked = false;

            anyInvoked |= await TryInvokeFieldMethodAsync(cacheStateManager, "_glamourHandler", "UpdateCaches");
            anyInvoked |= await TryInvokeFieldMethodAsync(cacheStateManager, "_modHandler", "UpdateModCache");
            anyInvoked |= await TryInvokeFieldMethodAsync(cacheStateManager, "_lociHandler", "UpdateLociCache");
            anyInvoked |= await TryInvokeFieldMethodAsync(cacheStateManager, "_cplusHandler", "UpdateProfileCache");
            anyInvoked |= await TryInvokeFieldMethodAsync(cacheStateManager, "_traitsHandler", "UpdateTraitCache");
            anyInvoked |= await TryInvokeFieldMethodAsync(cacheStateManager, "_arousalHandler", "UpdateFinalCache");
            anyInvoked |= await TryInvokeFieldMethodAsync(cacheStateManager, "_overlayHandler", "UpdateCaches");

            if (redraw)
                TryInvokeFieldMethod(cacheStateManager, "_redrawAssist", "RedrawObject");

            return anyInvoked;
        }

        private static async Task<bool> TryInvokeFieldMethodAsync(object owner, string fieldName, string methodName)
        {
            try
            {
                var field = owner.GetType().GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var target = field?.GetValue(owner);
                if (target == null)
                    return false;

                var method = target.GetType().GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);

                if (method == null)
                    return false;

                var result = method.Invoke(target, null);

                if (result is Task task)
                    await task.ConfigureAwait(false);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeFieldMethod(object owner, string fieldName, string methodName)
        {
            try
            {
                var field = owner.GetType().GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var target = field?.GetValue(owner);
                if (target == null)
                    return false;

                var method = target.GetType().GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);

                if (method == null)
                    return false;

                method.Invoke(target, null);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public object? GetNoneRestraintLayerValue()
        {
            if (!EnsureReady())
                return null;

            try
            {
                return Enum.Parse(restraintLayerType!, "None");
            }
            catch
            {
                try
                {
                    foreach (var value in Enum.GetValues(restraintLayerType!))
                    {
                        if (Convert.ToUInt64(value) == 0)
                            return value;
                    }
                }
                catch
                {
                    // ignored
                }
            }

            return null;
        }

        public object? GetPropertyValue(object obj, string name)
            => GetProp(obj, name);

        public bool SetPropertyOrFieldValue(object obj, string name, object? value)
            => SetPropOrFieldIfExists(obj, name, value);

        public Type[] SafeGetAssemblyTypes(Assembly asm)
            => SafeGetTypes(asm);

        public object? TryResolveServiceByTypeName(params string[] typeNameParts)
        {
            if (!EnsureReady())
                return null;

            var host = GetCurrentHost();
            if (host == null)
                return null;

            var services = GetServicesFromHost(host);
            if (services == null)
                return null;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .OrderByDescending(a => ReferenceEquals(a, gagSpeakType?.Assembly))
                .ToArray();

            foreach (var asm in assemblies)
            {
                Type[] types;

                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    var fullName = type.FullName ?? type.Name;

                    if (!typeNameParts.All(p =>
                            fullName.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    object? service = null;

                    try
                    {
                        service = services.GetService(type);
                    }
                    catch (ObjectDisposedException)
                    {
                        ResetCachedServices();
                        return null;
                    }
                    catch
                    {
                        // ignored
                    }

                    if (service != null)
                        return service;
                }
            }

            return null;
        }

        public object? TryResolveService(Type serviceType)
        {
            if (!EnsureReady())
                return null;

            var host = GetCurrentHost();
            if (host == null)
                return null;

            var services = GetServicesFromHost(host);
            if (services == null)
                return null;

            try
            {
                return services.GetService(serviceType);
            }
            catch (ObjectDisposedException)
            {
                ResetCachedServices();
                return null;
            }
            catch
            {
                return null;
            }
        }

        private object? GetCurrentHost()
        {
            if (gagSpeakInstance == null || hostField == null)
                return null;

            try
            {
                return hostField.GetValue(gagSpeakInstance);
            }
            catch
            {
                return null;
            }
        }

        private IServiceProvider? GetServicesFromHost(object host)
        {
            try
            {
                if (host is IHost typedHost)
                    return typedHost.Services;

                var servicesProp = host.GetType().GetProperty(
                    "Services",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                return servicesProp?.GetValue(host) as IServiceProvider;
            }
            catch (ObjectDisposedException)
            {
                ResetCachedServices();
                return null;
            }
            catch
            {
                return null;
            }
        }

        public void DumpEnumValues(Type enumType)
        {
            try
            {
                ChatPrintError($"Enum values for {enumType.FullName}:");

                foreach (var name in Enum.GetNames(enumType))
                    ChatPrintError($"- {name}");
            }
            catch
            {
                // ignored
            }
        }

        public void DumpDtoShape(Type dtoType)
        {
            ChatPrintError($"DTO type: {dtoType.FullName}");

            foreach (var ctor in dtoType.GetConstructors())
            {
                var parameters = string.Join(
                    ", ",
                    ctor.GetParameters().Select(p => $"{p.ParameterType.FullName} {p.Name}"));

                ChatPrintError($"Ctor: ({parameters})");
            }

            foreach (var prop in dtoType.GetProperties(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                ChatPrintError(
                    $"Prop: {prop.PropertyType.FullName} {prop.Name}, CanWrite: {prop.CanWrite}");
            }

            foreach (var field in dtoType.GetFields(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                ChatPrintError(
                    $"Field: {field.FieldType.FullName} {field.Name}, InitOnly: {field.IsInitOnly}");
            }
        }

        public void DumpMethods(Type type, string methodName)
        {
            try
            {
                foreach (var method in type.GetMethods(
                             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                        continue;

                    var parameters = string.Join(
                        ", ",
                        method.GetParameters().Select(p =>
                            $"{(p.IsOut ? "out " : string.Empty)}{p.ParameterType.FullName} {p.Name}"));

                    ChatPrintError($"Method: {method.Name}({parameters})");
                }
            }
            catch
            {
                // ignored
            }
        }

        public void DumpRelevantGagSpeakServicesAndMethods()
        {
            if (!EnsureReady())
                return;

            try
            {
                var host = GetCurrentHost();

                if (host == null)
                {
                    ChatPrintError("Could not dump services: host was null.");
                    return;
                }

                var services = GetServicesFromHost(host);
                if (services == null)
                {
                    ChatPrintError("Could not dump services: Services was null.");
                    return;
                }

                var descriptors = ExtractServiceDescriptors(services);
                if (descriptors == null)
                {
                    ChatPrintError("Could not extract ServiceDescriptors from ServiceProvider.");
                    return;
                }

                foreach (var descriptor in descriptors)
                {
                    var descriptorType = descriptor.GetType();

                    var serviceType = descriptorType
                        .GetProperty("ServiceType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(descriptor) as Type;

                    var implementationType = descriptorType
                        .GetProperty("ImplementationType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(descriptor) as Type;

                    var serviceName = serviceType?.FullName ?? "<null service>";
                    var implName = implementationType?.FullName ?? "<null impl>";

                    if (!IsRelevantServiceName(serviceName) && !IsRelevantServiceName(implName))
                        continue;

                    ChatPrint($"Service: {serviceName} -> {implName}");

                    var typeToDump = implementationType ?? serviceType;
                    if (typeToDump != null)
                        DumpRelevantMethods(typeToDump);
                }
            }
            catch (ObjectDisposedException)
            {
                ResetCachedServices();
                ChatPrintError("Could not dump services: GagSpeak service provider was disposed.");
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to dump relevant GagSpeak services: {ex}");
            }
        }

        private IEnumerable? ExtractServiceDescriptors(object serviceProvider)
        {
            try
            {
                var callSiteFactory = serviceProvider.GetType()
                    .GetProperty("CallSiteFactory", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(serviceProvider);

                if (callSiteFactory == null)
                {
                    callSiteFactory = serviceProvider.GetType()
                        .GetField("<CallSiteFactory>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(serviceProvider);
                }

                if (callSiteFactory == null)
                    return null;

                var descriptors =
                    callSiteFactory.GetType()
                        .GetProperty("Descriptors", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(callSiteFactory)
                    ??
                    callSiteFactory.GetType()
                        .GetField("_descriptors", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(callSiteFactory)
                    ??
                    callSiteFactory.GetType()
                        .GetField("descriptors", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(callSiteFactory);

                return descriptors as IEnumerable;
            }
            catch
            {
                return null;
            }
        }

        private static object? GetProp(object obj, string name)
        {
            try
            {
                return obj.GetType()
                    .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(obj);
            }
            catch
            {
                return null;
            }
        }

        private static bool SetPropOrFieldIfExists(object obj, string name, object? value)
        {
            var type = obj.GetType();

            var prop = type.GetProperty(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (prop != null && prop.CanWrite)
            {
                try
                {
                    prop.SetValue(obj, value);
                    return true;
                }
                catch
                {
                    // ignored
                }
            }

            var field = type.GetField(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (field != null && !field.IsInitOnly)
            {
                try
                {
                    field.SetValue(obj, value);
                    return true;
                }
                catch
                {
                    // ignored
                }
            }

            return false;
        }

        private static Type[] SafeGetTypes(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static bool IsRelevantServiceName(string name)
        {
            return name.Contains("Cache", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Visual", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Mod", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Penumbra", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Restriction", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Gag", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Garbler", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Appearance", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Moodle", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Customize", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Interop", StringComparison.OrdinalIgnoreCase);
        }

        private void DumpRelevantMethods(Type type)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var name = method.Name;

                if (!IsRelevantServiceName(name) &&
                    !name.Contains("Apply", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Remove", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Lock", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Unlock", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Refresh", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Update", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Rebuild", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("State", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parameters = string.Join(
                    ", ",
                    method.GetParameters().Select(p =>
                        $"{(p.IsOut ? "out " : string.Empty)}{p.ParameterType.FullName} {p.Name}"));

                ChatPrint($"  Method: {method.Name}({parameters}) -> {method.ReturnType.FullName}");
            }
        }
    }

    public sealed class ReferenceEqualityComparer : IEqualityComparer<object>
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
}
