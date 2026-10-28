using Fineage.Protocol;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public const int MaxGrantCount = 10_000;

    public async Task<string?> TryGrantItemAsync(ZoneSession session, GrantItemRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var toSelf = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryGrantItem(session, request, toSelf);
        if (error != null)
            return error;

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    string? TryGrantItem(ZoneSession session, GrantItemRequestMsg request, List<Pending> toSelf)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (request.ItemId <= 0 || request.Count <= 0 || request.Count > MaxGrantCount)
            return "bad_request";
        if (!_game.Catalog.TryGet(request.ItemId, out _))
            return "bad_request";

        var added = player.Inventory.Add(request.ItemId, request.Count);
        toSelf.Add(Pack(session, MsgType.InventoryUpdate,
            InventoryMessages.Update(InventoryChange.Add, added)));
        ZoneLog.Info($"grant object={session.ObjectId} item={request.ItemId} count={request.Count}");
        return null;
    }
}
