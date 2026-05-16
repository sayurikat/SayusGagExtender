using Dalamud.Plugin.Services;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SayusGagExtender.API.GagSpeak.GagSpeakReflectionContext;
using static SayusGagExtender.MoodleEnforcer;

namespace SayusGagExtender
{
    public sealed class PenumbraEnforcer : IDisposable
    {
        private readonly Plugin plugin;

        private readonly Dictionary<string, bool> lastWantedModStates = new(StringComparer.OrdinalIgnoreCase);
        private DateTime onUpdateNextUTC = DateTime.MinValue;
        private readonly TimeSpan OnUpdateCooldown = TimeSpan.FromSeconds(10);
        public bool IsEnforcing = false;
        public sealed class PenumbraEnforcerConfig
        {
            public string ModDirectory { get; set; } = string.Empty;
            public string ModName { get; set; } = string.Empty;

            public List<GagSpeakItem> RestraintSets { get; set; } = new();
            public List<GagSpeakItem> Restrictions { get; set; } = new();
            public List<GagSpeakItem> Gags { get; set; } = new();
        }

        public PenumbraEnforcer(Plugin plugin)
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

        public PenumbraEnforcerConfig GetOrCreatePenumbraEnforcerConfig(
            string modDirectory,
            string modName)
        {
            var existing = plugin.Configuration.PenumbraEnforcerMods.FirstOrDefault(x =>
                string.Equals(x.ModDirectory, modDirectory, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.ModName = modName;
                return existing;
            }

            var created = new PenumbraEnforcerConfig
            {
                ModDirectory = modDirectory,
                ModName = modName,
            };

            plugin.Configuration.PenumbraEnforcerMods.Add(created);
            return created;
        }

        public void Enforce()
        {
            IsEnforcing = false;

            if (!plugin.Configuration.PenumbraEnforcerEnabled)
                return;

            if (plugin.Configuration.PenumbraEnforcerMods.Count == 0)
                return;

            var activeState = GetActiveState();

            foreach (var modConfig in plugin.Configuration.PenumbraEnforcerMods)
            {
                if (string.IsNullOrWhiteSpace(modConfig.ModDirectory))
                    continue;

                if (modConfig.Restrictions.Count + modConfig.Gags.Count + modConfig.RestraintSets.Count <= 0)
                    continue;

                var shouldBeActive = ShouldModBeActive(modConfig, activeState);

                // Optional optimization. Uncomment if you want to avoid repeat IPC calls
                // when the desired state has not changed.
                //
                // if (lastWantedModStates.TryGetValue(modConfig.ModDirectory, out var lastState) &&
                //     lastState == shouldBeActive)
                // {
                //     continue;
                // }

                if (shouldBeActive)
                    IsEnforcing = true;

                lastWantedModStates[modConfig.ModDirectory] = shouldBeActive;

                EnsureModState(modConfig, shouldBeActive);
            }
        }

        private PenumbraEnforcerActiveState GetActiveState()
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

            return new PenumbraEnforcerActiveState
            {
                ActiveGagNames = activeGags,
                ActiveRestraintSetIds = activeRestraintSetIds,
                ActiveRestraintSetNames = activeRestraintSetNames,
                ActiveRestrictionIds = activeRestrictionIds,
                ActiveRestrictionNames = activeRestrictionNames,
            };
        }

        private bool ShouldModBeActive(
            PenumbraEnforcerConfig modConfig,
            PenumbraEnforcerActiveState activeState)
        {
            if (ContainsAnyByIdOrName(
                    modConfig.RestraintSets,
                    activeState.ActiveRestraintSetIds,
                    activeState.ActiveRestraintSetNames))
            {
                return true;
            }

            if (ContainsAnyByIdOrName(
                    modConfig.Restrictions,
                    activeState.ActiveRestrictionIds,
                    activeState.ActiveRestrictionNames))
            {
                return true;
            }

            if (ContainsAnyByName(
                    modConfig.Gags,
                    activeState.ActiveGagNames))
            {
                return true;
            }

            return false;
        }

        private static bool ContainsAnyById(
            List<GagSpeakItem> configuredItems,
            HashSet<Guid> activeIds)
        {
            foreach (var item in configuredItems)
            {
                if (item.Id != Guid.Empty && activeIds.Contains(item.Id))
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

        private void EnsureModState(
            PenumbraEnforcerConfig modConfig,
            bool shouldBeActive)
        {
            var isActive = plugin.PenumbraApi.IsModEnabledOnPlayerCollection(
                modConfig.ModDirectory,
                modConfig.ModName);

            if (isActive == shouldBeActive)
                return;

            plugin.PenumbraApi.SetModEnabledOnPlayerCollection(
                modConfig.ModDirectory,
                shouldBeActive,
                modConfig.ModName);
        }

        private sealed class PenumbraEnforcerActiveState
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
