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
                $"<html><body>%npcname%:<br>My Text is missing:<br>data/html/{path}</body></html>");
            return null;
        }

        SendHtml(session, player, npc, html);
        return null;
    }
}
