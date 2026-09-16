using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace VisibilityPlus;

public sealed class VisibilityPlusPlugin : IDalamudPlugin
{
    public string Name => "Visibility Plus";

    public const string BuildTag = "0.1.0.23";

    private const string Command = "/vplus";

    private readonly WindowSystem windowSystem = new("VisibilityPlus");
    private readonly ConfigWindow configWindow;
    private readonly Dictionary<uint, ZoneWindow> zoneWindows = [];
    private readonly Dictionary<(uint Zone, ushort World), WorldWindow> worldWindows = [];
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
        Service.Framework.Update += this.UpdateVoidPending;
        Service.ContextMenu.OnMenuOpened += this.OnContextMenuOpened;

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
            {
                if (this.controller.IsVoidlisted(VisibilityController.BuildVoidKey(go)))
                    group = "Voidlisted (hidden)";
                else if (this.config.HideNonSyncedPlayers && this.config.HideAllPlayers)
                    group = "Players (all hidden)";
                else
                    group = this.controller.IsSynced(target.Address) ? "Synced player (kept)" : "Non-Synced Players";
            }
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
        this.DrawVoidAdd();
        this.DrawWhiteAdd();
        this.DrawVoidUltimate();
    }

    private long voidHoldStart;
    private uint voidHoldTarget;
    private bool voidNeedRelease;

    // Pending voidlist VFX sequence: chain 0-2.6s, sound at 2.4s, burst at 2.5s, chain stop 2.6s, hide 2.8s.
    private const string ChainVfxPath = "vfx/common/eff/m0731_stloop_chain.avfx";
    private const string BurstVfxPath = "vfx/aoz/mgc_rod056/eff/mgc_rod056t0n1.avfx";

    private sealed class PendingVoid
    {
        public string Name = string.Empty;
        public ushort World;
        public long StartTick;
        public nint TargetAddress;
        public uint TargetEntityId;
        public nint ChainVfxPtr;
        public bool SoundPlayed;
        public bool SecondSpawned;
        public bool ChainStopped;
    }

    private readonly List<PendingVoid> pendingVoids = [];
    private readonly object pendingLock = new();

    private string? pendingUnvoidName;
    private ushort pendingUnvoidWorld;
    private System.Numerics.Vector2 pendingUnvoidPos;
    private bool prevRightDown;

    // WhiteList hold (green rect, no VFX, instant)
    private long whiteHoldStart;
    private uint whiteHoldTarget;
    private bool whiteNeedRelease;
    private string? pendingWhiteName;
    private ushort pendingWhiteWorld;
    private System.Numerics.Vector2 pendingWhitePos;

    // Void Ultimate drag-select: hold bind, paint a rubber-band box, release to
    // voidlist everyone inside. Right-click while dragging cancels.
    private const string UltimateVfxPath = "vfx/monster/c0901/eff/c0901sp_07t0m.avfx";
    private const uint VK_RBUTTON = 0x02;

    private sealed class UltTarget
    {
        public string Name = string.Empty;
        public ushort World;
        public nint Address;
        public uint EntityId;
        public System.Numerics.Vector2 Screen;
    }

    private sealed class PendingUltimate
    {
        public List<(string Name, ushort World)> Targets = [];
        public long DueTick;
    }

    private readonly List<PendingUltimate> pendingUltimates = [];
    private readonly List<UltTarget> ultSelected = [];
    private bool ultDragging;
    private bool ultCancelled;
    private System.Numerics.Vector2 ultAnchor;

    /// <summary>Hold the void bind 3s on a targeted player to voidlist them, with progress square.</summary>
    private void DrawVoidAdd()
    {
        bool down = this.config.VoidEnabled && this.config.VoidKey != 0
            && HoldKeybind.IsDown(this.config.VoidKey)
            && (!this.config.VoidCtrl || HoldKeybind.IsDown(HoldKeybind.VK_CONTROL))
            && (!this.config.VoidShift || HoldKeybind.IsDown(HoldKeybind.VK_SHIFT))
            && (!this.config.VoidAlt || HoldKeybind.IsDown(HoldKeybind.VK_MENU));
        if (!down || ImGui.GetIO().WantTextInput)
        {
            this.voidHoldStart = 0;
            if (!down)
            {
                this.voidNeedRelease = false;
                this.voidHoldTarget = 0; // forget target so re-holding restarts the 3s
            }
            return;
        }
        if (this.voidNeedRelease)
            return; // added already: release the key before adding another

        var local = Service.ObjectTable.LocalPlayer;
        IPlayerCharacter? target = this.config.VoidOnMouseHover
            ? Service.TargetManager.MouseOverTarget as IPlayerCharacter
            : Service.TargetManager.Target as IPlayerCharacter;
        if (target == null || local == null || target.Address == local.Address)
        {
            this.voidHoldStart = 0;
            return;
        }
        if (this.IsAlreadyHidden(target.Address))
        {
            this.voidHoldStart = 0; // already hidden: nothing to void
            return;
        }
        if (target.EntityId != this.voidHoldTarget)
        {
            this.voidHoldTarget = target.EntityId;
            this.voidHoldStart = Environment.TickCount64;
        }

        const long showAfterMs = 500;
        const long needMs = 3000;
        long held = Environment.TickCount64 - this.voidHoldStart;
        if (held < showAfterMs)
            return; // square appears after 0.5s; total 3.5s to add
        float frac = Math.Min(1f, (held - showAfterMs) / (float)needMs);

        var feet = target.Position;
        var head = new System.Numerics.Vector3(feet.X, feet.Y + 2f, feet.Z);
        if (Service.GameGui.WorldToScreen(feet, out var feetScreen)
            && Service.GameGui.WorldToScreen(head, out var headScreen))
        {
            float heightPx = System.Math.Abs(feetScreen.Y - headScreen.Y);
            if (heightPx >= 4f && heightPx <= 10000f)
            {
                float widthPx = heightPx * 0.6f;
                if (widthPx >= 2f && widthPx <= 10000f)
                {
                    float left = feetScreen.X - widthPx * 0.5f;
                    float top = feetScreen.Y - heightPx;
                    var dl = ImGui.GetBackgroundDrawList();
                    dl.AddRect(new System.Numerics.Vector2(left, top), new System.Numerics.Vector2(left + widthPx, feetScreen.Y), 0xFF0000FF, 0f, ImDrawFlags.None, 2f);
                    if (frac > 0f)
                        dl.AddRectFilled(new System.Numerics.Vector2(left, top), new System.Numerics.Vector2(left + widthPx * frac, feetScreen.Y), 0x640000FF);
                }
            }
        }

        if (held >= showAfterMs + needMs)
        {
            this.AddVoidFromPlayer(target.Name.TextValue, (ushort)target.HomeWorld.RowId, target.Address, target.EntityId);
            this.voidNeedRelease = true;
            this.voidHoldStart = 0;
        }
    }

    /// <summary>Hold the white bind 3s on a targeted player to whitelist them, with green progress square. No VFX.</summary>
    private void DrawWhiteAdd()
    {
        bool down = this.config.WhiteEnabled && this.config.WhiteKey != 0
            && HoldKeybind.IsDown(this.config.WhiteKey)
            && (!this.config.WhiteCtrl || HoldKeybind.IsDown(HoldKeybind.VK_CONTROL))
            && (!this.config.WhiteShift || HoldKeybind.IsDown(HoldKeybind.VK_SHIFT))
            && (!this.config.WhiteAlt || HoldKeybind.IsDown(HoldKeybind.VK_MENU));
        if (!down || ImGui.GetIO().WantTextInput)
        {
            this.whiteHoldStart = 0;
            if (!down)
            {
                this.whiteNeedRelease = false;
                this.whiteHoldTarget = 0;
            }
            return;
        }
        if (this.whiteNeedRelease)
            return;

        var local = Service.ObjectTable.LocalPlayer;
        IPlayerCharacter? target = this.config.WhiteOnMouseHover
            ? Service.TargetManager.MouseOverTarget as IPlayerCharacter
            : Service.TargetManager.Target as IPlayerCharacter;
        if (target == null || local == null || target.Address == local.Address)
        {
            this.whiteHoldStart = 0;
            return;
        }
        if (target.EntityId != this.whiteHoldTarget)
        {
            this.whiteHoldTarget = target.EntityId;
            this.whiteHoldStart = Environment.TickCount64;
        }

        const long showAfterMs = 500;
        const long needMs = 3000;
        long held = Environment.TickCount64 - this.whiteHoldStart;
        if (held < showAfterMs)
            return;
        float frac = Math.Min(1f, (held - showAfterMs) / (float)needMs);

        var feet = target.Position;
        var head = new System.Numerics.Vector3(feet.X, feet.Y + 2f, feet.Z);
        if (Service.GameGui.WorldToScreen(feet, out var feetScreen)
            && Service.GameGui.WorldToScreen(head, out var headScreen))
        {
            float heightPx = System.Math.Abs(feetScreen.Y - headScreen.Y);
            if (heightPx >= 4f && heightPx <= 10000f)
            {
                float widthPx = heightPx * 0.6f;
                if (widthPx >= 2f && widthPx <= 10000f)
                {
                    float left = feetScreen.X - widthPx * 0.5f;
                    float top = feetScreen.Y - heightPx;
                    var dl = ImGui.GetBackgroundDrawList();
                    dl.AddRect(new System.Numerics.Vector2(left, top), new System.Numerics.Vector2(left + widthPx, feetScreen.Y), 0xFF00FF00, 0f, ImDrawFlags.None, 2f);
                    if (frac > 0f)
                        dl.AddRectFilled(new System.Numerics.Vector2(left, top), new System.Numerics.Vector2(left + widthPx * frac, feetScreen.Y), 0x6400FF00);
                }
            }
        }

        if (held >= showAfterMs + needMs)
        {
            this.AddWhiteFromPlayer(target.Name.TextValue, (ushort)target.HomeWorld.RowId);
            this.whiteNeedRelease = true;
            this.whiteHoldStart = 0;
        }
    }

    /// <summary>Void Ultimate drag-select: hold the bind to arm left-mouse, then
    /// left-drag a rubber-band box. Red boxes mark players inside. Releasing the
    /// left button VFX + voidlists them after 200ms. Right-click cancels.</summary>
    private void DrawVoidUltimate()
    {
        // The bind only arms; the left mouse button does the selecting.
        bool armed = this.config.VoidUltimateEnabled && this.config.VoidUltimateKey != 0
            && HoldKeybind.IsDown(this.config.VoidUltimateKey)
            && (!this.config.VoidUltimateCtrl || HoldKeybind.IsDown(HoldKeybind.VK_CONTROL))
            && (!this.config.VoidUltimateShift || HoldKeybind.IsDown(HoldKeybind.VK_SHIFT))
            && (!this.config.VoidUltimateAlt || HoldKeybind.IsDown(HoldKeybind.VK_MENU));
        bool typing = ImGui.GetIO().WantTextInput;
        if (!armed || typing)
        {
            // Arming lost mid-drag (or typing started): discard, never confirm.
            if (this.ultDragging)
            {
                this.ultDragging = false;
                this.ultCancelled = false;
                this.ultSelected.Clear();
            }
            return;
        }

        // Fullscreen invisible overlay while armed: the active drag button makes
        // ImGui capture the mouse, so the left-drag paints the rubber band
        // instead of rotating the game camera / retargeting.
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(System.Numerics.Vector2.Zero);
        ImGui.SetNextWindowSize(io.DisplaySize);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new System.Numerics.Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.Border, new System.Numerics.Vector4(0f, 0f, 0f, 0f));
        const ImGuiWindowFlags overlayFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoNavInputs;
        ImGui.Begin("##voidUltOverlay", overlayFlags);
        ImGui.InvisibleButton("##voidUltDrag", io.DisplaySize);

        var mouse = ImGui.GetMousePos();
        bool leftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        if (!this.ultDragging && !leftDown)
        {
            ImGui.GetBackgroundDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(), mouse + new System.Numerics.Vector2(14, 14), 0xFF0000FF, "Void Ultimate: drag to select");
        }
        else
        {
            if (!this.ultDragging)
            {
                this.ultDragging = true;
                this.ultCancelled = false;
                this.ultAnchor = mouse;
                this.ultSelected.Clear();
            }
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) || HoldKeybind.IsDown((int)VK_RBUTTON))
                this.ultCancelled = true; // stays cancelled until the drag ends
            if (this.ultCancelled)
            {
                ImGui.GetBackgroundDrawList().AddRect(this.ultAnchor, mouse, 0xAA888888, 0f, ImDrawFlags.None, 1.5f);
                if (!leftDown)
                {
                    // Left released after cancel: discard.
                    this.ultDragging = false;
                    this.ultCancelled = false;
                    this.ultSelected.Clear();
                }
            }
            else if (!leftDown)
            {
                // Left released: confirm the drag.
                var done = new List<UltTarget>(this.ultSelected);
                this.ultDragging = false;
                this.ultCancelled = false;
                this.ultSelected.Clear();
                if (done.Count > 0)
                    this.ConfirmVoidUltimate(done);
            }
            else
            {
                float x0 = System.Math.Min(this.ultAnchor.X, mouse.X);
                float x1 = System.Math.Max(this.ultAnchor.X, mouse.X);
                float y0 = System.Math.Min(this.ultAnchor.Y, mouse.Y);
                float y1 = System.Math.Max(this.ultAnchor.Y, mouse.Y);

                var local = Service.ObjectTable.LocalPlayer;
                this.ultSelected.Clear();
                if (x1 - x0 < 8f && y1 - y0 < 8f)
                {
                    // Click without a drag: pick the closest player to the cursor.
                    UltTarget? best = null;
                    float bestDist = 28f;
                    foreach (var obj in Service.ObjectTable)
                    {
                        if (obj is not IPlayerCharacter pc || local == null || pc.Address == local.Address)
                            continue;
                        if (this.IsAlreadyHidden(pc.Address))
                            continue; // already hidden: nothing to void
                        if (!Service.GameGui.WorldToScreen(pc.Position, out var screen))
                            continue;
                        float dist = (mouse - screen).Length();
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = new UltTarget { Name = pc.Name.TextValue, World = (ushort)pc.HomeWorld.RowId, Address = pc.Address, EntityId = pc.EntityId, Screen = screen };
                        }
                    }
                    if (best != null)
                        this.ultSelected.Add(best);
                }
                else
                {
                    foreach (var obj in Service.ObjectTable)
                    {
                        if (obj is not IPlayerCharacter pc || local == null || pc.Address == local.Address)
                            continue;
                        if (this.IsAlreadyHidden(pc.Address))
                            continue; // already hidden: nothing to void
                        if (!Service.GameGui.WorldToScreen(pc.Position, out var screen))
                            continue;
                        if (screen.X >= x0 && screen.X <= x1 && screen.Y >= y0 && screen.Y <= y1)
                            this.ultSelected.Add(new UltTarget { Name = pc.Name.TextValue, World = (ushort)pc.HomeWorld.RowId, Address = pc.Address, EntityId = pc.EntityId, Screen = screen });
                    }
                }

                var dl = ImGui.GetBackgroundDrawList();
                dl.AddRect(this.ultAnchor, mouse, 0xFFFFFFFF, 0f, ImDrawFlags.None, 1.5f);
                dl.AddRectFilled(
                    new System.Numerics.Vector2(x0, y0),
                    new System.Numerics.Vector2(x1, y1),
                    0x330000FF);
                foreach (var t in this.ultSelected)
                    DrawUltimateBox(t.Address);
                if (this.ultSelected.Count > 0)
                    dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), mouse + new System.Numerics.Vector2(14, 14), 0xFF0000FF, $"{this.ultSelected.Count} selected");
            }
        }

        ImGui.End();
        ImGui.PopStyleColor(2);
    }

    /// <summary>True when the actor's model is currently invisible, no matter who hid it
    /// (us, another plugin, or the game). Fail-open: unreadable actors count as visible.</summary>
    private static bool IsActorModelHidden(nint address)
    {
        try
        {
            unsafe
            {
                var go = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)address;
                if (go == null)
                    return false;
                return (go->RenderFlags & FFXIVClientStructs.FFXIV.Client.Game.Object.VisibilityFlags.Model)
                    != FFXIVClientStructs.FFXIV.Client.Game.Object.VisibilityFlags.None;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Whether a player is already invisible and therefore not a void candidate.</summary>
    private bool IsAlreadyHidden(nint address)
        => this.controller.IsHiddenByPlugin(address) || IsActorModelHidden(address);

    /// <summary>Red outline box around a player actor, same style as the hold-to-void square.</summary>
    private static void DrawUltimateBox(nint address)
    {
        try
        {
            unsafe
            {
                var go = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)address;
                if (go == null)
                    return;
                var feet = go->Position;
                var head = new System.Numerics.Vector3(feet.X, feet.Y + 2f, feet.Z);
                if (!Service.GameGui.WorldToScreen(feet, out var feetScreen)
                    || !Service.GameGui.WorldToScreen(head, out var headScreen))
                    return;
                float heightPx = System.Math.Abs(feetScreen.Y - headScreen.Y);
                if (heightPx < 4f || heightPx > 10000f)
                    return;
                float widthPx = heightPx * 0.6f;
                if (widthPx < 2f || widthPx > 10000f)
                    return;
                float left = feetScreen.X - widthPx * 0.5f;
                float top = feetScreen.Y - heightPx;
                ImGui.GetBackgroundDrawList().AddRect(
                    new System.Numerics.Vector2(left, top),
                    new System.Numerics.Vector2(left + widthPx, feetScreen.Y),
                    0xFF0000FF, 0f, ImDrawFlags.None, 2f);
            }
        }
        catch
        {
        }
    }

    private void ConfirmVoidUltimate(List<UltTarget> targets)
    {
        try
        {
            // De-dupe and drop anyone already voidlisted or already pending.
            var fresh = new List<UltTarget>();
            var seen = new HashSet<string>();
            lock (this.pendingLock)
            {
                foreach (var t in targets)
                {
                    if (string.IsNullOrWhiteSpace(t.Name) || !seen.Add(t.Name + "@" + t.World))
                        continue;
                    if (this.IsAlreadyHidden(t.Address))
                        continue; // hidden since selection: nothing to void
                    string key = t.Name + "@" + t.World;
                    if (this.controller.IsVoidlisted(key))
                        continue;
                    bool pending = false;
                    foreach (var p in this.pendingVoids)
                        if (p.Name == t.Name && p.World == t.World) { pending = true; break; }
                    if (!pending)
                        foreach (var u in this.pendingUltimates)
                            foreach (var ut in u.Targets)
                                if (ut.Name == t.Name && ut.World == t.World) { pending = true; break; }
                    if (!pending)
                        fresh.Add(t);
                }
            }
            if (fresh.Count == 0)
            {
                Service.ChatGui.Print("[Visibility Plus] Void Ultimate: everyone selected is already voidlisted.");
                return;
            }

            // Most-center target carries the VFX.
            var centroid = System.Numerics.Vector2.Zero;
            foreach (var t in fresh)
                centroid += t.Screen;
            centroid /= fresh.Count;
            UltTarget center = fresh[0];
            float best = float.MaxValue;
            foreach (var t in fresh)
            {
                float d = (t.Screen - centroid).LengthSquared();
                if (d < best) { best = d; center = t; }
            }
            if (VfxHelper.TrySpawnOnActor(UltimateVfxPath, center.Address, center.Address, out var vfxPtr))
                Service.PluginLog.Info($"[Visibility Plus] Ultimate VFX on {center.Name} ptr=0x{vfxPtr:X}");
            else
                Service.PluginLog.Warning($"[Visibility Plus] Ultimate VFX failed for {center.Name} — voidlisting anyway after delay.");

            var names = new List<(string Name, ushort World)>(fresh.Count);
            foreach (var t in fresh)
                names.Add((t.Name, t.World));
            lock (this.pendingLock)
                this.pendingUltimates.Add(new PendingUltimate { Targets = names, DueTick = Environment.TickCount64 + 200 });
            Service.ChatGui.Print($"[Visibility Plus] Void Ultimate on {fresh.Count} player{(fresh.Count == 1 ? string.Empty : "s")}...");
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning($"[Visibility Plus] Void Ultimate failed: {ex.Message}");
        }
    }

    private const uint SndAsync = 0x1;
    private const uint SndFilename = 0x20000;
    private static bool voidSoundMissingLogged;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string pszSound, nint hmod, uint fdwSound);

    private static string VoidSoundPath
    {
        get
        {
            try
            {
                // Plugin dir = folder containing the dll (devPlugins or installed plugin dir).
                // AssemblyLocation is FileInfo (Dalamud API 15) — be defensive via object cast.
                object? locObj = Service.PluginInterface.AssemblyLocation;
                string? dir = null;
                if (locObj is System.IO.FileInfo fi) dir = fi.DirectoryName;
                else if (locObj is System.IO.DirectoryInfo di) dir = di.FullName;
                else if (locObj != null) dir = System.IO.Path.GetDirectoryName(locObj.ToString());
                if (string.IsNullOrEmpty(dir))
                    dir = System.IO.Path.GetDirectoryName(typeof(VisibilityPlusPlugin).Assembly.Location);
                if (!string.IsNullOrEmpty(dir))
                    return System.IO.Path.Combine(dir, "scream.wav");
            }
            catch
            {
            }
            return "scream.wav";
        }
    }

    private static void PlayVoidSound()
    {
        try
        {
            string path = VoidSoundPath;
            if (!System.IO.File.Exists(path))
            {
                if (!voidSoundMissingLogged)
                {
                    voidSoundMissingLogged = true;
                    Service.PluginLog.Warning("[Visibility Plus] Void sound missing: " + path);
                }
                return;
            }
            PlaySound(path, nint.Zero, SndAsync | SndFilename);
        }
        catch
        {
        }
    }

    private void AddVoidFromPlayer(string name, ushort world, nint targetAddress = 0, uint entityId = 0)
    {
        string key = name + "@" + world;
        // Already voidlisted or already pending — refuse with why.
        if (this.controller.IsVoidlisted(key))
        {
            Service.ChatGui.Print($"[Visibility Plus] {name} is already voidlisted.");
            return;
        }
        lock (this.pendingLock)
        {
            foreach (var p in this.pendingVoids)
                if (p.Name == name && p.World == world)
                {
                    Service.ChatGui.Print($"[Visibility Plus] {name} is already being voidlisted.");
                    return;
                }
        }

        // Resolve target address if caller didn't pass it (fallback to current target).
        if (targetAddress == nint.Zero)
        {
            try
            {
                var t = Service.TargetManager.Target as IPlayerCharacter ?? Service.ObjectTable.LocalPlayer as IPlayerCharacter;
                // Try to find by name/world in object table as last resort
                if (t != null && t.Name.TextValue == name && (ushort)t.HomeWorld.RowId == world)
                {
                    targetAddress = t.Address;
                    entityId = t.EntityId;
                }
                else
                {
                    foreach (var obj in Service.ObjectTable)
                    {
                        if (obj is IPlayerCharacter pc && pc.Name.TextValue == name && (ushort)pc.HomeWorld.RowId == world)
                        {
                            targetAddress = pc.Address;
                            entityId = pc.EntityId;
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        var pending = new PendingVoid
        {
            Name = name,
            World = world,
            StartTick = Environment.TickCount64,
            TargetAddress = targetAddress,
            TargetEntityId = entityId,
        };

        // Spawn chain VFX immediately on the target actor (follows them).
        nint chainPtr = nint.Zero;
        bool spawned = false;
        if (targetAddress != nint.Zero)
            spawned = VfxHelper.TrySpawnOnActor(ChainVfxPath, targetAddress, targetAddress, out chainPtr);
        pending.ChainVfxPtr = chainPtr;

        lock (this.pendingLock)
            this.pendingVoids.Add(pending);

        if (spawned)
            Service.PluginLog.Info($"[Visibility Plus] Chain VFX on {name}@{WorldName(world)} ptr=0x{chainPtr:X}");
        else
            Service.PluginLog.Warning($"[Visibility Plus] Chain VFX failed for {name}@{WorldName(world)} — will still hide after delay.");

        Service.ChatGui.Print($"[Visibility Plus] Voidlisting {name}@{WorldName(world)}...");
    }

    private void AddWhiteFromPlayer(string name, ushort world)
    {
        string key = name + "@" + world;
        if (this.controller.IsWhiteListed(key))
        {
            Service.ChatGui.Print($"[Visibility Plus] {name} is already whitelisted.");
            return;
        }
        if (this.controller.AddWhite(name, world))
            Service.ChatGui.Print($"[Visibility Plus] Whitelisted {name}@{WorldName(world)} (never hidden).");
        else
            Service.ChatGui.Print($"[Visibility Plus] {name} is already whitelisted.");
    }

    private void UpdateVoidPending(Dalamud.Plugin.Services.IFramework framework)
    {
        try
        {
            if (this.pendingVoids.Count == 0 && this.pendingUltimates.Count == 0)
                return;
            long now = Environment.TickCount64;
            // Void Ultimate: VFX already played on confirm, voidlist 200ms later.
            List<PendingUltimate> dueUltimates = new();
            lock (this.pendingLock)
            {
                foreach (var u in this.pendingUltimates)
                    if (now >= u.DueTick)
                        dueUltimates.Add(u);
                foreach (var u in dueUltimates)
                    this.pendingUltimates.Remove(u);
            }
            foreach (var u in dueUltimates)
            {
                int added = 0;
                foreach (var t in u.Targets)
                    if (this.controller.AddVoid(t.Name, t.World))
                        added++;
                if (added > 0)
                {
                    string msg = $"[Visibility Plus] Voidlisted {added} player{(added == 1 ? string.Empty : "s")} via Void Ultimate.";
                    if (!this.config.VoidEnabled)
                        msg += " (enable VoidList to hide them.)";
                    Service.ChatGui.Print(msg);
                }
                else
                    Service.ChatGui.Print("[Visibility Plus] Void Ultimate: everyone selected is already voidlisted.");
            }
            List<PendingVoid> toRemove = new();
            lock (this.pendingLock)
            {
                foreach (var p in this.pendingVoids)
                {
                    long elapsed = now - p.StartTick;

                    // 2.4s: sound 0.1s before burst.
                    if (elapsed >= 2400 && !p.SoundPlayed)
                    {
                        p.SoundPlayed = true;
                        PlayVoidSound();
                    }

                    // 2.5s: burst VFX. Resolve current address (target may have moved/despawned).
                    if (elapsed >= 2500 && !p.SecondSpawned)
                    {
                        p.SecondSpawned = true;
                        nint addr = this.ResolvePendingTarget(p);
                        if (addr != nint.Zero)
                        {
                            nint burstPtr = nint.Zero;
                            if (VfxHelper.TrySpawnOnActor(BurstVfxPath, addr, addr, out burstPtr))
                                Service.PluginLog.Info($"[Visibility Plus] Burst VFX on {p.Name} ptr=0x{burstPtr:X}");
                        }
                    }

                    // 2.6s: stop chain (0.1s after burst, 0.2s after sound).
                    if (elapsed >= 2600 && !p.ChainStopped)
                    {
                        p.ChainStopped = true;
                        if (p.ChainVfxPtr != nint.Zero)
                        {
                            VfxHelper.TryRemove(p.ChainVfxPtr);
                            p.ChainVfxPtr = nint.Zero;
                        }
                    }

                    // 2.8s: actually voidlist and hide (0.2s after chain stop).
                    if (elapsed >= 2800)
                    {
                        if (this.controller.AddVoid(p.Name, p.World))
                            Service.ChatGui.Print($"[Visibility Plus] Voidlisted {p.Name}@{WorldName(p.World)}.");
                        else
                            Service.ChatGui.Print($"[Visibility Plus] {p.Name} is already voidlisted.");
                        toRemove.Add(p);
                    }
                }

                foreach (var r in toRemove)
                    this.pendingVoids.Remove(r);
            }
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning($"[Visibility Plus] UpdateVoidPending failed: {ex.Message}");
        }
    }

    private nint ResolvePendingTarget(PendingVoid p)
    {
        try
        {
            // Prefer original address if it still points to same entityId.
            if (p.TargetAddress != nint.Zero)
            {
                try
                {
                    unsafe
                    {
                        var go = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)p.TargetAddress;
                        if (go != null && go->EntityId == p.TargetEntityId)
                            return p.TargetAddress;
                    }
                }
                catch { }
            }
            // Fallback: find by entityId then by name/world.
            if (p.TargetEntityId != 0)
            {
                var byId = Service.ObjectTable.SearchByEntityId(p.TargetEntityId);
                if (byId != null)
                    return byId.Address;
            }
            foreach (var obj in Service.ObjectTable)
            {
                if (obj is IPlayerCharacter pc && pc.Name.TextValue == p.Name && (ushort)pc.HomeWorld.RowId == p.World)
                    return pc.Address;
            }
        }
        catch { }
        return nint.Zero;
    }

    private static string WorldName(ushort worldId)
    {
        try
        {
            var name = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>()?.GetRow(worldId).Name.ToString();
            if (!string.IsNullOrEmpty(name))
                return name;
        }
        catch
        {
        }
        return worldId.ToString();
    }

    private void OnContextMenuOpened(IMenuOpenedArgs args)
    {
        try
        {
            if (args.MenuType != ContextMenuType.Default || args.Target is not MenuTargetDefault target)
                return;
            if (target.TargetObject is not IPlayerCharacter pc)
                return;
            var local = Service.ObjectTable.LocalPlayer;
            if (local == null || pc.Address == local.Address)
                return;
            string name = pc.Name.TextValue;
            ushort world = (ushort)pc.HomeWorld.RowId;
            nint addr = pc.Address;
            uint eid = pc.EntityId;
            args.AddMenuItem(new MenuItem
            {
                Name = "Add to VoidList (vPlus)",
                OnClicked = _ => this.AddVoidFromPlayer(name, world, addr, eid),
            });
            string whiteKey = name + "@" + world;
            bool isWhite = this.controller.IsWhiteListed(whiteKey);
            if (isWhite)
            {
                args.AddMenuItem(new MenuItem
                {
                    Name = "Remove from WhiteList (vPlus)",
                    OnClicked = _ =>
                    {
                        for (int i = 0; i < this.config.WhiteList.Count; i++)
                        {
                            var e = this.config.WhiteList[i];
                            if (e.Name == name && e.World == world)
                            {
                                this.controller.RemoveWhiteAt(i);
                                Service.ChatGui.Print($"[Visibility Plus] Removed {name}@{WorldName(world)} from WhiteList.");
                                break;
                            }
                        }
                    },
                });
            }
            else
            {
                args.AddMenuItem(new MenuItem
                {
                    Name = "Add to WhiteList (vPlus)",
                    OnClicked = _ => this.AddWhiteFromPlayer(name, world),
                });
            }
        }
        catch
        {
        }
    }

    /// <summary>Dots at hidden players. Cyan=semi hidden, Red=voidlisted. Name above. Right-click red to unVoid.</summary>
    private void DrawDots()
    {
        // Track right mouse every frame even when dots not visible, for edge detection
        bool rightDown = HoldKeybind.IsDown(0x02); // VK_RBUTTON
        bool rightClicked = rightDown && !this.prevRightDown;
        bool rightReleased = !rightDown && this.prevRightDown;
        this.prevRightDown = rightDown;
        bool imGuiRight = ImGui.IsMouseClicked(ImGuiMouseButton.Right) || ImGui.IsMouseReleased(ImGuiMouseButton.Right);

        if (!this.controller.DotsVisible)
            return;

        var infos = this.controller.GetDotInfos();
        if (infos.Length == 0)
            return;
        var drawList = ImGui.GetBackgroundDrawList();
        var mousePos = ImGui.GetMousePos();
        // First pass: find closest dot (void or hidden) within click radius (dot + name text) for right-click
        int hoveredIdx = -1;
        System.Numerics.Vector2 hoveredScreen = default;
        VisibilityController.DotInfo hoveredInfo = default;
        float bestDist = 30f;
        bool hasAny = false;
        for (int idx = 0; idx < infos.Length; idx++)
        {
            var info = infos[idx];
            if (!Service.GameGui.WorldToScreen(info.Position, out var screen)) continue;
            hasAny = true;
            float dist = (mousePos - screen).Length();
            bool hoverThis = dist < 28f;
            if (!hoverThis && !string.IsNullOrEmpty(info.Name))
            {
                var baseTs = ImGui.CalcTextSize(info.Name);
                var textSize = baseTs * 1.30f;
                var textPos = new System.Numerics.Vector2(screen.X - textSize.X * 0.5f, screen.Y - 14f - textSize.Y);
                if (mousePos.X >= textPos.X - 6 && mousePos.X <= textPos.X + textSize.X + 6
                    && mousePos.Y >= textPos.Y - 4 && mousePos.Y <= textPos.Y + textSize.Y + 4)
                {
                    hoverThis = true;
                    dist = 0f;
                }
            }
            if (hoverThis && dist < bestDist)
            {
                bestDist = dist;
                hoveredIdx = idx;
                hoveredScreen = screen;
                hoveredInfo = info;
            }
        }
        bool hoveredFound = hoveredIdx != -1;

        var local = Service.ObjectTable.LocalPlayer;
        // Second pass: draw all dots, highlight hovered (both void red and hidden cyan)
        for (int idx = 0; idx < infos.Length; idx++)
        {
            var info = infos[idx];
            if (!Service.GameGui.WorldToScreen(info.Position, out var screen))
                continue;
            bool isHovered = hoveredFound && idx == hoveredIdx;
            uint fill = info.IsVoid ? 0xAA0000FFu : 0xAAFFFF00u;
            uint outline = isHovered ? 0xFFFFFFFFu : 0xAA000000u;
            float radius = isHovered ? 8f : 6f;
            drawList.AddCircleFilled(screen, radius, fill);
            drawList.AddCircle(screen, radius, outline, 16, isHovered ? 2.2f : 1.5f);

            if (!string.IsNullOrEmpty(info.Name) && local != null)
            {
                float dist = System.Numerics.Vector3.Distance(local.Position, info.Position);
                if (dist > 35f) continue;
                var font = ImGui.GetFont();
                float bigSize = ImGui.GetFontSize() * 1.30f;
                var baseSize = ImGui.CalcTextSize(info.Name);
                var textSize = baseSize * 1.30f;
                var textPos = new System.Numerics.Vector2(screen.X - textSize.X * 0.5f, screen.Y - 14f - textSize.Y - (isHovered ? 2f : 0f));
                uint nameCol = info.IsVoid ? 0xFF0000FFu : 0xFFFFFF00u;
                drawList.AddText(font, bigSize, textPos + new System.Numerics.Vector2(1, 1), 0xAA000000u, info.Name);
                drawList.AddText(font, bigSize, textPos, nameCol, info.Name);
            }
        }

        // Right-click on dot -> void: unVoid, hidden (cyan): add to whitelist
        if (hasAny && hoveredFound && (rightClicked || rightReleased || imGuiRight))
        {
            if (!ImGui.GetIO().WantTextInput)
            {
                if (hoveredInfo.IsVoid)
                {
                    this.pendingUnvoidName = hoveredInfo.Name;
                    this.pendingUnvoidWorld = hoveredInfo.World;
                    this.pendingUnvoidPos = hoveredScreen + new System.Numerics.Vector2(12, 12);
                    this.pendingWhiteName = null; // clear other
                    ImGui.OpenPopup("##unvoidPopup");
                    Service.PluginLog.Info($"[Visibility Plus] unVoid popup for {hoveredInfo.Name}@{hoveredInfo.World}");
                }
                else
                {
                    this.pendingWhiteName = hoveredInfo.Name;
                    this.pendingWhiteWorld = hoveredInfo.World;
                    this.pendingWhitePos = hoveredScreen + new System.Numerics.Vector2(12, 12);
                    this.pendingUnvoidName = null;
                    ImGui.OpenPopup("##whitePopup");
                    Service.PluginLog.Info($"[Visibility Plus] white popup for {hoveredInfo.Name}@{hoveredInfo.World}");
                }
            }
        }

        if (this.pendingUnvoidName != null)
            ImGui.SetNextWindowPos(this.pendingUnvoidPos, ImGuiCond.Appearing);
        if (ImGui.BeginPopup("##unvoidPopup"))
        {
            string label = this.pendingUnvoidName != null
                ? $"{this.pendingUnvoidName}@{WorldName(this.pendingUnvoidWorld)}"
                : "Unknown";
            ImGui.Text(label);
            if (ImGui.Button("unVoid"))
            {
                if (this.pendingUnvoidName != null)
                {
                    for (int i = 0; i < this.config.VoidList.Count; i++)
                    {
                        var e = this.config.VoidList[i];
                        if (e.Name == this.pendingUnvoidName && e.World == this.pendingUnvoidWorld)
                        {
                            this.controller.RemoveVoidAt(i);
                            Service.ChatGui.Print($"[Visibility Plus] Un-voidlisted {label}.");
                            break;
                        }
                    }
                    this.pendingUnvoidName = null;
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (this.pendingWhiteName != null)
            ImGui.SetNextWindowPos(this.pendingWhitePos, ImGuiCond.Appearing);
        if (ImGui.BeginPopup("##whitePopup"))
        {
            string label = this.pendingWhiteName != null
                ? $"{this.pendingWhiteName}@{WorldName(this.pendingWhiteWorld)}"
                : "Unknown";
            ImGui.Text(label);
            if (ImGui.Button("Add to WhiteList"))
            {
                if (this.pendingWhiteName != null)
                {
                    if (this.controller.AddWhite(this.pendingWhiteName, this.pendingWhiteWorld))
                        Service.ChatGui.Print($"[Visibility Plus] Whitelisted {label} (never hidden).");
                    else
                        Service.ChatGui.Print($"[Visibility Plus] {label} is already whitelisted.");
                    this.pendingWhiteName = null;
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void OpenConfig() => this.configWindow.IsOpen = true;

    private void OpenZoneSettings(uint territoryType)
    {
        if (!this.zoneWindows.TryGetValue(territoryType, out var wnd))
        {
            wnd = new ZoneWindow(territoryType, this.configWindow.ZoneName(territoryType), this.config, this.controller, this.OpenWorldSettings);
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

    private void OpenWorldSettings(uint territoryType, ushort worldId)
    {
        var key = (territoryType, worldId);
        if (!this.worldWindows.TryGetValue(key, out var wnd))
        {
            var zoneOv = this.controller.GetOrCreateOverride(territoryType);
            var worldOv = this.controller.GetOrCreateWorldOverride(territoryType, worldId);
            wnd = new WorldWindow(territoryType, worldId, WorldName(worldId), this.config, zoneOv, worldOv);
            this.worldWindows[key] = wnd;
            this.windowSystem.AddWindow(wnd);
        }
        var mainPos = this.configWindow.LastPosition;
        var mainSize = this.configWindow.LastSize;
        if (mainPos != default && mainSize != default)
        {
            float step = 28f * (this.worldWindows.Count % 5);
            wnd.Position = new System.Numerics.Vector2(mainPos.X + mainSize.X + 12f + step, mainPos.Y + step);
            wnd.PositionCondition = ImGuiCond.Appearing;
        }
        wnd.IsOpen = true;
    }

    public void Dispose()
    {
        Service.ContextMenu.OnMenuOpened -= this.OnContextMenuOpened;
        Service.CommandManager.RemoveHandler(Command);
        Service.Framework.Update -= this.controller.OnUpdate;
        Service.Framework.Update -= this.UpdateVoidPending;
        Service.PluginInterface.UiBuilder.Draw -= this.DrawUi;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfig;
        // Clean up any looping chain VFX still active
        lock (this.pendingLock)
        {
            foreach (var p in this.pendingVoids)
                if (p.ChainVfxPtr != nint.Zero)
                    VfxHelper.TryRemove(p.ChainVfxPtr);
            this.pendingVoids.Clear();
            this.pendingUltimates.Clear();
        }
        this.ultDragging = false;
        this.ultSelected.Clear();
        this.windowSystem.RemoveAllWindows();
        this.controller.Dispose();
        GC.SuppressFinalize(this);
    }
}
