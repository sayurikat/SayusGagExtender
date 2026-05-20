using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SayusGagExtender.API.GagSpeak;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using static SayusGagExtender.CharacterHelper;

namespace SayusGagExtender
{
    public sealed class MirrorGagSpeak : IDisposable
    {
        private readonly Plugin plugin;
        private static readonly TimeSpan updateCooldown = TimeSpan.FromSeconds(2);
        private DateTime updateOnUTC = DateTime.MinValue;
        private bool forceGagSpeakState => plugin.Configuration.GagSpeakEnforcedRestraintCloner;
        private bool didLoginAndReady = false;
        private bool appliedAfterReload = false;
        public bool IsActive = false;
        private TimeSpan firstApplicationDelay = TimeSpan.FromSeconds(10);
        private DateTime firstApplicationDelayUntil = DateTime.MaxValue;
        private TimeSpan mirrorCooldown = TimeSpan.FromSeconds(5);
        private DateTime mirrorCooldownUntil = DateTime.MinValue;
        private string currentCharacterName = "";
        private string currentCharacterWorld = "";
        private bool treatPluginReloadAsCharacterLogin = false;

        private bool ApplyGagSpeakLoginBugOrRaceConditionFix = true;

        public MirrorGagSpeak(Plugin plugin)
        {
            this.plugin = plugin;

            Plugin.Framework.Update += OnFrameworkUpdate;
            plugin.GagSpeakRestraintSetApi.OnRestraintSetChanged += UpdateSavedRestraintSet;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged += UpdateSavedRestrictions;
            plugin.GagSpeakGagsApi.OnGagsChanged += UpdateSavedGags;
            
            plugin.CharacterHelper.OnCharacterReady += OnCharacterReady;

            if (treatPluginReloadAsCharacterLogin)
            {
                if (plugin.CharacterHelper.CurrentCharacter is { } character)
                    OnCharacterReady(character);
            }
            

        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (!plugin.Configuration.GagSpeakRestraintCloner && !ApplyGagSpeakLoginBugOrRaceConditionFix)
                return;

            if (!Plugin.Condition[ConditionFlag.NormalConditions])
                return;

            var now = DateTime.UtcNow;
            if (updateOnUTC > now)
                return;
            updateOnUTC = now + updateCooldown;


            if (didLoginAndReady && plugin.GagSpeakContext.EnsureReady())
            {
                if (firstApplicationDelayUntil == DateTime.MaxValue)
                {
                    firstApplicationDelayUntil = now + firstApplicationDelay;
                }
                if (now > firstApplicationDelayUntil)
                {
                    //redundant when ApplyGagSpeakLoginBugOrRaceConditionFix == false
                    if (plugin.Configuration.GagSpeakRestraintCloner)
                    {
                        if (!IsMasterCharacter())
                        {
                            MirrorGagSpeakState(didRelog: true);
                        }
                    }
                    
                    if (ApplyGagSpeakLoginBugOrRaceConditionFix)
                    {
                        GagSpeakLoginBugOrRaceConditionFix();
                    }
                    
                    didLoginAndReady = false;
                    firstApplicationDelayUntil = DateTime.MaxValue;
                }
            }
        }
        private void OnCharacterReady(CharacterIdentity character)
        {
            didLoginAndReady = true;
        }
        private void GagSpeakLoginBugOrRaceConditionFix()
        {
            /*
             * Appears to be a bug or issuen with race condition when character logging in.
             * Seems like character state are cached before items are applied properly.
             * Seems like it may happen more frequent with this plugin, as it may cause additional delays during the race conditions.
             * Refreshing visuals once we know we are logged in and ready.
             * 
             * 
             */
            Plugin.Log.Info($"GagSpeakLoginBugOrRaceConditionFix: RefreshGagSpeakVisuals");
            plugin.GagSpeakContext.RefreshGagSpeakVisualsAsync(redraw: true);
        }
        public void MirrorGagSpeakState(bool didRelog = false)
        {

            if (!forceGagSpeakState && !didRelog)
                return;

            var now = DateTime.UtcNow;
            if (now < mirrorCooldownUntil)
                return;
            mirrorCooldownUntil = now + mirrorCooldown;

            Plugin.ChatGui.Print($"Mirroring saved Gag Speak restraints. {forceGagSpeakState}");
            
            
            if (plugin.Configuration.GagSpeakMasterName == null || plugin.Configuration.GagSpeakMasterName.Length < 0 || plugin.Configuration.GagSpeakMasterWorld == null || plugin.Configuration.GagSpeakMasterWorld.Length < 0)
                return;

            var activeRestraintSet = plugin.GagSpeakRestraintSetApi.GetActiveRestraintSet().Value;
            var activeRestrictions = plugin.GagSpeakRestrictionsApi.GetActiveRestrictions();
            var activeGags = plugin.GagSpeakGagsApi.GetActiveGags();

            if (activeRestraintSet != plugin.Configuration.ActiveRestraintSet)
            {
                plugin.GagSpeakRestraintSetApi.RemoveRestraintSet(activeRestraintSet);
                plugin.GagSpeakRestraintSetApi.ApplyRestraintSet(plugin.Configuration.ActiveRestraintSet);
            }



            //int restrictionsMax = GagSpeakRestrictionsApi.MaxRestrictionsLayers;
            bool restrictionsDiffer = !HaveSameStrings(activeRestrictions, plugin.Configuration.ActiveRestrictions);
            if (restrictionsDiffer)
                {
                foreach (var restriction in activeRestrictions.Values)
                {
                    plugin.GagSpeakRestrictionsApi.RemoveRestriction(restriction);
                }
                foreach (var restriction in plugin.Configuration.ActiveRestrictions.Values)
                {
                    plugin.GagSpeakRestrictionsApi.ApplyRestriction(restriction);
                }
            }


            //int gagsMax = GagSpeakGagsApi.MaxGagLayers;
            bool gagsDiffer = !HaveSameStrings(activeGags, plugin.Configuration.ActiveGags);
            if (gagsDiffer)
            {
                foreach (var gag in activeGags.Values)
                {
                    plugin.GagSpeakGagsApi.RemoveGag(gag);
                }
                foreach (var gag in plugin.Configuration.ActiveGags.Values)
                {
                    plugin.GagSpeakGagsApi.ApplyGag(gag);
                }
            }
        }
        private void UpdateSavedRestraintSet(string newSet)
        {
            if (!plugin.Configuration.GagSpeakRestraintCloner)
                return;

            if (IsMasterCharacter())
            {
                plugin.Configuration.ActiveRestraintSet = newSet;
                plugin.Configuration.Save();
                Plugin.ChatGui.Print("Restraint set saved.");
            }
            else
            {
                //if (forceGagSpeakState)
                //{
                    MirrorGagSpeakState();
                //}
            }
        }
        private void UpdateSavedRestrictions(Dictionary<int, string> restrictions)
        {
            if (!plugin.Configuration.GagSpeakRestraintCloner)
                return;

            if (IsMasterCharacter())
            {
                plugin.Configuration.ActiveRestrictions = restrictions;
                plugin.Configuration.Save();
                Plugin.ChatGui.Print("Restrictions saved.");
            }
            else
            {
                //if (forceGagSpeakState)
                //{
                    MirrorGagSpeakState();
                //}
            }
        }
        private void UpdateSavedGags(Dictionary<int, string> gags)
        {
            if (!plugin.Configuration.GagSpeakRestraintCloner)
                return;

            if (IsMasterCharacter())
            {
                plugin.Configuration.ActiveGags = gags;
                plugin.Configuration.Save();
                Plugin.ChatGui.Print("Gags saved.");
            }
            else
            {
                //if (forceGagSpeakState)
                //{
                    MirrorGagSpeakState();
                //}
            }
        }


        public void Dispose()
        {
            Plugin.Framework.Update -= OnFrameworkUpdate;
            plugin.GagSpeakRestraintSetApi.OnRestraintSetChanged -= UpdateSavedRestraintSet;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged -= UpdateSavedRestrictions;
            plugin.GagSpeakGagsApi.OnGagsChanged -= UpdateSavedGags;
            plugin.CharacterHelper.OnCharacterReady -= OnCharacterReady;
        }
        private bool IsMasterCharacter()
        {
            string name = "";
            string world = "";
            name = Plugin.ObjectTable.LocalPlayer?.Name.ToString() ?? "";
            var homeWorld = Plugin.ObjectTable.LocalPlayer?.HomeWorld.RowId ?? 0u;
            world = plugin.Utils.WorldRowIDToString(homeWorld);

            if (name == plugin.Configuration.GagSpeakMasterName &&
                world == plugin.Configuration.GagSpeakMasterWorld)
            {
                IsActive = false;
                return true;
            }
            IsActive = true;
            return false;
        }
        private bool DidRelogToDifferentCharacter()
        {
            string name = "";
            string world = "";
            name = Plugin.ObjectTable.LocalPlayer?.Name.ToString() ?? "";
            var homeWorld = Plugin.ObjectTable.LocalPlayer?.HomeWorld.RowId ?? 0u;
            world = plugin.Utils.WorldRowIDToString(homeWorld);

            if (name == currentCharacterName && world == currentCharacterWorld)
            {
                return false;
            }

            currentCharacterName = name;
            currentCharacterWorld = world;
            return true;
        }

        public static bool HaveSameStrings(Dictionary<int, string> activeRestrictions, Dictionary<int, string> configurationActiveRestrictions)
        {
            if (activeRestrictions == null || configurationActiveRestrictions == null)
                return activeRestrictions == configurationActiveRestrictions;

            return activeRestrictions.Values
                .OrderBy(x => x)
                .SequenceEqual(configurationActiveRestrictions.Values.OrderBy(x => x));
        }
    }
}
