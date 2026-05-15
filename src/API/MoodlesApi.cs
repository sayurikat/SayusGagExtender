using Dalamud.Game.ClientState.Objects.SubKinds;
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

        private bool suppressMoodleChangeEvents;
        private bool moodleEventHandlersAttached;
        private bool disposed;
        private int ipcEventSuppressionGeneration;

        public event Action<IReadOnlyDictionary<Guid, string>>? ActiveMoodlesChanged;

        public MoodlesApi(Plugin plugin)
        {
            this.plugin = plugin;

            _ = Plugin.Framework.RunOnFrameworkThread(() =>
            {
                SubscribeToMoodleStatusChanges();
            });
        }

        // ----------------------------
        // Safe public async entry points
        // ----------------------------

        public Task<bool> IsStatusActiveAsync(string statusId)
        {
            return Plugin.Framework.RunOnFrameworkThread(() => IsStatusActive(statusId));
        }

        public Task<bool> IsStatusActiveAsync(Guid statusId)
        {
            return Plugin.Framework.RunOnFrameworkThread(() => IsStatusActive(statusId));
        }

        public Task<Dictionary<Guid, string>> GetAllMoodlesAsync()
        {
            return Plugin.Framework.RunOnFrameworkThread(GetAllMoodles);
        }

        public Task<Dictionary<Guid, string>> GetAllActiveMoodlesAsync()
        {
            return Plugin.Framework.RunOnFrameworkThread(GetAllActiveMoodles);
        }

        public Task<bool> ApplyMoodleAsync(string statusId)
        {
            return Plugin.Framework.RunOnFrameworkThread(() => ApplyMoodle(statusId));
        }

        public Task<bool> ApplyMoodleAsync(Guid statusId)
        {
            return Plugin.Framework.RunOnFrameworkThread(() => ApplyMoodle(statusId));
        }

        public Task<bool> ApplyMoodleAndCaptureAsync(string statusId)
        {
            // Kept for compatibility with existing callers.
            // This used to snapshot/restore Moodles state; now it is just a normal apply.
            return ApplyMoodleAsync(statusId);
        }

        public Task<bool> ApplyMoodleAndCaptureAsync(Guid statusId)
        {
            // Kept for compatibility with existing callers.
            // This used to snapshot/restore Moodles state; now it is just a normal apply.
            return ApplyMoodleAsync(statusId);
        }

        public Task<bool> RemoveMoodleAsync(string statusId)
        {
            return Plugin.Framework.RunOnFrameworkThread(() => RemoveMoodle(statusId));
        }

        public Task<bool> RemoveMoodleAsync(Guid statusId)
        {
            return Plugin.Framework.RunOnFrameworkThread(() => RemoveMoodle(statusId));
        }

        public Task<bool> RestoreLastKnownStatusManagerAsync()
        {
            // Snapshot restore intentionally removed.
            return Task.FromResult(false);
        }

        public Task RestoreAfterZoneDelayAsync(int delayMs = 3000)
        {
            // Snapshot/restore on zone change intentionally removed.
            // Moodles should keep self-applied moodles itself when we do not overwrite its status manager.
            return Task.CompletedTask;
        }

        public Task<bool> SubscribeToMoodleStatusChangesAsync()
        {
            return Plugin.Framework.RunOnFrameworkThread(SubscribeToMoodleStatusChanges);
        }

        // ----------------------------
        // Sync methods below assume framework thread
        // ----------------------------

        public bool IsStatusActive(string statusId)
        {
            return Guid.TryParse(statusId, out var guid) && IsStatusActive(guid);
        }

        public bool IsStatusActive(Guid statusId)
        {
            try
            {
                var active = GetAllActiveMoodles();
                return active.ContainsKey(statusId);
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
            return GetAllActiveMoodlesRaw();
        }

        private Dictionary<Guid, string> GetAllActiveMoodlesRaw()
        {
            try
            {
                if (Plugin.ObjectTable.LocalPlayer == null)
                    return new Dictionary<Guid, string>();

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
            return localPlayer != null && ApplyMoodle(statusId, localPlayer);
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
            return localPlayer != null && RemoveMoodle(statusId, localPlayer);
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
                    if (suppressMoodleChangeEvents)
                        return;

                    _ = Plugin.Framework.RunOnFrameworkThread(CheckAndRaiseActiveMoodlesChanged);
                };

                statusUpdatedHandler = (id, active) =>
                {
                    if (suppressMoodleChangeEvents)
                        return;

                    _ = Plugin.Framework.RunOnFrameworkThread(CheckAndRaiseActiveMoodlesChanged);
                };

                AttachMoodleEventHandlers(ipcProcessor);

                return true;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to subscribe to Moodles changes {ex}");
                statusManagerModifiedHandler = null;
                statusUpdatedHandler = null;
                moodleEventHandlersAttached = false;
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

                DetachMoodleEventHandlers(ipcProcessor);
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to unsubscribe from Moodles changes {ex}");
            }
            finally
            {
                statusManagerModifiedHandler = null;
                statusUpdatedHandler = null;
                moodleEventHandlersAttached = false;
            }
        }

        public void Dispose()
        {
            disposed = true;
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

            InvokeMoodlesIpcWithoutChangeCallbacks(
                ipcProcessor,
                () =>
                {
                    addOrUpdateMoodleByPlayerMethod.Invoke(
                        ipcProcessor,
                        new object[] { statusId, player });

                    // Moodles' IPC path marks the player's MyStatusManager as WasTouchedByIPC.
                    // Moodles deletes IPC-touched managers when the character unloads, which is
                    // why zoning/doors wipe both plugin-applied and self-applied moodles.
                    // Clear that flag immediately for the local/self manager so Moodles treats it
                    // like a normal self-managed status manager again.
                    ClearWasTouchedByIpcForPlayer(moodlesPlugin, player);
                });

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

            InvokeMoodlesIpcWithoutChangeCallbacks(
                ipcProcessor,
                () =>
                {
                    removeMoodleByPlayerMethod.Invoke(
                        ipcProcessor,
                        new object[] { statusId, player });

                    // Remove uses the same IPC path and also calls MarkSynced.
                    ClearWasTouchedByIpcForPlayer(moodlesPlugin, player);
                });

            return true;
        }

        private void InvokeMoodlesIpcWithoutChangeCallbacks(object ipcProcessor, Action invoke)
        {
            var generation = ++ipcEventSuppressionGeneration;

            suppressMoodleChangeEvents = true;
            DetachMoodleEventHandlers(ipcProcessor);

            try
            {
                invoke();
            }
            finally
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(250);

                    await Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        if (disposed || generation != ipcEventSuppressionGeneration)
                            return;

                        try
                        {
                            if (!TryCacheReflectionWithRefresh())
                                return;

                            var moodlesPlugin = pluginInstanceField!.GetValue(null);
                            if (moodlesPlugin == null)
                                return;

                            var currentIpcProcessor = ipcProcessorField!.GetValue(moodlesPlugin);
                            if (currentIpcProcessor == null)
                                return;

                            AttachMoodleEventHandlers(currentIpcProcessor);
                        }
                        finally
                        {
                            suppressMoodleChangeEvents = false;
                            lastActiveMoodles = GetAllActiveMoodlesRaw();
                        }
                    });
                });
            }
        }

        private void ClearWasTouchedByIpcForPlayer(object moodlesPlugin, IPlayerCharacter player)
        {
            try
            {
                var configField = moodlesPlugin.GetType().GetField(
                    "Config",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                var config = configField?.GetValue(moodlesPlugin);
                if (config == null)
                    return;

                var managersField = config.GetType().GetField(
                    "StatusManagers",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (managersField?.GetValue(config) is not IDictionary managers)
                    return;

                var playerName = GetPlayerNameText(player);
                var playerAddress = player.Address;

                foreach (DictionaryEntry entry in managers)
                {
                    if (entry.Value == null)
                        continue;

                    if (!LooksLikePlayersStatusManager(entry.Key, entry.Value, playerName, playerAddress))
                        continue;

                    var managerType = entry.Value.GetType();
                    var wasTouchedField = managerType.GetField(
                        "WasTouchedByIPC",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (wasTouchedField != null && wasTouchedField.FieldType == typeof(bool))
                        wasTouchedField.SetValue(entry.Value, false);
                }
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to clear Moodles IPC touched flag: {ex}");
            }
        }

        private static bool LooksLikePlayersStatusManager(object? key, object manager, string? playerName, nint playerAddress)
        {
            var keyString = key?.ToString();

            if (!string.IsNullOrWhiteSpace(playerName) && !string.IsNullOrWhiteSpace(keyString))
            {
                if (string.Equals(keyString, playerName, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (keyString.StartsWith(playerName + "@", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Best-effort fallback: if reflection can read Owner as a pointer-like value,
            // match it to the player address. Some runtimes expose unsafe pointer fields
            // as System.Reflection.Pointer, so this intentionally stays conservative.
            var ownerField = manager.GetType().GetField(
                "Owner",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (ownerField == null)
                return false;

            try
            {
                var owner = ownerField.GetValue(manager);
                if (owner is nint ownerPtr)
                    return ownerPtr == playerAddress;

                if (owner is IntPtr intPtr)
                    return intPtr == playerAddress;
            }
            catch
            {
                // Ignore pointer reflection failures; key matching above is the reliable path.
            }

            return false;
        }

        private static string? GetPlayerNameText(IPlayerCharacter player)
        {
            try
            {
                var nameObject = player.GetType().GetProperty("Name")?.GetValue(player);
                if (nameObject == null)
                    return null;

                var textValue = nameObject.GetType().GetProperty("TextValue")?.GetValue(nameObject) as string;
                if (!string.IsNullOrWhiteSpace(textValue))
                    return textValue;

                var extractedText = nameObject.GetType().GetMethod("ExtractText", Type.EmptyTypes)?.Invoke(nameObject, null) as string;
                if (!string.IsNullOrWhiteSpace(extractedText))
                    return extractedText;

                var fallback = nameObject.ToString();
                return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
            }
            catch
            {
                return null;
            }
        }

        private void AttachMoodleEventHandlers(object ipcProcessor)
        {
            if (moodleEventHandlersAttached)
                return;

            if (statusManagerModifiedHandler != null)
                AddDelegateToField(ipcProcessor, statusManagerModifiedField, statusManagerModifiedHandler);

            if (statusUpdatedHandler != null)
                AddDelegateToField(ipcProcessor, statusUpdatedField, statusUpdatedHandler);

            moodleEventHandlersAttached = true;
        }

        private void DetachMoodleEventHandlers(object ipcProcessor)
        {
            if (!moodleEventHandlersAttached)
                return;

            if (statusManagerModifiedHandler != null)
                RemoveDelegateFromField(ipcProcessor, statusManagerModifiedField, statusManagerModifiedHandler);

            if (statusUpdatedHandler != null)
                RemoveDelegateFromField(ipcProcessor, statusUpdatedField, statusUpdatedHandler);

            moodleEventHandlersAttached = false;
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

            if (getClientStatusManagerInfoMethod == null)
                return false;

            var activeStatuses = getClientStatusManagerInfoMethod.Invoke(ipcProcessor, null);

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

            var item3 = type.GetField("Item3", BindingFlags.Public | BindingFlags.Instance);
            if (item3?.GetValue(registeredMoodleInfo) is string item3FullPath && !string.IsNullOrWhiteSpace(item3FullPath))
                return item3FullPath;

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
