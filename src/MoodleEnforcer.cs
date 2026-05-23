using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using static SayusGagExtender.API.GagSpeak.GagSpeakReflectionContext;

namespace SayusGagExtender
{
    public sealed class MoodleEnforcer : IDisposable
    {
        private readonly Plugin plugin;
        private readonly Dictionary<Guid, HashSet<string>> externalEnforcedMoodles = new();

        //private readonly Dictionary<Guid, bool> lastWantedMoodleStates = new();
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
            IsActive = false;

            var now = DateTime.UtcNow;
            if (Plugin.Condition[ConditionFlag.BetweenAreas])
                betweenAreasNextUTC = now + betweenAreasCooldown;

            if (now < betweenAreasNextUTC)
                return;

            MoodleEnforcerActiveState? activeState = null;

            if (plugin.Configuration.MoodleEnforcerEnabled)
            {
                activeState = GetActiveState();

                foreach (var moodleConfig in plugin.Configuration.MoodleEnforcerMoodles)
                {
                    if (moodleConfig.MoodleId == Guid.Empty)
                        continue;

                    var hasConfiguredTriggers =
                        moodleConfig.Restrictions.Count +
                        moodleConfig.Gags.Count +
                        moodleConfig.RestraintSets.Count > 0;

                    // Only configured/monitored Moodles are controlled by regular enforcement.
                    // This still preserves random user-applied Moodles.
                    if (!hasConfiguredTriggers)
                        continue;

                    var wantedByRegular = ShouldMoodleBeActive(moodleConfig, activeState);
                    var wantedByExternal = IsExternallyEnforced(moodleConfig.MoodleId);

                    // Core rule:
                    // A Moodle stays active as long as regular OR external wants it.
                    var shouldBeActive = wantedByRegular || wantedByExternal;

                    if (shouldBeActive)
                        IsActive = true;

                    //var isActive = plugin.MoodlesApi.IsStatusActive(moodleConfig.MoodleId);
                    //if (!shouldBeActive && isActive)
                    //{
                    //    Plugin.ChatGui.Print($"removed during enforce, wantedByRegular {wantedByRegular} wantedByExternal {wantedByExternal} shouldBeActive {shouldBeActive} isActive {isActive}");
                    //}

                    EnsureMoodleState(moodleConfig.MoodleId, shouldBeActive);
                }
            }

            // External requests are not gated by MoodleEnforcerEnabled.
            // They are runtime ownership requests from other systems.
            foreach (var moodleId in externalEnforcedMoodles.Keys.ToArray())
            {
                if (moodleId == Guid.Empty)
                    continue;

                //if (!IsExternallyEnforced(moodleId))
                //    continue;
            
                IsActive = true;
                EnsureMoodleState(moodleId, true);
            }
        }

        public void AddEnforcedMoodle(Guid moodleId, string caller)
        {
            //Plugin.ChatGui.Print($"Adding moodle {moodleId} from {caller}");
            if (moodleId == Guid.Empty)
                return;

            caller = NormalizeExternalCaller(caller);

            if (!externalEnforcedMoodles.TryGetValue(moodleId, out var callers))
            {
                callers = new HashSet<string>(StringComparer.Ordinal);
                externalEnforcedMoodles[moodleId] = callers;
            }

            callers.Add(caller);

            Enforce();
        }

        public void AddEnforcedMoodle(Guid moodleId, Type callerType)
        {
            AddEnforcedMoodle(moodleId, callerType?.FullName ?? "unknown");
        }

        public void AddEnforcedMoodle(Guid moodleId, object caller)
        {
            AddEnforcedMoodle(moodleId, caller?.GetType().FullName ?? "unknown");
        }

        public bool RemoveEnforcedMoodle(Guid moodleId, string caller)
        {
            //Plugin.ChatGui.Print($"Removing moodle {moodleId} from {caller}");
            if (moodleId == Guid.Empty)
                return false;

            caller = NormalizeExternalCaller(caller);

            if (!externalEnforcedMoodles.TryGetValue(moodleId, out var callers))
                return false;

            // Only the same caller/source can clear its own request.
            var removed = callers.Remove(caller);

            if (!removed)
                return false;

            // If other external callers still enforce this Moodle, keep it active.
            if (callers.Count > 0)
            {
                Enforce();
                return true;
            }

            // This caller was the last external owner.
            externalEnforcedMoodles.Remove(moodleId);

            // External removal does not force the Moodle off.
            // It only means external no longer wants it.
            // If regular enforcement still wants it, it should stay active / be reapplied.
            var wantedByRegular = IsWantedByRegularEnforcer(moodleId);

            var isActive = plugin.MoodlesApi.IsStatusActive(moodleId);


            //Plugin.ChatGui.Print($"removed during RemoveEnforcedMoodle, wantedByRegular {wantedByRegular} isActive {isActive}");

            EnsureMoodleState(moodleId, wantedByRegular);

            Enforce();

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

        public bool IsExternallyEnforced(Guid moodleId)
        {
            return moodleId != Guid.Empty
                   && externalEnforcedMoodles.TryGetValue(moodleId, out var callers)
                   && callers.Count > 0;
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

                var hasConfiguredTriggers =
                    moodleConfig.Restrictions.Count +
                    moodleConfig.Gags.Count +
                    moodleConfig.RestraintSets.Count > 0;

                if (!hasConfiguredTriggers)
                    continue;

                if (ShouldMoodleBeActive(moodleConfig, activeState))
                    return true;
            }

            return false;
        }
        private static string NormalizeExternalCaller(string caller)
        {
            return string.IsNullOrWhiteSpace(caller)
                ? "unknown"
                : caller.Trim();
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
                //Plugin.ChatGui.Print($"ActiveRestrictions {moodleConfig.MoodleName}");
                return true;
            }

            if (ContainsAnyByName(moodleConfig.Gags, activeState.ActiveGagNames))
            {
                //Plugin.ChatGui.Print($"ActiveGags {moodleConfig.MoodleName}");
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
                {
                    //Plugin.ChatGui.Print($"ActiveRestrictions {item.Name}");
                    return true; 
                }
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
