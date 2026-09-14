using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;

namespace VisibilityPlus;

public sealed class ConfigWindow : Window
{
    private readonly PluginConfiguration config;
    private readonly VisibilityController controller;

    private readonly List<(uint Id, string Name)> allZones = [];
    private readonly Dictionary<uint, string> zoneNames = new();
    private string zoneSearch = string.Empty;
    private string? listeningHoldSlot;

    public ConfigWindow(PluginConfiguration config, VisibilityController controller)
        : base("Visibility Plus (/vplus)", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.controller = controller;
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

    private void DrawHoldKeybind(string slotId, ref int key, ref bool ctrl, ref bool shift, ref bool alt)
    {
        ImGui.SameLine();
        if (this.listeningHoldSlot != slotId)
        {
            string bindLabel = key == 0
                ? "Set hold-key..."
                : "Hold: " + HoldKeybind.ComboName(key, ctrl, shift, alt);
            ImGui.Button(bindLabel + "##holdkey" + slotId);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                this.listeningHoldSlot = slotId;
            else if (key != 0 && ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                key = 0;
                ctrl = shift = alt = false;
                this.config.Save();
            }
            if (ImGui.IsItemHovered())
            {
                string behavior = this.config.HotkeyMode switch
                {
                    HotkeyMode.Toggle => "Press to show/hide this group.",
                    HotkeyMode.Toggle30s => "Press to show this group for 30 seconds.",
                    _ => "While held, this group reappears.",
                };
                ImGui.SetTooltip("Left-click, then press a key combo.\n" + behavior + "\nGroups may share the same key.\nRight-click to clear.");
            }
        }
        else
        {
            if (ImGui.Button("Press keys... (Esc cancels)##holdkey" + slotId))
                this.listeningHoldSlot = null;
            else if (HoldKeybind.IsDown(HoldKeybind.VK_ESCAPE))
                this.listeningHoldSlot = null;
            else if (HoldKeybind.TryCapture(out int captured, out bool c, out bool s, out bool a))
            {
                key = captured;
                ctrl = c;
                shift = s;
                alt = a;
                this.config.Save();
                this.listeningHoldSlot = null;
            }
        }
    }

    public override void Draw()
    {
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

        bool hideNpcs = this.config.HideNpcs;
        if (ImGui.Checkbox("No-Name NPCs", ref hideNpcs))
        {
            this.config.HideNpcs = hideNpcs;
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hide NPCs: quest givers, vendors, guards and other friendly non-player characters.\nEnemies and other battle NPCs are never touched.");
        ImGui.SameLine();
        ImGui.BeginDisabled(!this.config.HideNpcs);
        bool allNpcs = this.config.HideNpcs && !this.config.OnlyUnnamedNpcs;
        if (ImGui.Checkbox("All NPCs", ref allNpcs))
        {
            this.config.OnlyUnnamedNpcs = !allNpcs;
            this.config.Save();
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Also hide NPCs showing a name.\nOnly available while No-Name NPCs is on.");
        ImGui.SameLine();
        this.DrawHoldKeybind("npcs", ref this.config.HoldKey, ref this.config.HoldCtrl, ref this.config.HoldShift, ref this.config.HoldAlt);

        bool hideEnemies = this.config.HideEnemies;
        if (ImGui.Checkbox("Enemies", ref hideEnemies))
        {
            this.config.HideEnemies = hideEnemies;
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Enemy battle NPCs.\nNote: city guards share this type and hide too.");
        this.DrawHoldKeybind("enemies", ref this.config.HoldKeyEnemies, ref this.config.HoldCtrlEnemies, ref this.config.HoldShiftEnemies, ref this.config.HoldAltEnemies);

        bool hideMinions = this.config.HideMinions;
        if (ImGui.Checkbox("Minions", ref hideMinions))
        {
            this.config.HideMinions = hideMinions;
            this.config.Save();
        }
        this.DrawHoldKeybind("minions", ref this.config.HoldKeyMinions, ref this.config.HoldCtrlMinions, ref this.config.HoldShiftMinions, ref this.config.HoldAltMinions);

        bool hidePets = this.config.HidePets;
        if (ImGui.Checkbox("Pets", ref hidePets))
        {
            this.config.HidePets = hidePets;
            this.config.Save();
        }
        this.DrawHoldKeybind("pets", ref this.config.HoldKeyPets, ref this.config.HoldCtrlPets, ref this.config.HoldShiftPets, ref this.config.HoldAltPets);

        bool hideChocobos = this.config.HideChocobos;
        if (ImGui.Checkbox("Chocobos", ref hideChocobos))
        {
            this.config.HideChocobos = hideChocobos;
            this.config.Save();
        }
        this.DrawHoldKeybind("chocobos", ref this.config.HoldKeyChocobos, ref this.config.HoldCtrlChocobos, ref this.config.HoldShiftChocobos, ref this.config.HoldAltChocobos);

        bool hidePlayers = this.config.HideNonSyncedPlayers;
        if (ImGui.Checkbox("Non-Synced Players", ref hidePlayers))
        {
            this.config.HideNonSyncedPlayers = hidePlayers;
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hide every player none of Snowcloak, PSync or Lightless is syncing.\nYourself is never hidden.\nNeeds one of them running.");
        ImGui.SameLine();
        ImGui.BeginDisabled(!this.config.HideNonSyncedPlayers);
        bool allPlayers = this.config.HideNonSyncedPlayers && this.config.HideAllPlayers;
        if (ImGui.Checkbox("All Players", ref allPlayers))
        {
            this.config.HideAllPlayers = allPlayers;
            this.config.Save();
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Also hide synced players.\nOnly available while Non-Synced Players is on.");
        this.DrawHoldKeybind("players", ref this.config.HoldKeyPlayers, ref this.config.HoldCtrlPlayers, ref this.config.HoldShiftPlayers, ref this.config.HoldAltPlayers);

        ImGui.TextDisabled(this.controller.SyncStatus);
        ImGui.TextDisabled("Your own minion, pet and chocobo are never hidden.");

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
        else
        {
            foreach (uint id in selected)
            {
                if (ImGui.Button($"X##rm{id}"))
                    this.controller.RemoveZone(id);
                ImGui.SameLine();
                ImGui.Text($"{id} - {this.ZoneName(id)}");
            }
        }

        uint current = Service.ClientState.TerritoryType;
        if (ImGui.Button("Add current zone"))
            this.controller.AddZone(current);
        ImGui.SameLine();
        if (ImGui.Button("Clear all"))
            this.controller.ClearZones();

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
