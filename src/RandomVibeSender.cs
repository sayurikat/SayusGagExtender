using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;

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

        public class WeightedVibeCommand
        {
            public string Command { get; set; } = "";
            public int Weight { get; set; } = 1;
        }
        public RandomVibeSender(Plugin plugin)
        {
            this.plugin = plugin;

            this.ScheduleCurrentHour();

            Plugin.Framework.Update += this.OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged += this.OnRestrictionsChanged;
            plugin.FriendListHelper.RequestFriendListUpdateWithCooldown();
        }

        public void Dispose()
        {
            Plugin.Framework.Update -= this.OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged -= this.OnRestrictionsChanged;
        }

        public void Enable()
        {
            plugin.Configuration.AutoVibeEnabled = true;
            plugin.FriendListHelper.RequestFriendListUpdateWithCooldown();
        }

        public void Disable()
        {
            plugin.Configuration.AutoVibeEnabled = false;
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
                return;

            var now = DateTime.UtcNow;

            if ((now - this.lastItemCheckUtc).TotalMilliseconds > 5000)
            {
                CheckIfWearingRestrictiveItems();
                this.lastItemCheckUtc = now;
            }

            if (!wearsRestrictedItems)
                return;

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

            this.SendVibeCommand();
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

        private void SendVibeCommand()
        {
            var controllerName = plugin.Configuration.VibeControllerName;

            if (controllerName == null)
                return;

            if (controllerName.Length > 3 && plugin.FriendListHelper.IsFriendOnline(controllerName))
            {
                Plugin.ChatGui.Print($"{controllerName} is online, skipping vibe command.");
                return;
            }

            try
            {
                Plugin.ChatGui.Print($"{controllerName} is not online, sending vibe command.");

                var vibeCommands = plugin.Configuration.AutoVibeCommands
                    .Where(x => !string.IsNullOrWhiteSpace(x.Command) && x.Weight > 0)
                    .ToList();

                if (vibeCommands.Count == 0)
                {
                    // Optional fallback if no commands are configured.
                    plugin.Utils.ExecuteCommand("/upset");
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
                        //plugin commands
                        Plugin.CommandManager.ProcessCommand(vibeCommand.Command);

                        //game commands
                        plugin.Utils.ExecuteCommand(vibeCommand.Command);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to send vibe command: {ex.Message}");
            }
        }
    }
}
