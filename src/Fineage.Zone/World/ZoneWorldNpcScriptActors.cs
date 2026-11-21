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
    string? RaidbossInfoBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string scriptEvent)
    {
        SendScriptUnavailable(session, player, npc);
        return null;
    }
}
