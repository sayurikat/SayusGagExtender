using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SayusGagExtender.API.GagSpeak;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SayusGagExtender
{
    public sealed class MirrorGagSpeak : IDisposable
    {
        private readonly Plugin plugin;
        private static readonly TimeSpan OnUpdateCooldown = TimeSpan.FromSeconds(2);
        private DateTime onUpdateNextUTC = DateTime.MinValue;
        private bool forceGagSpeakState = false;
        private bool appliedAfterReload = false;
        private TimeSpan waitAfterReload = TimeSpan.FromSeconds(10);
        private DateTime waitUntilAfterReload = DateTime.MaxValue;
        private TimeSpan mirrorCooldown = TimeSpan.FromSeconds(5);
        private DateTime mirrorCooldownUntil = DateTime.MinValue;

        public MirrorGagSpeak(Plugin plugin)
        {
            this.plugin = plugin;

            Plugin.Framework.Update += OnFrameworkUpdate;
            plugin.GagSpeakRestraintSetApi.OnRestraintSetChanged += UpdateSavedRestraintSet;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged += UpdateSavedRestrictions;
            plugin.GagSpeakGagsApi.OnGagsChanged += UpdateSavedGags;
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            var now = DateTime.UtcNow;
            if (onUpdateNextUTC > now)
                return;
            onUpdateNextUTC = now + OnUpdateCooldown;

            if (plugin.GagSpeakContext.EnsureReady() && Plugin.Condition.Any(ConditionFlag.NormalConditions))
            {
                if (!appliedAfterReload)
                {
                    if (!IsMasterCharacter())
                    {
                        waitUntilAfterReload = now + waitAfterReload;
                        //Plugin.ChatGui.Print($"time now. {now}");
                        //Plugin.ChatGui.Print($"Mirroring timer started. {waitUntilAfterReload}");
                    }
                    appliedAfterReload = true;
                }
            }
            else
            {
                //gag speak not ready, likely reloading GagSpeak or relogging character. Reset applied state.
                appliedAfterReload = false;
            }


            if (now > waitUntilAfterReload)
            {
                //Plugin.ChatGui.Print($"Mirroring timer complete. {now}");
                waitUntilAfterReload = DateTime.MaxValue;
                MirrorGagSpeakState();
            }
            


        }
        public void MirrorGagSpeakState()
        {
            var now = DateTime.UtcNow;
            if (now < mirrorCooldownUntil)
                return;
            mirrorCooldownUntil = now + mirrorCooldown;

            Plugin.ChatGui.Print("Mirroring saved Gag Speak restraints.");
            
            
            if (plugin.Configuration.GagSpeakMasterName == null || plugin.Configuration.GagSpeakMasterName.Length < 0 || plugin.Configuration.GagSpeakMasterWorld == null || plugin.Configuration.GagSpeakMasterWorld.Length < 0)
                return;

            var activeRestraintSet = plugin.GagSpeakRestraintSetApi.GetActiveRestraintSet();
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
            if (IsMasterCharacter())
            {
                plugin.Configuration.ActiveRestraintSet = newSet;
                plugin.Configuration.Save();
                Plugin.ChatGui.Print("Restraint set saved.");
            }
            else
            {
                if (forceGagSpeakState)
                {
                    MirrorGagSpeakState();
                }
            }
        }
        private void UpdateSavedRestrictions(Dictionary<int, string> restrictions)
        {
            if (IsMasterCharacter())
            {
                plugin.Configuration.ActiveRestrictions = restrictions;
                plugin.Configuration.Save();
                Plugin.ChatGui.Print("Restrictions saved.");
            }
            else
            {
                if (forceGagSpeakState)
                {
                    MirrorGagSpeakState();
                }
            }
        }
        private void UpdateSavedGags(Dictionary<int, string> gags)
        {
            if (IsMasterCharacter())
            {
                plugin.Configuration.ActiveGags = gags;
                plugin.Configuration.Save();
                Plugin.ChatGui.Print("Gags saved.");
            }
            else
            {
                if (forceGagSpeakState)
                {
                    MirrorGagSpeakState();
                }
            }
        }


        public void Dispose()
        {
            Plugin.Framework.Update -= OnFrameworkUpdate;
            plugin.GagSpeakRestraintSetApi.OnRestraintSetChanged -= UpdateSavedRestraintSet;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged -= UpdateSavedRestrictions;
            plugin.GagSpeakGagsApi.OnGagsChanged -= UpdateSavedGags;
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
                return true;
            }
            return false;
        }

        public static bool HaveSameStrings(
            Dictionary<int, string> activeRestrictions,
            Dictionary<int, string> configurationActiveRestrictions)
        {
            if (activeRestrictions == null || configurationActiveRestrictions == null)
                return activeRestrictions == configurationActiveRestrictions;

            return activeRestrictions.Values
                .OrderBy(x => x)
                .SequenceEqual(configurationActiveRestrictions.Values.OrderBy(x => x));
        }
    }
}
