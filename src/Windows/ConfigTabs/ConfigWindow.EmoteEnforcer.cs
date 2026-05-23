using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {
        private int emoteEnforcerSelectedAddIndex = 0;
        private string emoteEnforcerSearchText = string.Empty;

        private void DrawEmoteEnforcerTab()
        {
            var enabled = configuration.EmoteEnforcerEnabled;
            if (ImGui.Checkbox("Enable Emote Enforcer", ref enabled))
            {
                configuration.EmoteEnforcerEnabled = enabled;
                configuration.Save();
            }

            ImGui.TextWrapped("Enforce emote to restraints. Will cancel once when restraint is removed.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var cancelCommand = configuration.EmoteEnforcerCancelCommand;
            if (ImGui.InputText("Emote Cancel Command(s)", ref cancelCommand))
            {
                configuration.EmoteEnforcerCancelCommand = cancelCommand;
                configuration.Save();
            }
            ImGui.Text("Example: /sit /wait 0.5 /sit /examineself");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var availableEmotes = plugin.EmoteApi.GetAllEmotes();
            var availableRestrictions = plugin.GagSpeakRestrictionsApi.GetAvailableRestrictions();
            var availableRestraintSets = plugin.GagSpeakRestraintSetApi.GetAllRestraintSets();
            var availableGags = plugin.GagSpeakGagsApi.GetAvailableGags();

            if (availableEmotes.Count == 0)
            {
                ImGui.TextWrapped("No emotes found.");
                return;
            }

            DrawEmoteAddCombo(availableEmotes);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (configuration.EmoteEnforcerEmotes.Count == 0)
            {
                ImGui.TextDisabled("No emotes configured.");
                return;
            }

            for (var i = configuration.EmoteEnforcerEmotes.Count - 1; i >= 0; i--)
            {
                var emoteConfig = configuration.EmoteEnforcerEmotes[i];

                if (emoteConfig.EmoteId == 0)
                    continue;

                ImGui.PushID($"emote-enforcer-{emoteConfig.EmoteId}");

                var displayName = string.IsNullOrWhiteSpace(emoteConfig.EmoteName)
                    ? $"Emote #{emoteConfig.EmoteId}"
                    : emoteConfig.EmoteName;

                var headerOpen = ImGui.CollapsingHeader($"{displayName}##{emoteConfig.EmoteId}");

                //ImGui.SameLine();

                

                if (headerOpen)
                {
                    ImGui.Indent();

                    var ctrlHeld = ImGui.GetIO().KeyCtrl;

                    if (!ctrlHeld)
                        ImGui.BeginDisabled();

                    if (ImGui.SmallButton("Remove"))
                    {
                        if (ctrlHeld)
                        {
                            configuration.EmoteEnforcerEmotes.RemoveAt(i);
                            configuration.Save();

                            ImGui.PopID();
                            continue;
                        }
                    }

                    if (!ctrlHeld)
                        ImGui.EndDisabled();

                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.SetTooltip(ctrlHeld
                            ? "Remove this emote from the enforcer."
                            : "Hold CTRL to remove this emote from the enforcer.");
                    }

                    ImGui.TextDisabled($"Emote ID: {emoteConfig.EmoteId}");

                    ImGui.Spacing();

                    DrawGagSpeakItemList(
                        "Restraint Sets",
                        emoteConfig.RestraintSets,
                        availableRestraintSets,
                        $"emote-restraints-{emoteConfig.EmoteId}");

                    ImGui.Spacing();

                    DrawGagSpeakItemList(
                        "Restrictions",
                        emoteConfig.Restrictions,
                        availableRestrictions,
                        $"emote-restrictions-{emoteConfig.EmoteId}");

                    ImGui.Spacing();

                    DrawGagSpeakItemList(
                        "Gags",
                        emoteConfig.Gags,
                        availableGags,
                        $"emote-gags-{emoteConfig.EmoteId}");

                    ImGui.Unindent();
                }

                ImGui.PopID();
            }
        }
        private void DrawEmoteAddCombo(Dictionary<uint, string> availableEmotes)
        {
            var configuredEmoteIds = configuration.EmoteEnforcerEmotes
                .Select(x => x.EmoteId)
                .ToHashSet();

            var orderedAvailable = availableEmotes
                .Where(x => !configuredEmoteIds.Contains(x.Key))
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Key)
                .ToList();

            ImGui.Text("Add Emote");

            if (orderedAvailable.Count == 0)
            {
                ImGui.TextDisabled("All available emotes are already configured.");
                return;
            }

            if (emoteEnforcerSelectedAddIndex < 0 || emoteEnforcerSelectedAddIndex >= orderedAvailable.Count)
                emoteEnforcerSelectedAddIndex = 0;

            var selected = orderedAvailable[emoteEnforcerSelectedAddIndex];

            var selectedName = string.IsNullOrWhiteSpace(selected.Value)
                ? $"Emote #{selected.Key}"
                : selected.Value;

            ImGui.SetNextItemWidth(350);

            if (ImGui.BeginCombo("##add-emote-enforcer-emote", selectedName))
            {
                ImGui.SetNextItemWidth(-1);

                if (ImGui.IsWindowAppearing())
                    ImGui.SetKeyboardFocusHere();

                ImGui.InputTextWithHint(
                    "##search-emote-enforcer-emote",
                    "Search...",
                    ref emoteEnforcerSearchText,
                    128);

                ImGui.Separator();

                var filteredItems = string.IsNullOrWhiteSpace(emoteEnforcerSearchText)
                    ? orderedAvailable
                    : orderedAvailable
                        .Where(x =>
                            x.Value.Contains(emoteEnforcerSearchText, StringComparison.OrdinalIgnoreCase) ||
                            x.Key.ToString().Contains(emoteEnforcerSearchText, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (filteredItems.Count == 0)
                {
                    ImGui.TextDisabled("No matches.");
                }
                else
                {
                    foreach (var item in filteredItems)
                    {
                        var originalIndex = orderedAvailable.FindIndex(x => x.Key == item.Key);
                        var isSelected = originalIndex == emoteEnforcerSelectedAddIndex;

                        var label = string.IsNullOrWhiteSpace(item.Value)
                            ? $"Emote #{item.Key}"
                            : item.Value;

                        if (ImGui.Selectable($"{label}##emote-{item.Key}", isSelected))
                        {
                            emoteEnforcerSelectedAddIndex = originalIndex;
                            emoteEnforcerSearchText = string.Empty;

                            ImGui.CloseCurrentPopup();
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();

            if (ImGui.Button("Add##emote-enforcer-emote"))
            {
                var item = orderedAvailable[emoteEnforcerSelectedAddIndex];

                configuration.EmoteEnforcerEmotes.Add(new EmoteEnforcer.EmoteEnforcerEmoteConfig
                {
                    EmoteId = item.Key,
                    EmoteName = item.Value,
                });

                emoteEnforcerSearchText = string.Empty;
                emoteEnforcerSelectedAddIndex = 0;

                configuration.Save();
            }
        }
    }
}
