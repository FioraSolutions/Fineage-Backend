using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Trade opcode group extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class TradePacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public TradePacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnRequestAsync(
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

        var req = NetCodec.DecodeTradeRequest(envelope.Payload);
        string? error = await _world.TryTradeRequestAsync(
            zoneSession,
            new TradeRequestMsg { TargetObjectId = req.TargetObjectId },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Trade request failed.", ct).ConfigureAwait(false);
    }

    public async Task OnAnswerAsync(
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

        var answer = NetCodec.DecodeTradeAnswerRequest(envelope.Payload);
        string? error = await _world.TryTradeAnswerAsync(
            zoneSession,
            new TradeAnswerRequestMsg
            {
                RequestorObjectId = answer.RequestorObjectId,
                Response = answer.Response
            },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Trade answer failed.", ct).ConfigureAwait(false);
    }

    public async Task OnAddItemAsync(
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

        var add = NetCodec.DecodeTradeAddItemRequest(envelope.Payload);
        string? error = await _world.TryTradeAddItemAsync(
            zoneSession,
            new TradeAddItemRequestMsg
            {
                InventoryObjectId = add.InventoryObjectId,
                Count = add.Count
            },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Trade add item failed.", ct).ConfigureAwait(false);
    }

    public async Task OnDoneAsync(
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

        var done = NetCodec.DecodeTradeDoneRequest(envelope.Payload);
        string? error = await _world.TryTradeDoneAsync(
            zoneSession,
            new TradeDoneRequestMsg { Response = done.Response },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Trade done failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
