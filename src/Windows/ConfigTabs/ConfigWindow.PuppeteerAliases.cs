using Dalamud.Bindings.ImGui;
using SayusGagExtender.API.GagSpeak;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SayusGagExtender.Windows;

public partial class ConfigWindow
{
    private List<GagSpeakPuppeteerAliasesApi.PuppeteerAliasInfo> puppeteerAliasCache = new();
    private DateTime puppeteerAliasCacheTimeUtc = DateTime.MinValue;
    private string puppeteerAliasFilter = string.Empty;

    private void DrawPuppeteerAliasesTab()
    {
        ImGui.TextWrapped("Reads your GagSpeak Puppeteer aliases and lets you keep private notes for them in Sayu's Gag Extender config.");
        ImGui.TextWrapped("Alias folder paths are best-effort because GagSpeak stores the folder tree separately from the alias data.");
        ImGui.Spacing();

        if (ImGui.Button("Refresh aliases") || puppeteerAliasCacheTimeUtc == DateTime.MinValue)
        {
            RefreshPuppeteerAliasCache();
        }

        ImGui.SameLine();
        if (ImGui.Button("Dump to chat"))
        {
            plugin.GagSpeakPuppeteerAliasesApi.DumpAliases();
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"{puppeteerAliasCache.Count} aliases loaded");

        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##PuppeteerAliasFilter", "Filter aliases, triggers, folders, notes...", ref puppeteerAliasFilter, 128);

        ImGui.Spacing();

        if (puppeteerAliasCache.Count == 0)
        {
            ImGui.TextDisabled("No aliases found. Make sure GagSpeak is loaded and synced, then refresh.");
            return;
        }

        var aliases = puppeteerAliasCache.Where(PuppeteerAliasMatchesFilter).ToList();
        if (aliases.Count == 0)
        {
            ImGui.TextDisabled("No aliases match the current filter.");
            return;
        }

        if (!ImGui.BeginTable("PuppeteerAliasTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY, new System.Numerics.Vector2(0, ImGui.GetContentRegionAvail().Y)))
            return;

        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 48f);
        ImGui.TableSetupColumn("Folder", ImGuiTableColumnFlags.WidthFixed, 130f);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("Trigger", ImGuiTableColumnFlags.WidthFixed, 130f);
        ImGui.TableSetupColumn("Allowed", ImGuiTableColumnFlags.WidthFixed, 160f);
        ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var alias in aliases)
            DrawPuppeteerAliasRow(alias);

        ImGui.EndTable();
    }

    private void RefreshPuppeteerAliasCache()
    {
        puppeteerAliasCache = plugin.GagSpeakPuppeteerAliasesApi.GetAliases();
        puppeteerAliasCacheTimeUtc = DateTime.UtcNow;
    }

    private bool PuppeteerAliasMatchesFilter(GagSpeakPuppeteerAliasesApi.PuppeteerAliasInfo alias)
    {
        if (string.IsNullOrWhiteSpace(puppeteerAliasFilter))
            return true;

        var filter = puppeteerAliasFilter.Trim();
        var note = configuration.GagSpeakPuppeteerAliasNotes.TryGetValue(alias.Id, out var existingNote) ? existingNote : string.Empty;

        return alias.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || alias.TriggerCommand.Contains(filter, StringComparison.OrdinalIgnoreCase) || alias.FolderPath.Contains(filter, StringComparison.OrdinalIgnoreCase) || note.Contains(filter, StringComparison.OrdinalIgnoreCase) || alias.WhitelistedNames.Any(x => x.Contains(filter, StringComparison.OrdinalIgnoreCase)) || alias.WhitelistedUids.Any(x => x.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private void DrawPuppeteerAliasRow(GagSpeakPuppeteerAliasesApi.PuppeteerAliasInfo alias)
    {
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(alias.Enabled ? "On" : "Off");
        if (!alias.IsValid)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("!");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("GagSpeak does not currently consider this alias valid. It may be missing a trigger or actions.");
        }

        ImGui.TableNextColumn();
        ImGui.TextWrapped(string.IsNullOrWhiteSpace(alias.FolderPath) ? "<root>" : alias.FolderPath);

        ImGui.TableNextColumn();
        ImGui.TextWrapped(string.IsNullOrWhiteSpace(alias.Name) ? "<unnamed>" : alias.Name);

        ImGui.TableNextColumn();
        ImGui.TextWrapped(string.IsNullOrWhiteSpace(alias.TriggerCommand) ? "<none>" : alias.TriggerCommand);
        if (alias.IgnoreCase && ImGui.IsItemHovered())
            ImGui.SetTooltip("GagSpeak alias ignores case.");

        ImGui.TableNextColumn();
        if (alias.WhitelistedNames.Count == 0)
        {
            ImGui.TextWrapped("Everyone");
        }
        else
        {
            ImGui.TextWrapped(string.Join(", ", alias.WhitelistedNames));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(string.Join("\n", alias.WhitelistedUids));
        }

        ImGui.TableNextColumn();
        var note = configuration.GagSpeakPuppeteerAliasNotes.TryGetValue(alias.Id, out var existingNote) ? existingNote : string.Empty;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText($"##PuppeteerAliasNote{alias.Id}", ref note, 512))
        {
            if (string.IsNullOrWhiteSpace(note))
                configuration.GagSpeakPuppeteerAliasNotes.Remove(alias.Id);
            else
                configuration.GagSpeakPuppeteerAliasNotes[alias.Id] = note;

            configuration.Save();
        }
    }
}
