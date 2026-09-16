using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;

namespace VisibilityPlus;

/// <summary>Per-zone hide settings. Opens greyed out until enabled for the zone.</summary>
public sealed class ZoneWindow : Window
{
    private readonly uint zoneId;
    private readonly PluginConfiguration config;
    private readonly ZoneOverride zone;
    private readonly Action<uint, ushort> openWorldSettings;

    private readonly List<(ushort Id, string Name)> allWorlds = [];
    private readonly Dictionary<ushort, string> worldNames = new();
    private string worldSearch = string.Empty;

    public ZoneWindow(uint zoneId, string zoneName, PluginConfiguration config, VisibilityController controller, Action<uint, ushort> openWorldSettings)
        : base($"Zone {zoneId} - {zoneName}##vpluszone{zoneId}", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.zoneId = zoneId;
        this.config = config;
        this.zone = controller.GetOrCreateOverride(zoneId);
        this.openWorldSettings = openWorldSettings;
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new System.Numerics.Vector2(420, 200) };
        this.LoadWorlds();
    }

    private void LoadWorlds()
    {
        try
        {
            var sheet = Service.DataManager.GetExcelSheet<World>();
            if (sheet == null)
                return;
            foreach (var w in sheet)
            {
                if (!w.IsPublic)
                    continue; // skip dev/test rows like "Dev", "e-contents3"
                if (w.DataCenter.RowId == 0)
                    continue; // skip datacenter-less rows like "Cloudtest01/02"
                if (w.Name.IsEmpty)
                    continue;
                string name = w.Name.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                this.allWorlds.Add(((ushort)w.RowId, name));
                this.worldNames[(ushort)w.RowId] = name;
            }
            this.allWorlds.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // Degraded: dropdown stays empty, chips still show raw IDs.
        }
    }

    private string WorldName(ushort id)
    {
        if (this.worldNames.TryGetValue(id, out var name))
            return name;
        try
        {
            var row = Service.DataManager.GetExcelSheet<World>()?.GetRow(id).Name.ToString();
            if (!string.IsNullOrEmpty(row))
            {
                this.worldNames[id] = row;
                return row;
            }
        }
        catch
        {
        }
        return id.ToString();
    }

    public override void Draw()
    {
        bool use = this.zone.UseCustom;
        if (ImGui.Checkbox("Enable per-zone settings", ref use))
        {
            this.zone.UseCustom = use;
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"On = zone {this.zoneId} uses the settings below.\nOff = global settings apply.");
        ImGui.SameLine();
        if (ImGui.Button("Reset to global defaults"))
        {
            this.zone.CopyFrom(this.config);
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copy the current global settings into this zone.");

        ImGui.Separator();
        ImGui.BeginDisabled(!this.zone.UseCustom);
        HideGroupsUI.DrawGroups(this.zone, this.config, null!, null, $"z{this.zoneId}");

        ImGui.Separator();
        bool worldFilter = this.zone.UseWorldFilter;
        if (ImGui.Checkbox("World Filter", ref worldFilter))
        {
            this.zone.UseWorldFilter = worldFilter;
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("zone filters will only apply on these worlds");
        ushort currentWorld = VisibilityController.GetCurrentWorldId();
        if (currentWorld != 0)
            ImGui.TextDisabled($"You are on: {this.WorldName(currentWorld)}");
        ImGui.BeginDisabled(!this.zone.UseWorldFilter);
        if (ImGui.BeginCombo($"##worldCombo{this.zoneId}", "Add a world..."))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##worldSearch", "Search worlds...", ref this.worldSearch, 64);
            string filter = this.worldSearch.Trim().ToLowerInvariant();
            int shown = 0;
            foreach (var (id, name) in this.allWorlds)
            {
                if (filter.Length > 0
                    && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !id.ToString().Contains(filter, StringComparison.Ordinal))
                    continue;
                if (++shown > 150)
                {
                    ImGui.Selectable("... refine the search to see more ...", false, ImGuiSelectableFlags.Disabled);
                    break;
                }
                bool already = this.zone.WorldOverrides.ContainsKey(id);
                if (ImGui.Selectable($"{name}", false, already ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None))
                {
                    this.zone.WorldOverrides[id] = WorldOverride.FromZone(this.zone);
                    this.config.Save();
                }
            }
            ImGui.EndCombo();
        }
        foreach (ushort id in this.zone.WorldOverrides.Keys.OrderBy(this.WorldName).ToArray())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.95f, 0.25f, 0.25f, 1f));
            if (ImGui.Button($"X##world{id}"))
            {
                this.zone.WorldOverrides.Remove(id);
                this.config.Save();
            }
            ImGui.PopStyleColor();
            ImGui.SameLine();
            bool isCurrent = id == currentWorld && currentWorld != 0;
            if (isCurrent)
                ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.00f, 1.00f, 1.00f, 1f));
            ImGui.Text(this.WorldName(id));
            if (isCurrent)
                ImGui.PopStyleColor();
            ImGui.SameLine();
            bool custom = this.zone.WorldOverrides.TryGetValue(id, out var wov) && wov.UseCustom;
            if (custom)
                ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.35f, 0.95f, 0.35f, 1f));
            if (ImGuiComponents.IconButton((int)(2000 + id), FontAwesomeIcon.Cog))
                this.openWorldSettings(this.zoneId, id);
            if (custom)
                ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(custom ? "Per-world settings (custom ON)." : "Per-world settings.");
        }
        if (this.zone.WorldOverrides.Count == 0)
            ImGui.TextDisabled("No worlds — filter applies on every world until you add one.");
        ImGui.EndDisabled();
        ImGui.EndDisabled();
    }
}
