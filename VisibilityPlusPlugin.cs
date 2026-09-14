using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace VisibilityPlus;

public sealed class VisibilityPlusPlugin : IDalamudPlugin
{
    public string Name => "Visibility Plus";

    public const string BuildTag = "0.1.0.18";

    private const string Command = "/vplus";

    private readonly WindowSystem windowSystem = new("VisibilityPlus");
    private readonly ConfigWindow configWindow;
    private readonly Dictionary<uint, ZoneWindow> zoneWindows = [];
    private readonly VisibilityController controller;
    private readonly PluginConfiguration config;

    public VisibilityPlusPlugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();

        this.config = Service.PluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        if (this.config.BuildTag != BuildTag)
        {
            this.config.BuildTag = BuildTag;
            this.config.Save();
        }
        this.controller = new VisibilityController(this.config);
        this.configWindow = new ConfigWindow(this.config, this.controller, this.OpenZoneSettings);

        this.windowSystem.AddWindow(this.configWindow);

        Service.PluginInterface.UiBuilder.Draw += this.DrawUi;
        Service.PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfig;
        Service.Framework.Update += this.controller.OnUpdate;

        Service.CommandManager.AddHandler(Command, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open settings. /vplus on|off enables/disables. /vplus target inspects your target.",
        });
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "on":
                this.config.Enabled = true;
                this.config.Save();
                break;
            case "off":
                this.config.Enabled = false;
                this.config.Save();
                this.controller.ShowAll();
                break;
            case "target":
                this.DumpTarget();
                break;
            case "":
                this.OpenConfig();
                break;
            default:
                Service.ChatGui.PrintError($"[Visibility Plus] Unknown argument \"{args}\". Use /vplus, /vplus on, /vplus off, /vplus target.");
                break;
        }
    }

    /// <summary>One-shot inspector: prints what kind of object you have targeted.</summary>
    private unsafe void DumpTarget()
    {
        try
        {
            var target = Service.TargetManager.Target ?? Service.TargetManager.MouseOverTarget;
            if (target == null)
            {
                Service.ChatGui.PrintError("[Visibility Plus] No target. Target the NPC (or hover it), then run /vplus target.");
                return;
            }

            var go = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address;
            var chr = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)target.Address;
            byte kind = (byte)go->ObjectKind;
            byte sub = go->SubKind;
            string kindName;
            string subName;
            try
            {
                kindName = ((Dalamud.Game.ClientState.Objects.Enums.ObjectKind)kind).ToString();
            }
            catch
            {
                kindName = "???";
            }
            try
            {
                subName = ((FFXIVClientStructs.FFXIV.Client.Game.Object.BattleNpcSubKind)sub).ToString();
            }
            catch
            {
                subName = "???";
            }

            uint zone = Service.ClientState.TerritoryType;
            string group = VisibilityController.ClassifyGroup(kind, sub);
            if (kind == (byte)Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)
                group = this.config.HideNonSyncedPlayers && this.config.HideAllPlayers
                    ? "Players (all hidden)"
                    : this.controller.IsSynced(target.Address) ? "Synced player (kept)" : "Non-Synced Players";
            string line1 = $"[V+] Target: {target.Name}  id={target.EntityId} (0x{target.EntityId:X})";
            string line2 = $"[V+] Kind: {kindName} ({kind})  sub={subName} ({sub})  wrapper={target.GetType().Name}  nameId={chr->NameId}  named={VisibilityController.HasName(go)}  lvl={chr->Level}  icon={go->NamePlateIconId}";
            string line3 = $"[V+] owner={go->OwnerId}  companionOwner={chr->CompanionOwnerId}  flags={go->RenderFlags}  alpha={chr->Alpha}";
            string line4 = $"[V+] Group: {group}  hiddenByPlugin={this.controller.IsHiddenByPlugin(target.Address)}  zone={zone} (filtered={this.controller.IsFilterActiveFor(zone)} custom={this.controller.IsCustomActive(zone)})";
            Service.ChatGui.Print(line1);
            Service.ChatGui.Print(line2);
            Service.ChatGui.Print(line3);
            Service.ChatGui.Print(line4);
            string line5 = $"[V+] build={BuildTag} slot={this.controller.FindSlotIndex(target.Address)} enabled={this.config.Enabled} hideNpcs={this.config.HideNpcs} hiddenCount={this.controller.HiddenCount}";
            Service.ChatGui.Print(line5);
            Service.PluginLog.Warning("Target dump: " + line1 + " | " + line2 + " | " + line3 + " | " + line4 + " | " + line5);
        }
        catch (System.Exception ex)
        {
            Service.ChatGui.PrintError("[Visibility Plus] Target inspect failed: " + ex.Message);
        }
    }

    private void DrawUi()
    {
        this.windowSystem.Draw();
        this.DrawDots();
    }

    /// <summary>Red dots at hidden players while the dots key is held. Overlay only.</summary>
    private void DrawDots()
    {
        if (!this.config.Enabled || this.config.DotsKey == 0)
            return;
        if (!HoldKeybind.IsDown(this.config.DotsKey))
            return;
        if (this.config.DotsCtrl && !HoldKeybind.IsDown(HoldKeybind.VK_CONTROL))
            return;
        if (this.config.DotsShift && !HoldKeybind.IsDown(HoldKeybind.VK_SHIFT))
            return;
        if (this.config.DotsAlt && !HoldKeybind.IsDown(HoldKeybind.VK_MENU))
            return;
        if (ImGui.GetIO().WantTextInput)
            return;

        var points = this.controller.GetDotPositions();
        if (points.Length == 0)
            return;
        var drawList = ImGui.GetBackgroundDrawList();
        foreach (var p in points)
        {
            if (!Service.GameGui.WorldToScreen(p, out var screen))
                continue;
            drawList.AddCircleFilled(screen, 6f, 0xFF0000FF);
            drawList.AddCircle(screen, 6f, 0xFF000000, 16, 1.5f);
        }
    }

    private void OpenConfig() => this.configWindow.IsOpen = true;

    private void OpenZoneSettings(uint territoryType)
    {
        if (!this.zoneWindows.TryGetValue(territoryType, out var wnd))
        {
            wnd = new ZoneWindow(territoryType, this.configWindow.ZoneName(territoryType), this.config, this.controller);
            this.zoneWindows[territoryType] = wnd;
            this.windowSystem.AddWindow(wnd);
        }
        // Anchor next to the main window on every open: ImGui's ini memory
        // would otherwise pin first-opened windows top-left forever.
        // (Window.Position is write-only, so anchor from the main window's
        // live drawn position, not its Position property.)
        var mainPos = this.configWindow.LastPosition;
        var mainSize = this.configWindow.LastSize;
        if (mainPos != default && mainSize != default)
        {
            float step = 28f * (this.zoneWindows.Count % 5);
            wnd.Position = new System.Numerics.Vector2(mainPos.X + mainSize.X + 12f + step, mainPos.Y + step);
            wnd.PositionCondition = ImGuiCond.Appearing;
        }
        wnd.IsOpen = true;
    }

    public void Dispose()
    {
        Service.CommandManager.RemoveHandler(Command);
        Service.Framework.Update -= this.controller.OnUpdate;
        Service.PluginInterface.UiBuilder.Draw -= this.DrawUi;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfig;
        this.windowSystem.RemoveAllWindows();
        this.controller.Dispose();
        GC.SuppressFinalize(this);
    }
}
