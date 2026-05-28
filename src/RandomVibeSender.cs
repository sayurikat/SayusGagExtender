using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace SayusGagExtender
{
    public sealed class RandomVibeSender : IDisposable
    {
        private readonly Plugin plugin;
        private readonly Random random = new();

        private int scheduledHour = -1;
        private readonly List<DateTime> triggerTimes = new();
        private int nextTriggerIndex = 0;

        public bool wearsRestrictedItems { get; private set; } = false;
        public bool IsActive => wearsRestrictedItems;

        private DateTime lastItemCheckUtc = DateTime.MinValue;

        private bool autoVibeByControllerMoodleRequested;
        private bool autoVibeAutonomusMoodleRequested;

        private const string AutoVibeControllerMoodleSource = "RandomVibeSender.ControllerOnline";
        private const string AutoVibeAutonomousMoodleSource = "RandomVibeSender.AutonomousEngaged";

        private const float ControllerNearbyDistance = 30f;
        private ControllerPresence lastControllerPresence = ControllerPresence.Offline;

        private readonly List<IDisposable> honorificEmoteSubscriptions = new();
        public enum OperateWhen
        {
            Always,
            Distant,
            Offline
        }

        private enum ControllerPresence
        {
            Offline,
            OnlineDistant,
            OnlineNearby,
        }

        public class WeightedVibeCommand
        {
            public string Command { get; set; } = "";
            public int Weight { get; set; } = 1;

            public string HonorificTitle { get; set; } = "";
            public Vector3 HonorificColor { get; set; } = new(1.0f, 1.0f, 1.0f);
            public Vector3 HonorificGlow { get; set; } = new(0.0f, 0.0f, 0.0f);

            public int HonorificDurationSeconds { get; set; } = 30;
            public int HonorificPriority { get; set; } = 100; 
            public string HonorificTriggerCommand { get; set; } = "";
        }

        public RandomVibeSender(Plugin plugin)
        {
            this.plugin = plugin;

            this.ScheduleCurrentHour();

            Plugin.Framework.Update += this.OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged += this.OnRestrictionsChanged;
            plugin.FriendListHelper.RequestFriendListUpdateWithCooldown();

            RegisterAutoVibeMoodles();
            RefreshHonorificEmoteSubscriptions();
        }

        public void Dispose()
        {
            Plugin.Framework.Update -= this.OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged -= this.OnRestrictionsChanged;

            ClearHonorificEmoteSubscriptions();
            ClearAutoVibeMoodles();
        }

        public void Enable()
        {
            plugin.Configuration.AutoVibeEnabled = true;
        }

        public void Disable()
        {
            plugin.Configuration.AutoVibeEnabled = false;
            ClearAutoVibeMoodles();
        }
        public bool IsAutonomousRunning =>
    plugin.Configuration.AutoVibeEnabled &&
    wearsRestrictedItems &&
    ShouldOperateForPresence(lastControllerPresence);

        public string ControllerPresenceStatus
        {
            get
            {
                //var controllerName = plugin.Configuration.RemoteControllerName;
                //var controllerWorld = plugin.Configuration.RemoteControllerWorld;
                //
                //if (string.IsNullOrWhiteSpace(controllerName) || controllerName.Length <= 3)
                //    return "Not set";
                //
                //var name = string.IsNullOrWhiteSpace(controllerWorld)
                //    ? controllerName
                //    : $"{controllerName}@{controllerWorld}";

                return lastControllerPresence switch
                {
                    ControllerPresence.OnlineNearby => $"Nearby",
                    ControllerPresence.OnlineDistant => $"Distant",
                    ControllerPresence.Offline => $"Offline",
                    _ => $"Unknown",
                };
            }
        }

        public string AutonomousStatus
        {
            get
            {
                if (!plugin.Configuration.AutoVibeEnabled)
                    return "Disabled";

                if (!wearsRestrictedItems)
                    return "Inactive";

                return IsAutonomousRunning
                    ? $"Running (Require {plugin.Configuration.AutoVibeWhen})"
                    : $"Paused (Require {plugin.Configuration.AutoVibeWhen})";
            }
        }
        private void CheckIfWearingRestrictiveItems()
        {
            wearsRestrictedItems = plugin.GagSpeakRestrictionsApi.IsAnyListedRestrictionsActive(
                plugin.Configuration.AutoVibeRequiredRestrictions);
        }

        private void OnRestrictionsChanged(object obj)
        {
            CheckIfWearingRestrictiveItems();
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (!plugin.Configuration.AutoVibeEnabled)
            {
                lastControllerPresence = ControllerPresence.Offline;
                ClearAutoVibeMoodles();
                return;
            }

            var now = DateTime.UtcNow;

            if ((now - this.lastItemCheckUtc).TotalMilliseconds > 5000)
            {
                CheckIfWearingRestrictiveItems();
                this.lastItemCheckUtc = now;
            }

            if (!wearsRestrictedItems)
            {
                lastControllerPresence = ControllerPresence.Offline;
                ClearAutoVibeMoodles();
                return;
            }

            var presence = GetControllerPresence();
            var shouldOperate = ShouldOperateForPresence(presence);

            UpdateAutoVibeMoodles(presence, shouldOperate);

            // New hour: generate new random trigger times.
            if (now.Hour != this.scheduledHour)
            {
                this.ScheduleCurrentHour();
            }

            if (this.nextTriggerIndex >= this.triggerTimes.Count)
                return;

            var nextTriggerTime = this.triggerTimes[this.nextTriggerIndex];

            if (now < nextTriggerTime)
                return;

            // Trigger is due, but ignore it if it is more than 5 minutes old.
            if (now - nextTriggerTime > TimeSpan.FromMinutes(5))
            {
                this.nextTriggerIndex++;
                return;
            }

            if (shouldOperate)
            {
                TrySendRandomVibeCommand(presence);

                
            }

            this.nextTriggerIndex++;
        }

        private void ScheduleCurrentHour()
        {
            var now = DateTime.UtcNow;

            this.scheduledHour = now.Hour;
            this.triggerTimes.Clear();
            this.nextTriggerIndex = 0;

            var hourStart = new DateTime(
                now.Year,
                now.Month,
                now.Day,
                now.Hour,
                0,
                0,
                DateTimeKind.Utc);

            var nextHourStart = hourStart.AddHours(1);
            var totalSeconds = (int)(nextHourStart - hourStart).TotalSeconds;

            for (var i = 0; i < plugin.Configuration.AutoVibeCount; i++)
            {
                var randomSecond = this.random.Next(0, totalSeconds);
                var triggerTime = hourStart.AddSeconds(randomSecond);

                this.triggerTimes.Add(triggerTime);
            }

            this.triggerTimes.Sort();
        }

        private ControllerPresence GetControllerPresence()
        {
            var controllerName = plugin.Configuration.RemoteControllerName;
            var controllerWorld = plugin.Configuration.RemoteControllerWorld;

            if (string.IsNullOrWhiteSpace(controllerName) || controllerName.Length <= 3)
            {
                lastControllerPresence = ControllerPresence.Offline;
                return ControllerPresence.Offline;
            }

            var isOnline = plugin.FriendListHelper.IsFriendOnlineSafe(controllerName, controllerWorld);
            bool assumedOnline = false;
            if (isOnline == null)
            {
                if (lastControllerPresence != ControllerPresence.Offline)
                {
                    assumedOnline = true;
                }
                else
                {
                    assumedOnline = false;
                }
            }
            else
            {
                assumedOnline = (bool)isOnline;
            }

            if (!assumedOnline)
            {
                lastControllerPresence = ControllerPresence.Offline;
                return lastControllerPresence;
            }

            if (TryFindControllerInObjectTable(controllerName, controllerWorld, out var controller))
            {
                var localPlayer = Plugin.ObjectTable.LocalPlayer;

                if (localPlayer != null)
                {
                    var maxDistanceSq = ControllerNearbyDistance * ControllerNearbyDistance;

                    if (Vector3.DistanceSquared(localPlayer.Position, controller.Position) <= maxDistanceSq)
                    {
                        lastControllerPresence = ControllerPresence.OnlineNearby;
                        return lastControllerPresence;

                    }
                }
            }

            lastControllerPresence = ControllerPresence.OnlineDistant;
            return lastControllerPresence;
        }

        private bool ShouldOperateForPresence(ControllerPresence presence)
        {
            return plugin.Configuration.AutoVibeWhen switch
            {
                OperateWhen.Always => true,

                // Controller is offline OR online but not nearby.
                OperateWhen.Distant => presence is ControllerPresence.Offline or ControllerPresence.OnlineDistant,

                // Controller must be offline.
                OperateWhen.Offline => presence == ControllerPresence.Offline,

                _ => false,
            };
        }

        private bool TryFindControllerInObjectTable(
            string controllerName,
            string? controllerWorld,
            out IPlayerCharacter controller)
        {
            controller = null!;

            foreach (var obj in Plugin.ObjectTable)
            {
                if (obj == null)
                    continue;

                if (obj.ObjectKind != ObjectKind.Pc)
                    continue;

                if (obj is not IPlayerCharacter player)
                    continue;

                var name = player.Name.ToString();

                if (!string.Equals(name, controllerName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(controllerWorld))
                {
                    var homeWorld = plugin.Utils.WorldRowIDToString(player.HomeWorld.RowId);

                    if (!string.Equals(homeWorld, controllerWorld, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                controller = player;
                return true;
            }

            return false;
        }

        private void UpdateAutoVibeMoodles(ControllerPresence presence, bool shouldOperate)
        {
            
            var controllerNearby = presence != ControllerPresence.Offline;

            // Moodle for "controller is here / nearby".
            SetAutoVibeByControllerMoodle(controllerNearby);

            // Moodle for "Auto Vibe is currently operating".
            SetAutoVibeAutonomusMoodle(shouldOperate);
        }

        private void TrySendRandomVibeCommand(ControllerPresence presence)
        {
            if (plugin.EmoteEnforcer.ShouldBlockUserEmotes)
                return;

            var interruptedEmoteId = plugin.EmoteApi.GetCurrentLocalPlayerEmoteId();
            if (!plugin.EmoteApi.IsEmoteSpecial((uint)interruptedEmoteId))
            {
                interruptedEmoteId = 0;
            }
            string returnToEmote = "";

            var interuptedCommand = plugin.EmoteApi.GetEmoteCommand((uint)interruptedEmoteId);
            if (interuptedCommand != null)
            {
                returnToEmote = interuptedCommand;
            }
            try
            {
                //Plugin.ChatGui.Print($"Auto Vibe operating. Controller presence: {presence}.");
                

                var vibeCommands = plugin.Configuration.AutoVibeCommands
                    .Where(x => !string.IsNullOrWhiteSpace(x.Command) && x.Weight > 0)
                    .ToList();

                

                if (vibeCommands.Count == 0)
                {
                    plugin.EmoteGuard.QueueGuardedEmote("/blush" + " " + returnToEmote);
                    return;
                }

                var totalWeight = vibeCommands.Sum(x => x.Weight);
                var roll = this.random.Next(0, totalWeight);

                var currentWeight = 0;

                foreach (var vibeCommand in vibeCommands)
                {
                    currentWeight += vibeCommand.Weight;

                    if (roll < currentWeight)
                    {
                        plugin.EmoteGuard.QueueGuardedEmote(vibeCommand.Command + " " + returnToEmote);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to send vibe command: {ex.Message}");
            }
        }
        private void ApplyHonorificTitleForCommand(WeightedVibeCommand vibeCommand)
        {
            if (!plugin.Configuration.AutoVibeEnabled)
                return;

            if (!wearsRestrictedItems)
            {
                CheckIfWearingRestrictiveItems();

                if (!wearsRestrictedItems)
                    return;
            }

            if (string.IsNullOrWhiteSpace(vibeCommand.HonorificTitle))
                return;

            if (vibeCommand.HonorificDurationSeconds <= 0)
                return;

            var titleJson = plugin.HonorificManager.BuildTitleJson(
                vibeCommand.HonorificTitle,
                vibeCommand.HonorificColor,
                vibeCommand.HonorificGlow);

            if (string.IsNullOrWhiteSpace(titleJson))
                return;

            plugin.HonorificManager.SetTitle(
                titleJson,
                TimeSpan.FromSeconds(vibeCommand.HonorificDurationSeconds),
                vibeCommand.HonorificPriority,
                this);
        }
        private void RegisterAutoVibeMoodles()
        {
            plugin.MoodleEnforcer.RegisterExternalMoodle(plugin.Configuration.AutoVibeControllerOnlineMoodleId, AutoVibeControllerMoodleSource);
            plugin.MoodleEnforcer.RegisterExternalMoodle(plugin.Configuration.AutoVibeEngagedMoodleId, AutoVibeAutonomousMoodleSource);
        }

        private void ClearAutoVibeMoodles()
        {
            autoVibeByControllerMoodleRequested = false;
            autoVibeAutonomusMoodleRequested = false;
            plugin.MoodleEnforcer.RemoveEnforcedMoodle(AutoVibeControllerMoodleSource);
            plugin.MoodleEnforcer.RemoveEnforcedMoodle(AutoVibeAutonomousMoodleSource);
        }

        private void SetAutoVibeByControllerMoodle(bool active)
        {
            var moodleId = plugin.Configuration.AutoVibeControllerOnlineMoodleId;

            if (moodleId == Guid.Empty)
            {
                plugin.MoodleEnforcer.RemoveEnforcedMoodle(AutoVibeControllerMoodleSource);
                return;
            }

            if (active)
            {
                if (autoVibeByControllerMoodleRequested)
                    return;

                autoVibeByControllerMoodleRequested = true;
                plugin.MoodleEnforcer.AddEnforcedMoodle(moodleId, AutoVibeControllerMoodleSource);
            }
            else
            {
                if (!autoVibeByControllerMoodleRequested)
                    return;

                autoVibeByControllerMoodleRequested = false;
                plugin.MoodleEnforcer.RemoveEnforcedMoodle(AutoVibeControllerMoodleSource);
            }
        }

        private void SetAutoVibeAutonomusMoodle(bool active)
        {
            var moodleId = plugin.Configuration.AutoVibeEngagedMoodleId;

            if (moodleId == Guid.Empty)
            {
                plugin.MoodleEnforcer.RemoveEnforcedMoodle(AutoVibeAutonomousMoodleSource);
                return;
            }

            if (active)
            {
                if (autoVibeAutonomusMoodleRequested)
                    return;

                autoVibeAutonomusMoodleRequested = true;
                plugin.MoodleEnforcer.AddEnforcedMoodle(moodleId, AutoVibeAutonomousMoodleSource);
            }
            else
            {
                if (!autoVibeAutonomusMoodleRequested)
                    return;

                autoVibeAutonomusMoodleRequested = false;
                plugin.MoodleEnforcer.RemoveEnforcedMoodle(AutoVibeAutonomousMoodleSource);
            }
        }

        public void MoodleConfigChange()
        {
            ClearAutoVibeMoodles();
            RegisterAutoVibeMoodles();
        }
        public void RefreshHonorificEmoteSubscriptions()
        {
            ClearHonorificEmoteSubscriptions();

            foreach (var vibeCommand in plugin.Configuration.AutoVibeCommands)
            {
                if (string.IsNullOrWhiteSpace(vibeCommand.HonorificTriggerCommand))
                    continue;

                if (string.IsNullOrWhiteSpace(vibeCommand.HonorificTitle))
                    continue;

                if (vibeCommand.HonorificDurationSeconds <= 0)
                    continue;

                var capturedCommand = vibeCommand;

                var subscription = plugin.EmoteGuard.SubscribeToFiredEmoteCommand(
                    capturedCommand.HonorificTriggerCommand,
                    _ => ApplyHonorificTitleForCommand(capturedCommand));

                honorificEmoteSubscriptions.Add(subscription);
            }
        }

        private void ClearHonorificEmoteSubscriptions()
        {
            foreach (var subscription in honorificEmoteSubscriptions)
            {
                try
                {
                    subscription.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            honorificEmoteSubscriptions.Clear();
        }
    }
}
