using System.Collections.Generic;

namespace VisibilityPlus;

/// <summary>Per-zone hide/keep overrides. Inert unless <see cref="UseCustom"/> is set.</summary>
public class ZoneOverride : IHideSettings
{
    public bool UseCustom;
    public bool HideNpcs { get; set; }
    public bool OnlyUnnamedNpcs { get; set; } = true;
    public bool HideEnemies { get; set; }
    public bool HideMinions { get; set; }
    public bool HidePets { get; set; }
    public bool HideChocobos { get; set; }
    public bool HideNonSyncedPlayers { get; set; }
    public bool HideAllPlayers { get; set; }
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
    public bool KeepAggroEnemies { get; set; }
    public bool KeepQuestGivers { get; set; }
    public bool HideOwnMinions { get; set; }
    public bool HideOwnPets { get; set; }
    public bool HideOwnChocobos { get; set; }

    public void CopyFrom(IHideSettings src)
    {
        this.HideNpcs = src.HideNpcs;
        this.OnlyUnnamedNpcs = src.OnlyUnnamedNpcs;
        this.HideEnemies = src.HideEnemies;
        this.HideMinions = src.HideMinions;
        this.HidePets = src.HidePets;
        this.HideChocobos = src.HideChocobos;
        this.HideNonSyncedPlayers = src.HideNonSyncedPlayers;
        this.HideAllPlayers = src.HideAllPlayers;
        this.KeepFriendPlayers = src.KeepFriendPlayers;
        this.KeepPartyPlayers = src.KeepPartyPlayers;
        this.KeepFcPlayers = src.KeepFcPlayers;
        this.KeepFriendMinions = src.KeepFriendMinions;
        this.KeepPartyMinions = src.KeepPartyMinions;
        this.KeepFcMinions = src.KeepFcMinions;
        this.KeepFriendPets = src.KeepFriendPets;
        this.KeepPartyPets = src.KeepPartyPets;
        this.KeepFcPets = src.KeepFcPets;
        this.KeepFriendChocobos = src.KeepFriendChocobos;
        this.KeepPartyChocobos = src.KeepPartyChocobos;
        this.KeepFcChocobos = src.KeepFcChocobos;
        this.KeepAggroEnemies = src.KeepAggroEnemies;
        this.KeepQuestGivers = src.KeepQuestGivers;
        this.HideOwnMinions = src.HideOwnMinions;
        this.HideOwnPets = src.HideOwnPets;
        this.HideOwnChocobos = src.HideOwnChocobos;
    }

    /// <summary>Snapshot of the current globals; the zone's starting defaults.</summary>
    public static ZoneOverride FromGlobals(IHideSettings src)
    {
        var ov = new ZoneOverride();
        ov.CopyFrom(src);
        return ov;
    }
}
