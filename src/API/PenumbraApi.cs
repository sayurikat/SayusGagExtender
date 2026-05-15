using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SayusGagExtender.API
{
    public sealed class PenumbraApi : IDisposable
    {
        private readonly Plugin plugin;
        private readonly IDalamudPluginInterface pluginInterface;

        private ICallGateSubscriber<Dictionary<string, string>>? getModList;

        private ICallGateSubscriber<int, (bool ObjectValid, bool IndividualSet, (Guid Id, string Name) EffectiveCollection)>? getCollectionForObject;

        private ICallGateSubscriber<Guid, string, string, bool,
            (int Ec, (bool Enabled, int Priority, Dictionary<string, List<string>> Settings, bool Inherited)? Settings)>? getCurrentModSettings;

        private ICallGateSubscriber<Guid, string, string, bool, int>? trySetMod;

        public PenumbraApi(Plugin plugin)
        {
            this.plugin = plugin;

            // Adjust if your Plugin class exposes this differently.
            this.pluginInterface = Plugin.PluginInterface;

            CacheIpcSubscribers();
        }

        public void Dispose()
        {
            ClearCache();
        }

        public bool IsAvailable()
        {
            try
            {
                CacheIpcSubscribers();

                return getModList != null
                    && getCollectionForObject != null
                    && getCurrentModSettings != null
                    && trySetMod != null;
            }
            catch
            {
                return false;
            }
        }

        public Dictionary<string, string> GetAllMods()
        {
            try
            {
                CacheIpcSubscribers();

                if (getModList == null)
                    return new Dictionary<string, string>();

                return getModList.InvokeFunc() ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to get Penumbra mods: {ex}");
                return new Dictionary<string, string>();
            }
        }

        public Dictionary<string, string> GetAllModsOrdered()
        {
            return GetAllMods()
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Value);
        }

        public (Guid Id, string Name)? GetPlayerCollection()
        {
            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            if (localPlayer == null)
                return null;

            return GetCollectionForPlayer(localPlayer);
        }

        public (Guid Id, string Name)? GetCollectionForPlayer(IPlayerCharacter player)
        {
            try
            {
                CacheIpcSubscribers();

                if (getCollectionForObject == null)
                    return null;

                var result = getCollectionForObject.InvokeFunc(player.ObjectIndex);

                if (!result.ObjectValid)
                    return null;

                return result.EffectiveCollection;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to get Penumbra collection for player: {ex}");
                return null;
            }
        }

        public bool IsModEnabledOnPlayerCollection(
            string modDirectory,
            string modName = "",
            bool ignoreInheritance = false)
        {
            var collection = GetPlayerCollection();
            return collection != null
                && IsModEnabled(collection.Value.Id, modDirectory, modName, ignoreInheritance);
        }

        public bool IsModEnabled(
            Guid collectionId,
            string modDirectory,
            string modName = "",
            bool ignoreInheritance = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modDirectory))
                    return false;

                CacheIpcSubscribers();

                if (getCurrentModSettings == null)
                    return false;

                var result = getCurrentModSettings.InvokeFunc(
                    collectionId,
                    modDirectory,
                    modName ?? string.Empty,
                    ignoreInheritance);

                if ((PenumbraApiEc)result.Ec != PenumbraApiEc.Success)
                    return false;

                return result.Settings?.Enabled == true;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to check Penumbra mod state for {modDirectory}: {ex}");
                return false;
            }
        }

        public bool SetModEnabledOnPlayerCollection(
            string modDirectory,
            bool enabled,
            string modName = "")
        {
            var collection = GetPlayerCollection();
            return collection != null
                && SetModEnabled(collection.Value.Id, modDirectory, enabled, modName);
        }

        public bool SetModEnabled(
            Guid collectionId,
            string modDirectory,
            bool enabled,
            string modName = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modDirectory))
                    return false;

                CacheIpcSubscribers();

                if (trySetMod == null)
                    return false;

                var ec = (PenumbraApiEc)trySetMod.InvokeFunc(
                    collectionId,
                    modDirectory,
                    modName ?? string.Empty,
                    enabled);

                return ec is PenumbraApiEc.Success or PenumbraApiEc.NothingChanged;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to set Penumbra mod state for {modDirectory} to {enabled}: {ex}");
                return false;
            }
        }

        public bool EnableModOnPlayerCollection(string modDirectory, string modName = "")
        {
            return SetModEnabledOnPlayerCollection(modDirectory, true, modName);
        }

        public bool DisableModOnPlayerCollection(string modDirectory, string modName = "")
        {
            return SetModEnabledOnPlayerCollection(modDirectory, false, modName);
        }

        private void CacheIpcSubscribers()
        {
            getModList ??=
                pluginInterface.GetIpcSubscriber<Dictionary<string, string>>(
                    "Penumbra.GetModList");

            getCollectionForObject ??=
                pluginInterface.GetIpcSubscriber<int,
                    (bool ObjectValid, bool IndividualSet, (Guid Id, string Name) EffectiveCollection)>(
                    "Penumbra.GetCollectionForObject.V5");

            getCurrentModSettings ??=
                pluginInterface.GetIpcSubscriber<Guid, string, string, bool,
                    (int Ec, (bool Enabled, int Priority, Dictionary<string, List<string>> Settings, bool Inherited)? Settings)>(
                    "Penumbra.GetCurrentModSettings.V5");

            trySetMod ??=
                pluginInterface.GetIpcSubscriber<Guid, string, string, bool, int>(
                    "Penumbra.TrySetMod.V5");
        }

        private void ClearCache()
        {
            getModList = null;
            getCollectionForObject = null;
            getCurrentModSettings = null;
            trySetMod = null;
        }

        private enum PenumbraApiEc
        {
            Success = 0,
            NothingChanged = 1,
            CollectionMissing = 2,
            ModMissing = 3,
            OptionGroupMissing = 4,
            OptionMissing = 5,
            CharacterCollectionExists = 6,
            LowerPriority = 7,
            InvalidGamePath = 8,
            FileMissing = 9,
            InvalidManipulation = 10,
            InvalidArgument = 11,
            PathRenameFailed = 12,
            CollectionExists = 13,
            AssignmentCreationDisallowed = 14,
            AssignmentDeletionDisallowed = 15,
            InvalidIdentifier = 16,
            SystemDisposed = 17,
            AssignmentDeletionFailed = 18,
            TemporarySettingDisallowed = 19,
            TemporarySettingImpossible = 20,
            InvalidCredentials = 21,
            CollectionInactive = 22,
            UnknownError = 255,
        }
    }
}
