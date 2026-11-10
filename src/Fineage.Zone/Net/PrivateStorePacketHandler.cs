using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Private store opcode group extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class PrivateStorePacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public PrivateStorePacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnSetSellAsync(
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

        var req = NetCodec.DecodeSetPrivateStoreSell(envelope.Payload);
        var items = (req.Items ?? []).Select(row => new PrivateStoreSellRowMsg
        {
            InventoryObjectId = row.InventoryObjectId,
            Count = row.Count,
            Price = row.Price
        }).ToArray();
        string? error = await _world.TrySetPrivateStoreSellAsync(
            zoneSession,
            new SetPrivateStoreSellMsg
            {
                Message = req.Message ?? "",
                Packaged = req.Packaged != 0,
                Items = items
            },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Private store sell failed.", ct).ConfigureAwait(false);
    }

    public async Task OnQuitAsync(NetConnection conn, ZoneSession zoneSession, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryPrivateStoreQuitAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Private store quit failed.", ct).ConfigureAwait(false);
    }

    public async Task OnListAsync(
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

        var req = NetCodec.DecodePrivateStoreListRequest(envelope.Payload);
        string? error = await _world.TryPrivateStoreListAsync(
            zoneSession,
            new PrivateStoreListRequestMsg { StoreObjectId = req.StoreObjectId },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Private store list failed.", ct).ConfigureAwait(false);
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

        var req = NetCodec.DecodePrivateStoreBuy(envelope.Payload);
        var items = (req.Items ?? []).Select(row => new PrivateStoreSellRowMsg
        {
            InventoryObjectId = row.InventoryObjectId,
            Count = row.Count,
            Price = row.Price
        }).ToArray();
        string? error = await _world.TryPrivateStoreBuyAsync(
            zoneSession,
            new PrivateStoreBuyMsg { StoreObjectId = req.StoreObjectId, Items = items },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Private store buy failed.", ct).ConfigureAwait(false);
    }

    public async Task OnSetBuyAsync(
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

        var req = NetCodec.DecodeSetPrivateStoreBuy(envelope.Payload);
        var items = (req.Items ?? []).Select(row => new PrivateStoreBuyRowMsg
        {
            ItemId = row.ItemId,
            Count = row.Count,
            Price = row.Price
        }).ToArray();
        string? error = await _world.TrySetPrivateStoreBuyAsync(
            zoneSession,
            new SetPrivateStoreBuyMsg { Message = req.Message ?? "", Items = items },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Private store buy open failed.", ct).ConfigureAwait(false);
    }

    public async Task OnSellToAsync(
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

        var req = NetCodec.DecodePrivateStoreSellTo(envelope.Payload);
        var items = (req.Items ?? []).Select(row => new PrivateStoreSellRowMsg
        {
            InventoryObjectId = row.InventoryObjectId,
            Count = row.Count,
            Price = row.Price
        }).ToArray();
        string? error = await _world.TryPrivateStoreSellToAsync(
            zoneSession,
            new PrivateStoreSellToMsg { StoreObjectId = req.StoreObjectId, Items = items },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Private store sell-to failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
