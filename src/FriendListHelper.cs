using FFXIVClientStructs.FFXIV.Client.UI.Info;
using System;

namespace SayusGagExtender
{
    public class FriendListHelper
    {
        private readonly Plugin plugin;

        private DateTime lastFriendRefreshUtc = DateTime.MinValue;

        private static readonly TimeSpan FriendListRefreshCooldown = TimeSpan.FromSeconds(300);

        // How long the count must stop changing before we accept the refresh as complete.
        // This is not used alone; count must also recover to the previous stable count.
        private static readonly TimeSpan FriendListSettleTime = TimeSpan.FromSeconds(2);

        // Safety fallback in case the list never reaches the previous stable count
        // because friends were removed, hidden, server hiccup, etc.
        private static readonly TimeSpan FriendListHardTimeout = TimeSpan.FromSeconds(45);

        private bool friendListUpdateInProgress = false;

        private uint lastObservedEntryCount = 0;
        private uint lastStableEntryCount = 0;
        private uint refreshExpectedEntryCount = 0;

        private DateTime lastEntryCountChangedUtc = DateTime.MinValue;
        private DateTime refreshStartedUtc = DateTime.MinValue;

        private bool hasObservedInitialFriendList = false;

        public FriendListHelper(Plugin plugin)
        {
            this.plugin = plugin;
        }

        public unsafe bool RequestFriendListUpdate()
        {
            var proxy = InfoProxyFriendList.Instance();
            if (proxy == null)
                return false;

            try
            {
                if (proxy->VirtualTable == null || proxy->VirtualTable->RequestData == null)
                    return false;

                var result = proxy->VirtualTable->RequestData(proxy);

                if (result)
                {
                    MarkRefreshStarted(proxy->EntryCount);
                }

                return result;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to request friend list update: {ex.Message}");
                return false;
            }
        }

        public bool RequestFriendListUpdateWithCooldown()
        {
            var now = DateTime.UtcNow;

            if (IsFriendListUpdateInProgress())
                return false;

            if (now - this.lastFriendRefreshUtc < FriendListRefreshCooldown)
                return false;

            var result = this.RequestFriendListUpdate();

            if (result)
                this.lastFriendRefreshUtc = now;

            //Plugin.ChatGui.Print($"Requested Friend List Update: {result}");
            return result;
        }

        public bool IsFriendListUpdateInProgress()
        {
            UpdateFriendListRefreshState();
            return friendListUpdateInProgress;
        }

        public bool IsFriendOnline(string characterName, string? worldName = null)
        {
            UpdateFriendListRefreshState();

            var result = IsFriendOnlineInternal(characterName, worldName);

            _ = RequestFriendListUpdateWithCooldown();

            return result;
        }

        public bool? IsFriendOnlineSafe(string characterName, string? worldName = null)
        {
            if (IsFriendListUpdateInProgress())
                return null;

            var result = IsFriendOnlineInternal(characterName, worldName);

            _ = RequestFriendListUpdateWithCooldown();

            return result;
        }

        private unsafe void UpdateFriendListRefreshState()
        {
            var proxy = InfoProxyFriendList.Instance();
            if (proxy == null)
            {
                friendListUpdateInProgress = false;
                return;
            }

            var now = DateTime.UtcNow;
            var count = proxy->EntryCount;

            if (!hasObservedInitialFriendList)
            {
                hasObservedInitialFriendList = true;

                lastObservedEntryCount = count;
                lastStableEntryCount = count;
                refreshExpectedEntryCount = count;
                lastEntryCountChangedUtc = now;

                friendListUpdateInProgress = false;
                return;
            }

            var countChanged = count != lastObservedEntryCount;

            if (countChanged)
            {
                // External or internal refresh wipe detected.
                // This can happen repeatedly, so reset the refresh window every time
                // the list drops below the known complete count.
                if (count < lastStableEntryCount)
                {
                    MarkRefreshStarted(count);
                }

                lastObservedEntryCount = count;
                lastEntryCountChangedUtc = now;
            }

            if (!friendListUpdateInProgress)
            {
                // While idle, keep the stable baseline fresh.
                // If the user actually gains/removes friends while not refreshing,
                // this slowly becomes the new baseline.
                lastStableEntryCount = count;
                refreshExpectedEntryCount = count;
                return;
            }

            var recoveredToExpectedCount = count >= refreshExpectedEntryCount;
            var countHasSettled = now - lastEntryCountChangedUtc >= FriendListSettleTime;
            var timedOut = now - refreshStartedUtc >= FriendListHardTimeout;

            // Not time alone:
            // Normal completion requires both recovered count and settled count.
            if (recoveredToExpectedCount && countHasSettled)
            {
                MarkRefreshFinished(count);
                return;
            }

            // Safety fallback:
            // If the server never returns the old count, do not stay stuck forever.
            // This handles deleted friends, changed visibility, failed downloads, etc.
            if (timedOut && countHasSettled)
            {
                MarkRefreshFinished(count);
            }
        }

        private void MarkRefreshStarted(uint currentCount)
        {
            var now = DateTime.UtcNow;

            if (!friendListUpdateInProgress)
            {
                refreshExpectedEntryCount = lastStableEntryCount;
            }

            friendListUpdateInProgress = true;
            refreshStartedUtc = now;
            lastEntryCountChangedUtc = now;
            lastObservedEntryCount = currentCount;

            // Keep expected count at the highest known stable size.
            // This protects against multiple refreshes in succession.
            if (lastStableEntryCount > refreshExpectedEntryCount)
                refreshExpectedEntryCount = lastStableEntryCount;
        }

        private void MarkRefreshFinished(uint finalCount)
        {
            friendListUpdateInProgress = false;

            lastStableEntryCount = finalCount;
            refreshExpectedEntryCount = finalCount;
            lastObservedEntryCount = finalCount;
            lastEntryCountChangedUtc = DateTime.UtcNow;
        }

        private unsafe bool IsFriendOnlineInternal(string characterName, string? worldName = null)
        {
            var proxy = InfoProxyFriendList.Instance();
            if (proxy == null)
                return false;

            var count = proxy->EntryCount;

            for (uint i = 0; i < count; i++)
            {
                var entry = proxy->GetEntry(i);
                if (entry == null)
                    continue;

                var name = entry->NameString;
                if (string.IsNullOrEmpty(name))
                    continue;

                var homeWorld = plugin.Utils.WorldRowIDToString(entry->HomeWorld);
                var currentWorld = plugin.Utils.WorldRowIDToString(entry->CurrentWorld);

                if (!string.Equals(name, characterName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(worldName) &&
                    !string.Equals(homeWorld, worldName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var isOnline = entry->State != InfoProxyCommonList.CharacterData.OnlineStatus.Offline;

                return isOnline;
            }

            return false;
        }
    }
}
