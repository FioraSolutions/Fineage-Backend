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
    string Description);

public enum AdminCommandSurface
{
    /// <summary>Mobius <c>admin_*</c> bypass verb sent through the HTML bypass opcode.</summary>
    Bypass,
    /// <summary>Chat verb handled by the GM chat dispatcher (<c>//kick</c>, …).</summary>
    Chat,
    /// <summary>Dedicated Net admin opcode.</summary>
    NetOpcode
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
        new("admin_jail", AdminRole.Gm, AdminCommandSurface.Bypass, "jail a character"),
        new("admin_unjail", AdminRole.Gm, AdminCommandSurface.Bypass, "release a character from jail"),
        new("admin_ban_chat", AdminRole.Gm, AdminCommandSurface.Bypass, "ban chat for a character"),
        new("admin_unban_chat", AdminRole.Gm, AdminCommandSurface.Bypass, "unban chat for a character"),
        new("admin_ban_char", AdminRole.Gm, AdminCommandSurface.Bypass, "ban a character"),
        new("admin_unban_char", AdminRole.Gm, AdminCommandSurface.Bypass, "unban a character"),
        new("admin_ban_acc", AdminRole.Admin, AdminCommandSurface.Bypass, "ban an account"),
        new("admin_unban_acc", AdminRole.Admin, AdminCommandSurface.Bypass, "unban an account"),
        new("admin_festival_enter", AdminRole.Gm, AdminCommandSurface.Bypass, "enter the festival"),
        new("admin_festival_end", AdminRole.Gm, AdminCommandSurface.Bypass, "end the festival"),
        new("admin_show_moves", AdminRole.Helper, AdminCommandSurface.Bypass, "open the teleport menu"),
        new("admin_show_moves_other", AdminRole.Helper, AdminCommandSurface.Bypass, "open the other-player teleport menu"),
        new("admin_show_teleport", AdminRole.Helper, AdminCommandSurface.Bypass, "show the teleport-character form"),
        new("admin_tele", AdminRole.Helper, AdminCommandSurface.Bypass, "open the move menu"),
        new("admin_move_to", AdminRole.Gm, AdminCommandSurface.Bypass, "teleport self to coordinates"),
        new("admin_teleportto", AdminRole.Gm, AdminCommandSurface.Bypass, "teleport self to a player"),
        new("admin_teleport_to_character", AdminRole.Gm, AdminCommandSurface.Bypass, "teleport self to the selected character"),
        new("admin_teleport_character", AdminRole.Gm, AdminCommandSurface.Bypass, "teleport the selected character"),
        new("admin_recall", AdminRole.Gm, AdminCommandSurface.Bypass, "recall a player"),
        new("admin_sendhome", AdminRole.Gm, AdminCommandSurface.Bypass, "send a player home"),
        new("admin_gonorth", AdminRole.Gm, AdminCommandSurface.Bypass, "move north"),
        new("admin_gosouth", AdminRole.Gm, AdminCommandSurface.Bypass, "move south"),
        new("admin_goeast", AdminRole.Gm, AdminCommandSurface.Bypass, "move east"),
        new("admin_gowest", AdminRole.Gm, AdminCommandSurface.Bypass, "move west"),
        new("admin_goup", AdminRole.Gm, AdminCommandSurface.Bypass, "move up"),
        new("admin_godown", AdminRole.Gm, AdminCommandSurface.Bypass, "move down")
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
