using FFXIVClientStructs.FFXIV.Client.UI.Info;
using System;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SayusGagExtender
{
    public class FriendListHelper
    {
        Plugin plugin;
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

                return proxy->VirtualTable->RequestData(proxy);
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to request friend list update: {ex.Message}");
                return false;
            }
        }
        private DateTime lastFriendRefreshUtc = DateTime.MinValue;

        public bool RequestFriendListUpdateWithCooldown()
        {
            var now = DateTime.UtcNow;

            if ((now - this.lastFriendRefreshUtc).TotalSeconds < 30)
                return false;

            this.lastFriendRefreshUtc = now;
            var result = this.RequestFriendListUpdate();
            Plugin.ChatGui.Print($"Requested Friend List Update: {result}");
            return result;
        }
        public bool IsFriendOnline(string characterName, string? worldName = null)
        {
            var result = IsFriendOnlineInternal(characterName, worldName);

            _ = RequestFriendListUpdateWithCooldown();

            return result;
        }
        private unsafe bool IsFriendOnlineInternal(string characterName, string? worldName = null)
        {
            var proxy = InfoProxyFriendList.Instance();
            if (proxy == null)
                return false;

            var count = proxy->EntryCount;
            //Plugin.ChatGui.Print($"Friend count {count}!");

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

                if (worldName != null &&
                    !string.Equals(homeWorld, worldName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var isOnline = entry->State != InfoProxyCommonList.CharacterData.OnlineStatus.Offline;

                //Plugin.ChatGui.Print(isOnline ? $"Friend {name} is online on world {currentWorld}!" : $"Friend {name} is offline.");

                return isOnline;
            }

            return false;
        }
    }
}
