using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;

namespace VisibilityPlus;

public sealed class ConfigWindow : Window
{
    private readonly PluginConfiguration config;
    private readonly VisibilityController controller;
    private readonly Action<uint> openZoneSettings;
    private readonly HoldCaptureState holdState = new();

    private readonly List<(uint Id, string Name)> allZones = [];
    private readonly Dictionary<uint, string> zoneNames = new();
    private string zoneSearch = string.Empty;

    public ConfigWindow(PluginConfiguration config, VisibilityController controller, Action<uint> openZoneSettings)
        : base("Visibility Plus (/vplus)", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.controller = controller;
        this.openZoneSettings = openZoneSettings;
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new System.Numerics.Vector2(560, 300) };
        this.LoadZones();
    }

    private void LoadZones()
    {
        try
        {
            var sheet = Service.DataManager.GetExcelSheet<TerritoryType>();
            if (sheet == null)
                return;

            foreach (var terr in sheet)
            {
                if (terr.Name.IsEmpty)
                    continue;
                string place = terr.PlaceName.ValueNullable?.Name.ToString() ?? terr.Name.ToString();
                if (string.IsNullOrWhiteSpace(place))
                    place = terr.Name.ToString();
                this.allZones.Add((terr.RowId, place));
                this.zoneNames[terr.RowId] = place;
            }

            this.allZones.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // Degraded: dropdown stays empty, chips still show raw IDs.
        }
    }

    public string ZoneName(uint id)
        => this.zoneNames.TryGetValue(id, out var name) ? name : "Unknown zone";

    /// <summary>Live position/size from the last drawn frame (Window.Position is write-only).</summary>
    public System.Numerics.Vector2 LastPosition { get; private set; }

    public System.Numerics.Vector2 LastSize { get; private set; }

    private void DrawZoneChips(uint[] selected)
    {
        foreach (uint id in selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.95f, 0.25f, 0.25f, 1f));
            if (ImGui.Button($"X##rm{id}"))
                this.controller.RemoveZone(id);
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.Text($"{id} - {this.ZoneName(id)}");
            ImGui.SameLine();
            bool custom = this.controller.IsCustomActive(id);
            if (custom)
                ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.35f, 0.95f, 0.35f, 1f));
            if (ImGuiComponents.IconButton((int)(1000 + id), FontAwesomeIcon.Cog))
                this.openZoneSettings(id);
            if (custom)
                ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(custom ? "Per-zone settings (custom ON)." : "Per-zone settings.");
        }
    }

    public override void Draw()
    {
        this.LastPosition = ImGui.GetWindowPos();
        this.LastSize = ImGui.GetWindowSize();

        bool enabled = this.config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            this.config.Enabled = enabled;
            this.config.Save();
        }
        ImGui.SameLine();
        ImGui.Text("Hotkey Mode:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(130);
        string[] modes = ["Hold", "Toggle", "Toggle 30s"];
        int modeIdx = (int)this.config.HotkeyMode;
        if (ImGui.Combo("##hotkeymode", ref modeIdx, modes, modes.Length))
        {
            this.config.HotkeyMode = (HotkeyMode)modeIdx;
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hold: group reappears while the key is held.\nToggle: press to show/hide.\nToggle 30s: press to show for 30 seconds.");

        ImGui.Separator();
        ImGui.Text("Hide in filtered zones:");
        HideGroupsUI.DrawGroups(this.config, this.config, this.controller, this.holdState, string.Empty);

        ImGui.Text("Dots for hidden players:");
        HideGroupsUI.DrawHoldKeybind("dots", ref this.config.DotsKey, ref this.config.DotsCtrl, ref this.config.DotsShift, ref this.config.DotsAlt,
            this.holdState, this.config,
            "Left-click, then press a key combo.\nWhile held, red dots mark hidden players.\nRight-click to clear.");

        ImGui.Separator();
        ImGui.Text("Apply only in these zones:");

        ImGui.InputTextWithHint("##zoneSearch", "Search zones...", ref this.zoneSearch, 128);

        string filter = this.zoneSearch.Trim().ToLowerInvariant();
        if (ImGui.BeginCombo("##zoneCombo", "Select a zone to add..."))
        {
            int shown = 0;
            foreach (var (id, name) in this.allZones)
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

                bool already = this.config.ZoneIds.Contains(id);
                if (ImGui.Selectable($"{id} - {name}", false, already ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None))
                    this.controller.AddZone(id);
            }
            ImGui.EndCombo();
        }

        var selected = this.config.ZoneIds.ToArray();
        ImGui.Text($"Selected zones ({selected.Length}):");

        if (selected.Length == 0)
        {
            ImGui.TextDisabled("None — hiding applies nowhere until you add a zone.");
        }
        else if (selected.Length > 10)
        {
            using var list = ImRaii.Child("##zonelist", new System.Numerics.Vector2(-1, ImGui.GetFrameHeightWithSpacing() * 10f), true);
            if (list.Success)
                this.DrawZoneChips(selected);
        }
        else
        {
            this.DrawZoneChips(selected);
        }

        uint current = Service.ClientState.TerritoryType;
        if (ImGui.Button("Add current zone"))
            this.controller.AddZone(current);
        ImGui.SameLine();
        ImGui.BeginDisabled(!this.config.ZoneIds.Contains(current));
        if (ImGui.Button("Clear Current"))
            this.controller.RemoveZone(current);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Remove the zone you are standing in from the filter.");

        ImGui.Separator();

        if (Service.ClientState.IsLoggedIn && Service.ObjectTable.LocalPlayer != null)
        {
            string action = this.config.Enabled && this.controller.IsFilterActiveFor(current)
                ? "Hiding active here."
                : "Idle in this zone.";
            ImGui.Text($"Current: {current} - {this.ZoneName(current)} — {action}");
        }
        else
        {
            ImGui.TextDisabled("Log in to apply visibility.");
        }

        ImGui.TextDisabled($"Hidden now: {this.controller.HiddenCount}   build {this.config.BuildTag}");
    }
}
