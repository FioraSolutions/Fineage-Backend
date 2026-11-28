using Fineage.Net.Contract;
using Fineage.Zone.Bosses;
using Fineage.Zone.DimensionalRift;
using Fineage.Zone.Punishment;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    string? OracleTeleportBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string scriptEvent)
    {
        string evt = (scriptEvent ?? "").Trim();
        if (evt.Equals("Dimensional", StringComparison.OrdinalIgnoreCase))
        {
            var peace = _dimensionalRift.Room(DimensionalRiftSettings.WaitingRoomType, DimensionalRiftSettings.WaitingRoomId);
            Relocate(player, peace.Teleport.X, peace.Teleport.Y, peace.Teleport.Z);
            PlayerZoneChecks.RegisterRiftParticipation(this, player.ObjectId, inside: false);
            return null;
        }
        if (evt.Equals("Return", StringComparison.OrdinalIgnoreCase))
        {
            Relocate(player, 83400, 147943, -3400);
            return null;
        }
        return FinishUnhandledScriptPage(session, player, npc, "OracleTeleport", evt, "unknown event");
    }
    string? TeleportToRaceTrackBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string scriptEvent)
    {
        var track = _monsterRaceSettings.Track;
        if ((scriptEvent ?? "").Trim().Equals("TELEPORT", StringComparison.OrdinalIgnoreCase))
        {
            Relocate(player, 15670, 142983, -2705);
            return null;
        }
        if (!_monsterRaceSettings.Enabled)
        {
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>The monster race is not active on this server.</body></html>");
            return null;
        }
        Relocate(player, track.OriginX, track.OriginYBase - track.LaneSpacing * 3, track.OriginZ);
        return null;
    }
    /// <summary>
    /// Mobius <c>custom.RaidbossInfo</c>: level HTML from <c>data/html/raidboss_info/</c>,
    /// digit events place a radar marker on the raid's <see cref="Bosses.BossCatalog"/> spawn.
    /// </summary>
    string? RaidbossInfoBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string scriptEvent)
    {
        string evt = (scriptEvent ?? "").Trim();
        if (evt.Length == 0)
            return SendRaidbossInfoPage(session, player, npc, "info.htm");

        if (evt.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
            || evt.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            // Mobius only serves known level pages under the script folder — reject path tricks.
            string file = Path.GetFileName(evt);
            if (!file.Equals(evt, StringComparison.OrdinalIgnoreCase)
                || file.Contains("..", StringComparison.Ordinal))
            {
                return FinishUnhandledScriptPage(session, player, npc, "RaidbossInfo", evt, "bad html path");
            }

            return SendRaidbossInfoPage(session, player, npc, file);
        }

        if (int.TryParse(evt, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int bossId) && bossId > 0)
        {
            if (!_bosses.Catalog.TryGet(bossId, out var definition))
            {
                ZoneLog.Error($"[npc-script] RaidbossInfo boss npcId={bossId} is not in {BossCatalog.FileName}");
                return null;
            }

            if (!definition.HasWorldSpawnPoint)
            {
                ZoneLog.Error(
                    $"[npc-script] RaidbossInfo boss npcId={bossId} has no world spawn point in {BossCatalog.FileName}");
                return null;
            }

            // Fineage radar arrow (Net QuestDirection) — same client path as quest targets.
            NetBridge?.FanoutQuestDirection([session.ObjectId], new QuestDirectionNetMsg
            {
                QuestId = 0,
                Show = true,
                X = definition.X,
                Y = definition.Y,
                Z = definition.Z
            });
            return null;
        }

        return FinishUnhandledScriptPage(session, player, npc, "RaidbossInfo", evt, "unknown event");
    }

    string? SendRaidbossInfoPage(ZoneSession session, ZoneEntity player, ZoneEntity npc, string fileName)
    {
        string path = $"raidboss_info/{fileName}";
        if (!TryPage(session.Language, path, out var html) || string.IsNullOrWhiteSpace(html))
        {
            ZoneLog.Error($"[npc-script] RaidbossInfo missing HTML data/html/{path}");
            SendHtml(session, player, npc,
                $"<html><body>%npcname%:<br>My text is missing:<br>data/html/{path}</body></html>");
            return null;
        }

        SendHtml(session, player, npc, html);
        return null;
    }

    /// <summary>
    /// Mobius <c>custom.NpcLocationInfo</c>: starter-village guide HTML under
    /// <c>data/html/npc_location_info/</c>; digit events place a radar marker on a spawn of that npcId.
    /// </summary>
    string? NpcLocationInfoBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string scriptEvent)
    {
        var catalog = _npcLocationInfo ??= Fineage.Zone.NpcScripts.NpcLocationInfoCatalog.LoadDefault();
        string evt = (scriptEvent ?? "").Trim();
        if (evt.Length == 0)
        {
            if (!catalog.IsGuide(npc.NpcId))
            {
                ZoneLog.Error($"[npc-script] NpcLocationInfo talk npcId={npc.NpcId} is not a guide in {Fineage.Zone.NpcScripts.NpcLocationInfoCatalog.FileName}");
                SendScriptUnavailable(session, player, npc);
                return null;
            }

            return SendNpcLocationInfoPage(session, player, npc, $"{npc.NpcId}.htm");
        }

        if (evt.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
            || evt.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            string file = Path.GetFileName(evt);
            if (!file.Equals(evt, StringComparison.OrdinalIgnoreCase)
                || file.Contains("..", StringComparison.Ordinal))
            {
                return FinishUnhandledScriptPage(session, player, npc, "NpcLocationInfo", evt, "bad html path");
            }

            return SendNpcLocationInfoPage(session, player, npc, file);
        }

        if (int.TryParse(evt, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int targetNpcId) && targetNpcId > 0)
        {
            if (!catalog.IsRadarTarget(targetNpcId))
            {
                ZoneLog.Error(
                    $"[npc-script] NpcLocationInfo radar npcId={targetNpcId} is not in {Fineage.Zone.NpcScripts.NpcLocationInfoCatalog.FileName}");
                return null;
            }

            if (!TryFindNpcSpawnPoint(targetNpcId, out int x, out int y, out int z))
            {
                ZoneLog.Error(
                    $"[npc-script] NpcLocationInfo radar npcId={targetNpcId} has no world spawn (live or {Spawns.SpawnCatalog.SpawnsDirectoryName}/)");
                return null;
            }

            NetBridge?.FanoutQuestDirection([session.ObjectId], new QuestDirectionNetMsg
            {
                QuestId = 0,
                Show = true,
                X = x,
                Y = y,
                Z = z
            });
            return SendNpcLocationInfoPage(session, player, npc, "MoveToLoc.htm");
        }

        return FinishUnhandledScriptPage(session, player, npc, "NpcLocationInfo", evt, "unknown event");
    }

    string? SendNpcLocationInfoPage(ZoneSession session, ZoneEntity player, ZoneEntity npc, string fileName)
    {
        string path = $"npc_location_info/{fileName}";
        if (!TryPage(session.Language, path, out var html) || string.IsNullOrWhiteSpace(html))
        {
            ZoneLog.Error($"[npc-script] NpcLocationInfo missing HTML data/html/{path}");
            SendHtml(session, player, npc,
                $"<html><body>%npcname%:<br>My text is missing:<br>data/html/{path}</body></html>");
            return null;
        }

        SendHtml(session, player, npc, html);
        return null;
    }

    /// <summary>
    /// Mobius <c>custom.ShadowWeapons</c>: coupon pages under <c>data/html/shadow_weapons/</c>
    /// (D-grade 8869 / C-grade 8870).
    /// </summary>
    string? ShadowWeaponsBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string scriptEvent)
    {
        _ = scriptEvent;
        bool hasD = player.Inventory != null && player.Inventory.CountOf(ShadowWeaponCouponD) > 0;
        bool hasC = player.Inventory != null && player.Inventory.CountOf(ShadowWeaponCouponC) > 0;
        string file = (hasD, hasC) switch
        {
            (false, false) => "exchange_no.html",
            (false, true) => "exchange_c.html",
            (true, false) => "exchange_d.html",
            (true, true) => "exchange_both.html"
        };
        string path = $"shadow_weapons/{file}";
        if (!TryPage(session.Language, path, out var html) || string.IsNullOrWhiteSpace(html))
        {
            ZoneLog.Error($"[npc-script] ShadowWeapons missing HTML data/html/{path}");
            SendHtml(session, player, npc,
                $"<html><body>%npcname%:<br>My text is missing:<br>data/html/{path}</body></html>");
            return null;
        }

        SendHtml(session, player, npc, html);
        return null;
    }

    const int ShadowWeaponCouponD = 8869;
    const int ShadowWeaponCouponC = 8870;

    Fineage.Zone.NpcScripts.NpcLocationInfoCatalog? _npcLocationInfo;

    bool TryFindNpcSpawnPoint(int npcId, out int x, out int y, out int z)
    {
        foreach (var entity in _byId.Values)
        {
            if (entity.Kind != ZoneEntityKind.Npc || entity.NpcId != npcId || entity.CurHp <= 0)
                continue;
            x = entity.X;
            y = entity.Y;
            z = entity.Z;
            return true;
        }

        foreach (var map in _spawnCatalog.Maps.Values)
        {
            foreach (var row in map.Npcs)
            {
                if (row.NpcId != npcId)
                    continue;
                x = row.X;
                y = row.Y;
                z = row.Z;
                return true;
            }
        }

        x = y = z = 0;
        return false;
    }
}
