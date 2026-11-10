using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Buy / sell / multisell opcode group extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class ShopPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public ShopPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnBuyAsync(
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

        var buy = NetCodec.DecodeRequestBuyItem(envelope.Payload);
        var items = (buy.Items ?? [])
            .Select(i => new Fineage.Game.Shops.ShopLine(i.ItemId, i.Count))
            .ToArray();
        string? error = await _world.TryBuyAsync(
            zoneSession, buy.NpcObjectId, buy.ListId, items, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Buy failed.", ct).ConfigureAwait(false);
    }

    public async Task OnSellAsync(
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

        var sell = NetCodec.DecodeRequestSellItem(envelope.Payload);
        var items = (sell.Items ?? [])
            .Select(i => new Fineage.Game.Shops.ShopObjectLine(i.ObjectId, i.Count))
            .ToArray();
        string? error = await _world.TrySellAsync(
            zoneSession, sell.NpcObjectId, items, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Sell failed.", ct).ConfigureAwait(false);
    }

    public async Task OnMultiSellAsync(
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

        var choose = NetCodec.DecodeRequestMultiSellChoose(envelope.Payload);
        string? error = await _world.TryMultiSellAsync(
            zoneSession, choose.NpcObjectId, choose.ListId, choose.EntryId, choose.Amount, ct)
            .ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "MultiSell failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
