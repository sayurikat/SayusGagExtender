using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using ECommons.ExcelServices.TerritoryEnumeration;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Excel.Sheets.Experimental;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static Dalamud.Interface.Utility.Raii.ImRaii;
using static SayusGagExtender.Configuration;
using Item = Lumina.Excel.Sheets.Item;
using World = Lumina.Excel.Sheets.World;

namespace SayusGagExtender
{
    public class Utils
    {
        Plugin plugin;
        public const float pullbackDistance = 1f;
        public const float maxDistanceToInteract = 2f;


        public Utils(Plugin instance)
        {
            plugin = instance;
        }
        public string WorldRowIDToString(uint id)
        {

            string world = "Unknown";

            //await Plugin.Framework.RunOnFrameworkThread(() =>
            //{
            try
            {
                var worldSheet = Plugin.DataManager.GetExcelSheet<World>();
                var worldRow = worldSheet?.GetRow(id);
                world = worldRow?.Name.ToString() ?? "Unknown";
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"error searching for target \n{ex.ToString()}");
            }
            //});

            return (world);
            //return world;

        }
        public unsafe void ExecuteNativeCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            command = command.Trim();

            if (!command.StartsWith('/'))
                return;

            var shellModule = RaptureShellModule.Instance();
            var uiModule = UIModule.Instance();

            if (shellModule == null || uiModule == null)
                return;

            Utf8String cmd = default;

            try
            {
                cmd.SetString(command);

                cmd.SanitizeString(
                    AllowedEntities.Unknown9 |
                    AllowedEntities.Payloads |
                    AllowedEntities.OtherCharacters |
                    AllowedEntities.SpecialCharacters |
                    AllowedEntities.Numbers |
                    AllowedEntities.LowercaseLetters |
                    AllowedEntities.UppercaseLetters);

                if (cmd.Length > 500)
                    return;

                shellModule->ExecuteCommandInner(&cmd, uiModule);

                // If your ClientStructs build uses the overload with a bool:
                // shellModule->ExecuteCommandInner(&cmd, uiModule, false);
            }
            finally
            {
                cmd.Dtor(true);
            }
        }
        public unsafe void ExecuteCommand(string command)
        {
            var shell = RaptureShellModule.Instance();
            var uiModule = UIModule.Instance();

            if (shell == null || uiModule == null)
                return;

            using var cmd = new Utf8String(command);

            shell->ExecuteCommandInner(&cmd, uiModule);
        }
        public string FormatCompactTimeSpan(TimeSpan value)
        {
            if (value < TimeSpan.Zero)
                value = TimeSpan.Zero;

            var days = (int)value.TotalDays;
            var hours = value.Hours;
            var minutes = value.Minutes;
            var seconds = value.Seconds;

            if (days > 0)
            {
                if (hours > 0)
                    return $"{days}d{hours}h";

                return $"{days}d";
            }

            if (hours > 0)
            {
                if (minutes > 0)
                    return $"{hours}h{minutes}m";

                return $"{hours}h";
            }

            if (minutes > 0)
            {
                if (seconds > 0)
                    return $"{minutes}m{seconds}s";

                return $"{minutes}m";
            }

            return $"{Math.Max(1, seconds)}s";
        }
    }
}
