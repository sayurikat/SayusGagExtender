using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace SayusGagExtender.API
{
    public sealed class MoodlesApi : IDisposable
    {
        private readonly Plugin plugin;

        private Type? moodlesType;
        private FieldInfo? pluginInstanceField;
        private FieldInfo? ipcProcessorField;

        private MethodInfo? getClientStatusManagerInfoMethod;
        private MethodInfo? getRegisteredMoodlesMethod;
        private MethodInfo? getStatusInfoListMethod;

        private MethodInfo? addOrUpdateMoodleByPlayerMethod;
        private MethodInfo? removeMoodleByPlayerMethod;

        private FieldInfo? statusManagerModifiedField;
        private FieldInfo? statusUpdatedField;

        private Action<nint>? statusManagerModifiedHandler;
        private Action<Guid, bool>? statusUpdatedHandler;

        private Dictionary<Guid, string> lastActiveMoodles = new();

        public event Action<IReadOnlyDictionary<Guid, string>>? ActiveMoodlesChanged;

        public MoodlesApi(Plugin plugin)
        {
            this.plugin = plugin;
            SubscribeToMoodleStatusChanges();
        }
        public bool IsStatusActive(string statusId)
        {
            return Guid.TryParse(statusId, out var guid) && IsStatusActive(guid);
        }

        public bool IsStatusActive(Guid statusId)
        {
            try
            {
                return GetAllActiveMoodles().ContainsKey(statusId);
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to check Moodles status {ex}");
                return false;
            }
        }

        public Dictionary<Guid, string> GetAllMoodles()
        {
            try
            {
                if (!TryCacheReflectionWithRefresh())
                    return new Dictionary<Guid, string>();

                if (TryReadAllMoodles(out var result))
                    return result;

                ClearCache();

                if (!TryCacheReflectionWithRefresh())
                    return new Dictionary<Guid, string>();

                return TryReadAllMoodles(out result)
                    ? result
                    : new Dictionary<Guid, string>();
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to get all Moodles {ex}");
                return new Dictionary<Guid, string>();
            }
        }

        public Dictionary<Guid, string> GetAllActiveMoodles()
        {
            try
            {
                if (!TryCacheReflectionWithRefresh())
                    return new Dictionary<Guid, string>();

                if (TryReadActiveMoodles(out var result))
                    return result;

                ClearCache();

                if (!TryCacheReflectionWithRefresh())
                    return new Dictionary<Guid, string>();

                return TryReadActiveMoodles(out result)
                    ? result
                    : new Dictionary<Guid, string>();
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to get active Moodles {ex}");
                return new Dictionary<Guid, string>();
            }
        }

        public bool ApplyMoodle(string statusId)
        {
            return Guid.TryParse(statusId, out var guid) && ApplyMoodle(guid);
        }

        public bool ApplyMoodle(Guid statusId)
        {
            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            if (localPlayer == null)
                return false;

            return ApplyMoodle(statusId, localPlayer);
        }

        public bool ApplyMoodle(string statusId, IPlayerCharacter player)
        {
            return Guid.TryParse(statusId, out var guid) && ApplyMoodle(guid, player);
        }

        public bool ApplyMoodle(Guid statusId, IPlayerCharacter player)
        {
            try
            {
                if (!TryCacheReflectionWithRefresh())
                    return false;

                if (TryApplyMoodle(statusId, player))
                    return true;

                ClearCache();

                return TryCacheReflectionWithRefresh() && TryApplyMoodle(statusId, player);
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to apply Moodle {statusId}: {ex}");
                return false;
            }
        }

        public bool RemoveMoodle(string statusId)
        {
            return Guid.TryParse(statusId, out var guid) && RemoveMoodle(guid);
        }

        public bool RemoveMoodle(Guid statusId)
        {
            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            if (localPlayer == null)
                return false;

            return RemoveMoodle(statusId, localPlayer);
        }

        public bool RemoveMoodle(string statusId, IPlayerCharacter player)
        {
            return Guid.TryParse(statusId, out var guid) && RemoveMoodle(guid, player);
        }

        public bool RemoveMoodle(Guid statusId, IPlayerCharacter player)
        {
            try
            {
                if (!TryCacheReflectionWithRefresh())
                    return false;

                if (TryRemoveMoodle(statusId, player))
                    return true;

                ClearCache();

                return TryCacheReflectionWithRefresh() && TryRemoveMoodle(statusId, player);
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to remove Moodle {statusId}: {ex}");
                return false;
            }
        }

        public bool SubscribeToMoodleStatusChanges()
        {
            try
            {
                if (statusManagerModifiedHandler != null || statusUpdatedHandler != null)
                    return true;

                if (!TryCacheReflectionWithRefresh())
                    return false;

                var moodlesPlugin = pluginInstanceField!.GetValue(null);
                if (moodlesPlugin == null)
                    return false;

                var ipcProcessor = ipcProcessorField!.GetValue(moodlesPlugin);
                if (ipcProcessor == null)
                    return false;

                lastActiveMoodles = GetAllActiveMoodles();

                statusManagerModifiedHandler = unused =>
                {
                    _ = Plugin.Framework.RunOnFrameworkThread(CheckAndRaiseActiveMoodlesChanged);
                };

                statusUpdatedHandler = (_, _) =>
                {
                    _ = Plugin.Framework.RunOnFrameworkThread(CheckAndRaiseActiveMoodlesChanged);
                };



                AddDelegateToField(ipcProcessor, statusManagerModifiedField, statusManagerModifiedHandler);
                AddDelegateToField(ipcProcessor, statusUpdatedField, statusUpdatedHandler);

                return true;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to subscribe to Moodles changes {ex}");
                statusManagerModifiedHandler = null;
                statusUpdatedHandler = null;
                return false;
            }
        }

        public void UnsubscribeFromMoodleStatusChanges()
        {
            try
            {
                if (!TryCacheReflection())
                    return;

                var moodlesPlugin = pluginInstanceField!.GetValue(null);
                if (moodlesPlugin == null)
                    return;

                var ipcProcessor = ipcProcessorField!.GetValue(moodlesPlugin);
                if (ipcProcessor == null)
                    return;

                if (statusManagerModifiedHandler != null)
                    RemoveDelegateFromField(ipcProcessor, statusManagerModifiedField, statusManagerModifiedHandler);

                if (statusUpdatedHandler != null)
                    RemoveDelegateFromField(ipcProcessor, statusUpdatedField, statusUpdatedHandler);
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to unsubscribe from Moodles changes {ex}");
            }
            finally
            {
                statusManagerModifiedHandler = null;
                statusUpdatedHandler = null;
            }
        }

        public void Dispose()
        {
            UnsubscribeFromMoodleStatusChanges();
            ClearCache();
        }

        private bool TryApplyMoodle(Guid statusId, IPlayerCharacter player)
        {
            var moodlesPlugin = pluginInstanceField!.GetValue(null);
            if (moodlesPlugin == null)
                return false;

            var ipcProcessor = ipcProcessorField!.GetValue(moodlesPlugin);
            if (ipcProcessor == null)
                return false;

            if (addOrUpdateMoodleByPlayerMethod == null)
                return false;

            addOrUpdateMoodleByPlayerMethod.Invoke(
                ipcProcessor,
                new object[] { statusId, player });

            return true;
        }

        private bool TryRemoveMoodle(Guid statusId, IPlayerCharacter player)
        {
            var moodlesPlugin = pluginInstanceField!.GetValue(null);
            if (moodlesPlugin == null)
                return false;

            var ipcProcessor = ipcProcessorField!.GetValue(moodlesPlugin);
            if (ipcProcessor == null)
                return false;

            if (removeMoodleByPlayerMethod == null)
                return false;

            removeMoodleByPlayerMethod.Invoke(
                ipcProcessor,
                new object[] { statusId, player });

            return true;
        }

        private void CheckAndRaiseActiveMoodlesChanged()
        {
            var current = GetAllActiveMoodles();

            if (DictionariesEqual(lastActiveMoodles, current))
                return;

            lastActiveMoodles = new Dictionary<Guid, string>(current);
            ActiveMoodlesChanged?.Invoke(current);
        }

        private bool TryReadAllMoodles(out Dictionary<Guid, string> result)
        {
            result = new Dictionary<Guid, string>();

            var moodlesPlugin = pluginInstanceField!.GetValue(null);
            if (moodlesPlugin == null)
                return false;

            var ipcProcessor = ipcProcessorField!.GetValue(moodlesPlugin);
            if (ipcProcessor == null)
                return false;

            var registered = getRegisteredMoodlesMethod?.Invoke(ipcProcessor, null);

            if (registered is IEnumerable registeredEntries)
            {
                foreach (var entry in registeredEntries)
                {
                    var guid = ExtractGuid(entry);
                    var name = ExtractRegisteredMoodleName(entry);

                    if (guid != null && !string.IsNullOrWhiteSpace(name))
                        result[guid.Value] = name!;
                }

                return true;
            }

            var statusList = getStatusInfoListMethod?.Invoke(ipcProcessor, null);

            if (statusList is not IEnumerable statuses)
                return false;

            foreach (var statusInfo in statuses)
            {
                var guid = ExtractGuid(statusInfo);
                var name = ExtractStatusName(statusInfo);

                if (guid != null && !string.IsNullOrWhiteSpace(name))
                    result[guid.Value] = name!;
            }

            return true;
        }

        private bool TryReadActiveMoodles(out Dictionary<Guid, string> result)
        {
            result = new Dictionary<Guid, string>();

            var allMoodles = GetAllMoodles();

            var moodlesPlugin = pluginInstanceField!.GetValue(null);
            if (moodlesPlugin == null)
                return false;

            var ipcProcessor = ipcProcessorField!.GetValue(moodlesPlugin);
            if (ipcProcessor == null)
                return false;

            var activeStatuses = getClientStatusManagerInfoMethod!.Invoke(ipcProcessor, null);

            if (activeStatuses is not IEnumerable statuses)
                return false;

            foreach (var statusInfo in statuses)
            {
                var guid = ExtractGuid(statusInfo);
                if (guid == null)
                    continue;

                if (allMoodles.TryGetValue(guid.Value, out var knownName))
                {
                    result[guid.Value] = knownName;
                }
                else
                {
                    var fallbackName = ExtractStatusName(statusInfo);
                    result[guid.Value] = string.IsNullOrWhiteSpace(fallbackName)
                        ? guid.Value.ToString()
                        : fallbackName!;
                }
            }

            return true;
        }

        private bool TryCacheReflectionWithRefresh()
        {
            if (TryCacheReflection())
                return true;

            ClearCache();
            return TryCacheReflection();
        }

        private bool TryCacheReflection()
        {
            if (IsCachedReflectionStillLive())
                return true;

            ClearCache();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(asm.GetName().Name, "Moodles", StringComparison.OrdinalIgnoreCase))
                    continue;

                var candidateMoodlesType = asm.GetType("Moodles.Moodles", throwOnError: false);
                if (candidateMoodlesType == null)
                    continue;

                var candidatePluginInstanceField = candidateMoodlesType.GetField(
                    "P",
                    BindingFlags.Public | BindingFlags.Static);

                if (candidatePluginInstanceField == null)
                    continue;

                var candidatePlugin = candidatePluginInstanceField.GetValue(null);
                if (candidatePlugin == null)
                    continue;

                var candidateIpcProcessorField = candidateMoodlesType.GetField(
                    "IPCProcessor",
                    BindingFlags.Public | BindingFlags.Instance);

                if (candidateIpcProcessorField == null)
                    continue;

                var candidateIpcProcessor = candidateIpcProcessorField.GetValue(candidatePlugin);
                if (candidateIpcProcessor == null)
                    continue;

                var ipcType = candidateIpcProcessor.GetType();

                var candidateActiveMethod = ipcType.GetMethod(
                    "GetClientStatusManagerInfo",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                candidateActiveMethod ??= ipcType.GetMethod(
                    "GetClientStatusManagerInfoV2",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var candidateRegisteredMethod = ipcType.GetMethod(
                    "GetRegisteredMoodlesV2",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var candidateStatusInfoListMethod = ipcType.GetMethod(
                    "GetStatusInfoListV2",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var candidateAddOrUpdateMoodleByPlayerMethod = ipcType.GetMethod(
                    "AddOrUpdateMoodleByPlayerV2",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var candidateRemoveMoodleByPlayerMethod = ipcType.GetMethod(
                    "RemoveMoodleByPlayerV2",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var candidateStatusManagerModifiedField = ipcType.GetField(
                    "StatusManagerModified",
                    BindingFlags.Public | BindingFlags.Instance);

                var candidateStatusUpdatedField = ipcType.GetField(
                    "StatusUpdated",
                    BindingFlags.Public | BindingFlags.Instance);

                if (candidateActiveMethod == null)
                    continue;

                if (candidateRegisteredMethod == null && candidateStatusInfoListMethod == null)
                    continue;

                moodlesType = candidateMoodlesType;
                pluginInstanceField = candidatePluginInstanceField;
                ipcProcessorField = candidateIpcProcessorField;

                getClientStatusManagerInfoMethod = candidateActiveMethod;
                getRegisteredMoodlesMethod = candidateRegisteredMethod;
                getStatusInfoListMethod = candidateStatusInfoListMethod;

                addOrUpdateMoodleByPlayerMethod = candidateAddOrUpdateMoodleByPlayerMethod;
                removeMoodleByPlayerMethod = candidateRemoveMoodleByPlayerMethod;

                statusManagerModifiedField = candidateStatusManagerModifiedField;
                statusUpdatedField = candidateStatusUpdatedField;

                return true;
            }

            Plugin.ChatGui.PrintError("No live Moodles instance found.");
            return false;
        }

        private bool IsCachedReflectionStillLive()
        {
            try
            {
                if (moodlesType == null ||
                    pluginInstanceField == null ||
                    ipcProcessorField == null ||
                    getClientStatusManagerInfoMethod == null)
                    return false;

                var moodlesPlugin = pluginInstanceField.GetValue(null);
                if (moodlesPlugin == null)
                    return false;

                var ipcProcessor = ipcProcessorField.GetValue(moodlesPlugin);
                if (ipcProcessor == null)
                    return false;

                if (!getClientStatusManagerInfoMethod.DeclaringType!.IsInstanceOfType(ipcProcessor))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ClearCache()
        {
            moodlesType = null;
            pluginInstanceField = null;
            ipcProcessorField = null;

            getClientStatusManagerInfoMethod = null;
            getRegisteredMoodlesMethod = null;
            getStatusInfoListMethod = null;

            addOrUpdateMoodleByPlayerMethod = null;
            removeMoodleByPlayerMethod = null;

            statusManagerModifiedField = null;
            statusUpdatedField = null;
        }

        private static void AddDelegateToField(object target, FieldInfo? field, Delegate handler)
        {
            if (field == null)
                return;

            var existing = field.GetValue(target) as Delegate;
            var combined = Delegate.Combine(existing, handler);
            field.SetValue(target, combined);
        }

        private static void RemoveDelegateFromField(object target, FieldInfo? field, Delegate handler)
        {
            if (field == null)
                return;

            var existing = field.GetValue(target) as Delegate;
            if (existing == null)
                return;

            var reduced = Delegate.Remove(existing, handler);
            field.SetValue(target, reduced);
        }

        private static bool DictionariesEqual(
            IReadOnlyDictionary<Guid, string> left,
            IReadOnlyDictionary<Guid, string> right)
        {
            if (left.Count != right.Count)
                return false;

            foreach (var kvp in left)
            {
                if (!right.TryGetValue(kvp.Key, out var rightValue))
                    return false;

                if (!string.Equals(kvp.Value, rightValue, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static Guid? ExtractGuid(object statusInfo)
        {
            var type = statusInfo.GetType();

            var guidField = type.GetField("GUID", BindingFlags.Public | BindingFlags.Instance);
            if (guidField?.GetValue(statusInfo) is Guid namedGuid)
                return namedGuid;

            var item1 = type.GetField("Item1", BindingFlags.Public | BindingFlags.Instance);
            if (item1?.GetValue(statusInfo) is Guid item1Guid)
                return item1Guid;

            var item2 = type.GetField("Item2", BindingFlags.Public | BindingFlags.Instance);
            if (item2?.GetValue(statusInfo) is Guid item2Guid)
                return item2Guid;

            return null;
        }


        private static string? ExtractStatusName(object statusInfo)
        {
            var type = statusInfo.GetType();

            foreach (var name in new[] { "FullPath", "Path", "StatusPath" })
            {
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field?.GetValue(statusInfo) is string fieldValue && !string.IsNullOrWhiteSpace(fieldValue))
                    return fieldValue;

                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop?.GetValue(statusInfo) is string propValue && !string.IsNullOrWhiteSpace(propValue))
                    return propValue;
            }

            foreach (var name in new[] { "Title", "Name", "StatusName" })
            {
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field?.GetValue(statusInfo) is string fieldValue && !string.IsNullOrWhiteSpace(fieldValue))
                    return fieldValue;

                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop?.GetValue(statusInfo) is string propValue && !string.IsNullOrWhiteSpace(propValue))
                    return propValue;
            }

            return null;
        }
        private static string? ExtractRegisteredMoodleName(object registeredMoodleInfo)
        {
            var type = registeredMoodleInfo.GetType();

            foreach (var name in new[] { "FullPath", "Path" })
            {
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field?.GetValue(registeredMoodleInfo) is string fieldValue && !string.IsNullOrWhiteSpace(fieldValue))
                    return fieldValue;

                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop?.GetValue(registeredMoodleInfo) is string propValue && !string.IsNullOrWhiteSpace(propValue))
                    return propValue;
            }

            // GetRegisteredMoodlesV2 tuple:
            // Item1 = GUID
            // Item2 = IconID
            // Item3 = FullPath
            // Item4 = Title
            var item3 = type.GetField("Item3", BindingFlags.Public | BindingFlags.Instance);
            if (item3?.GetValue(registeredMoodleInfo) is string item3FullPath && !string.IsNullOrWhiteSpace(item3FullPath))
                return item3FullPath;

            // Fallback to Title only if path/full path was not available.
            foreach (var name in new[] { "Title", "Name", "StatusName" })
            {
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field?.GetValue(registeredMoodleInfo) is string fieldValue && !string.IsNullOrWhiteSpace(fieldValue))
                    return fieldValue;

                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop?.GetValue(registeredMoodleInfo) is string propValue && !string.IsNullOrWhiteSpace(propValue))
                    return propValue;
            }

            var item4 = type.GetField("Item4", BindingFlags.Public | BindingFlags.Instance);
            if (item4?.GetValue(registeredMoodleInfo) is string item4Title && !string.IsNullOrWhiteSpace(item4Title))
                return item4Title;

            return null;
        }
    }
}
