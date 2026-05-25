using Dalamud.Bindings.ImGui;
using System;
using System.Linq;
using System.Numerics;
using static SayusGagExtender.HonorificEnforcer;

namespace SayusGagExtender.Windows;

partial class ConfigWindow
{
    private void DrawHonorificEnforcerTab()
    {
        var enabled = configuration.HonorificEnforcerEnabled;
        if (ImGui.Checkbox("Enable Honorific Enforcer", ref enabled))
        {
            configuration.HonorificEnforcerEnabled = enabled;
            configuration.Save();

            if (!enabled)
                plugin.HonorificEnforcer.Enforce();
        }

        ImGui.TextWrapped("Enforce Honorific titles from restraints. Highest matching priority wins.");
        ImGui.TextWrapped("HonorificManager owns the final title and restores the original title when no managed title is active.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Add Honorific Title##honorific-enforcer-add"))
        {
            configuration.HonorificEnforcerTitles.Add(new HonorificEnforcerConfig
            {
                HonorificTitle = string.Empty,
                HonorificColor = new Vector3(1.0f, 1.0f, 1.0f),
                HonorificGlow = new Vector3(0.0f, 0.0f, 0.0f),
                HonorificPriority = 100,
            });

            configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var availableRestrictions = plugin.GagSpeakRestrictionsApi.GetAvailableRestrictions();
        var availableRestraintSets = plugin.GagSpeakRestraintSetApi.GetAllRestraintSets();
        var availableGags = plugin.GagSpeakGagsApi.GetAvailableGags();

        if (configuration.HonorificEnforcerTitles.Count == 0)
        {
            ImGui.TextDisabled("No Honorific titles configured.");
            return;
        }

        for (var i = configuration.HonorificEnforcerTitles.Count - 1; i >= 0; i--)
        {
            var titleConfig = configuration.HonorificEnforcerTitles[i];

            ImGui.PushID($"honorific-enforcer-{i}");

            var displayName = string.IsNullOrWhiteSpace(titleConfig.HonorificTitle)
                ? "Disabled Honorific Title"
                : titleConfig.HonorificTitle;

            if (plugin.HonorificEnforcer.IsActive &&
                string.Equals(displayName, titleConfig.HonorificTitle, StringComparison.Ordinal))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 1.0f, 0.2f, 1.0f));
            }

            var headerOpen = ImGui.CollapsingHeader($"{displayName} P:{titleConfig.HonorificPriority}###honorific-enforcer-entry-{i}");

            if (plugin.HonorificEnforcer.IsActive &&
                string.Equals(displayName, titleConfig.HonorificTitle, StringComparison.Ordinal))
            {
                ImGui.PopStyleColor();
            }

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
                        configuration.HonorificEnforcerTitles.RemoveAt(i);
                        configuration.Save();

                        plugin.HonorificEnforcer.MarkConfigDirty();
                        plugin.HonorificEnforcer.Enforce();

                        ImGui.PopID();
                        continue;
                    }
                }

                if (!ctrlHeld)
                    ImGui.EndDisabled();

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip(ctrlHeld
                        ? "Remove this Honorific title from the enforcer."
                        : "Hold CTRL to remove this Honorific title from the enforcer.");
                }

                ImGui.Spacing();

                var honorificTitle = titleConfig.HonorificTitle;
                var honorificColor = titleConfig.HonorificColor;
                var honorificGlow = titleConfig.HonorificGlow;
                var honorificPriority = titleConfig.HonorificPriority;

                if (plugin.HonorificManager.DrawPermanentTitleConfigEditors(ref honorificTitle, ref honorificColor, ref honorificGlow, ref honorificPriority, titleWidth: 180f, priorityWidth: 60f))
                {
                    titleConfig.HonorificTitle = honorificTitle.Trim();
                    titleConfig.HonorificColor = honorificColor;
                    titleConfig.HonorificGlow = honorificGlow;
                    titleConfig.HonorificPriority = honorificPriority;

                    configuration.Save();

                    // Important: do not Enforce() directly here.
                    // Updating Honorific IPC on every keypress can steal focus.
                    plugin.HonorificEnforcer.MarkConfigDirty(delayApply: true);
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawGagSpeakItemList(
                    "Restraint Sets",
                    titleConfig.RestraintSets,
                    availableRestraintSets,
                    $"honorific-restraints-{i}",
                    () => plugin.HonorificEnforcer.MarkConfigDirty());

                DrawGagSpeakItemList(
                    "Restrictions",
                    titleConfig.Restrictions,
                    availableRestrictions,
                    $"honorific-restrictions-{i}",
                    () => plugin.HonorificEnforcer.MarkConfigDirty());

                DrawGagSpeakItemList(
                    "Gags",
                    titleConfig.Gags,
                    availableGags,
                    $"honorific-gags-{i}",
                    () => plugin.HonorificEnforcer.MarkConfigDirty());

                ImGui.Unindent();
            }

            ImGui.PopID();
        }
    }
}
