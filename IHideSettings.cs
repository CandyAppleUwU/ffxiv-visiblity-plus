namespace VisibilityPlus;

/// <summary>
/// The hide/keep toggles. Implemented by the global <see cref="PluginConfiguration"/>
/// and by per-zone <see cref="ZoneOverride"/>s so both windows share one UI.
/// Hotkeys, the zone list and the master switch stay global.
/// </summary>
public interface IHideSettings
{
    bool HideNpcs { get; set; }
    bool OnlyUnnamedNpcs { get; set; }
    bool HideEnemies { get; set; }
    bool HideMinions { get; set; }
    bool HidePets { get; set; }
    bool HideChocobos { get; set; }
    bool HideNonSyncedPlayers { get; set; }
    bool HideAllPlayers { get; set; }
    bool KeepFriendPlayers { get; set; }
    bool KeepPartyPlayers { get; set; }
    bool KeepFcPlayers { get; set; }
    bool KeepFriendMinions { get; set; }
    bool KeepPartyMinions { get; set; }
    bool KeepFcMinions { get; set; }
    bool KeepFriendPets { get; set; }
    bool KeepPartyPets { get; set; }
    bool KeepFcPets { get; set; }
    bool KeepFriendChocobos { get; set; }
    bool KeepPartyChocobos { get; set; }
    bool KeepFcChocobos { get; set; }
    bool KeepAggroEnemies { get; set; }
    bool KeepQuestGivers { get; set; }
    bool HideOwnMinions { get; set; }
    bool HideOwnPets { get; set; }
    bool HideOwnChocobos { get; set; }
}
