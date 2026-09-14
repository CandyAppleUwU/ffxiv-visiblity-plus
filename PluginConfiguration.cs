using System.Collections.Generic;
using Dalamud.Configuration;

namespace VisibilityPlus;

public class PluginConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled = true;

    public bool HideNpcs;
    public bool HideEnemies;
    public bool HideMinions;
    public bool HidePets;
    public bool HideChocobos;

    /// <summary>When true, NPCs showing a name (nameplate) are skipped; only nameless ones hide.</summary>
    public bool OnlyUnnamedNpcs = true;

    /// <summary>Hold-keybind (VK code, 0 = unbound): while held, named NPCs reappear.</summary>
    public int HoldKey;
    public bool HoldCtrl;
    public bool HoldShift;
    public bool HoldAlt;

    /// <summary>Hold-keybind: while held, hidden enemies reappear. May share a key with other groups.</summary>
    public int HoldKeyEnemies;
    public bool HoldCtrlEnemies;
    public bool HoldShiftEnemies;
    public bool HoldAltEnemies;

    /// <summary>Hold-keybind: while held, hidden minions reappear. May share a key with other groups.</summary>
    public int HoldKeyMinions;
    public bool HoldCtrlMinions;
    public bool HoldShiftMinions;
    public bool HoldAltMinions;

    /// <summary>Hold-keybind: while held, hidden pets reappear. May share a key with other groups.</summary>
    public int HoldKeyPets;
    public bool HoldCtrlPets;
    public bool HoldShiftPets;
    public bool HoldAltPets;

    /// <summary>Hold-keybind: while held, hidden chocobos reappear. May share a key with other groups.</summary>
    public int HoldKeyChocobos;
    public bool HoldCtrlChocobos;
    public bool HoldShiftChocobos;
    public bool HoldAltChocobos;

    /// <summary>Hide every player Snowcloak is not syncing (except yourself).</summary>
    public bool HideNonSyncedPlayers;

    /// <summary>Widens Non-Synced Players to every player (except yourself).</summary>
    public bool HideAllPlayers;

    /// <summary>Hold-keybind: while held, hidden non-synced players reappear. May share a key.</summary>
    public int HoldKeyPlayers;
    public bool HoldCtrlPlayers;
    public bool HoldShiftPlayers;
    public bool HoldAltPlayers;

    public HotkeyMode HotkeyMode;

    /// <summary>TerritoryType RowIds the hiding applies to. Empty = hiding applies nowhere.</summary>
    public List<uint> ZoneIds { get; set; } = [];

    /// <summary>Provenance stamp so shared configs can be traced to a build.</summary>
    public string BuildTag = "0.1.0.17";

    public void Save() => Service.PluginInterface.SavePluginConfig(this);
}
