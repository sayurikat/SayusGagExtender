using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using static FFXIVClientStructs.FFXIV.Client.Game.Character.CustomizeData.Delegates;

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
        private DateTime lastItemChekUtc = DateTime.MinValue;
        public class WeightedZapCommand
        {
            public string Command { get; set; } = "";
            public int Weight { get; set; } = 1;
        }
        public RandomZapSender(Plugin plugin)
        {
            this.plugin = plugin;

            this.ScheduleCurrentHour();
            Plugin.Framework.Update += this.OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged += this.OnRestrictionsChanged;
            plugin.FriendListHelper.RequestFriendListUpdateWithCooldown();
            //this.SendEmote();
        }

        public void Dispose()
        {
            Plugin.Framework.Update -= this.OnFrameworkUpdate;
            plugin.GagSpeakRestrictionsApi.OnRestrictionsChanged -= this.OnRestrictionsChanged;
        }
        public void Enable()
        {
            plugin.Configuration.AutoZapEnabled = true;
            plugin.FriendListHelper.RequestFriendListUpdateWithCooldown();
        }
        public void Disable()
        {
            plugin.Configuration.AutoZapEnabled = false;
        }
        private void CheckIfWearingRestrictiveItems()
        {
            wearsRestrictedItems = plugin.GagSpeakRestrictionsApi.IsAnyListedRestrictionsActive(plugin.Configuration.AutoZapRequiredRestrictions);
        }
        private void OnRestrictionsChanged(object obj)
        {
            CheckIfWearingRestrictiveItems();
        }
        private void OnFrameworkUpdate(IFramework framework)
        {
            if (!plugin.Configuration.AutoZapEnabled)
                return;

            var now = DateTime.UtcNow;

            if ((now - this.lastItemChekUtc).TotalMilliseconds > 5000)
            {
                CheckIfWearingRestrictiveItems();
                this.lastItemChekUtc = now;
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

            this.SendEmote();
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
                0);

            var nextHourStart = hourStart.AddHours(1);

            ///Plugin.ChatGui.Print($"Zap triggers this hour");
            for (var i = 0; i < plugin.Configuration.AutoZapCount; i++)
            {
                var totalSeconds = (int)(nextHourStart - hourStart).TotalSeconds;
                var randomSecond = this.random.Next(0, totalSeconds);

                var triggerTime = hourStart.AddSeconds(randomSecond);
                
                // If plugin starts halfway through the hour, avoid scheduling past times.
                //if (triggerTime <= now)
                //{
                //    var remainingSeconds = Math.Max(1, (int)(nextHourStart - now).TotalSeconds);
                //    triggerTime = now.AddSeconds(this.random.Next(1, remainingSeconds));
                //}

                this.triggerTimes.Add(triggerTime);
                //Plugin.ChatGui.Print($"{i+1}: {triggerTime}");
            }

            this.triggerTimes.Sort();
        }

        private void SendEmote()
        {
            var controllerName = plugin.Configuration.ZapControllerName;
            if (controllerName == null) return;

            if (controllerName.Length > 3 && plugin.FriendListHelper.IsFriendOnline(controllerName))
            {
                Plugin.ChatGui.Print($"{controllerName} is online, skipping emote.");
                return;
            }
            try
            {
                Plugin.ChatGui.Print($"{controllerName} is not online, sending emote.");
                //plugin.Utils.ExecuteNativeCommand("/upset");

                //Plugin.CommandManager.ProcessCommand("/quack eval /standup /upset");
                //plugin.Utils.ExecuteCommand("/upset");

                var zapCommands = plugin.Configuration.AutoZapCommands
                    .Where(x => !string.IsNullOrWhiteSpace(x.Command) && x.Weight > 0)
                    .ToList();

                if (zapCommands.Count == 0)
                {
                    // Optional fallback if no commands are configured
                    //Plugin.CommandManager.ProcessCommand("/quack eval /standup \"/wait 0.5\" /upset");
                    //Plugin.CommandManager.ProcessCommand("/upset");
                    plugin.Utils.ExecuteCommand("/upset");
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
                        //Plugin.ChatGui.Print($"Rolled: {roll}, using {zapCommand.Command}");
                        Plugin.CommandManager.ProcessCommand(zapCommand.Command);
                        plugin.Utils.ExecuteCommand(zapCommand.Command);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to send zap command: {ex.Message}");
            }
        }
        public static unsafe class GameCommandHelper
        {
            
        }
    }
}
