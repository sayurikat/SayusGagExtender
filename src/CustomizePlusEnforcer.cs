using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using static SayusGagExtender.API.GagSpeak.GagSpeakReflectionContext;

namespace SayusGagExtender
{
    public sealed class CustomizePlusEnforcer : IDisposable
    {
        private readonly Plugin plugin;

        private readonly Dictionary<Guid, bool> lastWantedProfileStates = new();
        private DateTime onUpdateNextUTC = DateTime.MinValue;
        private readonly TimeSpan OnUpdateCooldown = TimeSpan.FromSeconds(10);
        public bool IsEnforcing = false;
        public sealed class CustomizePlusEnforcerConfig
        {
            public Guid ProfileId { get; set; } = Guid.Empty;
            public string ProfileName { get; set; } = string.Empty;
            public string VirtualPath { get; set; } = string.Empty;

            public List<GagSpeakItem> RestraintSets { get; set; } = new();
            public List<GagSpeakItem> Restrictions { get; set; } = new();
            public List<GagSpeakItem> Gags { get; set; } = new();
        }

        public CustomizePlusEnforcer(Plugin plugin)
        {
            this.plugin = plugin;

            Plugin.Framework.Update += this.OnFrameworkUpdate;

            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged += this.OnAnyChanged;
            plugin.GagSpeakGagsApi.OnGagsChanged += this.OnAnyChanged;
            plugin.GagSpeakRestraintSetApi.OnRestraintSetChanged += this.OnAnyChanged;
        }

        public void Dispose()
        {
            Plugin.Framework.Update -= this.OnFrameworkUpdate;

            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged -= this.OnAnyChanged;
            plugin.GagSpeakGagsApi.OnGagsChanged -= this.OnAnyChanged;
            plugin.GagSpeakRestraintSetApi.OnRestraintSetChanged -= this.OnAnyChanged;
        }

        private void OnAnyChanged(object obj)
        {
            Enforce();
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (onUpdateNextUTC > DateTime.UtcNow)
                return;

            onUpdateNextUTC = DateTime.UtcNow + OnUpdateCooldown;

            Enforce();
        }

        public CustomizePlusEnforcerConfig GetOrCreateCustomizePlusEnforcerConfig(
            Guid profileId,
            string profileName,
            string virtualPath = "")
        {
            var existing = plugin.Configuration.CustomizePlusEnforcerProfiles
                .FirstOrDefault(x => x.ProfileId == profileId);

            if (existing != null)
            {
                existing.ProfileName = profileName;
                existing.VirtualPath = virtualPath;
                return existing;
            }

            var created = new CustomizePlusEnforcerConfig
            {
                ProfileId = profileId,
                ProfileName = profileName,
                VirtualPath = virtualPath,
            };

            plugin.Configuration.CustomizePlusEnforcerProfiles.Add(created);
            return created;
        }

        public void Enforce()
        {
            IsEnforcing = false;

            if (!plugin.Configuration.CustomizePlusEnforcerEnabled)
                return;

            if (plugin.Configuration.CustomizePlusEnforcerProfiles.Count == 0)
            {
                EnsureDefaultProfileState(true);
                return;
            }

            var activeState = GetActiveState();
            var anyLinkedProfileShouldBeActive = false;

            foreach (var profileConfig in plugin.Configuration.CustomizePlusEnforcerProfiles)
            {
                if (profileConfig.ProfileId == Guid.Empty)
                    continue;

                if (profileConfig.Restrictions.Count + profileConfig.Gags.Count + profileConfig.RestraintSets.Count <= 0)
                    continue;

                var shouldBeActive = ShouldProfileBeActive(profileConfig, activeState);

                if (shouldBeActive)
                {
                    IsEnforcing = true;
                    anyLinkedProfileShouldBeActive = true;
                }

                lastWantedProfileStates[profileConfig.ProfileId] = shouldBeActive;

                EnsureProfileState(profileConfig, shouldBeActive);
            }

            EnsureDefaultProfileState(!anyLinkedProfileShouldBeActive);
        }
        private void EnsureDefaultProfileState(bool shouldBeActive)
        {
            var defaultProfileId = plugin.Configuration.CustomizePlusDefaultProfileId;

            if (defaultProfileId == Guid.Empty)
                return;

            // If the same profile is also configured as a linked profile, do not fight
            // the linked-profile logic. Linked config wins.
            var isAlsoLinkedProfile = plugin.Configuration.CustomizePlusEnforcerProfiles
                .Any(x => x.ProfileId == defaultProfileId);

            if (isAlsoLinkedProfile)
                return;

            var isActive = plugin.CustomizePlusApi.IsProfileEnabled(defaultProfileId);

            if (isActive == shouldBeActive)
                return;

            var ok = plugin.CustomizePlusApi.SetProfileEnabled(defaultProfileId, shouldBeActive);

            if (!ok)
            {
                Plugin.ChatGui.PrintError(
                    $"Failed to set default Customize+ profile '{plugin.Configuration.CustomizePlusDefaultProfileName}' ({defaultProfileId}) to {shouldBeActive}.");
            }
        }
        private CustomizePlusEnforcerActiveState GetActiveState()
        {
            var activeGags = plugin.GagSpeakGagsApi
                .GetActiveGags()
                .Values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var activeRestraintSet = plugin.GagSpeakRestraintSetApi.GetActiveRestraintSet();

            var activeRestraintSetIds = new HashSet<Guid>();

            if (activeRestraintSet.Key != Guid.Empty)
                activeRestraintSetIds.Add(activeRestraintSet.Key);

            var activeRestraintSetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(activeRestraintSet.Value))
                activeRestraintSetNames.Add(activeRestraintSet.Value);

            var activeRestrictions = plugin.GagSpeakRestrictionsApi.GetActiveRestrictionsWithId();

            var activeRestrictionIds = activeRestrictions
                .Where(x => x.Key != Guid.Empty)
                .Select(x => x.Key)
                .ToHashSet();

            var activeRestrictionNames = activeRestrictions
                .Values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return new CustomizePlusEnforcerActiveState
            {
                ActiveGagNames = activeGags,
                ActiveRestraintSetIds = activeRestraintSetIds,
                ActiveRestraintSetNames = activeRestraintSetNames,
                ActiveRestrictionIds = activeRestrictionIds,
                ActiveRestrictionNames = activeRestrictionNames,
            };
        }

        private bool ShouldProfileBeActive(
            CustomizePlusEnforcerConfig profileConfig,
            CustomizePlusEnforcerActiveState activeState)
        {
            if (ContainsAnyByIdOrName(
                    profileConfig.RestraintSets,
                    activeState.ActiveRestraintSetIds,
                    activeState.ActiveRestraintSetNames))
            {
                return true;
            }

            if (ContainsAnyByIdOrName(
                    profileConfig.Restrictions,
                    activeState.ActiveRestrictionIds,
                    activeState.ActiveRestrictionNames))
            {
                return true;
            }

            if (ContainsAnyByName(
                    profileConfig.Gags,
                    activeState.ActiveGagNames))
            {
                return true;
            }

            return false;
        }

        private static bool ContainsAnyByIdOrName(
            List<GagSpeakItem> configuredItems,
            HashSet<Guid> activeIds,
            HashSet<string> activeNames)
        {
            foreach (var item in configuredItems)
            {
                if (item.Id != Guid.Empty && activeIds.Contains(item.Id))
                    return true;

                if (!string.IsNullOrWhiteSpace(item.Name) && activeNames.Contains(item.Name))
                    return true;
            }

            return false;
        }

        private static bool ContainsAnyByName(
            List<GagSpeakItem> configuredItems,
            HashSet<string> activeNames)
        {
            foreach (var item in configuredItems)
            {
                if (!string.IsNullOrWhiteSpace(item.Name) && activeNames.Contains(item.Name))
                    return true;
            }

            return false;
        }

        private void EnsureProfileState(
            CustomizePlusEnforcerConfig profileConfig,
            bool shouldBeActive)
        {
            var isActive = plugin.CustomizePlusApi.IsProfileEnabled(profileConfig.ProfileId);

            if (isActive == shouldBeActive)
                return;

            var ok = plugin.CustomizePlusApi.SetProfileEnabled(
                profileConfig.ProfileId,
                shouldBeActive);

            if (!ok)
            {
                Plugin.ChatGui.PrintError(
                    $"Failed to set Customize+ profile '{profileConfig.ProfileName}' ({profileConfig.ProfileId}) to {shouldBeActive}.");
            }
        }

        private sealed class CustomizePlusEnforcerActiveState
        {
            public HashSet<Guid> ActiveGagIds { get; init; } = new();
            public HashSet<string> ActiveGagNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);

            public HashSet<Guid> ActiveRestraintSetIds { get; init; } = new();
            public HashSet<string> ActiveRestraintSetNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);

            public HashSet<Guid> ActiveRestrictionIds { get; init; } = new();
            public HashSet<string> ActiveRestrictionNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
