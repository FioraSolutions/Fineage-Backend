using System.Globalization;
using Fineage.Protocol;
using Fineage.Zone.Geo.ZoneTypes;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius <c>handlers/admincommandhandlers/AdminTeleport</c>: the GM teleport bypasses the admin teleport pages
/// (<c>admin/teleports.htm</c>, <c>admin/move.htm</c>) are built from. Click-to-move (<c>admin_teleto</c>,
/// <c>admin_instant_move</c>), <c>admin_walk</c> and <c>admin_recall_npc</c> are not implemented: they refuse
/// through the unknown-command path, which names the verb to the GM.
/// </summary>
public sealed partial class ZoneWorld
{
    const string AdminTeleportsPage = "admin/teleports.htm";
    const string AdminMovePage = "admin/move.htm";
    const string AdminMovesOtherPage = "admin/tele/other.html";
    const int AdminGoDefaultOffset = 150;

    /// <summary>Mobius <c>AdminTeleport.teleportHome</c>: the map region each race is sent home to.</summary>
    static readonly Dictionary<string, string> AdminHomeRegionByRace = new(StringComparer.Ordinal)
    {
        ["HUMAN"] = "talking_island_town",
        ["ELF"] = "elf_town",
        ["DARK_ELF"] = "darkelf_town",
        ["ORC"] = "orc_town",
        ["DWARF"] = "dwarf_town",
    };

    string? TryAdminTeleport(ZoneSession session, ZoneEntity gm, string verb, string args)
    {
        string? refusal = verb.ToLowerInvariant() switch
        {
            "admin_show_moves" => SendAdminHtml(session, gm, AdminTeleportsPage),
            "admin_show_moves_other" => SendAdminHtml(session, gm, AdminMovesOtherPage),
            "admin_tele" => SendAdminHtml(session, gm, AdminMovePage),
            "admin_move_to" => AdminMoveTo(session, gm, args),
            "admin_teleportto" => AdminTeleportToName(session, gm, args),
            "admin_teleport_to_character" => AdminTeleportToTarget(session, gm),
            "admin_show_teleport" => AdminShowTeleportCharacter(session, gm),
            "admin_teleport_character" => AdminTeleportTarget(session, gm, args),
            "admin_recall" => AdminRecall(session, gm, args),
            "admin_sendhome" => AdminSendHome(session, gm, args),
            _ => AdminGo(session, gm, verb["admin_go".Length..].ToLowerInvariant(), args)
        };
        if (refusal != null)
            ZoneLog.Warn($"admin teleport refused cmd={verb} args=\"{args}\" object={gm.ObjectId}: {refusal}");
        return refusal;
    }

    /// <summary><c>admin_move_to x y z</c>; without coordinates Mobius opens the teleport menu.</summary>
    string? AdminMoveTo(ZoneSession session, ZoneEntity gm, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return SendAdminHtml(session, gm, AdminTeleportsPage);
        if (!TryParseXyz(args, out int x, out int y, out int z))
        {
            FanGmSystem(session, "Usage: //move_to <x> <y> <z>");
            return "bad_request";
        }

        return AdminRelocate(session, gm, x, y, z, $"You have been teleported to {x} {y} {z}");
    }

    string? AdminTeleportToName(ZoneSession session, ZoneEntity gm, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            FanGmSystem(session, "Usage: //teleportto <playername>");
            return "bad_request";
        }

        return AdminTeleportToPlayer(session, gm, FindOnlinePlayer(args.Trim()));
    }

    string? AdminTeleportToTarget(ZoneSession session, ZoneEntity gm) =>
        AdminTeleportToPlayer(session, gm, AdminTargetPlayer(session));

    string? AdminTeleportToPlayer(ZoneSession session, ZoneEntity gm, ZoneEntity? target)
    {
        if (target == null)
        {
            FanGmSystem(session, "Incorrect target.");
            return "target";
        }

        if (target.ObjectId == gm.ObjectId)
        {
            FanGmSystem(session, "You cannot self teleport.");
            return "target";
        }

        return AdminRelocate(session, gm, target.X, target.Y, target.Z, $"You have teleported to character {target.Name}.");
    }

    /// <summary>Mobius <c>showTeleportCharWindow</c>: coordinates form for the targeted player.</summary>
    string? AdminShowTeleportCharacter(ZoneSession session, ZoneEntity gm)
    {
        var target = AdminTargetPlayer(session);
        if (target == null)
        {
            FanGmSystem(session, "Incorrect target.");
            return "target";
        }

        string html =
            "<html><title>Teleport Character</title><body>The character you will teleport is " + target.Name + "." +
            "<br>Co-ordinate x<edit var=\"char_cord_x\" width=110>Co-ordinate y<edit var=\"char_cord_y\" width=110>" +
            "Co-ordinate z<edit var=\"char_cord_z\" width=110>" +
            "<button value=\"Teleport\" action=\"bypass -h admin_teleport_character $char_cord_x $char_cord_y $char_cord_z\" width=65 height=21 back=\"L2UI_ch3.smallbutton2_over\" fore=\"L2UI_ch3.smallbutton2\">" +
            $"<button value=\"Teleport near you\" action=\"bypass -h admin_teleport_character {gm.X} {gm.Y} {gm.Z}\" width=95 height=21 back=\"bigbutton_over\" fore=\"bigbutton\">" +
            "</body></html>";
        SendAdminRawHtml(session, gm, html);
        return null;
    }

    string? AdminTeleportTarget(ZoneSession session, ZoneEntity gm, string args)
    {
        var target = AdminTargetPlayer(session);
        if (target == null)
        {
            FanGmSystem(session, "Incorrect target.");
            return "target";
        }

        if (target.ObjectId == gm.ObjectId)
        {
            FanGmSystem(session, "You cannot teleport yourself.");
            return "target";
        }

        if (!TryParseXyz(args, out int x, out int y, out int z))
        {
            FanGmSystem(session, "Wrong or no Coordinates given.");
            return "bad_request";
        }

        return AdminRelocate(session, target, x, y, z, $"You have recalled {target.Name}", notify: session);
    }

    string? AdminRecall(ZoneSession session, ZoneEntity gm, string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 1)
        {
            FanGmSystem(session, "Usage: //recall <playername>");
            return "bad_request";
        }

        var target = FindOnlinePlayer(parts[0]);
        if (target == null)
        {
            FanGmSystem(session, "That player is not online.");
            return "target";
        }

        return AdminRelocate(session, target, gm.X, gm.Y, gm.Z, $"You have recalled {target.Name}", notify: session);
    }

    string? AdminSendHome(ZoneSession session, ZoneEntity gm, string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1)
        {
            FanGmSystem(session, "Usage: //sendhome <playername>");
            return "bad_request";
        }

        var target = parts.Length == 1 ? FindOnlinePlayer(parts[0]) : AdminTargetPlayer(session);
        if (target == null)
        {
            FanGmSystem(session, parts.Length == 1 ? "That player is not online." : "Incorrect target.");
            return "target";
        }

        string race = ZoneRaces.Name(target.Race)
            ?? throw new InvalidOperationException($"admin_sendhome: player {target.ObjectId} has unknown race {target.Race}");
        string regionName = AdminHomeRegionByRace[race];
        var region = _mapRegions.ByName(regionName)
            ?? throw new InvalidOperationException($"admin_sendhome: map region '{regionName}' is missing from map-regions.json");
        var point = region.RespawnPoints.Count > 0
            ? region.RespawnPoints[Rng(_respawnRandom, target).Next(region.RespawnPoints.Count)]
            : throw new InvalidOperationException($"admin_sendhome: map region '{regionName}' has no respawn point");
        return AdminRelocate(session, target, point.X, point.Y, point.Z, $"Sent {target.Name} home to {region.Town}.", notify: session);
    }

    /// <summary><c>admin_go&lt;north|south|east|west|up|down&gt; [offset]</c> (default 150), then the move menu again.</summary>
    string? AdminGo(ZoneSession session, ZoneEntity gm, string direction, string args)
    {
        int offset = AdminGoDefaultOffset;
        string text = args.Trim();
        if (text.Length > 0 && !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
        {
            FanGmSystem(session, "Usage: //go<north|south|east|west|up|down> [offset] (default 150)");
            return "bad_request";
        }

        int x = gm.X, y = gm.Y, z = gm.Z;
        switch (direction)
        {
            case "east": x += offset; break;
            case "west": x -= offset; break;
            case "north": y -= offset; break;
            case "south": y += offset; break;
            case "up": z += offset; break;
            case "down": z -= offset; break;
            default:
                FanGmSystem(session, "Usage: //go<north|south|east|west|up|down> [offset] (default 150)");
                return "bad_request";
        }

        string? refusal = AdminRelocate(session, gm, x, y, z, message: null);
        return refusal ?? SendAdminHtml(session, gm, AdminMovePage);
    }

    string? AdminRelocate(ZoneSession session, ZoneEntity who, int x, int y, int z, string? message, ZoneSession? notify = null)
    {
        if (!Loaded(x, y))
        {
            FanGmSystem(session, $"{NpcServiceProtocol.DestinationUnloadedText} ({x}, {y}, {z})");
            return NpcServiceProtocol.TeleportRefusalCode(NpcServiceProtocol.DestinationUnloaded);
        }

        Relocate(who, x, y, z);
        ZoneLog.Info($"admin teleport object={who.ObjectId} by={session.ObjectId} -> {x},{y},{z}");
        if (message != null)
            FanGmSystem(notify ?? session, message);
        return null;
    }

    ZoneEntity? AdminTargetPlayer(ZoneSession session) =>
        session.SelectedTargetId > 0 && _byId.TryGetValue(session.SelectedTargetId, out var target) &&
        target.Kind == ZoneEntityKind.Player
            ? target
            : null;

    static bool TryParseXyz(string args, out int x, out int y, out int z)
    {
        x = y = z = 0;
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3
               && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
               && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y)
               && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out z);
    }
}
