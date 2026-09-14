using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using BattleNpcSubKind = FFXIVClientStructs.FFXIV.Client.Game.Object.BattleNpcSubKind;

namespace VisibilityPlus;

/// <summary>
/// Hides entity groups by setting GameObject RenderFlags (Model | Nameplate),
/// same technique as the reference Visibility plugin. Only touches objects it
/// hid itself, so it never fights other plugins over render state.
/// </summary>
public sealed class VisibilityController : IDisposable
{
    private readonly PluginConfiguration config;

    // Tracked by native address, NOT entity ID: live objects can share the
    // 0xE0000000 sentinel ID (server never assigns them a real one), so
    // ID-keyed tracking would hide/show the wrong objects.
    // The value records exactly which flag bits WE set, so restore never
    // clears bits owned by other plugins.
    private readonly Dictionary<nint, VisibilityFlags> hidden = new(256);

    // Toggle-mode latch state (per group). Not persisted; zone exits reset them.
    private bool latchedNpcs, latchedEnemies, latchedMinions, latchedPets, latchedChocobos, latchedPlayers;
    private bool prevNpcs, prevEnemies, prevMinions, prevPets, prevChocobos, prevPlayers;
    private long untilNpcs, untilEnemies, untilMinions, untilPets, untilChocobos, untilPlayers;
    private const long Toggle30sMs = 30_000;

    // Sync state from Snowcloak and/or Mare, polled throttled over IPC. Never
    // latched: a failed poll goes stale instead, and stale data hides nothing.
    private readonly List<SyncSource> syncSources = [];
    private HashSet<nint> syncedAddrs = [];
    private long lastSyncPoll;
    private const long SyncPollIntervalMs = 2_000;
    private const long SyncStaleAfterMs = 10_000;

    private sealed class SyncSource
    {
        public string Name = string.Empty;
        public ICallGateSubscriber<List<nint>> Subscriber = null!;
        public int LastCount;
        public long LastOkAt;
    }

    // Immutable snapshot so Framework.Update never enumerates a list the UI thread may mutate.
    private HashSet<uint> zoneSnapshot = [];
    private readonly object zoneLock = new();

    private bool disposed;

    public VisibilityController(PluginConfiguration config)
    {
        this.config = config;
        this.RefreshZoneSnapshot();
        this.syncSources.Add(new SyncSource { Name = "Snowcloak", Subscriber = Service.PluginInterface.GetIpcSubscriber<List<nint>>("Snowcloak.GetHandledAddresses") });
        this.syncSources.Add(new SyncSource { Name = "PSync", Subscriber = Service.PluginInterface.GetIpcSubscriber<List<nint>>("MareSynchronos.GetHandledAddresses") });
        this.syncSources.Add(new SyncSource { Name = "PSync", Subscriber = Service.PluginInterface.GetIpcSubscriber<List<nint>>("PlayerSync.GetHandledAddresses") });
        this.syncSources.Add(new SyncSource { Name = "Lightless", Subscriber = Service.PluginInterface.GetIpcSubscriber<List<nint>>("LightlessSync.GetHandledAddresses") });
    }

    public int HiddenCount
    {
        get { lock (this.zoneLock) { return this.hidden.Count; } }
    }

    public void AddZone(uint territoryType)
    {
        lock (this.zoneLock)
        {
            if (!this.config.ZoneIds.Contains(territoryType))
            {
                this.config.ZoneIds.Add(territoryType);
                this.config.Save();
            }
            this.RefreshZoneSnapshot();
        }
    }

    public void RemoveZone(uint territoryType)
    {
        lock (this.zoneLock)
        {
            this.config.ZoneIds.Remove(territoryType);
            this.config.Save();
            this.RefreshZoneSnapshot();
        }
    }

    public void ClearZones()
    {
        lock (this.zoneLock)
        {
            this.config.ZoneIds.Clear();
            this.config.Save();
            this.RefreshZoneSnapshot();
        }
    }

    public void RefreshZoneSnapshot()
    {
        lock (this.zoneLock)
        {
            this.zoneSnapshot = new HashSet<uint>(this.config.ZoneIds);
        }
    }

    public bool IsFilterActiveFor(uint territoryType) => this.zoneSnapshot.Contains(territoryType);

    public unsafe void OnUpdate(IFramework framework)
    {
        try
        {
            var client = Service.ClientState;
            var localPlayer = Service.ObjectTable.LocalPlayer;
            if (!client.IsLoggedIn || localPlayer == null)
                return;

            uint territory = client.TerritoryType;
            if (territory == 0)
                return; // mid-load flap, leave state alone

            if (Service.Condition[ConditionFlag.BetweenAreas])
                return;

            if (!this.config.Enabled || !this.zoneSnapshot.Contains(territory))
            {
                this.ResetHotkeyState();
                if (this.hidden.Count > 0)
                    this.ShowAll();
                return;
            }

            var manager = GameObjectManager.Instance();
            if (manager == null)
                return;

            uint localId = localPlayer.EntityId;
            nint localAddr = localPlayer.Address;

            // Hotkeys: reveal the bound group per the global HotkeyMode.
            // Groups may share the same key, revealing everything at once.
            bool holdNpcs = this.HotkeyShows(ref this.latchedNpcs, ref this.prevNpcs, ref this.untilNpcs, this.config.HoldKey, this.config.HoldCtrl, this.config.HoldShift, this.config.HoldAlt);
            bool holdEnemies = this.HotkeyShows(ref this.latchedEnemies, ref this.prevEnemies, ref this.untilEnemies, this.config.HoldKeyEnemies, this.config.HoldCtrlEnemies, this.config.HoldShiftEnemies, this.config.HoldAltEnemies);
            bool holdMinions = this.HotkeyShows(ref this.latchedMinions, ref this.prevMinions, ref this.untilMinions, this.config.HoldKeyMinions, this.config.HoldCtrlMinions, this.config.HoldShiftMinions, this.config.HoldAltMinions);
            bool holdPets = this.HotkeyShows(ref this.latchedPets, ref this.prevPets, ref this.untilPets, this.config.HoldKeyPets, this.config.HoldCtrlPets, this.config.HoldShiftPets, this.config.HoldAltPets);
            bool holdChocobos = this.HotkeyShows(ref this.latchedChocobos, ref this.prevChocobos, ref this.untilChocobos, this.config.HoldKeyChocobos, this.config.HoldCtrlChocobos, this.config.HoldShiftChocobos, this.config.HoldAltChocobos);
            bool holdPlayers = this.HotkeyShows(ref this.latchedPlayers, ref this.prevPlayers, ref this.untilPlayers, this.config.HoldKeyPlayers, this.config.HoldCtrlPlayers, this.config.HoldShiftPlayers, this.config.HoldAltPlayers);

            this.PollSyncedPlayers();

            // Walk the WHOLE table, not just slots 0-199: slots 200+ hold
            // non-networked objects and slots 489+ hold lively actors, i.e.
            // exactly the named NPCs we want to hide.
            int slots = manager->Objects.IndexSorted.Length;
            for (int i = 0; i < slots; ++i)
            {
                GameObject* obj = manager->Objects.IndexSorted[i];
                if (obj == null || (nint)obj == localAddr)
                    continue;
                if (obj->EntityId == 0)
                    continue; // empty slot; note 0xE0000000 IS used by real NPCs, don't skip it

                // Kind pre-filter replaces the IsCharacter() native call.
                var kind = (ObjectKind)obj->ObjectKind;
                if (kind != ObjectKind.Pc && kind != ObjectKind.EventNpc && kind != ObjectKind.BattleNpc && kind != ObjectKind.Companion)
                    continue;

                switch (kind)
                {
                    case ObjectKind.Pc:
                        // Local player already skipped by address above: never hide yourself.
                        bool hidePlayer = this.config.HideNonSyncedPlayers
                            && (this.config.HideAllPlayers || (this.SyncedDataFresh() && !this.syncedAddrs.Contains((nint)obj)));
                        if (holdPlayers)
                            hidePlayer = false; // hotkey reveals hidden players in either scope
                        if (hidePlayer) this.Hide(obj);
                        else this.Unhide(obj);
                        break;

                    case ObjectKind.EventNpc:
                    {
                        bool named = HasName(obj);
                        bool hide = this.config.HideNpcs && (!this.config.OnlyUnnamedNpcs || !named);
                        if (holdNpcs && named)
                            hide = false; // hold-key: named NPCs reappear while held
                        if (hide) this.Hide(obj);
                        else this.Unhide(obj);
                        break;
                    }

                    case ObjectKind.BattleNpc:
                        byte sub = obj->SubKind;
                        if (sub == (byte)BattleNpcSubKind.Pet)
                        {
                            if (((Character*)obj)->NameId == 6565)
                                break; // Earthly Star: combat visual, never touch
                            if (obj->OwnerId == localId)
                                break; // own pet
                            if (this.config.HidePets && !holdPets) this.Hide(obj);
                            else this.Unhide(obj);
                        }
                        else if (sub == (byte)BattleNpcSubKind.Buddy)
                        {
                            if (obj->OwnerId == localId)
                                break; // own chocobo
                            if (this.config.HideChocobos && !holdChocobos) this.Hide(obj);
                            else this.Unhide(obj);
                        }
                        else if (sub is (byte)BattleNpcSubKind.Player or (byte)BattleNpcSubKind.NpcPartyMember)
                        {
                            // Friendly human NPCs: guards, quest allies, duty-support
                            // members and client-side scenario actors. Treated as NPCs.
                            bool named = HasName(obj);
                            bool hide = this.config.HideNpcs && (!this.config.OnlyUnnamedNpcs || !named);
                            if (holdNpcs && named)
                                hide = false; // hold-key: named NPCs reappear while held
                            if (hide) this.Hide(obj);
                            else this.Unhide(obj);
                        }
                        else if (sub == (byte)BattleNpcSubKind.Combatant)
                        {
                            // Enemies (note: guards share this type).
                            if (this.config.HideEnemies && !holdEnemies) this.Hide(obj);
                            else this.Unhide(obj);
                        }
                        else
                        {
                            // Other battle NPCs (boss parts, racing chocobos,
                            // LoVM minions...): never touched.
                            break;
                        }
                        break;

                    case ObjectKind.Companion:
                        if (((Character*)obj)->CompanionOwnerId == localId)
                            break; // own minion
                        if (this.config.HideMinions && !holdMinions) this.Hide(obj);
                        else this.Unhide(obj);
                        break;
                }
            }
        }
        catch
        {
            // Never throw on the framework thread.
        }
    }

    private unsafe void Hide(GameObject* obj)
    {
        nint addr = (nint)obj;
        var flags = VisibilityFlags.Model | VisibilityFlags.Nameplate;
        var before = obj->RenderFlags;
        if (this.hidden.TryGetValue(addr, out var mine))
        {
            // Already ours: top up any missing bits.
            var added = flags & ~before;
            if (added != VisibilityFlags.None)
            {
                obj->RenderFlags |= flags;
                this.hidden[addr] = mine | added;
            }
            return;
        }
        if ((before & flags) == flags)
            return; // another plugin hid all of it first: yield, don't track
        obj->RenderFlags |= flags;
        this.hidden[addr] = flags & ~before;
    }

    private unsafe void Unhide(GameObject* obj)
    {
        nint addr = (nint)obj;
        if (!this.hidden.TryGetValue(addr, out var mine))
            return;
        if (mine != VisibilityFlags.None)
            obj->RenderFlags &= ~mine;
        this.hidden.Remove(addr);
    }

    /// <summary>Whether an object has a name (and therefore shows a nameplate).</summary>
    public static unsafe bool HasName(GameObject* obj)
    {
        // First byte of the GameObject name buffer (CS GameObject._name at 0x30).
        return *(byte*)((byte*)obj + 0x30) != 0;
    }

    private void ResetHotkeyState()
    {
        this.latchedNpcs = this.latchedEnemies = this.latchedMinions = this.latchedPets = this.latchedChocobos = this.latchedPlayers = false;
        this.prevNpcs = this.prevEnemies = this.prevMinions = this.prevPets = this.prevChocobos = this.prevPlayers = false;
        this.untilNpcs = this.untilEnemies = this.untilMinions = this.untilPets = this.untilChocobos = this.untilPlayers = 0;
    }

    /// <summary>Hotkey behavior per the global HotkeyMode: Hold (level), Toggle (latch),
    /// or Toggle30s (latch with 30s auto-release). Typing freezes edge detection.</summary>
    private bool HotkeyShows(ref bool latched, ref bool prev, ref long until, int key, bool ctrl, bool shift, bool alt)
    {
        bool typing = false;
        try
        {
            // ImGui IO read from the framework thread: benign aligned read, guarded anyway.
            typing = ImGui.GetIO().WantTextInput;
        }
        catch
        {
        }

        bool raw = key != 0
            && HoldKeybind.IsDown(key)
            && (!ctrl || HoldKeybind.IsDown(HoldKeybind.VK_CONTROL))
            && (!shift || HoldKeybind.IsDown(HoldKeybind.VK_SHIFT))
            && (!alt || HoldKeybind.IsDown(HoldKeybind.VK_MENU));

        if (!typing)
        {
            if (raw && !prev)
            {
                if (this.config.HotkeyMode == HotkeyMode.Toggle)
                    latched = !latched;
                else if (this.config.HotkeyMode == HotkeyMode.Toggle30s)
                    until = Environment.TickCount64 + Toggle30sMs;
            }
            prev = raw;
        }

        return this.config.HotkeyMode switch
        {
            HotkeyMode.Toggle => latched,
            HotkeyMode.Toggle30s => Environment.TickCount64 < until,
            _ => raw && !typing,
        };
    }

    /// <summary>Index of an object in the native table, or -1. Used by the target inspector.</summary>
    public unsafe int FindSlotIndex(nint address)
    {
        try
        {
            var manager = GameObjectManager.Instance();
            if (manager == null)
                return -1;
            int slots = manager->Objects.IndexSorted.Length;
            for (int i = 0; i < slots; ++i)
            {
                GameObject* obj = manager->Objects.IndexSorted[i];
                if (obj != null && (nint)obj == address)
                    return i;
            }
            return -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Which hide-group an object belongs to, for the /vplus target inspector.</summary>
    public static string ClassifyGroup(byte objectKind, byte subKind)
    {
        switch ((ObjectKind)objectKind)
        {
            case ObjectKind.Pc:
                return "Non-Synced Players";
            case ObjectKind.EventNpc:
                return "NPCs";
            case ObjectKind.BattleNpc:
                if (subKind == (byte)BattleNpcSubKind.Pet)
                    return "Pets";
                if (subKind == (byte)BattleNpcSubKind.Buddy)
                    return "Chocobos";
                if (subKind is (byte)BattleNpcSubKind.Player or (byte)BattleNpcSubKind.NpcPartyMember)
                    return "NPCs";
                if (subKind == (byte)BattleNpcSubKind.Combatant)
                    return "Enemies";
                return "none (battle NPC)";
            case ObjectKind.Companion:
                return "Minions";
            default:
                return "none";
        }
    }

    public bool IsHiddenByPlugin(nint address) => this.hidden.ContainsKey(address);

    public bool IsSynced(nint address) => this.syncedAddrs.Contains(address);

    public bool SyncedDataFresh()
    {
        long now = Environment.TickCount64;
        foreach (var source in this.syncSources)
        {
            if (source.LastOkAt != 0 && now - source.LastOkAt < SyncStaleAfterMs)
                return true;
        }
        return false;
    }

    public string SyncStatus
    {
        get
        {
            long now = Environment.TickCount64;
            var parts = new List<string>();
            var seen = new HashSet<string>();
            foreach (var source in this.syncSources)
            {
                if (source.LastOkAt == 0 || now - source.LastOkAt >= SyncStaleAfterMs || !seen.Add(source.Name))
                    continue;
                int total = 0;
                foreach (var other in this.syncSources)
                {
                    if (other.Name == source.Name && other.LastOkAt != 0 && now - other.LastOkAt < SyncStaleAfterMs)
                        total = Math.Max(total, other.LastCount);
                }
                parts.Add($"{source.Name}: {total} synced");
            }
            return parts.Count > 0 ? string.Join(" · ", parts) : "Snowcloak/PSync/Lightless: not detected";
        }
    }

    /// <summary>Throttled IPC poll of handled (synced) addresses from all sources. Fail-open.</summary>
    private void PollSyncedPlayers()
    {
        if (!this.config.HideNonSyncedPlayers)
            return;
        long now = Environment.TickCount64;
        if (now - this.lastSyncPoll < SyncPollIntervalMs)
            return;
        this.lastSyncPoll = now;
        var union = new HashSet<nint>();
        bool anyOk = false;
        foreach (var source in this.syncSources)
        {
            try
            {
                var addrs = source.Subscriber.InvokeFunc();
                if (addrs == null)
                    continue;
                union.UnionWith(addrs);
                source.LastCount = addrs.Count;
                source.LastOkAt = now;
                anyOk = true;
            }
            catch
            {
                // Provider missing (not installed/loading) or call failed.
            }
        }
        if (anyOk)
            this.syncedAddrs = union;
    }

    /// <summary>Restore every object this plugin hid. Safe to call from Dispose.</summary>
    public void ShowAll()
    {
        try
        {
            if (this.hidden.Count == 0)
                return;

            foreach (var obj in Service.ObjectTable)
            {
                if (obj == null)
                    continue;
                if (!this.hidden.TryGetValue(obj.Address, out var mine))
                    continue;
                unsafe
                {
                    var go = (GameObject*)obj.Address;
                    if (go == null)
                        continue;
                    if (mine != VisibilityFlags.None)
                        go->RenderFlags &= ~mine;
                }
                this.hidden.Remove(obj.Address);
            }

            // Whatever is left despawned; nothing left to restore.
            this.hidden.Clear();
        }
        catch
        {
            // Best effort only.
        }
    }

    public void Dispose()
    {
        if (this.disposed)
            return;
        this.disposed = true;
        this.ShowAll();
    }
}
