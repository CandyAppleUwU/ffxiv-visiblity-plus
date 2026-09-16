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
    private string voidSearch = string.Empty;
    private string whiteSearch = string.Empty;

    public ConfigWindow(PluginConfiguration config, VisibilityController controller, Action<uint> openZoneSettings)
        : base("Visibility Plus (/vplus)", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.controller = controller;
        this.openZoneSettings = openZoneSettings;
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new System.Numerics.Vector2(336, 300) };
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

    private readonly Dictionary<ushort, string> worldNames = [];

    private string VoidWorldName(ushort worldId)
    {
        if (!this.worldNames.TryGetValue(worldId, out var name))
        {
            try
            {
                name = Service.DataManager.GetExcelSheet<World>()?.GetRow(worldId).Name.ToString();
            }
            catch
            {
                name = null;
            }
            if (string.IsNullOrEmpty(name))
                name = worldId.ToString();
            this.worldNames[worldId] = name;
        }
        return name;
    }

    private void DrawHoldKeybindVoid()
    {
        string hint = this.config.VoidOnMouseHover
            ? "Left-click, then press a key combo.\nHold 3s on the hovered player to voidlist them.\nRight-click to clear."
            : "Left-click, then press a key combo.\nHold 3s on a targeted player to voidlist them.\nRight-click to clear.";
        HideGroupsUI.DrawHoldKeybind("voidadd", ref this.config.VoidKey, ref this.config.VoidCtrl, ref this.config.VoidShift, ref this.config.VoidAlt,
            this.holdState, this.config, hint);
    }

    private void DrawHoldKeybindUltimate()
    {
        const string hint = "Hold this combo to arm left-mouse drag-select.\nWhile held, left-drag a box: everyone inside is voidlisted on release.\nRight-click while dragging to cancel.\nRight-click the button to clear.";
        HideGroupsUI.DrawHoldKeybind("voidultimate", ref this.config.VoidUltimateKey, ref this.config.VoidUltimateCtrl, ref this.config.VoidUltimateShift, ref this.config.VoidUltimateAlt,
            this.holdState, this.config, hint);
    }

    private void DrawHoldKeybindWhite()
    {
        string hint = this.config.WhiteOnMouseHover
            ? "Left-click, then press a key combo.\nHold 3s on the hovered player to whitelist them.\nRight-click to clear."
            : "Left-click, then press a key combo.\nHold 3s on a targeted player to whitelist them.\nRight-click to clear.";
        HideGroupsUI.DrawHoldKeybind("whiteadd", ref this.config.WhiteKey, ref this.config.WhiteCtrl, ref this.config.WhiteShift, ref this.config.WhiteAlt,
            this.holdState, this.config, hint);
    }

    private string WhiteWorldName(ushort worldId) => this.VoidWorldName(worldId);

    /// <summary>Live position/size from the last drawn frame (Window.Position is write-only).</summary>
    public System.Numerics.Vector2 LastPosition { get; private set; }

    public System.Numerics.Vector2 LastSize { get; private set; }

    private void DrawZoneChips(uint[] selected)
    {
        uint current = 0;
        try { current = Service.ClientState.TerritoryType; } catch { }
        foreach (uint id in selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.95f, 0.25f, 0.25f, 1f));
            if (ImGui.Button($"X##rm{id}"))
                this.controller.RemoveZone(id);
            ImGui.PopStyleColor();
            ImGui.SameLine();
            bool isCurrent = id == current && current != 0;
            if (isCurrent)
                ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.00f, 1.00f, 1.00f, 1f));
            ImGui.Text($"{id} - {this.ZoneName(id)}");
            if (isCurrent)
                ImGui.PopStyleColor();
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

        // Enabled toggle keybind on left of Enabled (says Set Key / shows key)
        if (this.holdState.ListeningSlot == "enabledToggle")
        {
            if (ImGui.Button("Press keys... (Esc cancels)##enabledToggle"))
                this.holdState.ListeningSlot = null;
            else if (HoldKeybind.IsDown(HoldKeybind.VK_ESCAPE))
                this.holdState.ListeningSlot = null;
            else if (HoldKeybind.TryCapture(out int cap, out bool c, out bool s, out bool a))
            {
                this.config.EnabledToggleKey = cap;
                this.config.EnabledToggleCtrl = c;
                this.config.EnabledToggleShift = s;
                this.config.EnabledToggleAlt = a;
                this.config.Save();
                this.holdState.ListeningSlot = null;
            }
        }
        else
        {
            string label = this.config.EnabledToggleKey == 0 ? "Set Key" : HoldKeybind.ComboName(this.config.EnabledToggleKey, this.config.EnabledToggleCtrl, this.config.EnabledToggleShift, this.config.EnabledToggleAlt);
            if (ImGui.Button(label + "##enabledToggle"))
                this.holdState.ListeningSlot = "enabledToggle";
            else if (this.config.EnabledToggleKey != 0 && ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                this.config.EnabledToggleKey = 0;
                this.config.EnabledToggleCtrl = this.config.EnabledToggleShift = this.config.EnabledToggleAlt = false;
                this.config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Left-click to set a key that toggles Enabled.\nRight-click to clear.");
        }
        ImGui.SameLine();
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

        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.95f, 0.25f, 0.25f, 1f));
        if (ImGuiComponents.IconButton(7, FontAwesomeIcon.Cog))
            ImGui.OpenPopup("voidlist");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show voidlisted players.");
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(420, 0), ImGuiCond.Always);
        if (ImGui.BeginPopup("voidlist"))
        {
            ImGui.Text("Voidlisted players:");
            // Search bar -- always present
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##voidSearch", "Search name or world...", ref this.voidSearch, 64);
            string vFilter = this.voidSearch.Trim().ToLowerInvariant();
            bool hasFilter = vFilter.Length > 0;

            if (this.config.VoidList.Count == 0)
            {
                ImGui.TextDisabled("Empty. Hold the bind key 3s on a targeted player, or right-click them.");
            }
            else
            {
                // Build filtered index list (reverse order: newest first)
                var indices = new List<int>();
                for (int i = this.config.VoidList.Count - 1; i >= 0; i--)
                {
                    if (!hasFilter)
                    {
                        indices.Add(i);
                        continue;
                    }
                    var e = this.config.VoidList[i];
                    string worldName = this.VoidWorldName(e.World).ToLowerInvariant();
                    string nameLower = e.Name.ToLowerInvariant();
                    string combined = $"{nameLower}@{worldName}";
                    if (nameLower.Contains(vFilter, StringComparison.Ordinal)
                        || worldName.Contains(vFilter, StringComparison.Ordinal)
                        || combined.Contains(vFilter, StringComparison.Ordinal)
                        || e.World.ToString().Contains(vFilter, StringComparison.Ordinal))
                        indices.Add(i);
                }

                if (indices.Count == 0)
                {
                    ImGui.TextDisabled(hasFilter ? "No matches." : "Empty.");
                }
                else
                {
                    bool needScroll = indices.Count > 15;
                    if (needScroll)
                    {
                        ImGui.TextDisabled($"{indices.Count} entries");
                        {
                            using var child = ImRaii.Child("##voidlistScroll", new System.Numerics.Vector2(-1, ImGui.GetFrameHeightWithSpacing() * 15f), true);
                            if (child.Success)
                            {
                                foreach (int i in indices)
                                {
                                    var entry = this.config.VoidList[i];
                                    ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.95f, 0.25f, 0.25f, 1f));
                                    if (ImGui.Button($"X##void{i}"))
                                        this.controller.RemoveVoidAt(i);
                                    ImGui.PopStyleColor();
                                    ImGui.SameLine();
                                    ImGui.Text($"{entry.Name}@{this.VoidWorldName(entry.World)}");
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (int i in indices)
                        {
                            var entry = this.config.VoidList[i];
                            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.95f, 0.25f, 0.25f, 1f));
                            if (ImGui.Button($"X##void{i}"))
                                this.controller.RemoveVoidAt(i);
                            ImGui.PopStyleColor();
                            ImGui.SameLine();
                            ImGui.Text($"{entry.Name}@{this.VoidWorldName(entry.World)}");
                        }
                        if (hasFilter)
                            ImGui.TextDisabled($"{indices.Count}/{this.config.VoidList.Count} shown.");
                    }
                }
            }
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        bool voidOn = this.config.VoidEnabled;
        if (ImGui.Checkbox("VoidList", ref voidOn))
        {
            this.config.VoidEnabled = voidOn;
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Listed players are always hidden, in every zone.\nNo exemptions apply.");
        this.DrawHoldKeybindVoid();
        bool hover = this.config.VoidOnMouseHover;
        if (ImGui.Checkbox("Void on Mouse Hover", ref hover))
        {
            this.config.VoidOnMouseHover = hover;
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When enabled, the hold keybind voids the player under your mouse cursor\ninstead of your hard target. Hover the nameplate/model, hold the bind.");
        bool ultOn = this.config.VoidUltimateEnabled;
        if (ImGui.Checkbox("Void Ultimate", ref ultOn))
        {
            this.config.VoidUltimateEnabled = ultOn;
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Drag select an area to voidlist multiple targets at the same time.");
        this.DrawHoldKeybindUltimate();

        // WhiteList - same concept as Void but never hidden, green hold rect, no VFX
        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.25f, 0.85f, 0.25f, 1f));
        if (ImGuiComponents.IconButton(8, FontAwesomeIcon.Cog))
            ImGui.OpenPopup("whitelist");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show whitelisted players.");
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(420, 0), ImGuiCond.Always);
        if (ImGui.BeginPopup("whitelist"))
        {
            ImGui.Text("Whitelisted players:");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##whiteSearch", "Search name or world...", ref this.whiteSearch, 64);
            string wFilter = this.whiteSearch.Trim().ToLowerInvariant();
            bool wHasFilter = wFilter.Length > 0;

            if (this.config.WhiteList.Count == 0)
            {
                ImGui.TextDisabled("Empty. Hold the bind key 3s on a targeted player, or right-click a blue dot.");
            }
            else
            {
                var wIndices = new List<int>();
                for (int i = this.config.WhiteList.Count - 1; i >= 0; i--)
                {
                    if (!wHasFilter)
                    {
                        wIndices.Add(i);
                        continue;
                    }
                    var e = this.config.WhiteList[i];
                    string worldName = this.WhiteWorldName(e.World).ToLowerInvariant();
                    string nameLower = e.Name.ToLowerInvariant();
                    string combined = $"{nameLower}@{worldName}";
                    if (nameLower.Contains(wFilter, StringComparison.Ordinal)
                        || worldName.Contains(wFilter, StringComparison.Ordinal)
                        || combined.Contains(wFilter, StringComparison.Ordinal)
                        || e.World.ToString().Contains(wFilter, StringComparison.Ordinal))
                        wIndices.Add(i);
                }

                if (wIndices.Count == 0)
                {
                    ImGui.TextDisabled(wHasFilter ? "No matches." : "Empty.");
                }
                else
                {
                    bool wNeedScroll = wIndices.Count > 15;
                    if (wNeedScroll)
                    {
                        ImGui.TextDisabled($"{wIndices.Count} entries");
                        {
                            using var child = ImRaii.Child("##whitelistScroll", new System.Numerics.Vector2(-1, ImGui.GetFrameHeightWithSpacing() * 15f), true);
                            if (child.Success)
                            {
                                foreach (int i in wIndices)
                                {
                                    var entry = this.config.WhiteList[i];
                                    ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.25f, 0.85f, 0.25f, 1f));
                                    if (ImGui.Button($"X##white{i}"))
                                        this.controller.RemoveWhiteAt(i);
                                    ImGui.PopStyleColor();
                                    ImGui.SameLine();
                                    ImGui.Text($"{entry.Name}@{this.WhiteWorldName(entry.World)}");
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (int i in wIndices)
                        {
                            var entry = this.config.WhiteList[i];
                            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.25f, 0.85f, 0.25f, 1f));
                            if (ImGui.Button($"X##white{i}"))
                                this.controller.RemoveWhiteAt(i);
                            ImGui.PopStyleColor();
                            ImGui.SameLine();
                            ImGui.Text($"{entry.Name}@{this.WhiteWorldName(entry.World)}");
                        }
                        if (wHasFilter)
                            ImGui.TextDisabled($"{wIndices.Count}/{this.config.WhiteList.Count} shown.");
                    }
                }
            }
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        bool whiteOn = this.config.WhiteEnabled;
        if (ImGui.Checkbox("WhiteList", ref whiteOn))
        {
            this.config.WhiteEnabled = whiteOn;
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Listed players are never hidden, in any zone.\nTakes priority over void and hide filters.");
        this.DrawHoldKeybindWhite();
        bool whiteHover = this.config.WhiteOnMouseHover;
        if (ImGui.Checkbox("White on Mouse Hover", ref whiteHover))
        {
            this.config.WhiteOnMouseHover = whiteHover;
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When enabled, the hold keybind whitelists the player under your mouse cursor\ninstead of your hard target. Hover the nameplate/model, hold the bind.");

        ImGui.Text("Dots for hidden players:");
        HideGroupsUI.DrawHoldKeybind("dots", ref this.config.DotsKey, ref this.config.DotsCtrl, ref this.config.DotsShift, ref this.config.DotsAlt,
            this.holdState, this.config);

        ImGui.Separator();
        ImGui.Text("Apply only in these zones:");

        var selected = this.config.ZoneIds.ToArray();
        if (ImGui.BeginCombo("##zoneCombo", "Select a zone to add..."))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##zoneSearch", "Search zones...", ref this.zoneSearch, 128);
            string filter = this.zoneSearch.Trim().ToLowerInvariant();
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

        ImGui.TextDisabled($"Hidden now: {this.controller.HiddenCount}");
    }
}
