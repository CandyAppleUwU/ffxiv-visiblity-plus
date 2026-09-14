using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace VisibilityPlus;

/// <summary>Keybind-capture state, owned by the main window (zone windows have no hotkeys).</summary>
public sealed class HoldCaptureState
{
    public string? ListeningSlot;
}

/// <summary>Hide-group rows shared by the main window and per-zone windows.</summary>
public static class HideGroupsUI
{
    /// <returns>True if anything was toggled (caller writes the locals back into the settings).</returns>
    public static bool DrawKeepPopup(int cogId, string popupId, ref bool keepFriend, ref bool keepParty, ref bool keepFc, PluginConfiguration cfg)
    {
        bool changed = false;
        if (ImGuiComponents.IconButton(cogId, FontAwesomeIcon.Cog))
            ImGui.OpenPopup(popupId);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Keep friends, party or FC members.");
        if (ImGui.BeginPopup(popupId))
        {
            ImGui.Text("Keep:");
            if (ImGui.Checkbox("Friends", ref keepFriend))
            {
                cfg.Save();
                changed = true;
            }
            if (ImGui.Checkbox("Party Members", ref keepParty))
            {
                cfg.Save();
                changed = true;
            }
            if (ImGui.Checkbox("FC Members", ref keepFc))
            {
                cfg.Save();
                changed = true;
            }
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        return changed;
    }

    public static void DrawHoldKeybind(string slotId, ref int key, ref bool ctrl, ref bool shift, ref bool alt, HoldCaptureState hold, PluginConfiguration cfg, string? hintOverride = null)
    {
        ImGui.SameLine();
        if (hold.ListeningSlot != slotId)
        {
            string bindLabel = key == 0
                ? "Set hold-key..."
                : "Hold: " + HoldKeybind.ComboName(key, ctrl, shift, alt);
            ImGui.Button(bindLabel + "##holdkey" + slotId);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                hold.ListeningSlot = slotId;
            else if (key != 0 && ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                key = 0;
                ctrl = shift = alt = false;
                cfg.Save();
            }
            if (ImGui.IsItemHovered())
            {
                if (hintOverride != null)
                {
                    ImGui.SetTooltip(hintOverride);
                }
                else
                {
                    string behavior = cfg.HotkeyMode switch
                    {
                        HotkeyMode.Toggle => "Press to show/hide this group.",
                        HotkeyMode.Toggle30s => "Press to show this group for 30 seconds.",
                        _ => "While held, this group reappears.",
                    };
                    ImGui.SetTooltip("Left-click, then press a key combo.\n" + behavior + "\nGroups may share the same key.\nRight-click to clear.");
                }
            }
        }
        else
        {
            if (ImGui.Button("Press keys... (Esc cancels)##holdkey" + slotId))
                hold.ListeningSlot = null;
            else if (HoldKeybind.IsDown(HoldKeybind.VK_ESCAPE))
                hold.ListeningSlot = null;
            else if (HoldKeybind.TryCapture(out int captured, out bool c, out bool s, out bool a))
            {
                key = captured;
                ctrl = c;
                shift = s;
                alt = a;
                cfg.Save();
                hold.ListeningSlot = null;
            }
        }
    }

    private static void DrawKeeps(int cogId, string popupId, IHideSettings s, PluginConfiguration cfg,
        bool keepF, bool keepP, bool keepC,
        System.Action<bool, bool, bool> apply)
    {
        bool f = keepF, p = keepP, c = keepC;
        if (DrawKeepPopup(cogId, popupId, ref f, ref p, ref c, cfg))
            apply(f, p, c);
    }

    /// <param name="hold">Null in per-zone windows (hotkeys stay global).</param>
    /// <param name="ctl">Null in per-zone windows (no sync status there).</param>
    public static void DrawGroups(IHideSettings s, PluginConfiguration cfg, VisibilityController? ctl, HoldCaptureState? hold, string idSuffix)
    {
        if (ImGuiComponents.IconButton(6, FontAwesomeIcon.Cog))
            ImGui.OpenPopup("keepnpcs" + idSuffix);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Keep NPCs that have quests for you.");
        if (ImGui.BeginPopup("keepnpcs" + idSuffix))
        {
            ImGui.Text("Keep:");
            bool keepQuests = s.KeepQuestGivers;
            if (ImGui.Checkbox("Quest Givers", ref keepQuests))
            {
                s.KeepQuestGivers = keepQuests;
                cfg.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("NPCs showing a quest marker (!/?) stay visible.");
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        bool hideNpcs = s.HideNpcs;
        if (ImGui.Checkbox("No-Name NPCs", ref hideNpcs))
        {
            s.HideNpcs = hideNpcs;
            cfg.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hide NPCs: quest givers, vendors, guards and other friendly non-player characters.\nEnemies and other battle NPCs are never touched.");
        ImGui.SameLine();
        ImGui.BeginDisabled(!s.HideNpcs);
        bool allNpcs = s.HideNpcs && !s.OnlyUnnamedNpcs;
        if (ImGui.Checkbox("All NPCs", ref allNpcs))
        {
            s.OnlyUnnamedNpcs = !allNpcs;
            cfg.Save();
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Also hide NPCs showing a name.\nOnly available while No-Name NPCs is on.");
        if (hold != null)
            DrawHoldKeybind("npcs" + idSuffix, ref cfg.HoldKey, ref cfg.HoldCtrl, ref cfg.HoldShift, ref cfg.HoldAlt, hold, cfg);

        if (ImGuiComponents.IconButton(5, FontAwesomeIcon.Cog))
            ImGui.OpenPopup("keepenemies" + idSuffix);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Keep enemies that can still aggro you.");
        if (ImGui.BeginPopup("keepenemies" + idSuffix))
        {
            ImGui.Text("Keep:");
            bool keepAggro = s.KeepAggroEnemies;
            if (ImGui.Checkbox("Aggro range", ref keepAggro))
            {
                s.KeepAggroEnemies = keepAggro;
                cfg.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Enemies within 10 levels below you, or any level above,\nstay visible. Lower ones hide.");
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        bool hideEnemies = s.HideEnemies;
        if (ImGui.Checkbox("Enemies", ref hideEnemies))
        {
            s.HideEnemies = hideEnemies;
            cfg.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Enemy battle NPCs.\nNote: city guards share this type and hide too.");
        if (hold != null)
            DrawHoldKeybind("enemies" + idSuffix, ref cfg.HoldKeyEnemies, ref cfg.HoldCtrlEnemies, ref cfg.HoldShiftEnemies, ref cfg.HoldAltEnemies, hold, cfg);

        DrawKeeps(1, "keepminions" + idSuffix, s, cfg,
            s.KeepFriendMinions, s.KeepPartyMinions, s.KeepFcMinions,
            (f, p, c) => { s.KeepFriendMinions = f; s.KeepPartyMinions = p; s.KeepFcMinions = c; });
        bool hideMinions = s.HideMinions;
        if (ImGui.Checkbox("Minions", ref hideMinions))
        {
            s.HideMinions = hideMinions;
            cfg.Save();
        }
        if (hold != null)
            DrawHoldKeybind("minions" + idSuffix, ref cfg.HoldKeyMinions, ref cfg.HoldCtrlMinions, ref cfg.HoldShiftMinions, ref cfg.HoldAltMinions, hold, cfg);

        DrawKeeps(2, "keeppets" + idSuffix, s, cfg,
            s.KeepFriendPets, s.KeepPartyPets, s.KeepFcPets,
            (f, p, c) => { s.KeepFriendPets = f; s.KeepPartyPets = p; s.KeepFcPets = c; });
        bool hidePets = s.HidePets;
        if (ImGui.Checkbox("Pets", ref hidePets))
        {
            s.HidePets = hidePets;
            cfg.Save();
        }
        if (hold != null)
            DrawHoldKeybind("pets" + idSuffix, ref cfg.HoldKeyPets, ref cfg.HoldCtrlPets, ref cfg.HoldShiftPets, ref cfg.HoldAltPets, hold, cfg);

        DrawKeeps(3, "keepchocobos" + idSuffix, s, cfg,
            s.KeepFriendChocobos, s.KeepPartyChocobos, s.KeepFcChocobos,
            (f, p, c) => { s.KeepFriendChocobos = f; s.KeepPartyChocobos = p; s.KeepFcChocobos = c; });
        bool hideChocobos = s.HideChocobos;
        if (ImGui.Checkbox("Chocobos", ref hideChocobos))
        {
            s.HideChocobos = hideChocobos;
            cfg.Save();
        }
        if (hold != null)
            DrawHoldKeybind("chocobos" + idSuffix, ref cfg.HoldKeyChocobos, ref cfg.HoldCtrlChocobos, ref cfg.HoldShiftChocobos, ref cfg.HoldAltChocobos, hold, cfg);

        DrawKeeps(4, "keepplayers" + idSuffix, s, cfg,
            s.KeepFriendPlayers, s.KeepPartyPlayers, s.KeepFcPlayers,
            (f, p, c) => { s.KeepFriendPlayers = f; s.KeepPartyPlayers = p; s.KeepFcPlayers = c; });
        bool hidePlayers = s.HideNonSyncedPlayers;
        if (ImGui.Checkbox("Non-Synced Players", ref hidePlayers))
        {
            s.HideNonSyncedPlayers = hidePlayers;
            cfg.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hide every player none of Snowcloak, PSync or Lightless is syncing.\nYourself is never hidden.\nNeeds one of them running.");
        ImGui.SameLine();
        ImGui.BeginDisabled(!s.HideNonSyncedPlayers);
        bool allPlayers = s.HideNonSyncedPlayers && s.HideAllPlayers;
        if (ImGui.Checkbox("All Players", ref allPlayers))
        {
            s.HideAllPlayers = allPlayers;
            cfg.Save();
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Also hide synced players.\nOnly available while Non-Synced Players is on.");
        if (hold != null)
            DrawHoldKeybind("players" + idSuffix, ref cfg.HoldKeyPlayers, ref cfg.HoldCtrlPlayers, ref cfg.HoldShiftPlayers, ref cfg.HoldAltPlayers, hold, cfg);

        if (hold != null && ctl != null)
            ImGui.TextDisabled(ctl.SyncStatus);

        ImGui.TextDisabled("Your own minion, pet and chocobo are never hidden.");
    }
}
