using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using static SayusGagExtender.CustomizePlusEnforcer;

using CustomizePlusProfileTuple =
    (System.Guid UniqueId,
     string Name,
     string VirtualPath,
     System.Collections.Generic.List<(string Name, ushort WorldId, byte CharacterType, ushort CharacterSubType)> Characters,
     int Priority,
     bool IsEnabled);

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {
        private int customizePlusEnforcerSelectedAddIndex = 0;
        private string customizePlusEnforcerSearchText = string.Empty;
        private int customizePlusDefaultSelectedIndex = 0;
        private string customizePlusDefaultSearchText = string.Empty;

        private void DrawCustomizePlusEnforcerTab()
        {
            var enabled = configuration.CustomizePlusEnforcerEnabled;
            if (ImGui.Checkbox("Enable Customize+ Enforcer", ref enabled))
            {
                configuration.CustomizePlusEnforcerEnabled = enabled;
                configuration.Save();
            }

            ImGui.TextWrapped("Enforce Customize+ profiles to restraints. Profile will not be usable outside of this context. Duplicate profile if needed.");

            var availableProfiles = plugin.CustomizePlusApi.GetAllProfiles();
            var availableRestrictions = plugin.GagSpeakRestrictionsApi.GetAvailableRestrictions();
            var availableRestraintSets = plugin.GagSpeakRestraintSetApi.GetAllRestraintSets();
            var availableGags = plugin.GagSpeakGagsApi.GetAvailableGags();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawCustomizePlusDefaultProfileCombo(availableProfiles);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            

            if (availableProfiles.Count == 0)
            {
                ImGui.TextWrapped("No Customize+ profiles found. Make sure Customize+ is loaded and available.");
                return;
            }

            DrawCustomizePlusProfileAddCombo(availableProfiles);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (configuration.CustomizePlusEnforcerProfiles.Count == 0)
            {
                ImGui.TextDisabled("No Customize+ profiles configured.");
                return;
            }

            for (var i = configuration.CustomizePlusEnforcerProfiles.Count - 1; i >= 0; i--)
            {
                var profileConfig = configuration.CustomizePlusEnforcerProfiles[i];

                ImGui.PushID($"customize-plus-enforcer-{profileConfig.ProfileId}");

                var profileEnabled = plugin.CustomizePlusApi.IsProfileEnabled(profileConfig.ProfileId);

                if (profileEnabled)
                    ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.2f, 1.0f, 0.2f, 1.0f));

                var displayName = string.IsNullOrWhiteSpace(profileConfig.ProfileName)
                    ? profileConfig.ProfileId.ToString()
                    : profileConfig.ProfileName;

                var headerOpen = ImGui.CollapsingHeader($"{displayName}##{profileConfig.ProfileId}");

                if (profileEnabled)
                    ImGui.PopStyleColor();

                ImGui.SameLine();

                var ctrlHeld = ImGui.GetIO().KeyCtrl;

                if (!ctrlHeld)
                    ImGui.BeginDisabled();

                if (ImGui.SmallButton("Remove"))
                {
                    if (ctrlHeld)
                    {
                        configuration.CustomizePlusEnforcerProfiles.RemoveAt(i);
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
                        ? "Remove this Customize+ profile from the enforcer."
                        : "Hold CTRL to remove this Customize+ profile from the enforcer.");
                }

                if (headerOpen)
                {
                    ImGui.Indent();

                    ImGui.TextDisabled($"Profile ID: {profileConfig.ProfileId}");

                    if (!string.IsNullOrWhiteSpace(profileConfig.VirtualPath))
                        ImGui.TextDisabled($"Path: {profileConfig.VirtualPath}");

                    ImGui.Spacing();

                    DrawGagSpeakItemList(
                        "Restraint Sets",
                        profileConfig.RestraintSets,
                        availableRestraintSets,
                        $"customize-plus-restraints-{profileConfig.ProfileId}");

                    ImGui.Spacing();

                    DrawGagSpeakItemList(
                        "Restrictions",
                        profileConfig.Restrictions,
                        availableRestrictions,
                        $"customize-plus-restrictions-{profileConfig.ProfileId}");

                    ImGui.Spacing();

                    DrawGagSpeakItemList(
                        "Gags",
                        profileConfig.Gags,
                        availableGags,
                        $"customize-plus-gags-{profileConfig.ProfileId}");

                    ImGui.Unindent();
                }

                ImGui.PopID();
            }
        }
       

        private void DrawCustomizePlusDefaultProfileCombo(IList<CustomizePlusProfileTuple> availableProfiles)
        {
            ImGui.Text("Default Customize+ Profile");

            ImGui.TextWrapped("This profile is enabled when none of the linked Customize+ profiles should be active. Select None to disable default behavior.");

            var orderedProfiles = availableProfiles
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.VirtualPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.UniqueId)
                .ToList();

            var currentProfile = orderedProfiles
                .FirstOrDefault(x => x.UniqueId == configuration.CustomizePlusDefaultProfileId);

            var currentDisplayName = configuration.CustomizePlusDefaultProfileId == Guid.Empty
                ? "None"
                : currentProfile.UniqueId != Guid.Empty
                    ? GetCustomizePlusProfileDisplayName(currentProfile)
                    : $"{configuration.CustomizePlusDefaultProfileName} (missing)";

            ImGui.SetNextItemWidth(350);

            if (ImGui.BeginCombo("##customize-plus-default-profile", currentDisplayName))
            {
                if (ImGui.Selectable("None##customize-plus-default-none", configuration.CustomizePlusDefaultProfileId == Guid.Empty))
                {
                    configuration.CustomizePlusDefaultProfileId = Guid.Empty;
                    configuration.CustomizePlusDefaultProfileName = string.Empty;
                    configuration.CustomizePlusDefaultProfileVirtualPath = string.Empty;

                    customizePlusDefaultSearchText = string.Empty;
                    configuration.Save();

                    ImGui.CloseCurrentPopup();
                }

                ImGui.Separator();

                ImGui.SetNextItemWidth(-1);

                if (ImGui.IsWindowAppearing())
                    ImGui.SetKeyboardFocusHere();

                ImGui.InputTextWithHint(
                    "##search-customize-plus-default-profile",
                    "Search...",
                    ref customizePlusDefaultSearchText,
                    128);

                ImGui.Separator();

                var filteredProfiles = string.IsNullOrWhiteSpace(customizePlusDefaultSearchText)
                    ? orderedProfiles
                    : orderedProfiles
                        .Where(x =>
                            x.Name.Contains(customizePlusDefaultSearchText, StringComparison.OrdinalIgnoreCase) ||
                            x.VirtualPath.Contains(customizePlusDefaultSearchText, StringComparison.OrdinalIgnoreCase) ||
                            x.UniqueId.ToString().Contains(customizePlusDefaultSearchText, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (filteredProfiles.Count == 0)
                {
                    ImGui.TextDisabled("No matches.");
                }
                else
                {
                    foreach (var profile in filteredProfiles)
                    {
                        var isSelected = profile.UniqueId == configuration.CustomizePlusDefaultProfileId;
                        var label = GetCustomizePlusProfileDisplayName(profile);

                        if (ImGui.Selectable($"{label}##customize-plus-default-{profile.UniqueId}", isSelected))
                        {
                            configuration.CustomizePlusDefaultProfileId = profile.UniqueId;
                            configuration.CustomizePlusDefaultProfileName = profile.Name;
                            configuration.CustomizePlusDefaultProfileVirtualPath = profile.VirtualPath;

                            customizePlusDefaultSearchText = string.Empty;
                            configuration.Save();

                            ImGui.CloseCurrentPopup();
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            if (configuration.CustomizePlusDefaultProfileId != Guid.Empty)
            {
                ImGui.SameLine();

                if (ImGui.SmallButton("Clear##customize-plus-default-profile"))
                {
                    configuration.CustomizePlusDefaultProfileId = Guid.Empty;
                    configuration.CustomizePlusDefaultProfileName = string.Empty;
                    configuration.CustomizePlusDefaultProfileVirtualPath = string.Empty;

                    configuration.Save();
                }
            }
        }
        private void DrawCustomizePlusProfileAddCombo(IList<CustomizePlusProfileTuple> availableProfiles)
        {
            var configuredProfileIds = configuration.CustomizePlusEnforcerProfiles
                .Select(x => x.ProfileId)
                .ToHashSet();

            var orderedAvailable = availableProfiles
                .Where(x => !configuredProfileIds.Contains(x.UniqueId))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.VirtualPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.UniqueId)
                .ToList();

            ImGui.Text("Add Customize+ Profile");

            if (orderedAvailable.Count == 0)
            {
                ImGui.TextDisabled("All available Customize+ profiles are already configured.");
                return;
            }

            if (customizePlusEnforcerSelectedAddIndex < 0 || customizePlusEnforcerSelectedAddIndex >= orderedAvailable.Count)
                customizePlusEnforcerSelectedAddIndex = 0;

            var selected = orderedAvailable[customizePlusEnforcerSelectedAddIndex];
            var selectedName = GetCustomizePlusProfileDisplayName(selected);

            ImGui.SetNextItemWidth(350);

            if (ImGui.BeginCombo("##add-customize-plus-enforcer-profile", selectedName))
            {
                ImGui.SetNextItemWidth(-1);

                if (ImGui.IsWindowAppearing())
                    ImGui.SetKeyboardFocusHere();

                if (ImGui.InputTextWithHint("##search-customize-plus-enforcer-profile", "Search...", ref customizePlusEnforcerSearchText, 128))
                {
                }

                ImGui.Separator();

                var filteredItems = string.IsNullOrWhiteSpace(customizePlusEnforcerSearchText)
                    ? orderedAvailable
                    : orderedAvailable
                        .Where(x =>
                            x.Name.Contains(customizePlusEnforcerSearchText, StringComparison.OrdinalIgnoreCase) ||
                            x.VirtualPath.Contains(customizePlusEnforcerSearchText, StringComparison.OrdinalIgnoreCase) ||
                            x.UniqueId.ToString().Contains(customizePlusEnforcerSearchText, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (filteredItems.Count == 0)
                {
                    ImGui.TextDisabled("No matches.");
                }
                else
                {
                    foreach (var item in filteredItems)
                    {
                        var originalIndex = orderedAvailable.FindIndex(x => x.UniqueId == item.UniqueId);
                        var isSelected = originalIndex == customizePlusEnforcerSelectedAddIndex;

                        var label = GetCustomizePlusProfileDisplayName(item);

                        if (ImGui.Selectable($"{label}##customize-plus-profile-{item.UniqueId}", isSelected))
                        {
                            customizePlusEnforcerSelectedAddIndex = originalIndex;
                            customizePlusEnforcerSearchText = string.Empty;
                            ImGui.CloseCurrentPopup();
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();

            if (ImGui.Button("Add##customize-plus-enforcer-profile"))
            {
                var item = orderedAvailable[customizePlusEnforcerSelectedAddIndex];

                configuration.CustomizePlusEnforcerProfiles.Add(new CustomizePlusEnforcerConfig
                {
                    ProfileId = item.UniqueId,
                    ProfileName = item.Name,
                    VirtualPath = item.VirtualPath,
                });

                customizePlusEnforcerSearchText = string.Empty;
                customizePlusEnforcerSelectedAddIndex = 0;

                configuration.Save();
            }
        }

        private static string GetCustomizePlusProfileDisplayName(CustomizePlusProfileTuple profile)
        {
            if (!string.IsNullOrWhiteSpace(profile.VirtualPath))
                return $"{profile.Name} ({profile.VirtualPath})";

            if (!string.IsNullOrWhiteSpace(profile.Name))
                return profile.Name;

            return profile.UniqueId.ToString();
        }
    }
}
