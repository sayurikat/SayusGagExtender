using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace SayusGagExtender.API
{
    public sealed class MoodlesApi
    {
        private readonly Plugin plugin;

        private Type? moodlesType;
        private FieldInfo? pluginInstanceField;
        private FieldInfo? ipcProcessorField;
        private MethodInfo? getClientStatusManagerInfoMethod;

        public MoodlesApi(Plugin plugin)
        {
            this.plugin = plugin;

            _ = Plugin.Framework.RunOnFrameworkThread(() =>
            {
                var id1 = "2a09b34d-9ef7-47e4-81dd-344e34b7dcd0";
                var id2 = "c9cce383-1950-414d-8295-6bcc7a60cc68";

                //Plugin.ChatGui.Print($"Moodles status {id1}: {IsStatusActive(id1)}");
                //Plugin.ChatGui.Print($"Moodles status {id2}: {IsStatusActive(id2)}");
            });
        }

        public bool IsStatusActive(string statusId)
        {
            return Guid.TryParse(statusId, out var guid) && IsStatusActive(guid);
        }

        public bool IsStatusActive(Guid statusId)
        {
            try
            {
                if (!TryCacheReflection())
                    return false;

                if (TryReadStatusActive(statusId, out var active))
                    return active;

                // Cached objects may have gone stale mid-call. Refresh once.
                ClearCache();

                if (!TryCacheReflection())
                    return false;

                return TryReadStatusActive(statusId, out active) && active;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to check Moodles status {ex}");
                return false;
            }
        }

        private bool TryReadStatusActive(Guid statusId, out bool active)
        {
            active = false;

            var moodlesPlugin = pluginInstanceField!.GetValue(null);
            if (moodlesPlugin == null)
                return false;

            var ipcProcessor = ipcProcessorField!.GetValue(moodlesPlugin);
            if (ipcProcessor == null)
                return false;

            var result = getClientStatusManagerInfoMethod!.Invoke(ipcProcessor, null);

            if (result is not IEnumerable statuses)
                return false;

            foreach (var statusInfo in statuses)
            {
                var guid = ExtractGuidFromMoodlesStatusInfo(statusInfo);

                if (guid == statusId)
                {
                    active = true;
                    return true;
                }
            }

            return true;
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

                var candidateMethod = candidateIpcProcessor.GetType().GetMethod(
                    "GetClientStatusManagerInfo",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (candidateMethod == null)
                    continue;

                moodlesType = candidateMoodlesType;
                pluginInstanceField = candidatePluginInstanceField;
                ipcProcessorField = candidateIpcProcessorField;
                getClientStatusManagerInfoMethod = candidateMethod;

                //Plugin.ChatGui.PrintError($"Cached live Moodles reflection objects.");
                return true;
            }

            Plugin.ChatGui.PrintError($"No live Moodles instance found.");
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

                // Important: make sure the cached MethodInfo belongs to the current processor type.
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
        }

        private static Guid? ExtractGuidFromMoodlesStatusInfo(object statusInfo)
        {
            var type = statusInfo.GetType();

            // MoodlesStatusInfo is a named ValueTuple:
            // (int Version, Guid GUID, int IconID, ...)
            //
            // Runtime fields are Item1, Item2, Item3...
            // GUID is Item2.
            var item2 = type.GetField("Item2", BindingFlags.Public | BindingFlags.Instance);

            if (item2?.GetValue(statusInfo) is Guid guid)
                return guid;

            // Fallback in case tuple metadata survives differently.
            var guidField = type.GetField("GUID", BindingFlags.Public | BindingFlags.Instance);

            if (guidField?.GetValue(statusInfo) is Guid namedGuid)
                return namedGuid;

            return null;
        }
    }
}
