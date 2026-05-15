using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SayusGagExtender
{
    public sealed class MoodleEnforcer : IDisposable
    {
        private readonly Plugin plugin;

        private readonly Dictionary<Guid, bool> lastWantedMoodleStates = new();
        private DateTime onUpdateNextUTC = DateTime.MinValue;
        private TimeSpan OnUpdateCooldown = TimeSpan.FromSeconds(10);

        public class MoodleEnforcerMoodleConfig
        {
            public Guid MoodleId { get; set; } = Guid.Empty;
            public string MoodleName { get; set; } = string.Empty;
            public bool IsMoodleEnabled { get; set; } = false;
            public List<MoodleEnforcerItem> RestraintSets { get; set; } = new();
            public List<MoodleEnforcerItem> Restrictions { get; set; } = new();
            public List<MoodleEnforcerItem> Gags { get; set; } = new();
        }

        public class MoodleEnforcerItem
        {
            public Guid Id { get; set; } = Guid.Empty;
            public string Name { get; set; } = string.Empty;
        }

        public MoodleEnforcer(Plugin plugin)
        {
            this.plugin = plugin;

            Plugin.Framework.Update += this.OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged += this.OnAnyChanged;
            plugin.GagSpeakGagsApi.OnGagsChanged += this.OnAnyChanged;
            plugin.GagSpeakRestraintSetApi.OnRestraintSetChanged += this.OnAnyChanged;
            plugin.MoodlesApi.ActiveMoodlesChanged += this.OnAnyChanged;
        }
        public void Dispose()
        {
            Plugin.Framework.Update -= this.OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged -= this.OnAnyChanged;
            plugin.GagSpeakGagsApi.OnGagsChanged -= this.OnAnyChanged;
            plugin.GagSpeakRestraintSetApi.OnRestraintSetChanged -= this.OnAnyChanged;
            plugin.MoodlesApi.ActiveMoodlesChanged -= this.OnAnyChanged;
        }
        private void OnAnyChanged(object obj)
        {
            Plugin.ChatGui.Print("OnAnyChanged.");
            Enforce();
        }
        private void OnFrameworkUpdate(IFramework framework)
        {
            if (onUpdateNextUTC > DateTime.UtcNow)
                return;
            onUpdateNextUTC = DateTime.UtcNow + OnUpdateCooldown;
            
            Enforce();
        }

        public MoodleEnforcerMoodleConfig GetOrCreateMoodleEnforcerConfig(Guid moodleId, string moodleName)
        {
            var existing = plugin.Configuration.MoodleEnforcerMoodles.FirstOrDefault(x => x.MoodleId == moodleId);
            if (existing != null)
            {
                existing.MoodleName = moodleName;
                return existing;
            }

            var created = new MoodleEnforcerMoodleConfig
            {
                MoodleId = moodleId,
                MoodleName = moodleName,
            };

            plugin.Configuration.MoodleEnforcerMoodles.Add(created);
            return created;
        }

        public void Enforce()
        {
            Plugin.ChatGui.Print("GagSpeak Enforce.");

            if (!plugin.Configuration.MoodleEnforcerEnabled)
                return;

            if (plugin.Configuration.MoodleEnforcerMoodles.Count == 0)
                return;
            //Plugin.ChatGui.Print("GagSpeak Enforce..");

            var activeState = GetActiveState();

            foreach (var moodleConfig in plugin.Configuration.MoodleEnforcerMoodles)
            {
                if (moodleConfig.MoodleId == Guid.Empty)
                    continue;

                if (moodleConfig.Restrictions.Count + moodleConfig.Gags.Count + moodleConfig.RestraintSets.Count <= 0)
                //if (!moodleConfig.IsMoodleEnabled)
                    continue;

                var shouldBeActive = ShouldMoodleBeActive(moodleConfig, activeState);

                //if (lastWantedMoodleStates.TryGetValue(moodleConfig.MoodleId, out var lastState) &&
                //    lastState == shouldBeActive)
                //{
                //    continue;
                //}

                lastWantedMoodleStates[moodleConfig.MoodleId] = shouldBeActive;

                EnsureMoodleState(moodleConfig, shouldBeActive);
            }
        }

        private MoodleEnforcerActiveState GetActiveState()
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

            return new MoodleEnforcerActiveState
            {
                ActiveGagNames = activeGags,
                ActiveRestraintSetIds = activeRestraintSetIds,
                ActiveRestraintSetNames = activeRestraintSetNames,
                ActiveRestrictionIds = activeRestrictionIds,
                ActiveRestrictionNames = activeRestrictionNames,
            };
        }

        private bool ShouldMoodleBeActive(MoodleEnforcerMoodleConfig moodleConfig, MoodleEnforcerActiveState activeState)
        {
            if (ContainsAnyById(moodleConfig.RestraintSets, activeState.ActiveRestraintSetIds))
            {
                return true;
            }

            if (ContainsAnyById(moodleConfig.Restrictions, activeState.ActiveRestrictionIds))
            {
                //Plugin.ChatGui.Print("ActiveRestrictionIds");
                Plugin.ChatGui.Print($"ActiveRestrictions {moodleConfig.MoodleName}");
                return true;
            }

            if (ContainsAnyByName(moodleConfig.Gags, activeState.ActiveGagNames))
            {
                Plugin.ChatGui.Print($"ActiveGags {moodleConfig.MoodleName}");
                return true;
            }

            return false;
        }

        private static bool ContainsAnyById(
            List<MoodleEnforcerItem> configuredItems,
            HashSet<Guid> activeIds)
        {
            foreach (var item in configuredItems)
            {
                if (item.Id != Guid.Empty && activeIds.Contains(item.Id))
                {
                    Plugin.ChatGui.Print($"ActiveRestrictions {item.Name}");
                    return true; 
                }
            }

            return false;
        }
        private static bool ContainsAnyByIdOrName(
            List<MoodleEnforcerItem> configuredItems,
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
            List<MoodleEnforcerItem> configuredItems,
            HashSet<string> activeNames)
        {
            foreach (var item in configuredItems)
            {
                if (!string.IsNullOrWhiteSpace(item.Name) && activeNames.Contains(item.Name))
                    return true;
            }

            return false;
        }

        private void EnsureMoodleState(
            MoodleEnforcerMoodleConfig moodleConfig,
            bool shouldBeActive)
        {
            var isActive = plugin.MoodlesApi.IsStatusActive(moodleConfig.MoodleId);

            if (isActive == shouldBeActive)
                return;

            if (shouldBeActive)
                plugin.MoodlesApi.ApplyMoodle(moodleConfig.MoodleId);
            else
                plugin.MoodlesApi.RemoveMoodle(moodleConfig.MoodleId);
        }

        private sealed class MoodleEnforcerActiveState
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
