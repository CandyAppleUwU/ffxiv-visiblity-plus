using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace VisibilityPlus;

/// <summary>Per-zone hide settings. Opens greyed out until enabled for the zone.</summary>
public sealed class ZoneWindow : Window
{
    private readonly uint zoneId;
    private readonly PluginConfiguration config;
    private readonly ZoneOverride zone;

    public ZoneWindow(uint zoneId, string zoneName, PluginConfiguration config, VisibilityController controller)
        : base($"Zone {zoneId} - {zoneName}##vpluszone{zoneId}", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.zoneId = zoneId;
        this.config = config;
        this.zone = controller.GetOrCreateOverride(zoneId);
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new System.Numerics.Vector2(420, 200) };
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
        ImGui.EndDisabled();
    }
}
