using System.Collections.Generic;
using Dalamud.Configuration;

namespace VisibilityPlus;

public class PluginConfiguration : IPluginConfiguration, IHideSettings
{
    public int Version { get; set; } = 1;

    public bool Enabled = true;

    public int EnabledToggleKey;
    public bool EnabledToggleCtrl;
    public bool EnabledToggleShift;
    public bool EnabledToggleAlt;

    public bool HideNpcs { get; set; }
    public bool HideEnemies { get; set; }
    public bool HideMinions { get; set; }
    public bool HidePets { get; set; }
    public bool HideChocobos { get; set; }

    /// <summary>When true, NPCs showing a name (nameplate) are skipped; only nameless ones hide.</summary>
    public bool OnlyUnnamedNpcs { get; set; } = true;

    // Keep-lists: exempt friends / party / FC (or their minions, pets, chocobos) from hiding.
    public bool KeepFriendPlayers { get; set; }
    public bool KeepPartyPlayers { get; set; }
    public bool KeepFcPlayers { get; set; }
    public bool KeepFriendMinions { get; set; }
    public bool KeepPartyMinions { get; set; }
    public bool KeepFcMinions { get; set; }
    public bool KeepFriendPets { get; set; }
    public bool KeepPartyPets { get; set; }
    public bool KeepFcPets { get; set; }
    public bool KeepFriendChocobos { get; set; }
    public bool KeepPartyChocobos { get; set; }
    public bool KeepFcChocobos { get; set; }

    /// <summary>Keep enemies within 10 levels below you (or any level above).</summary>
    public bool KeepAggroEnemies { get; set; }

    /// <summary>Keep NPCs showing a quest marker (!/?).</summary>
    public bool KeepQuestGivers { get; set; }

    public bool HideOwnMinions { get; set; }
    public bool HideOwnPets { get; set; }
    public bool HideOwnChocobos { get; set; }

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
    public bool HideNonSyncedPlayers { get; set; }

    /// <summary>Widens Non-Synced Players to every player (except yourself).</summary>
    public bool HideAllPlayers { get; set; }

    /// <summary>Hold-keybind: while held, hidden non-synced players reappear. May share a key.</summary>
    public int HoldKeyPlayers;
    public bool HoldCtrlPlayers;
    public bool HoldShiftPlayers;
    public bool HoldAltPlayers;

    /// <summary>Hotkey: red dots mark hidden players. Follows Hotkey Mode like group hotkeys.</summary>
    public int DotsKey;
    public bool DotsCtrl;
    public bool DotsShift;
    public bool DotsAlt;

    /// <summary>One voidlisted player: always hidden while VoidEnabled.</summary>
    public class VoidEntry
    {
        public string Name { get; set; } = string.Empty;
        public ushort World { get; set; }
    }

    public HotkeyMode HotkeyMode;

    /// <summary>Voidlist master switch.</summary>
    public bool VoidEnabled;

    public List<VoidEntry> VoidList { get; set; } = [];

    /// <summary>Hold 3s on a targeted player to voidlist them.</summary>
    public int VoidKey;
    public bool VoidCtrl;
    public bool VoidShift;
    public bool VoidAlt;

    /// <summary>When true, the hold-to-void uses mouse hover target instead of hard target.</summary>
    public bool VoidOnMouseHover;

    /// <summary>While bound by a duty, voidlisted players stay visible. On by default.</summary>
    public bool VoidDontHideInDuty = true;

    /// <summary>Drag-select master switch: hold the bind, paint a box, release to voidlist everyone inside.</summary>
    public bool VoidUltimateEnabled;

    /// <summary>Hold this combo and move the mouse to paint the selection box. Release to confirm, right-click to cancel.</summary>
    public int VoidUltimateKey;
    public bool VoidUltimateCtrl;
    public bool VoidUltimateShift;
    public bool VoidUltimateAlt;

    // WhiteList: same shape as VoidList but never hidden, no VFX, green hold rect.
    public class WhiteEntry
    {
        public string Name { get; set; } = string.Empty;
        public ushort World { get; set; }
    }

    public bool WhiteEnabled;

    public List<WhiteEntry> WhiteList { get; set; } = [];

    public int WhiteKey;
    public bool WhiteCtrl;
    public bool WhiteShift;
    public bool WhiteAlt;

    public bool WhiteOnMouseHover;

    /// <summary>TerritoryType RowIds the hiding applies to. Empty = hiding applies nowhere.</summary>
    public List<uint> ZoneIds { get; set; } = [];

    /// <summary>Per-zone overrides, keyed by TerritoryType. Inert unless enabled per zone.</summary>
    public Dictionary<uint, ZoneOverride> ZoneOverrides { get; set; } = [];

    /// <summary>Provenance stamp so shared configs can be traced to a build.</summary>
    public string BuildTag = "0.1.0.23";

    public void Save() => Service.PluginInterface.SavePluginConfig(this);
}
