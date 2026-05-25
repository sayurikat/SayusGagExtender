using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using static SayusGagExtender.API.GagSpeak.GagSpeakReflectionContext;

namespace SayusGagExtender.Windows;

public partial class ConfigWindow
{
    private int cammyEnforcerSelectedAddIndex = 0;
    private string cammyEnforcerSearchText = string.Empty;

    private int cammyDefaultSelectedIndex = 0;
    private string cammyDefaultSearchText = string.Empty;

    private void DrawCammyEnforcerTab()
    {
        var enabled = configuration.CammyEnforcerEnabled;
        if (ImGui.Checkbox("Enable Cammy Enforcer", ref enabled))
        {
            configuration.CammyEnforcerEnabled = enabled;
            configuration.Save();
        }

        ImGui.TextWrapped("Enforce Cammy presets from restraints. If multiple presets match, the highest priority preset wins.");
        ImGui.TextWrapped("When no enforced preset is active anymore, the selected default preset is applied once. After that, Cammy is left alone until enforcement triggers again.");

        if (!plugin.CammyApi.IsAvailable())
        {
            ImGui.Spacing();
            ImGui.TextWrapped("Cammy is not available. Make sure Cammy is installed and loaded.");
            return;
        }

        var availablePresets = plugin.CammyApi.GetAllPresets()
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var availableRestrictions = plugin.GagSpeakRestrictionsApi.GetAvailableRestrictions();
        var availableRestraintSets = plugin.GagSpeakRestraintSetApi.GetAllRestraintSets();
        var availableGags = plugin.GagSpeakGagsApi.GetAvailableGags();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawCammyDefaultPresetCombo(availablePresets);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (availablePresets.Count == 0)
        {
            ImGui.TextWrapped("No Cammy presets found.");
            return;
        }

        DrawCammyPresetAddCombo(availablePresets);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (configuration.CammyEnforcerPresets.Count == 0)
        {
            ImGui.TextDisabled("No Cammy presets configured.");
            return;
        }

        for (var i = configuration.CammyEnforcerPresets.Count - 1; i >= 0; i--)
        {
            var presetConfig = configuration.CammyEnforcerPresets[i];

            if (string.IsNullOrWhiteSpace(presetConfig.PresetName))
                continue;

            ImGui.PushID($"cammy-enforcer-{presetConfig.PresetName}");

            var currentPreset = plugin.CammyApi.GetCurrentActivePreset();
            var presetIsActive =
                currentPreset != null &&
                string.Equals(currentPreset.Value.Name, presetConfig.PresetName, StringComparison.OrdinalIgnoreCase);

            if (presetIsActive)
                ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.2f, 1.0f, 0.2f, 1.0f));

            var headerOpen = ImGui.CollapsingHeader($"{presetConfig.PresetName}  |  Priority {presetConfig.Priority}###cammy-enforcer-entry-{i}");

            if (presetIsActive)
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
                        configuration.CammyEnforcerPresets.RemoveAt(i);
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
                        ? "Remove this Cammy preset from the enforcer."
                        : "Hold CTRL to remove this Cammy preset from the enforcer.");
                }

                ImGui.Spacing();

                var priorityText = presetConfig.Priority.ToString();

                ImGui.SetNextItemWidth(90);
                if (ImGui.InputText(
                        "Priority",
                        ref priorityText,
                        8,
                        ImGuiInputTextFlags.CharsDecimal))
                {
                    if (int.TryParse(priorityText.Trim(), out var parsedPriority))
                    {
                        presetConfig.Priority = parsedPriority;
                        configuration.Save();
                    }
                }

                ImGui.TextDisabled("Higher priority wins when multiple Cammy presets are triggered.");

                ImGui.Spacing();

                DrawGagSpeakItemList(
                    "Restraint Sets",
                    presetConfig.RestraintSets,
                    availableRestraintSets,
                    $"cammy-restraints-{i}");

                ImGui.Spacing();

                DrawGagSpeakItemList(
                    "Restrictions",
                    presetConfig.Restrictions,
                    availableRestrictions,
                    $"cammy-restrictions-{i}");

                ImGui.Spacing();

                DrawGagSpeakItemList(
                    "Gags",
                    presetConfig.Gags,
                    availableGags,
                    $"cammy-gags-{i}");

                ImGui.Unindent();
            }

            ImGui.PopID();
        }
    }

    private void DrawCammyDefaultPresetCombo(IList<SayusGagExtender.API.CammyPresetInfo> availablePresets)
    {
        ImGui.Text("Default Cammy Preset");

        ImGui.TextWrapped("Applied once when Cammy enforcement stops. Select None to clear Cammy override instead.");

        var currentDefaultName = configuration.CammyEnforcerDefaultPresetName;

        var currentDisplayName = string.IsNullOrWhiteSpace(currentDefaultName)
            ? "None"
            : availablePresets.Any(x => string.Equals(x.Name, currentDefaultName, StringComparison.OrdinalIgnoreCase))
                ? currentDefaultName
                : $"{currentDefaultName} (missing)";

        ImGui.SetNextItemWidth(350);

        if (ImGui.BeginCombo("##cammy-default-preset", currentDisplayName))
        {
            if (ImGui.Selectable("None##cammy-default-none", string.IsNullOrWhiteSpace(currentDefaultName)))
            {
                configuration.CammyEnforcerDefaultPresetName = string.Empty;
                cammyDefaultSearchText = string.Empty;
                configuration.Save();

                ImGui.CloseCurrentPopup();
            }

            ImGui.Separator();

            ImGui.SetNextItemWidth(-1);

            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();

            ImGui.InputTextWithHint(
                "##search-cammy-default-preset",
                "Search...",
                ref cammyDefaultSearchText,
                128);

            ImGui.Separator();

            var filteredPresets = string.IsNullOrWhiteSpace(cammyDefaultSearchText)
                ? availablePresets
                : availablePresets
                    .Where(x => x.Name.Contains(cammyDefaultSearchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (filteredPresets.Count == 0)
            {
                ImGui.TextDisabled("No matches.");
            }
            else
            {
                foreach (var preset in filteredPresets)
                {
                    var isSelected = string.Equals(
                        preset.Name,
                        configuration.CammyEnforcerDefaultPresetName,
                        StringComparison.OrdinalIgnoreCase);

                    if (ImGui.Selectable($"{preset.Name}##cammy-default-{preset.Index}", isSelected))
                    {
                        configuration.CammyEnforcerDefaultPresetName = preset.Name;

                        cammyDefaultSearchText = string.Empty;
                        configuration.Save();

                        ImGui.CloseCurrentPopup();
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        if (!string.IsNullOrWhiteSpace(configuration.CammyEnforcerDefaultPresetName))
        {
            ImGui.SameLine();

            if (ImGui.SmallButton("Clear##cammy-default-preset"))
            {
                configuration.CammyEnforcerDefaultPresetName = string.Empty;
                configuration.Save();
            }
        }
    }

    private void DrawCammyPresetAddCombo(IList<SayusGagExtender.API.CammyPresetInfo> availablePresets)
    {
        var configuredPresetNames = configuration.CammyEnforcerPresets
            .Where(x => !string.IsNullOrWhiteSpace(x.PresetName))
            .Select(x => x.PresetName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orderedAvailable = availablePresets
            .Where(x => !configuredPresetNames.Contains(x.Name))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ImGui.Text("Add Cammy Preset");

        if (orderedAvailable.Count == 0)
        {
            ImGui.TextDisabled("All available Cammy presets are already configured.");
            return;
        }

        if (cammyEnforcerSelectedAddIndex < 0 || cammyEnforcerSelectedAddIndex >= orderedAvailable.Count)
            cammyEnforcerSelectedAddIndex = 0;

        var selected = orderedAvailable[cammyEnforcerSelectedAddIndex];

        ImGui.SetNextItemWidth(350);

        if (ImGui.BeginCombo("##add-cammy-enforcer-preset", selected.Name))
        {
            ImGui.SetNextItemWidth(-1);

            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();

            ImGui.InputTextWithHint(
                "##search-cammy-enforcer-preset",
                "Search...",
                ref cammyEnforcerSearchText,
                128);

            ImGui.Separator();

            var filteredItems = string.IsNullOrWhiteSpace(cammyEnforcerSearchText)
                ? orderedAvailable
                : orderedAvailable
                    .Where(x => x.Name.Contains(cammyEnforcerSearchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (filteredItems.Count == 0)
            {
                ImGui.TextDisabled("No matches.");
            }
            else
            {
                foreach (var item in filteredItems)
                {
                    var originalIndex = orderedAvailable.FindIndex(x =>
                        string.Equals(x.Name, item.Name, StringComparison.OrdinalIgnoreCase));

                    var isSelected = originalIndex == cammyEnforcerSelectedAddIndex;

                    if (ImGui.Selectable($"{item.Name}##cammy-preset-{item.Index}", isSelected))
                    {
                        cammyEnforcerSelectedAddIndex = originalIndex;
                        cammyEnforcerSearchText = string.Empty;

                        ImGui.CloseCurrentPopup();
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        if (ImGui.Button("Add##cammy-enforcer-preset"))
        {
            var item = orderedAvailable[cammyEnforcerSelectedAddIndex];

            configuration.CammyEnforcerPresets.Add(new CammyEnforcer.CammyEnforcerConfig
            {
                PresetName = item.Name,
                Priority = 0,
            });

            cammyEnforcerSearchText = string.Empty;
            cammyEnforcerSelectedAddIndex = 0;

            configuration.Save();
        }
    }
}
