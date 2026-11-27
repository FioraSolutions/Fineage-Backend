namespace Fineage.Zone.Security;

/// <summary>
/// The single role map for every registered admin surface. Dispatch and the Net admin opcode handlers
/// consult this catalog instead of testing names or command strings inline. A new command must be added
/// here; the coverage test walks the catalog and the dispatcher so an unregistered verb cannot become a
/// role-policy hole.
/// </summary>
public sealed record AdminCommandSpec(
    string Name,
    AdminRole MinimumRole,
    AdminCommandSurface Surface,
    string Description,
    AdminCommandFamily Family = AdminCommandFamily.Core);

public enum AdminCommandSurface
{
    /// <summary>Mobius <c>admin_*</c> bypass verb sent through the HTML bypass opcode.</summary>
    Bypass,
    /// <summary>Chat verb handled by the GM chat dispatcher (<c>//kick</c>, …).</summary>
    Chat,
    /// <summary>Dedicated Net admin opcode.</summary>
    NetOpcode
}

/// <summary>
/// The handler family a bypass verb routes to. Registration in the catalog is what makes a verb
/// reachable: <see cref="Fineage.Zone.World.ZoneWorld"/> dispatches by this family instead of keeping
/// a second verb list, so a new verb cannot be handled without also passing the role gate.
/// </summary>
public enum AdminCommandFamily
{
    Core,
    Punishment,
    Festival,
    Teleport,
    Combat,
    Announce,
    Status,
    Petition,
    Ride
}

public static class AdminCommandCatalog
{
    static readonly AdminCommandSpec[] BypassCommands =
    [
        new("admin_gmshop", AdminRole.Gm, AdminCommandSurface.Bypass, "open the GM shop"),
        new("admin_admin", AdminRole.Gm, AdminCommandSurface.Bypass, "open the GM shop alias"),
        new("admin_admin7", AdminRole.Gm, AdminCommandSurface.Bypass, "open the GM shop alias"),
        new("admin_html", AdminRole.Helper, AdminCommandSurface.Bypass, "open an admin HTML page"),
        new("admin_buy", AdminRole.Admin, AdminCommandSurface.Bypass, "open a free admin buy list"),
        new("admin_create_item", AdminRole.Admin, AdminCommandSurface.Bypass, "create an item"),
        new("admin_give", AdminRole.Admin, AdminCommandSurface.Bypass, "grant an item"),
        new("admin_giveitem", AdminRole.Admin, AdminCommandSurface.Bypass, "grant an item alias"),
        new("admin_setlevel", AdminRole.Gm, AdminCommandSurface.Bypass, "set a level"),
        new("admin_set_level", AdminRole.Gm, AdminCommandSurface.Bypass, "set a level alias"),
        new("admin_addlevel", AdminRole.Gm, AdminCommandSurface.Bypass, "add levels"),
        new("admin_add_level", AdminRole.Gm, AdminCommandSurface.Bypass, "add levels alias"),
        new("admin_setclass", AdminRole.Gm, AdminCommandSurface.Bypass, "set a class"),
        new("admin_buff", AdminRole.Gm, AdminCommandSurface.Bypass, "apply the GM buff pack"),
        new("admin_buffs", AdminRole.Gm, AdminCommandSurface.Bypass, "apply the GM buff pack alias"),
        new("admin_mbuff", AdminRole.Gm, AdminCommandSurface.Bypass, "apply the mage buff pack"),
        new("admin_magebuff", AdminRole.Gm, AdminCommandSurface.Bypass, "apply the mage buff pack alias"),
        new("admin_fbuff", AdminRole.Gm, AdminCommandSurface.Bypass, "apply the fighter buff pack"),
        new("admin_fighterbuff", AdminRole.Gm, AdminCommandSurface.Bypass, "apply the fighter buff pack alias"),
        new("admin_buff_clear", AdminRole.Gm, AdminCommandSurface.Bypass, "clear buffs"),
        new("admin_clearbuffs", AdminRole.Gm, AdminCommandSurface.Bypass, "clear buffs alias"),
        new("admin_spawn", AdminRole.Gm, AdminCommandSurface.Bypass, "spawn NPCs"),
        new("admin_spawn_monster", AdminRole.Gm, AdminCommandSurface.Bypass, "spawn NPCs alias"),
        new("admin_kick", AdminRole.Gm, AdminCommandSurface.Bypass, "kick a player"),
        new("admin_jail", AdminRole.Gm, AdminCommandSurface.Bypass, "jail a character", AdminCommandFamily.Punishment),
        new("admin_unjail", AdminRole.Gm, AdminCommandSurface.Bypass, "release a character from jail", AdminCommandFamily.Punishment),
        new("admin_ban_chat", AdminRole.Gm, AdminCommandSurface.Bypass, "ban chat for a character", AdminCommandFamily.Punishment),
        new("admin_unban_chat", AdminRole.Gm, AdminCommandSurface.Bypass, "unban chat for a character", AdminCommandFamily.Punishment),
        new("admin_ban_char", AdminRole.Gm, AdminCommandSurface.Bypass, "ban a character", AdminCommandFamily.Punishment),
        new("admin_unban_char", AdminRole.Gm, AdminCommandSurface.Bypass, "unban a character", AdminCommandFamily.Punishment),
        new("admin_ban_acc", AdminRole.Admin, AdminCommandSurface.Bypass, "ban an account", AdminCommandFamily.Punishment),
        new("admin_unban_acc", AdminRole.Admin, AdminCommandSurface.Bypass, "unban an account", AdminCommandFamily.Punishment),
        new("admin_festival_enter", AdminRole.Gm, AdminCommandSurface.Bypass, "enter the festival", AdminCommandFamily.Festival),
        new("admin_festival_end", AdminRole.Gm, AdminCommandSurface.Bypass, "end the festival", AdminCommandFamily.Festival),
        new("admin_show_moves", AdminRole.Helper, AdminCommandSurface.Bypass, "open the teleport menu", AdminCommandFamily.Teleport),
        new("admin_show_moves_other", AdminRole.Helper, AdminCommandSurface.Bypass, "open the other-player teleport menu", AdminCommandFamily.Teleport),
        new("admin_show_teleport", AdminRole.Helper, AdminCommandSurface.Bypass, "show the teleport-character form", AdminCommandFamily.Teleport),
        new("admin_tele", AdminRole.Helper, AdminCommandSurface.Bypass, "open the move menu", AdminCommandFamily.Teleport),
        new("admin_move_to", AdminRole.Gm, AdminCommandSurface.Bypass, "teleport self to coordinates", AdminCommandFamily.Teleport),
        new("admin_teleportto", AdminRole.Gm, AdminCommandSurface.Bypass, "teleport self to a player", AdminCommandFamily.Teleport),
        new("admin_teleport_to_character", AdminRole.Gm, AdminCommandSurface.Bypass, "teleport self to the selected character", AdminCommandFamily.Teleport),
        new("admin_teleport_character", AdminRole.Gm, AdminCommandSurface.Bypass, "teleport the selected character", AdminCommandFamily.Teleport),
        new("admin_recall", AdminRole.Gm, AdminCommandSurface.Bypass, "recall a player", AdminCommandFamily.Teleport),
        new("admin_sendhome", AdminRole.Gm, AdminCommandSurface.Bypass, "send a player home", AdminCommandFamily.Teleport),
        new("admin_gonorth", AdminRole.Gm, AdminCommandSurface.Bypass, "move north", AdminCommandFamily.Teleport),
        new("admin_gosouth", AdminRole.Gm, AdminCommandSurface.Bypass, "move south", AdminCommandFamily.Teleport),
        new("admin_goeast", AdminRole.Gm, AdminCommandSurface.Bypass, "move east", AdminCommandFamily.Teleport),
        new("admin_gowest", AdminRole.Gm, AdminCommandSurface.Bypass, "move west", AdminCommandFamily.Teleport),
        new("admin_goup", AdminRole.Gm, AdminCommandSurface.Bypass, "move up", AdminCommandFamily.Teleport),
        new("admin_godown", AdminRole.Gm, AdminCommandSurface.Bypass, "move down", AdminCommandFamily.Teleport),

        // Mobius AdminHeal / AdminKill / AdminRes / AdminDelete.
        new("admin_heal", AdminRole.Gm, AdminCommandSurface.Bypass, "heal self, a target or a radius", AdminCommandFamily.Combat),
        new("admin_kill", AdminRole.Gm, AdminCommandSurface.Bypass, "kill a target or a radius", AdminCommandFamily.Combat),
        new("admin_kill_monster", AdminRole.Gm, AdminCommandSurface.Bypass, "kill monsters in a radius", AdminCommandFamily.Combat),
        new("admin_res", AdminRole.Gm, AdminCommandSurface.Bypass, "resurrect a player or a radius", AdminCommandFamily.Combat),
        new("admin_res_monster", AdminRole.Gm, AdminCommandSurface.Bypass, "resurrect a monster target", AdminCommandFamily.Combat),
        new("admin_delete", AdminRole.Gm, AdminCommandSurface.Bypass, "delete NPCs", AdminCommandFamily.Combat),

        // Mobius AdminAnnounce.
        new("admin_announce", AdminRole.Gm, AdminCommandSurface.Bypass, "announce to all players", AdminCommandFamily.Announce),
        new("admin_announce_crit", AdminRole.Gm, AdminCommandSurface.Bypass, "critical announce to all players", AdminCommandFamily.Announce),

        // Mobius AdminAdmin (online report, invulnerability, hide/invisible).
        new("admin_online", AdminRole.Helper, AdminCommandSurface.Bypass, "report online players", AdminCommandFamily.Status),
        new("admin_invul", AdminRole.Gm, AdminCommandSurface.Bypass, "toggle invulnerability", AdminCommandFamily.Status),
        new("admin_setinvul", AdminRole.Gm, AdminCommandSurface.Bypass, "toggle invulnerability on a target", AdminCommandFamily.Status),
        new("admin_hide", AdminRole.Gm, AdminCommandSurface.Bypass, "hide or show self", AdminCommandFamily.Status),
        new("admin_invis", AdminRole.Gm, AdminCommandSurface.Bypass, "become invisible", AdminCommandFamily.Status),
        new("admin_invisible", AdminRole.Gm, AdminCommandSurface.Bypass, "become invisible alias", AdminCommandFamily.Status),
        new("admin_setinvis", AdminRole.Gm, AdminCommandSurface.Bypass, "become invisible alias", AdminCommandFamily.Status),
        new("admin_vis", AdminRole.Gm, AdminCommandSurface.Bypass, "become visible", AdminCommandFamily.Status),
        new("admin_visible", AdminRole.Gm, AdminCommandSurface.Bypass, "become visible alias", AdminCommandFamily.Status),

        // Mobius AdminPetition.
        new("admin_view_petitions", AdminRole.Helper, AdminCommandSurface.Bypass, "list pending petitions", AdminCommandFamily.Petition),
        new("admin_view_petition", AdminRole.Helper, AdminCommandSurface.Bypass, "show one petition", AdminCommandFamily.Petition),
        new("admin_accept_petition", AdminRole.Gm, AdminCommandSurface.Bypass, "accept a petition", AdminCommandFamily.Petition),
        new("admin_reject_petition", AdminRole.Gm, AdminCommandSurface.Bypass, "reject a petition", AdminCommandFamily.Petition),
        new("admin_reset_petitions", AdminRole.Gm, AdminCommandSurface.Bypass, "reset pending petitions", AdminCommandFamily.Petition),
        new("admin_force_peti", AdminRole.Gm, AdminCommandSurface.Bypass, "force a petition for a player", AdminCommandFamily.Petition),

        // Mobius AdminRide.
        new("admin_ride_wyvern", AdminRole.Gm, AdminCommandSurface.Bypass, "ride a wyvern", AdminCommandFamily.Ride),
        new("admin_ride_strider", AdminRole.Gm, AdminCommandSurface.Bypass, "ride a strider", AdminCommandFamily.Ride),
        new("admin_unride_wyvern", AdminRole.Gm, AdminCommandSurface.Bypass, "dismount a wyvern", AdminCommandFamily.Ride),
        new("admin_unride_strider", AdminRole.Gm, AdminCommandSurface.Bypass, "dismount a strider", AdminCommandFamily.Ride),
        new("admin_unride", AdminRole.Gm, AdminCommandSurface.Bypass, "dismount", AdminCommandFamily.Ride)
    ];

    static readonly AdminCommandSpec[] ChatCommands =
    [
        new("teleport", AdminRole.Gm, AdminCommandSurface.Chat, "teleport self to coordinates"),
        new("tele", AdminRole.Gm, AdminCommandSurface.Chat, "teleport alias"),
        new("goto", AdminRole.Gm, AdminCommandSurface.Chat, "teleport self to a player"),
        new("recall", AdminRole.Gm, AdminCommandSurface.Chat, "recall a player"),
        new("loc", AdminRole.Helper, AdminCommandSurface.Chat, "report own coordinates"),
        new("town", AdminRole.Gm, AdminCommandSurface.Chat, "teleport to a town"),
        new("towns", AdminRole.Helper, AdminCommandSurface.Chat, "list towns"),
        new("kick", AdminRole.Gm, AdminCommandSurface.Chat, "kick a player")
    ];

    static readonly AdminCommandSpec[] NetCommands =
    [
        new("AdminBossSchedule", AdminRole.Admin, AdminCommandSurface.NetOpcode, "kill / force-spawn a boss"),
        new("AdminCaptureCastle", AdminRole.Admin, AdminCommandSurface.NetOpcode, "capture a castle or clan hall"),
        new("AdminReportOlympiadResult", AdminRole.Admin, AdminCommandSurface.NetOpcode, "report an Olympiad result")
    ];

    static readonly Dictionary<string, AdminCommandSpec> BypassByName =
        BypassCommands.ToDictionary(spec => spec.Name, StringComparer.OrdinalIgnoreCase);

    static readonly Dictionary<string, AdminCommandSpec> ChatByName =
        ChatCommands.ToDictionary(spec => spec.Name, StringComparer.OrdinalIgnoreCase);

    static readonly Dictionary<string, AdminCommandSpec> NetByName =
        NetCommands.ToDictionary(spec => spec.Name, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<AdminCommandSpec> AllBypassCommands => BypassCommands;
    public static IReadOnlyList<AdminCommandSpec> AllChatCommands => ChatCommands;
    public static IReadOnlyList<AdminCommandSpec> AllNetCommands => NetCommands;

    public static bool TryGetBypass(string? verb, out AdminCommandSpec spec)
    {
        spec = null!;
        return !string.IsNullOrWhiteSpace(verb) && BypassByName.TryGetValue(verb.Trim(), out spec!);
    }

    public static bool TryGetChat(string? verb, out AdminCommandSpec spec)
    {
        spec = null!;
        return !string.IsNullOrWhiteSpace(verb) && ChatByName.TryGetValue(verb.Trim(), out spec!);
    }

    public static bool TryGetNet(string? name, out AdminCommandSpec spec)
    {
        spec = null!;
        return !string.IsNullOrWhiteSpace(name) && NetByName.TryGetValue(name.Trim(), out spec!);
    }

    /// <summary>Role ordering check used by every dispatcher.</summary>
    public static bool Allows(AdminRole actual, AdminRole required) => actual >= required;

    public static bool Allows(AdminRole actual, string? bypassVerb) =>
        TryGetBypass(bypassVerb, out var spec) && Allows(actual, spec.MinimumRole);
}
