using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using ECommons;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {
        private XivChatType selectedRemoteChannelToAdd = XivChatType.TellIncoming;

        private static readonly XivChatType[] RemoteChannelOptions =
        [
            XivChatType.TellIncoming,

            // CWLS
            XivChatType.CrossLinkShell1,
            XivChatType.CrossLinkShell2,
            XivChatType.CrossLinkShell3,
            XivChatType.CrossLinkShell4,
            XivChatType.CrossLinkShell5,
            XivChatType.CrossLinkShell6,
            XivChatType.CrossLinkShell7,
            XivChatType.CrossLinkShell8,

            // Optional future-use channels.
            XivChatType.Party,
            XivChatType.Alliance,
            XivChatType.FreeCompany,
            XivChatType.Ls1,
            XivChatType.Ls2,
            XivChatType.Ls3,
            XivChatType.Ls4,
            XivChatType.Ls5,
            XivChatType.Ls6,
            XivChatType.Ls7,
            XivChatType.Ls8,
            XivChatType.Say,
            XivChatType.Yell,
            XivChatType.Shout,
        ];

        private void DrawGeneralTab()
        {
            var emoteGuardEnabled = configuration.EmoteGuardEnabled;
            if (ImGui.Checkbox("Enable Emote Guard", ref emoteGuardEnabled))
            {
                configuration.EmoteGuardEnabled = emoteGuardEnabled;
                configuration.Save();
            }

            ImGui.TextWrapped("Makes sure emotes can be used when running, in combat or mounted.");
            ImGui.TextWrapped("Will block most actions until emote is executed.");
            ImGui.TextWrapped("Will also dismount for certain emotes.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var remoteCommandsEnabled = configuration.RemoteChatCommandsEnabled;
            if (ImGui.Checkbox("Enable Remote Controller", ref remoteCommandsEnabled))
            {
                configuration.RemoteChatCommandsEnabled = remoteCommandsEnabled;
                configuration.Save();
            }


            var generalControllerName = configuration.RemoteControllerName;
            if (ImGui.InputText("Controller Name", ref generalControllerName))
            {
                configuration.RemoteControllerName = generalControllerName;
                configuration.Save();
            }

            var generalControllerWorld = configuration.RemoteControllerWorld;
            if (ImGui.InputText("Controller World", ref generalControllerWorld))
            {
                configuration.RemoteControllerWorld = generalControllerWorld;
                configuration.Save();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawRemoteAcceptedChannels();
        }

        private void DrawRemoteAcceptedChannels()
        {
            ImGui.Text("Accepted Remote Channels");
            ImGui.TextWrapped("Remote commands will only be accepted from the configured controller and one of these channels.");

            configuration.RemoteAcceptedChannels ??= new List<XivChatType>();

            var selectedChannels = configuration.RemoteAcceptedChannels
                .Distinct()
                .ToList();

            if (selectedChannels.Count != configuration.RemoteAcceptedChannels.Count)
            {
                configuration.RemoteAcceptedChannels = selectedChannels;
                configuration.Save();
            }

            var availableChannels = RemoteChannelOptions
                .Where(x => !selectedChannels.Contains(x))
                .ToList();

            if (availableChannels.Count > 0 && !availableChannels.Contains(selectedRemoteChannelToAdd))
                selectedRemoteChannelToAdd = availableChannels[0];

            var preview = availableChannels.Count == 0
                ? "No channels available"
                : GetRemoteChannelLabel(selectedRemoteChannelToAdd);

            ImGui.SetNextItemWidth(260);

            if (availableChannels.Count == 0)
                ImGui.BeginDisabled();

            if (ImGui.BeginCombo("##RemoteAcceptedChannelCombo", preview))
            {
                foreach (var channel in availableChannels)
                {
                    var selected = channel == selectedRemoteChannelToAdd;

                    if (ImGui.Selectable($"{GetRemoteChannelLabel(channel)}##remote-channel-{channel}", selected))
                    {
                        selectedRemoteChannelToAdd = channel;
                        ImGui.CloseCurrentPopup();
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            if (availableChannels.Count == 0)
                ImGui.EndDisabled();

            ImGui.SameLine();

            var canAdd = availableChannels.Count > 0 &&
                         !configuration.RemoteAcceptedChannels.Contains(selectedRemoteChannelToAdd);

            if (!canAdd)
                ImGui.BeginDisabled();

            if (ImGui.Button("Add Channel"))
            {
                configuration.RemoteAcceptedChannels.Add(selectedRemoteChannelToAdd);
                configuration.Save();

                var nextAvailable = RemoteChannelOptions
                    .FirstOrDefault(x => !configuration.RemoteAcceptedChannels.Contains(x));

                selectedRemoteChannelToAdd = nextAvailable;
            }

            if (!canAdd)
                ImGui.EndDisabled();

            ImGui.Spacing();

            if (configuration.RemoteAcceptedChannels.Count == 0)
            {
                ImGui.TextDisabled("No accepted channels configured.");
                return;
            }

            var ctrlHeld = ImGui.GetIO().KeyCtrl;
            const float selectedRowWidth = 260f;

            ImGui.Indent();

            for (var i = configuration.RemoteAcceptedChannels.Count - 1; i >= 0; i--)
            {
                var channel = configuration.RemoteAcceptedChannels[i];

                ImGui.PushID($"remote-accepted-channel-{channel}");

                if (DrawRemoteChannelRow(GetRemoteChannelLabel(channel), selectedRowWidth, ctrlHeld))
                {
                    configuration.RemoteAcceptedChannels.RemoveAt(i);
                    configuration.Save();
                }

                ImGui.PopID();
            }

            ImGui.Unindent();
        }

        private static bool DrawRemoteChannelRow(string text, float width, bool removeEnabled)
        {
            var style = ImGui.GetStyle();

            const string removeLabel = "X";

            var rowHeight = ImGui.GetFrameHeight();

            var removeWidth =
                ImGui.CalcTextSize(removeLabel).X +
                style.FramePadding.X * 2f;

            var selectableWidth =
                width -
                removeWidth -
                style.ItemSpacing.X;

            selectableWidth = Math.Max(selectableWidth, 1f);

            ImGui.Selectable(
                $"{text}##selected-remote-channel-row",
                true,
                ImGuiSelectableFlags.None,
                new System.Numerics.Vector2(selectableWidth, rowHeight));

            ImGui.SameLine();

            if (!removeEnabled)
                ImGui.BeginDisabled();

            var clicked = ImGui.SmallButton(removeLabel);

            if (!removeEnabled)
                ImGui.EndDisabled();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(removeEnabled ? "Remove channel" : "Hold CTRL to remove channel");

            return clicked;
        }

        private static string GetRemoteChannelLabel(XivChatType channel)
        {
            return channel switch
            {
                XivChatType.TellIncoming => "Tell",

                XivChatType.CrossLinkShell1 => "CWLS 1",
                XivChatType.CrossLinkShell2 => "CWLS 2",
                XivChatType.CrossLinkShell3 => "CWLS 3",
                XivChatType.CrossLinkShell4 => "CWLS 4",
                XivChatType.CrossLinkShell5 => "CWLS 5",
                XivChatType.CrossLinkShell6 => "CWLS 6",
                XivChatType.CrossLinkShell7 => "CWLS 7",
                XivChatType.CrossLinkShell8 => "CWLS 8",

                XivChatType.Ls1 => "Linkshell 1",
                XivChatType.Ls2 => "Linkshell 2",
                XivChatType.Ls3 => "Linkshell 3",
                XivChatType.Ls4 => "Linkshell 4",
                XivChatType.Ls5 => "Linkshell 5",
                XivChatType.Ls6 => "Linkshell 6",
                XivChatType.Ls7 => "Linkshell 7",
                XivChatType.Ls8 => "Linkshell 8",

                XivChatType.Party => "Party",
                XivChatType.Alliance => "Alliance",
                XivChatType.FreeCompany => "Free Company",
                XivChatType.Say => "Say",
                XivChatType.Yell => "Yell",
                XivChatType.Shout => "Shout",

                _ => channel.ToString(),
            };
        }
    }
}
