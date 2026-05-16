using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;
using System.Collections.Generic;
using System.Linq;

using CustomizePlusCharacterTuple =
    (string Name, ushort WorldId, byte CharacterType, ushort CharacterSubType);

using CustomizePlusProfileTuple =
    (System.Guid UniqueId,
     string Name,
     string VirtualPath,
     System.Collections.Generic.List<(string Name, ushort WorldId, byte CharacterType, ushort CharacterSubType)> Characters,
     int Priority,
     bool IsEnabled);

namespace SayusGagExtender.API
{
    public sealed class CustomizePlusApi : IDisposable
    {
        private readonly Plugin plugin;
        private readonly IDalamudPluginInterface pluginInterface;

        private ICallGateSubscriber<IList<CustomizePlusProfileTuple>>? getProfileList;
        private ICallGateSubscriber<Guid, int>? enableProfileByUniqueId;
        private ICallGateSubscriber<Guid, int>? disableProfileByUniqueId;

        public CustomizePlusApi(Plugin plugin)
        {
            this.plugin = plugin;
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

                return getProfileList != null
                    && enableProfileByUniqueId != null
                    && disableProfileByUniqueId != null;
            }
            catch
            {
                return false;
            }
        }

        public IList<CustomizePlusProfileTuple> GetAllProfiles()
        {
            try
            {
                CacheIpcSubscribers();

                if (getProfileList == null)
                    return new List<CustomizePlusProfileTuple>();

                return getProfileList.InvokeFunc()
                    ?? new List<CustomizePlusProfileTuple>();
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to get Customize+ profiles: {ex}");
                return new List<CustomizePlusProfileTuple>();
            }
        }

        public IList<CustomizePlusProfileTuple> GetAllProfilesOrdered()
        {
            return GetAllProfiles()
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.VirtualPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.UniqueId)
                .ToList();
        }

        public CustomizePlusProfileTuple? GetProfile(Guid profileId)
        {
            if (profileId == Guid.Empty)
                return null;

            return GetAllProfiles()
                .FirstOrDefault(x => x.UniqueId == profileId);
        }

        public bool IsProfileEnabled(Guid profileId)
        {
            var profile = GetProfile(profileId);
            return profile?.IsEnabled == true;
        }

        public bool SetProfileEnabled(Guid profileId, bool enabled)
        {
            try
            {
                if (profileId == Guid.Empty)
                    return false;

                CacheIpcSubscribers();

                var ec = enabled
                    ? enableProfileByUniqueId?.InvokeFunc(profileId)
                    : disableProfileByUniqueId?.InvokeFunc(profileId);

                return (CustomizePlusApiEc?)ec == CustomizePlusApiEc.Success;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to set Customize+ profile {profileId} to {enabled}: {ex}");
                return false;
            }
        }

        public bool EnableProfile(Guid profileId)
        {
            return SetProfileEnabled(profileId, true);
        }

        public bool DisableProfile(Guid profileId)
        {
            return SetProfileEnabled(profileId, false);
        }

        private void CacheIpcSubscribers()
        {
            getProfileList ??=
                pluginInterface.GetIpcSubscriber<IList<CustomizePlusProfileTuple>>(
                    "CustomizePlus.Profile.GetList");

            enableProfileByUniqueId ??=
                pluginInterface.GetIpcSubscriber<Guid, int>(
                    "CustomizePlus.Profile.EnableByUniqueId");

            disableProfileByUniqueId ??=
                pluginInterface.GetIpcSubscriber<Guid, int>(
                    "CustomizePlus.Profile.DisableByUniqueId");
        }

        private void ClearCache()
        {
            getProfileList = null;
            enableProfileByUniqueId = null;
            disableProfileByUniqueId = null;
        }

        private enum CustomizePlusApiEc
        {
            Success = 0,
            InvalidCharacter = 1,
            CorruptedProfile = 2,
            ProfileNotFound = 3,
            InvalidArgument = 4,
            UnknownError = 255,
        }
    }

    public readonly record struct CustomizePlusProfile(
        Guid UniqueId,
        string Name,
        string VirtualPath,
        List<CustomizePlusCharacter> Characters,
        int Priority,
        bool IsEnabled);

    public readonly record struct CustomizePlusCharacter(
        string Name,
        ushort WorldId,
        byte CharacterType,
        ushort CharacterSubType);


}
