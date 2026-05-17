using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Text;

namespace SayusGagExtender.Windows
{
    partial class ConfigWindow
    {
        private void DrawGagSpeakMirrorTab()
        {
            var restraintCloner = configuration.GagSpeakRestraintCloner;
            if (ImGui.Checkbox("Mirror restriants to alt characters", ref restraintCloner))
            {
                configuration.GagSpeakRestraintCloner = restraintCloner;
                configuration.Save();
            }

            ImGui.Spacing();

            var enforced = configuration.GagSpeakEnforcedRestraintCloner;
            if (ImGui.Checkbox("Locked mirroring (Prevents from changing restraints on alt characters)", ref enforced))
            {
                configuration.GagSpeakEnforcedRestraintCloner = enforced;
                configuration.Save();
            }

            ImGui.Spacing();

            ImGui.SetNextItemWidth(160);
            var gagSpeakMasterName = configuration.GagSpeakMasterName;
            if (ImGui.InputText("GagSpeak Main Char Name", ref gagSpeakMasterName))
            {
                configuration.GagSpeakMasterName = gagSpeakMasterName;
                configuration.Save();
            }

            ImGui.SetNextItemWidth(160);
            var gagSpeakMasterWorld = configuration.GagSpeakMasterWorld;
            if (ImGui.InputText("GagSpeak Main Char World", ref gagSpeakMasterWorld))
            {
                configuration.GagSpeakMasterWorld = gagSpeakMasterWorld;
                configuration.Save();
            }

            var name = Plugin.ObjectTable.LocalPlayer?.Name.ToString() ?? "";
            var homeWorld = Plugin.ObjectTable.LocalPlayer?.HomeWorld.RowId ?? 0u;
            var world = plugin.Utils.WorldRowIDToString(homeWorld);

            if (ImGui.Button($"Use {name}@{world}"))
            {
                configuration.GagSpeakMasterName = name;
                configuration.GagSpeakMasterWorld = world;
                configuration.Save();
            }

            ImGui.Spacing();


            var ctrlHeld = ImGui.GetIO().KeyCtrl;

            if (!ctrlHeld)
                ImGui.BeginDisabled();

            if (ImGui.Button($"Apply saved restraints now"))
            {
                plugin.MirrorGagSpeak.MirrorGagSpeakState();
            }

            if (!ctrlHeld)
                ImGui.EndDisabled();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(ctrlHeld
                    ? "Apply saved restraint set from main character"
                    : "Hold Ctrl to apply saved restraint set from main character");
            }

            /*
            if (ImGui.Button("Save active restraints"))
            {
                var restraintSet = plugin.GagSpeakRestraintSetApi.GetActiveRestraintSet();
                Plugin.ChatGui.Print($"Active Restraint Set: {restraintSet}");
                configuration.ActiveRestraintSet = restraintSet;

                var restrictions = plugin.GagSpeakRestrictionsApi.GetActiveRestrictions();
                foreach (var restriction in restrictions)
                {
                    Plugin.ChatGui.Print($"Active Restriction: {restriction}");
                }
                configuration.ActiveRestrictions = restrictions;

                var gags = plugin.GagSpeakGagsApi.GetActiveGags();
                foreach (var gag in gags)
                {
                    Plugin.ChatGui.Print($"Active Gag: {gag}");
                }
                configuration.ActiveGags = gags;

                configuration.Save();
            }
            */
        }
    }
}
