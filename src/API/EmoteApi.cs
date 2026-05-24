using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using ECommons;

namespace SayusGagExtender.API
{
    public sealed class EmoteApi
    {
        private readonly Plugin plugin;

        private Dictionary<uint, string>? emoteCommandsById;
        private HashSet<uint>? specialEmotes;
        private readonly Dictionary<string, Dictionary<uint, bool>> emoteMatchCacheByCommand = new();

        private static readonly uint[] ManualGroundSitEmoteIds =
        [
            // Ground sit
            52,
            97,
            98,
            117,
        ];
        public bool IsGroundSit(uint id) => (ManualGroundSitEmoteIds.Contains(id));
        private static readonly uint[] ManualChairSitEmoteIds =
        [
            // Chair sit
            50,
            95,
            96,
            254,
            255,
        ];
        public bool IsChairSit(uint id) => (ManualChairSitEmoteIds.Contains(id));
        private static readonly uint[] ManualSleepEmoteIds =
        [
            // Doze / sleep
            88,
            99,
            100,
        ];
        public bool IsSleep(uint id) => (ManualSleepEmoteIds.Contains(id));
        public bool IsAnySit(uint id) => (ManualGroundSitEmoteIds.Contains(id) || ManualChairSitEmoteIds.Contains(id));
        public bool IsAnySitOrSleep(uint id) => (ManualGroundSitEmoteIds.Contains(id) || ManualChairSitEmoteIds.Contains(id) || ManualSleepEmoteIds.Contains(id));
        public EmoteApi(Plugin plugin)
        {
            this.plugin = plugin;

        }
        
    // Playdead
    //143,


        public Dictionary<uint, string> GetAllEmotes()
        {
            try
            {
                var sheet = Plugin.DataManager.GetExcelSheet<Emote>();
                if (sheet == null)
                    return new Dictionary<uint, string>();

                return sheet
                    .Where(x => x.RowId > 0)
                    .Select(x =>
                    {
                        var name = x.Name.ToString();

                        return new
                        {
                            Id = x.RowId,
                            Name = string.IsNullOrWhiteSpace(name)
                                ? $"Emote #{x.RowId}"
                                : name,
                        };
                    })
                    .GroupBy(x => x.Id)
                    .ToDictionary(
                        x => x.Key,
                        x => x.First().Name);
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to get emotes: {ex}");
                return new Dictionary<uint, string>();
            }
        }

        public string? GetEmoteCommand(uint emoteId)
        {
            try
            {
                emoteCommandsById ??= BuildEmoteCommandCache();

                return emoteCommandsById.TryGetValue(emoteId, out var command)
                    ? command
                    : null;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to get emote command for {emoteId}: {ex}");
                return null;
            }
        }
        public ushort GetCurrentLocalPlayerEmoteId()
        {
            return GetCurrentEmoteId(Plugin.ObjectTable.LocalPlayer);
        }
        public unsafe ushort GetCurrentEmoteId(IPlayerCharacter? player)
        {
            try
            {
                if (player == null || player.Address == nint.Zero)
                    return 0;

                var character = (Character*)player.Address;
                return character->EmoteController.EmoteId;
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to get current emote id: {ex}");
                return 0;
            }
        }
        public bool ExecuteEmote(uint emoteId)
        {
            Plugin.ChatGui.Print("ExecuteEmote.");
            var command = GetEmoteCommand(emoteId);

            if (string.IsNullOrWhiteSpace(command))
                return false;

            if (!command.StartsWith("/", StringComparison.Ordinal))
                command = "/" + command;

            // "motion" prevents spammy public emote text.
            plugin.Utils.ExecuteCommand(command);
            return true;//Plugin.CommandManager.ProcessCommand($"{command} motion");
        }

        
        public bool IsEmoteSpecial(uint emoteId)
        {
            try
            {
                specialEmotes ??= BuildSpecialEmoteCache();
                return specialEmotes.Contains(emoteId);
            }
            catch (Exception ex)
            {
                Plugin.ChatGui.PrintError($"Failed to get emote for {emoteId}: {ex}");
                return false;
            }
        }
        
        public bool IsThisThatEmote(uint emoteId, string command)
        {
            if (emoteMatchCacheByCommand.TryGetValue(command, out var cachedByEmoteId)
                && cachedByEmoteId.TryGetValue(emoteId, out var cachedResult))
            {
                return cachedResult;
            }

            var result = IsThisThatEmoteUncached(emoteId, command);

            if (!emoteMatchCacheByCommand.TryGetValue(command, out cachedByEmoteId))
            {
                cachedByEmoteId = new Dictionary<uint, bool>();
                emoteMatchCacheByCommand[command] = cachedByEmoteId;
            }

            cachedByEmoteId[emoteId] = result;

            return result;
        }

        private bool IsThisThatEmoteUncached(uint emoteId, string command)
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Emote>();
            if (sheet == null)
                return false;

            foreach (var emote in sheet)
            {
                if (emote.RowId != emoteId)
                    continue;

                var readCommand = TryExtractCommand(emote);

                // manual override
                if (TryOverrideCommand(emote.RowId, out var commandOverride))
                {
                    readCommand = commandOverride;
                }

                return readCommand == command;
            }

            return false;
        }

        private Dictionary<uint, string> BuildEmoteCommandCache()
        {
            var result = new Dictionary<uint, string>();

            var sheet = Plugin.DataManager.GetExcelSheet<Emote>();
            if (sheet == null)
                return result;

            foreach (var emote in sheet)
            {
                if (emote.RowId == 0)
                    continue;

                var command = TryExtractCommand(emote);

                //manual override
                if (TryOverrideCommand(emote.RowId, out var commandOverride))
                {
                    command = commandOverride;
                }

                if (!string.IsNullOrWhiteSpace(command))
                    result[emote.RowId] = command;
            }

            return result;
        }
        private static string? TryExtractCommand(Emote emote)
        {
            try
            {
                // Depending on generated Lumina version, this is usually a RowRef<TextCommand>.
                var textCommand = emote.TextCommand.ValueNullable;
                if (textCommand == null)
                    return null;

                var command = textCommand.Value.Command.ToString();

                if (string.IsNullOrWhiteSpace(command))
                    return null;

                return command.StartsWith("/", StringComparison.Ordinal)
                    ? command
                    : "/" + command;
            }
            catch
            {
                return null;
            }
        }
        private static bool TryOverrideCommand(uint emoteId, out string command)
        {
            command = "";
            if (ManualGroundSitEmoteIds.Contains(emoteId))
            {
                command = "/groundsit"; return true;
            }
            if (ManualChairSitEmoteIds.Contains(emoteId))
            {
                command = "/sit"; return true;
            }
            if (ManualSleepEmoteIds.Contains(emoteId))
            {
                command = "/doze"; return true;
            }
            return false;
        }
        private HashSet<uint> BuildSpecialEmoteCache()
        {
            var result = new HashSet<uint>();

            var sheet = Plugin.DataManager.GetExcelSheet<Emote>();
            if (sheet == null)
                return result;

            foreach (var emote in sheet)
            {
                if (emote.RowId == 0)
                    continue;

                if (IsAnySitOrSleep(emote.RowId))
                {
                    result.Add(emote.RowId);
                    continue;
                }

                if (emote.EmoteCategory.RowId == 2 || string.Equals(emote.EmoteCategory.Value.Name.ToString(), "Special", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(emote.RowId);
                }
            }

            return result;
        }
        
    }
}
