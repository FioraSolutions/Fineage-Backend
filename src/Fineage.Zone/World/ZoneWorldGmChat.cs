using System.Globalization;
using Fineage.Game.Services;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius-style GM chat verbs (<c>//teleport</c>, …). Swallowed before normal chat fanout; gated by
/// <see cref="ZoneAdmin"/>.
/// </summary>
public sealed partial class ZoneWorld
{
    const string GmChatPrefix = "//";
    const string AdminDenied = "You are not authorized to use GM commands.";
    const string TeleportUsage = "Usage: //teleport x y z";
    const string GotoUsage = "Usage: //goto characterName";
    const string RecallUsage = "Usage: //recall characterName";
    const string TownUsage = "Usage: //town name (see //towns)";

    static readonly GmChatCommand[] GmChatCommands =
    [
        new("teleport", ["tele"], GmCmdTeleport),
        new("goto", [], GmCmdGoto),
        new("recall", [], GmCmdRecall),
        new("loc", [], GmCmdLoc),
        new("town", [], GmCmdTown),
        new("towns", [], GmCmdTowns),
    ];

    readonly Dictionary<string, GmChatCommand> _gmChatByVerb = BuildGmChatIndex();

    static Dictionary<string, GmChatCommand> BuildGmChatIndex()
    {
        var map = new Dictionary<string, GmChatCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in GmChatCommands)
        {
            map[cmd.Name] = cmd;
            foreach (string alias in cmd.Aliases)
                map[alias] = cmd;
        }

        return map;
    }

    /// <summary>Returns a refusal for the speaker, or null when the line was handled.</summary>
    SystemMessageMsg? TryGmChat(ZoneEntity player, ZoneSession session, string text)
    {
        if (!GmChatCommand.TrySplit(text, out string verb, out string args))
            return Plain("Unknown GM command.");

        if (!ZoneAdmin.IsAdminCharacter(player.Name))
        {
            ZoneLog.Info($"gmchat denied object={player.ObjectId} name={player.Name} cmd={verb}");
            return Plain(AdminDenied);
        }

        if (!_gmChatByVerb.TryGetValue(verb, out var command))
        {
            ZoneLog.Info($"gmchat unknown object={player.ObjectId} cmd={verb}");
            return Plain($"Unknown GM command: {verb}");
        }

        string? err = command.Handler(this, player, session, args);
        if (err != null)
        {
            ZoneLog.Info($"gmchat fail object={player.ObjectId} cmd={verb} reason={err}");
            return Plain(err);
        }

        ZoneLog.Info($"gmchat ok object={player.ObjectId} cmd={verb}");
        return null;
    }

    static string? GmCmdTeleport(ZoneWorld world, ZoneEntity player, ZoneSession session, string args) =>
        world.GmTeleportCoords(player, args);

    static string? GmCmdGoto(ZoneWorld world, ZoneEntity player, ZoneSession session, string args) =>
        world.GmGotoPlayer(player, args);

    static string? GmCmdRecall(ZoneWorld world, ZoneEntity player, ZoneSession session, string args) =>
        world.GmRecallPlayer(player, args);

    static string? GmCmdLoc(ZoneWorld world, ZoneEntity player, ZoneSession session, string args) =>
        world.GmReportLoc(session, player);

    static string? GmCmdTown(ZoneWorld world, ZoneEntity player, ZoneSession session, string args) =>
        world.GmTeleportTown(player, args);

    static string? GmCmdTowns(ZoneWorld world, ZoneEntity player, ZoneSession session, string args) =>
        world.GmListTowns(session);

    string? GmTeleportCoords(ZoneEntity player, string args)
    {
        string[] parts = args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
            return TeleportUsage;

        return GmRelocate(player, x, y, z);
    }

    string? GmGotoPlayer(ZoneEntity gm, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return GotoUsage;
        var target = FindOnlinePlayer(args.Trim());
        if (target == null)
            return "That player is not online.";
        return GmRelocate(gm, target.X, target.Y, target.Z);
    }

    string? GmRecallPlayer(ZoneEntity gm, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return RecallUsage;
        var target = FindOnlinePlayer(args.Trim());
        if (target == null)
            return "That player is not online.";
        return GmRelocate(target, gm.X, gm.Y, gm.Z);
    }

    string? GmReportLoc(ZoneSession session, ZoneEntity player)
    {
        FanGmSystem(session, $"{player.X} {player.Y} {player.Z} heading {player.Heading}");
        return null;
    }

    string? GmTeleportTown(ZoneEntity player, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return TownUsage;
        var town = _gmTeleports.Find(args.Trim());
        if (town == null)
            return $"Unknown town: {args.Trim()}. Use //towns for the list.";
        return GmRelocate(player, town.X, town.Y, town.Z, town.DisplayName);
    }

    string? GmListTowns(ZoneSession session)
    {
        if (_gmTeleports.Towns.Count == 0)
        {
            FanGmSystem(session, "No GM town teleports are configured.");
            return null;
        }

        var parts = new List<string>(_gmTeleports.Towns.Count);
        foreach (var town in _gmTeleports.Towns)
            parts.Add($"{town.PrimaryKey} ({town.DisplayName})");

        FanGmSystem(session, "Towns: " + string.Join(", ", parts));
        return null;
    }

    string? GmRelocate(ZoneEntity entity, int x, int y, int z, string? label = null)
    {
        if (!Loaded(x, y))
            return NpcServiceProtocol.DestinationUnloadedText;

        Relocate(entity, x, y, z);
        if (entity.Session != null && label != null)
            FanGmSystem(entity.Session, "Teleported to " + label + ".");
        else if (entity.Session != null)
            FanGmSystem(entity.Session, $"Teleported to {x}, {y}, {z}.");
        return null;
    }

    void FanGmSystem(ZoneSession session, string text) =>
        NetBridge?.FanoutSystemMessage(
            [session.ObjectId],
            new SystemMessageMsg { Id = ChatProtocol.SystemPlain, Params = [], Text = text });

    ZoneEntity? FindOnlinePlayer(string name)
    {
        foreach (var player in Players())
        {
            if (!string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (player.Session?.InWorld != true)
                return null;
            return player;
        }

        return null;
    }

    readonly struct GmChatCommand
    {
        public GmChatCommand(string name, string[] aliases, GmChatHandler handler)
        {
            Name = name;
            Aliases = aliases;
            Handler = handler;
        }

        public string Name { get; }
        public string[] Aliases { get; }
        public GmChatHandler Handler { get; }

        public static bool TrySplit(string text, out string verb, out string args)
        {
            verb = "";
            args = "";
            if (!text.StartsWith(GmChatPrefix, StringComparison.Ordinal))
                return false;
            string body = text[GmChatPrefix.Length..].TrimStart();
            if (body.Length == 0)
                return false;
            int space = body.IndexOf(' ');
            if (space < 0)
            {
                verb = body;
                return true;
            }

            verb = body[..space];
            args = body[(space + 1)..].TrimStart();
            return true;
        }
    }

    delegate string? GmChatHandler(ZoneWorld world, ZoneEntity player, ZoneSession session, string args);
}