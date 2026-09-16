using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace VisibilityPlus;

/// <summary>Per-world hide settings inside a zone. Greyed out until enabled for the world.</summary>
public sealed class WorldWindow : Window
{
    private readonly ushort worldId;
    private readonly PluginConfiguration config;
    private readonly ZoneOverride zone;
    private readonly WorldOverride world;

    public WorldWindow(uint zoneId, ushort worldId, string worldName, PluginConfiguration config, ZoneOverride zone, WorldOverride world)
        : base($"World {worldName} (zone {zoneId})##vplusworld{zoneId}_{worldId}", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.worldId = worldId;
        this.config = config;
        this.zone = zone;
        this.world = world;
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new System.Numerics.Vector2(420, 200) };
    }

    public override void Draw()
    {
        bool use = this.world.UseCustom;
        if (ImGui.Checkbox("Enable per-world settings", ref use))
        {
            this.world.UseCustom = use;
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"On = world {this.worldId} uses the settings below.\nOff = zone settings apply.");
        ImGui.SameLine();
        if (ImGui.Button("Reset to zone defaults"))
        {
            this.world.CopyFrom(this.zone);
            this.config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copy the parent zone's settings into this world.");

        ImGui.Separator();
        ImGui.BeginDisabled(!this.world.UseCustom);
        HideGroupsUI.DrawGroups(this.world, this.config, null!, null, $"w{this.worldId}");
        ImGui.EndDisabled();
    }
}
