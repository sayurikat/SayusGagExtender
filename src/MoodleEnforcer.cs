using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using static SayusGagExtender.API.GagSpeak.GagSpeakReflectionContext;

namespace SayusGagExtender
{
    public sealed class MoodleEnforcer : IDisposable
    {
        private readonly Plugin plugin;
        private readonly Dictionary<string, Guid> registeredExternalMoodles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Guid> requestedExternalMoodles = new(StringComparer.Ordinal);
        private readonly HashSet<Guid> staleExternalMoodlesToRelease = new();

        private DateTime onUpdateNextUTC = DateTime.MinValue;
        private TimeSpan OnUpdateCooldown = TimeSpan.FromSeconds(10);
        private DateTime betweenAreasNextUTC = DateTime.MinValue;
        private TimeSpan betweenAreasCooldown = TimeSpan.FromSeconds(10);
        public bool IsActive = false;

        public class MoodleEnforcerMoodleConfig
        {
            public Guid MoodleId { get; set; } = Guid.Empty;
            public string MoodleName { get; set; } = string.Empty;
            public bool IsMoodleEnabled { get; set; } = false;
            public List<GagSpeakItem> RestraintSets { get; set; } = new();
            public List<GagSpeakItem> Restrictions { get; set; } = new();
            public List<GagSpeakItem> Gags { get; set; } = new();
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
            RequestEnforceSoon();
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (onUpdateNextUTC > DateTime.UtcNow)
                return;

            onUpdateNextUTC = DateTime.UtcNow + OnUpdateCooldown;
            Enforce();
        }

        private void RequestEnforceSoon()
        {
            onUpdateNextUTC = DateTime.MinValue;
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
            IsActive = false;

            var now = DateTime.UtcNow;
            if (Plugin.Condition[ConditionFlag.BetweenAreas])
                betweenAreasNextUTC = now + betweenAreasCooldown;

            if (now < betweenAreasNextUTC)
                return;

            var activeState = plugin.Configuration.MoodleEnforcerEnabled ? GetActiveState() : null;
            var controlledMoodleIds = GetControlledMoodleIds(activeState);
            var requestedMoodleIds = requestedExternalMoodles.Values.Where(x => x != Guid.Empty).ToHashSet();

            foreach (var moodleId in controlledMoodleIds)
            {
                var wantedByRegular = activeState != null && IsWantedByRegularEnforcer(moodleId, activeState);
                var wantedByExternal = requestedMoodleIds.Contains(moodleId);
                var shouldBeActive = wantedByRegular || wantedByExternal;

                if (shouldBeActive)
                    IsActive = true;

                EnsureMoodleState(moodleId, shouldBeActive);
            }

            staleExternalMoodlesToRelease.Clear();
        }

        private HashSet<Guid> GetControlledMoodleIds(MoodleEnforcerActiveState? activeState)
        {
            var controlledMoodleIds = new HashSet<Guid>();

            if (activeState != null)
            {
                foreach (var moodleConfig in plugin.Configuration.MoodleEnforcerMoodles)
                {
                    if (moodleConfig.MoodleId == Guid.Empty)
                        continue;

                    var hasConfiguredTriggers = moodleConfig.Restrictions.Count + moodleConfig.Gags.Count + moodleConfig.RestraintSets.Count > 0;
                    if (hasConfiguredTriggers)
                        controlledMoodleIds.Add(moodleConfig.MoodleId);
                }
            }

            foreach (var moodleId in registeredExternalMoodles.Values)
            {
                if (moodleId != Guid.Empty)
                    controlledMoodleIds.Add(moodleId);
            }

            foreach (var moodleId in requestedExternalMoodles.Values)
            {
                if (moodleId != Guid.Empty)
                    controlledMoodleIds.Add(moodleId);
            }

            foreach (var moodleId in staleExternalMoodlesToRelease)
            {
                if (moodleId != Guid.Empty)
                    controlledMoodleIds.Add(moodleId);
            }

            return controlledMoodleIds;
        }

        public void RegisterExternalMoodle(Guid moodleId, string sourceKey)
        {
            sourceKey = NormalizeExternalSourceKey(sourceKey);

            if (registeredExternalMoodles.TryGetValue(sourceKey, out var oldMoodleId) && oldMoodleId == moodleId)
                return;

            if (registeredExternalMoodles.TryGetValue(sourceKey, out oldMoodleId) && oldMoodleId != Guid.Empty && oldMoodleId != moodleId)
                staleExternalMoodlesToRelease.Add(oldMoodleId);

            if (moodleId == Guid.Empty)
                registeredExternalMoodles.Remove(sourceKey);
            else
                registeredExternalMoodles[sourceKey] = moodleId;

            if (requestedExternalMoodles.TryGetValue(sourceKey, out var requestedMoodleId) && requestedMoodleId != moodleId)
            {
                if (requestedMoodleId != Guid.Empty)
                    staleExternalMoodlesToRelease.Add(requestedMoodleId);

                requestedExternalMoodles.Remove(sourceKey);
            }

            RequestEnforceSoon();
        }

        public void RegisterExternalMoodle(Guid moodleId, Type callerType)
        {
            RegisterExternalMoodle(moodleId, callerType?.FullName ?? "unknown");
        }

        public void RegisterExternalMoodle(Guid moodleId, object caller)
        {
            RegisterExternalMoodle(moodleId, caller?.GetType().FullName ?? "unknown");
        }

        public bool UnregisterExternalMoodle(string sourceKey)
        {
            sourceKey = NormalizeExternalSourceKey(sourceKey);
            var changed = false;

            if (registeredExternalMoodles.Remove(sourceKey, out var registeredMoodleId))
            {
                if (registeredMoodleId != Guid.Empty)
                    staleExternalMoodlesToRelease.Add(registeredMoodleId);

                changed = true;
            }

            if (requestedExternalMoodles.Remove(sourceKey, out var requestedMoodleId))
            {
                if (requestedMoodleId != Guid.Empty)
                    staleExternalMoodlesToRelease.Add(requestedMoodleId);

                changed = true;
            }

            if (changed)
                RequestEnforceSoon();

            return changed;
        }

        public bool UnregisterExternalMoodle(Type callerType)
        {
            return UnregisterExternalMoodle(callerType?.FullName ?? "unknown");
        }

        public bool UnregisterExternalMoodle(object caller)
        {
            return UnregisterExternalMoodle(caller?.GetType().FullName ?? "unknown");
        }

        public void AddEnforcedMoodle(Guid moodleId, string sourceKey)
        {
            if (moodleId == Guid.Empty)
            {
                RemoveEnforcedMoodle(sourceKey);
                return;
            }

            sourceKey = NormalizeExternalSourceKey(sourceKey);
            RegisterExternalMoodle(moodleId, sourceKey);
            requestedExternalMoodles[sourceKey] = moodleId;
            RequestEnforceSoon();
        }

        public void AddEnforcedMoodle(Guid moodleId, Type callerType)
        {
            AddEnforcedMoodle(moodleId, callerType?.FullName ?? "unknown");
        }

        public void AddEnforcedMoodle(Guid moodleId, object caller)
        {
            AddEnforcedMoodle(moodleId, caller?.GetType().FullName ?? "unknown");
        }

        public bool RemoveEnforcedMoodle(string sourceKey)
        {
            sourceKey = NormalizeExternalSourceKey(sourceKey);

            if (!requestedExternalMoodles.Remove(sourceKey, out var requestedMoodleId))
                return false;

            if (requestedMoodleId != Guid.Empty)
                staleExternalMoodlesToRelease.Add(requestedMoodleId);

            RequestEnforceSoon();
            return true;
        }

        public bool RemoveEnforcedMoodle(Guid moodleId, string sourceKey)
        {
            if (moodleId == Guid.Empty)
                return false;

            sourceKey = NormalizeExternalSourceKey(sourceKey);

            if (!requestedExternalMoodles.TryGetValue(sourceKey, out var requestedMoodleId))
                return false;

            if (requestedMoodleId != moodleId)
                return false;

            requestedExternalMoodles.Remove(sourceKey);
            staleExternalMoodlesToRelease.Add(moodleId);
            RequestEnforceSoon();
            return true;
        }

        public bool RemoveEnforcedMoodle(Guid moodleId, Type callerType)
        {
            return RemoveEnforcedMoodle(moodleId, callerType?.FullName ?? "unknown");
        }

        public bool RemoveEnforcedMoodle(Guid moodleId, object caller)
        {
            return RemoveEnforcedMoodle(moodleId, caller?.GetType().FullName ?? "unknown");
        }

        public bool RemoveEnforcedMoodle(Type callerType)
        {
            return RemoveEnforcedMoodle(callerType?.FullName ?? "unknown");
        }

        public bool RemoveEnforcedMoodle(object caller)
        {
            return RemoveEnforcedMoodle(caller?.GetType().FullName ?? "unknown");
        }

        public bool IsExternallyEnforced(Guid moodleId)
        {
            return moodleId != Guid.Empty && requestedExternalMoodles.Values.Any(x => x == moodleId);
        }

        private bool IsWantedByRegularEnforcer(Guid moodleId)
        {
            if (moodleId == Guid.Empty)
                return false;

            if (!plugin.Configuration.MoodleEnforcerEnabled)
                return false;

            var activeState = GetActiveState();
            return IsWantedByRegularEnforcer(moodleId, activeState);
        }

        private bool IsWantedByRegularEnforcer(Guid moodleId, MoodleEnforcerActiveState activeState)
        {
            if (moodleId == Guid.Empty)
                return false;

            foreach (var moodleConfig in plugin.Configuration.MoodleEnforcerMoodles)
            {
                if (moodleConfig.MoodleId != moodleId)
                    continue;

                var hasConfiguredTriggers = moodleConfig.Restrictions.Count + moodleConfig.Gags.Count + moodleConfig.RestraintSets.Count > 0;
                if (!hasConfiguredTriggers)
                    continue;

                if (ShouldMoodleBeActive(moodleConfig, activeState))
                    return true;
            }

            return false;
        }

        private static string NormalizeExternalSourceKey(string sourceKey)
        {
            return string.IsNullOrWhiteSpace(sourceKey) ? "unknown" : sourceKey.Trim();
        }

        private MoodleEnforcerActiveState GetActiveState()
        {
            var activeGags = plugin.GagSpeakGagsApi.GetActiveGags().Values.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var activeRestraintSet = plugin.GagSpeakRestraintSetApi.GetActiveRestraintSet();
            var activeRestraintSetIds = new HashSet<Guid>();

            if (activeRestraintSet.Key != Guid.Empty)
                activeRestraintSetIds.Add(activeRestraintSet.Key);

            var activeRestraintSetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(activeRestraintSet.Value))
                activeRestraintSetNames.Add(activeRestraintSet.Value);

            var activeRestrictions = plugin.GagSpeakRestrictionsApi.GetActiveRestrictionsWithId();
            var activeRestrictionIds = activeRestrictions.Where(x => x.Key != Guid.Empty).Select(x => x.Key).ToHashSet();
            var activeRestrictionNames = activeRestrictions.Values.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);

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
                return true;

            if (ContainsAnyById(moodleConfig.Restrictions, activeState.ActiveRestrictionIds))
                return true;

            if (ContainsAnyByName(moodleConfig.Gags, activeState.ActiveGagNames))
                return true;

            return false;
        }

        private static bool ContainsAnyById(List<GagSpeakItem> configuredItems, HashSet<Guid> activeIds)
        {
            foreach (var item in configuredItems)
            {
                if (item.Id != Guid.Empty && activeIds.Contains(item.Id))
                    return true;
            }

            return false;
        }

        private static bool ContainsAnyByName(List<GagSpeakItem> configuredItems, HashSet<string> activeNames)
        {
            foreach (var item in configuredItems)
            {
                if (!string.IsNullOrWhiteSpace(item.Name) && activeNames.Contains(item.Name))
                    return true;
            }

            return false;
        }

        private void EnsureMoodleState(Guid moodleId, bool shouldBeActive)
        {
            if (moodleId == Guid.Empty)
                return;

            var isActive = plugin.MoodlesApi.IsStatusActive(moodleId);

            if (isActive == shouldBeActive)
                return;

            if (shouldBeActive)
                plugin.MoodlesApi.ApplyMoodle(moodleId);
            else
                plugin.MoodlesApi.RemoveMoodle(moodleId);
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
