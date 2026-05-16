using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SayusGagExtender.PenumbraEnforcer;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {
        private int penumbraEnforcerSelectedAddIndex = 0;
        private string penumbraEnforcerSearchText = string.Empty;

        private void DrawPenumbraEnforcerTab()
        {
            var enabled = configuration.PenumbraEnforcerEnabled;
            if (ImGui.Checkbox("Enable Penumbra Enforcer", ref enabled))
            {
                configuration.PenumbraEnforcerEnabled = enabled;
                configuration.Save();
            }

            ImGui.TextWrapped("Enforce Penumbra mod to restraints. Mod will not be usable outside of this context. Duplicate mod if needed.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var availableMods = plugin.PenumbraApi.GetAllMods();
            var availableRestrictions = plugin.GagSpeakRestrictionsApi.GetAvailableRestrictions();
            var availableRestraintSets = plugin.GagSpeakRestraintSetApi.GetAllRestraintSets();
            var availableGags = plugin.GagSpeakGagsApi.GetAvailableGags();

            if (availableMods.Count == 0)
            {
                ImGui.TextWrapped("No Penumbra mods found. Make sure Penumbra is loaded and available.");
                return;
            }

            DrawPenumbraModAddCombo(availableMods);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (configuration.PenumbraEnforcerMods.Count == 0)
            {
                ImGui.TextDisabled("No Penumbra mods configured.");
                return;
            }

            for (var i = configuration.PenumbraEnforcerMods.Count - 1; i >= 0; i--)
            {
                var modConfig = configuration.PenumbraEnforcerMods[i];

                ImGui.PushID($"penumbra-enforcer-{modConfig.ModDirectory}");

                var modEnabled = plugin.PenumbraApi.IsModEnabledOnPlayerCollection(
                    modConfig.ModDirectory,
                    modConfig.ModName);

                if (modEnabled)
                    ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.2f, 1.0f, 0.2f, 1.0f));

                var displayName = string.IsNullOrWhiteSpace(modConfig.ModName)
                    ? modConfig.ModDirectory
                    : modConfig.ModName;

                var headerOpen = ImGui.CollapsingHeader($"{displayName}##{modConfig.ModDirectory}");

                if (modEnabled)
                    ImGui.PopStyleColor();


                

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
                            configuration.PenumbraEnforcerMods.RemoveAt(i);
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
                            ? "Remove this Penumbra mod from the enforcer."
                            : "Hold CTRL to remove this Penumbra mod from the enforcer.");
                    }

                    ImGui.TextDisabled($"Directory: {modConfig.ModDirectory}");

                    ImGui.Spacing();

                    DrawGagSpeakItemList(
                        "Restraint Sets",
                        modConfig.RestraintSets,
                        availableRestraintSets,
                        $"penumbra-restraints-{modConfig.ModDirectory}");

                    ImGui.Spacing();

                    DrawGagSpeakItemList(
                        "Restrictions",
                        modConfig.Restrictions,
                        availableRestrictions,
                        $"penumbra-restrictions-{modConfig.ModDirectory}");

                    ImGui.Spacing();

                    DrawGagSpeakItemList(
                        "Gags",
                        modConfig.Gags,
                        availableGags,
                        $"penumbra-gags-{modConfig.ModDirectory}");

                    ImGui.Unindent();
                }

                ImGui.PopID();
            }
        }

        private void DrawPenumbraModAddCombo(Dictionary<string, string> availableMods)
        {
            var configuredDirectories = configuration.PenumbraEnforcerMods
                .Select(x => x.ModDirectory)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var orderedAvailable = availableMods
                .Where(x => !configuredDirectories.Contains(x.Key))
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ImGui.Text("Add Penumbra Mod");

            if (orderedAvailable.Count == 0)
            {
                ImGui.TextDisabled("All available Penumbra mods are already configured.");
                return;
            }

            if (penumbraEnforcerSelectedAddIndex < 0 || penumbraEnforcerSelectedAddIndex >= orderedAvailable.Count)
                penumbraEnforcerSelectedAddIndex = 0;

            var selected = orderedAvailable[penumbraEnforcerSelectedAddIndex];
            var selectedName = string.IsNullOrWhiteSpace(selected.Value)
                ? selected.Key
                : selected.Value;

            ImGui.SetNextItemWidth(350);

            if (ImGui.BeginCombo("##add-penumbra-enforcer-mod", selectedName))
            {
                ImGui.SetNextItemWidth(-1);

                if (ImGui.IsWindowAppearing())
                    ImGui.SetKeyboardFocusHere();

                if (ImGui.InputTextWithHint("##search-penumbra-enforcer-mod", "Search...", ref penumbraEnforcerSearchText, 128))
                {
                }

                ImGui.Separator();

                var filteredItems = string.IsNullOrWhiteSpace(penumbraEnforcerSearchText)
                    ? orderedAvailable
                    : orderedAvailable
                        .Where(x =>
                            x.Value.Contains(penumbraEnforcerSearchText, StringComparison.OrdinalIgnoreCase) ||
                            x.Key.Contains(penumbraEnforcerSearchText, StringComparison.OrdinalIgnoreCase))
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
                        var isSelected = originalIndex == penumbraEnforcerSelectedAddIndex;

                        var label = string.IsNullOrWhiteSpace(item.Value)
                            ? item.Key
                            : item.Value;

                        if (ImGui.Selectable($"{label}##penumbra-mod-{item.Key}", isSelected))
                        {
                            penumbraEnforcerSelectedAddIndex = originalIndex;
                            penumbraEnforcerSearchText = string.Empty;
                            ImGui.CloseCurrentPopup();
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();

            if (ImGui.Button("Add##penumbra-enforcer-mod"))
            {
                var item = orderedAvailable[penumbraEnforcerSelectedAddIndex];

                configuration.PenumbraEnforcerMods.Add(new PenumbraEnforcerConfig
                {
                    ModDirectory = item.Key,
                    ModName = item.Value,
                });

                penumbraEnforcerSearchText = string.Empty;
                penumbraEnforcerSelectedAddIndex = 0;

                configuration.Save();
            }
        }
    }
}
