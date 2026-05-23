using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace SayusGagExtender.API
{
    public sealed class EmoteApi
    {
        private readonly Plugin plugin;

        private Dictionary<uint, string>? emoteCommandsById;

        public EmoteApi(Plugin plugin)
        {
            this.plugin = plugin;
        }

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

        public bool CancelEmote()
        {
            // There is no nice universal "/stopemote" command.
            // /sit is the common safe-ish way to break most looping emotes.
            // If the player is already sitting, this can stand them up, so only call it
            // when you know the enforcer started the currently active emote.
            Plugin.ChatGui.Print("CancelEmote.");
            //plugin commands
            //Plugin.CommandManager.ProcessCommand(plugin.Configuration.EmoteEnforcerCancelCommand);

            //game commands
            //plugin.Utils.ExecuteCommand(plugin.Configuration.EmoteEnforcerCancelCommand);

            plugin.EmoteGuard.QueueGuardedEmote(plugin.Configuration.EmoteEnforcerCancelCommand);

            //plugin.EmoteGuard.QueueGuardedEmote("/sit");
            return true;// Plugin.CommandManager.ProcessCommand("/sit");
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
    }
}
