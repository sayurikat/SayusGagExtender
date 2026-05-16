using Dalamud.Plugin.Services;
using SayusGagExtender.API;
using System;
using System.Collections.Generic;
using System.Linq;
using static SayusGagExtender.API.GagSpeak.GagSpeakReflectionContext;

namespace SayusGagExtender
{
    public sealed partial class EmoteEnforcer : IDisposable
    {
        private readonly Plugin plugin;

        private uint? currentEnforcedEmoteId = null;

        private DateTime onUpdateNextUTC = DateTime.MinValue;
        private readonly TimeSpan OnUpdateCooldown = TimeSpan.FromSeconds(1);

        private DateTime nextEmoteCommandUTC = DateTime.MinValue;
        private readonly TimeSpan EmoteCommandCooldown = TimeSpan.FromSeconds(1);
        public bool IsActive = false;
        private bool RequestedBlockState = false;
        public bool ShouldBlockUserEmotes => plugin.Configuration.EmoteEnforcerEnabled && IsActive;

        public class EmoteEnforcerEmoteConfig
        {
            public uint EmoteId { get; set; } = 0;
            public string EmoteName { get; set; } = string.Empty;

            public List<GagSpeakItem> RestraintSets { get; set; } = new();
            public List<GagSpeakItem> Restrictions { get; set; } = new();
            public List<GagSpeakItem> Gags { get; set; } = new();
        }

        public EmoteEnforcer(Plugin plugin)
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

        public EmoteEnforcerEmoteConfig GetOrCreateEmoteEnforcerConfig(
            uint emoteId,
            string emoteName)
        {
            var existing = plugin.Configuration.EmoteEnforcerEmotes.FirstOrDefault(x => x.EmoteId == emoteId);
            if (existing != null)
            {
                existing.EmoteName = emoteName;
                return existing;
            }

            var created = new EmoteEnforcerEmoteConfig
            {
                EmoteId = emoteId,
                EmoteName = emoteName,
            };

            plugin.Configuration.EmoteEnforcerEmotes.Add(created);
            return created;
        }

        public void Enforce()
        {
            IsActive = false;

            if (!plugin.Configuration.EmoteEnforcerEnabled)
            {
                CancelCurrentEnforcedEmoteOnce();
                return;
            }

            if (plugin.Configuration.EmoteEnforcerEmotes.Count == 0)
            {
                CancelCurrentEnforcedEmoteOnce();
                return;
            }

            var activeState = GetActiveState();

            EmoteEnforcerEmoteConfig? wantedEmoteConfig = null;

            foreach (var emoteConfig in plugin.Configuration.EmoteEnforcerEmotes)
            {
                if (emoteConfig.EmoteId == 0)
                    continue;

                if (emoteConfig.Restrictions.Count + emoteConfig.Gags.Count + emoteConfig.RestraintSets.Count <= 0)
                    continue;

                if (ShouldEmoteBeActive(emoteConfig, activeState))
                {
                    wantedEmoteConfig = emoteConfig;
                    break; // only enforce the first matching emote
                }
            }

            if (wantedEmoteConfig == null)
            {
                CancelCurrentEnforcedEmoteOnce();
                return;
            }

            IsActive = true;

            // If the enforced emote changed, cancel the old one first.
            if (currentEnforcedEmoteId.HasValue &&
                currentEnforcedEmoteId.Value != wantedEmoteConfig.EmoteId)
            {
                CancelCurrentEnforcedEmoteOnce();

                // Respect cooldown. Next update will start the new emote.
                if (!CanSendEmoteCommand())
                    return;
            }

            if (IsActive != RequestedBlockState)
            {
                if (IsActive)
                {
                    plugin.MovementBlocker.RequestBlock(nameof(EmoteEnforcer));
                    plugin.ActionBlocker.RequestBlock(nameof(EmoteEnforcer));
                }
                else
                {
                    plugin.MovementBlocker.ClearBlock(nameof(EmoteEnforcer));
                    plugin.ActionBlocker.ClearBlock(nameof(EmoteEnforcer));
                }
            }
            

            EnsureCurrentEmote(wantedEmoteConfig);
        }

        private EmoteEnforcerActiveState GetActiveState()
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

            return new EmoteEnforcerActiveState
            {
                ActiveGagNames = activeGags,
                ActiveRestraintSetIds = activeRestraintSetIds,
                ActiveRestraintSetNames = activeRestraintSetNames,
                ActiveRestrictionIds = activeRestrictionIds,
                ActiveRestrictionNames = activeRestrictionNames,
            };
        }

        private bool ShouldEmoteBeActive(
            EmoteEnforcerEmoteConfig emoteConfig,
            EmoteEnforcerActiveState activeState)
        {
            if (ContainsAnyByIdOrName(
                    emoteConfig.RestraintSets,
                    activeState.ActiveRestraintSetIds,
                    activeState.ActiveRestraintSetNames))
            {
                return true;
            }

            if (ContainsAnyByIdOrName(
                    emoteConfig.Restrictions,
                    activeState.ActiveRestrictionIds,
                    activeState.ActiveRestrictionNames))
            {
                return true;
            }

            if (ContainsAnyByName(
                    emoteConfig.Gags,
                    activeState.ActiveGagNames))
            {
                return true;
            }

            return false;
        }

        private void EnsureCurrentEmote(EmoteEnforcerEmoteConfig emoteConfig)
        {
            var emoteId = emoteConfig.EmoteId;
            var currentEmoteId = plugin.EmoteApi.GetCurrentLocalPlayerEmoteId();

            if (currentEmoteId == emoteId)
            {
                currentEnforcedEmoteId = emoteId;
                return;
            }

            if (!CanSendEmoteCommand())
                return;

            if (plugin.EmoteApi.ExecuteEmote(emoteId))
            {
                currentEnforcedEmoteId = emoteId;
                nextEmoteCommandUTC = DateTime.UtcNow + EmoteCommandCooldown;
            }
            //plugin.PenumbraApi.RedrawSelf();
        }

        private bool CanSendEmoteCommand()
        {
            return DateTime.UtcNow >= nextEmoteCommandUTC;
        }

        private void CancelCurrentEnforcedEmoteOnce()
        {
            plugin.MovementBlocker.ClearBlock(nameof(EmoteEnforcer));
            plugin.ActionBlocker.ClearBlock(nameof(EmoteEnforcer));
            RequestedBlockState = false;

            if (!currentEnforcedEmoteId.HasValue)
                return;

            var emoteId = currentEnforcedEmoteId.Value;
            var currentEmoteId = plugin.EmoteApi.GetCurrentLocalPlayerEmoteId();

            currentEnforcedEmoteId = null;

            if (currentEmoteId != emoteId)
                return;

            if (!CanSendEmoteCommand())
                return;

            plugin.EmoteApi.CancelEmote();
            nextEmoteCommandUTC = DateTime.UtcNow + EmoteCommandCooldown;
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

        private sealed class EmoteEnforcerActiveState
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
