using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class WorldEventPacketHandler
{
    public async Task OnSevenSignsStatusAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TrySevenSignsStatusAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "SevenSigns status failed.", ct).ConfigureAwait(false);
        // The record and the sky go out as fanout, so the reply body is empty on purpose.
    }

    public async Task OnSevenSignsActionAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var request = NetCodec.DecodeSevenSignsActionRequest(envelope.Payload);
        string? error = await _world.TrySevenSignsActionAsync(
            zoneSession,
            new SevenSignsActionRequestMsg
            {
                Action = request.Action,
                Cabal = request.Cabal,
                Seal = request.Seal,
                BlueStones = request.BlueStones,
                GreenStones = request.GreenStones,
                RedStones = request.RedStones
            },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "SevenSigns action was rejected.", ct).ConfigureAwait(false);
    }

    public async Task OnDungeonAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var request = NetCodec.DecodeDungeonEnterRequest(envelope.Payload);
        string? error = await _world.TryDungeonAsync(
            zoneSession,
            new DungeonEnterRequestMsg { DungeonId = request.DungeonId, Action = request.Action },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Dungeon request was rejected.", ct).ConfigureAwait(false);
        // DungeonEntered goes out as fanout, so the reply body is empty on purpose.
    }

}
