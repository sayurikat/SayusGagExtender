using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static SayusGagExtender.RandomVibeSender;

namespace SayusGagExtender
{
    public sealed class RandomZapSender : IDisposable
    {
        private readonly Plugin plugin;
        private readonly Random random = new();

        private int scheduledHour = -1;
        private readonly List<DateTime> triggerTimes = new();
        private int nextTriggerIndex = 0;

        public bool wearsRestrictedItems { get; private set; } = false;
        public bool IsActive => wearsRestrictedItems;

        private DateTime lastItemCheckUtc = DateTime.MinValue;

        private bool autoZapByControllerMoodleRequested;
        private bool autoZapAutonomusMoodleRequested;

        private const string AutoZapControllerMoodleSource = "RandomZapSender.ControllerOnline";
        private const string AutoZapAutonomousMoodleSource = "RandomZapSender.AutonomousEngaged";

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

        public class WeightedZapCommand
        {
            public string Command { get; set; } = "";
            public string EmoteEnforceAlternative { get; set; } = "";
            public int Weight { get; set; } = 1;

            public string HonorificTitle { get; set; } = "";
            public Vector3 HonorificColor { get; set; } = new(1.0f, 1.0f, 1.0f);
            public Vector3 HonorificGlow { get; set; } = new(0.0f, 0.0f, 0.0f);

            public int HonorificDurationSeconds { get; set; } = 30;
            public int HonorificPriority { get; set; } = 100;
            public string HonorificTriggerCommand { get; set; } = "";
        }

        public RandomZapSender(Plugin plugin)
        {
            this.plugin = plugin;

            this.ScheduleCurrentHour();

            Plugin.Framework.Update += this.OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged += this.OnRestrictionsChanged;

            RegisterAutoZapMoodles();
            RefreshHonorificEmoteSubscriptions();
        }

        public void Dispose()
        {
            Plugin.Framework.Update -= this.OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged -= this.OnRestrictionsChanged;

            ClearHonorificEmoteSubscriptions();
            ClearAutoZapMoodles();
        }

        public void Enable()
        {
            plugin.Configuration.AutoZapEnabled = true;
        }

        public void Disable()
        {
            plugin.Configuration.AutoZapEnabled = false;
            ClearAutoZapMoodles();
        }
        public bool IsAutonomousRunning =>
        plugin.Configuration.AutoZapEnabled &&
        wearsRestrictedItems &&
        ShouldOperateForPresence(lastControllerPresence);

        public string ControllerPresenceStatus
        {
            get
            {
                //var controllerName = plugin.Configuration.RemoteControllerName;
                //var controllerWorld = plugin.Configuration.RemoteControllerWorld;

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
                if (!plugin.Configuration.AutoZapEnabled)
                    return "Disabled";

                if (!wearsRestrictedItems)
                    return "Inactive";

                return IsAutonomousRunning
                    ? $"Running (Require {plugin.Configuration.AutoZapWhen})"
                    : $"Paused (Require {plugin.Configuration.AutoZapWhen})";
            }
        }
        public void UpdateHourlyCount()
        {
            this.ScheduleCurrentHour();
        }
        private void CheckIfWearingRestrictiveItems()
        {
            wearsRestrictedItems = plugin.GagSpeakRestrictionsApi.IsAnyListedRestrictionsActive(
                plugin.Configuration.AutoZapRequiredRestrictions);
        }

        private void OnRestrictionsChanged(object obj)
        {
            CheckIfWearingRestrictiveItems();
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (!plugin.Configuration.AutoZapEnabled)
            {
                lastControllerPresence = ControllerPresence.Offline;
                ClearAutoZapMoodles();
                return;
            }
            if (!plugin.CharacterHelper.IsCharacterAvailable || plugin.GagSpeakConfinementApi.ShouldTemporarilyReleaseMovementLocks()) return;

            var now = DateTime.UtcNow;

            if ((now - this.lastItemCheckUtc).TotalMilliseconds > 5000)
            {
                CheckIfWearingRestrictiveItems();
                this.lastItemCheckUtc = now;
            }

            if (!wearsRestrictedItems)
            {   
                lastControllerPresence = ControllerPresence.Offline;
                ClearAutoZapMoodles();
                return;
            }

            var presence = GetControllerPresence();
            var shouldOperate = ShouldOperateForPresence(presence);

            UpdateAutoZapMoodles(presence, shouldOperate);

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
                TrySendRandomZapCommand(presence);
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

            for (var i = 0; i < plugin.Configuration.AutoZapCount; i++)
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
                return ControllerPresence.Offline;


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
            return plugin.Configuration.AutoZapWhen switch
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

        private void UpdateAutoZapMoodles(ControllerPresence presence, bool shouldOperate)
        {
            //lugin.ChatGui.Print($"UpdateAutoZapMoodles {presence.ToString()} {shouldOperate}");
            var controllerNearby = presence != ControllerPresence.Offline;

            // Moodle for "controller is here / nearby".
            SetAutoZapByControllerMoodle(controllerNearby);

            // Moodle for "Auto Zap is currently operating".
            SetAutoZapAutonomusMoodle(shouldOperate);
        }
        
        private void TrySendRandomZapCommand(ControllerPresence presence)
        {
            var useEmoteEnforceAlternative = plugin.EmoteEnforcer.IsActive;
            if (plugin.EmoteEnforcer.ShouldBlockUserEmotes && !useEmoteEnforceAlternative)
                return;

            var interruptedEmoteId = plugin.EmoteApi.GetCurrentLocalPlayerEmoteId();
            if (!plugin.EmoteApi.IsEmoteSpecial((uint)interruptedEmoteId))
            {
                Plugin.ChatGui.Print($" not special");
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
                //Plugin.ChatGui.Print($"Auto Zap operating. Controller presence: {presence}.");
                
                


                var zapCommands = plugin.Configuration.AutoZapCommands
                    .Where(x => x.Weight > 0 && !string.IsNullOrWhiteSpace(useEmoteEnforceAlternative ? x.EmoteEnforceAlternative : x.Command))
                    .ToList();

                if (zapCommands.Count == 0)
                {
                    plugin.EmoteGuard.QueueGuardedEmote("/upset" + " " + returnToEmote);
                    return;
                }

                var totalWeight = zapCommands.Sum(x => x.Weight);
                var roll = this.random.Next(0, totalWeight);

                var currentWeight = 0;

                foreach (var zapCommand in zapCommands)
                {
                    currentWeight += zapCommand.Weight;

                    if (roll < currentWeight)
                    {
                        var commandToSend = useEmoteEnforceAlternative ? zapCommand.EmoteEnforceAlternative : zapCommand.Command;
                        ApplyHonorificTitleForCommand(zapCommand);
                        Plugin.ChatGui.Print($" zap command: {commandToSend + " " + returnToEmote}");
                        plugin.EmoteGuard.QueueGuardedEmote(commandToSend + " " + returnToEmote);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to send zap command: {ex.Message}");
            }
        }
        private void ApplyHonorificTitleForCommand(WeightedZapCommand zapCommand)
        {
            if (string.IsNullOrWhiteSpace(zapCommand.HonorificTitle))
                return;

            if (zapCommand.HonorificDurationSeconds <= 0)
                return;

            var titleJson = plugin.HonorificManager.BuildTitleJson(
                zapCommand.HonorificTitle,
                zapCommand.HonorificColor,
                zapCommand.HonorificGlow);

            if (string.IsNullOrWhiteSpace(titleJson))
                return;

            plugin.HonorificManager.SetTitle(
                titleJson,
                TimeSpan.FromSeconds(zapCommand.HonorificDurationSeconds),
                zapCommand.HonorificPriority,
                this);
        }
        private void RegisterAutoZapMoodles()
        {
            plugin.MoodleEnforcer.RegisterExternalMoodle(plugin.Configuration.AutoZapControllerOnlineMoodleId, AutoZapControllerMoodleSource);
            plugin.MoodleEnforcer.RegisterExternalMoodle(plugin.Configuration.AutoZapEngagedMoodleId, AutoZapAutonomousMoodleSource);
        }

        private void ClearAutoZapMoodles()
        {
            autoZapByControllerMoodleRequested = false;
            autoZapAutonomusMoodleRequested = false;
            plugin.MoodleEnforcer.RemoveEnforcedMoodle(AutoZapControllerMoodleSource);
            plugin.MoodleEnforcer.RemoveEnforcedMoodle(AutoZapAutonomousMoodleSource);
        }

        private void SetAutoZapByControllerMoodle(bool active)
        {
            var moodleId = plugin.Configuration.AutoZapControllerOnlineMoodleId;

            if (moodleId == Guid.Empty)
            {
                plugin.MoodleEnforcer.RemoveEnforcedMoodle(AutoZapControllerMoodleSource);
                return;
            }

            if (active)
            {
                if (autoZapByControllerMoodleRequested)
                    return;

                autoZapByControllerMoodleRequested = true;
                plugin.MoodleEnforcer.AddEnforcedMoodle(moodleId, AutoZapControllerMoodleSource);
            }
            else
            {
                if (!autoZapByControllerMoodleRequested)
                    return;

                autoZapByControllerMoodleRequested = false;
                plugin.MoodleEnforcer.RemoveEnforcedMoodle(AutoZapControllerMoodleSource);
            }
        }

        private void SetAutoZapAutonomusMoodle(bool active)
        {
            var moodleId = plugin.Configuration.AutoZapEngagedMoodleId;

            if (moodleId == Guid.Empty)
            {
                plugin.MoodleEnforcer.RemoveEnforcedMoodle(AutoZapAutonomousMoodleSource);
                return;
            }

            if (active)
            {
                if (autoZapAutonomusMoodleRequested)
                    return;

                autoZapAutonomusMoodleRequested = true;
                plugin.MoodleEnforcer.AddEnforcedMoodle(moodleId, AutoZapAutonomousMoodleSource);
            }
            else
            {
                if (!autoZapAutonomusMoodleRequested)
                    return;

                autoZapAutonomusMoodleRequested = false;
                plugin.MoodleEnforcer.RemoveEnforcedMoodle(AutoZapAutonomousMoodleSource);
            }
        }
        public void MoodleConfigChange()
        {
            ClearAutoZapMoodles();
            RegisterAutoZapMoodles();
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

        public void RefreshHonorificEmoteSubscriptions()
        {
            ClearHonorificEmoteSubscriptions();

            foreach (var zapCommand in plugin.Configuration.AutoZapCommands)
            {
                if (string.IsNullOrWhiteSpace(zapCommand.HonorificTriggerCommand) && string.IsNullOrWhiteSpace(zapCommand.EmoteEnforceAlternative))
                    continue;

                if (string.IsNullOrWhiteSpace(zapCommand.HonorificTitle))
                    continue;

                if (zapCommand.HonorificDurationSeconds <= 0)
                    continue;

                var capturedCommand = zapCommand;

                if (!string.IsNullOrWhiteSpace(capturedCommand.HonorificTriggerCommand))
                {
                    var subscription = plugin.EmoteGuard.SubscribeToFiredEmoteCommand(capturedCommand.HonorificTriggerCommand, _ => ApplyHonorificTitleForCommand(capturedCommand));
                    honorificEmoteSubscriptions.Add(subscription);
                }

                if (!string.IsNullOrWhiteSpace(capturedCommand.EmoteEnforceAlternative) && (string.IsNullOrWhiteSpace(capturedCommand.HonorificTriggerCommand) || !string.Equals(capturedCommand.EmoteEnforceAlternative.Trim(), capturedCommand.HonorificTriggerCommand.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    var alternativeSubscription = plugin.EmoteGuard.SubscribeToFiredEmoteCommand(capturedCommand.EmoteEnforceAlternative, _ => ApplyHonorificTitleForCommand(capturedCommand));
                    honorificEmoteSubscriptions.Add(alternativeSubscription);
                }
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
