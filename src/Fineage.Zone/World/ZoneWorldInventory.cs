using Fineage.Protocol;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public const int MaxGrantCount = 10_000;

    public Task<string?> TryGrantItemAsync(ZoneSession session, GrantItemRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = TryGrantItem(session, request);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    string? TryGrantItem(ZoneSession session, GrantItemRequestMsg request)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (request.ItemId <= 0 || request.Count <= 0 || request.Count > MaxGrantCount)
            return "bad_request";
        if (!_game.Items.TryGet(request.ItemId, out _))
            return "bad_request";

        var added = player.Inventory.Add(request.ItemId, request.Count);
        // Same reasoning as GiveItem: the Net client would otherwise never learn the row appeared.
        NetBridge?.FanoutInventoryUpdate(
            [session.ObjectId],
            ToNetInventoryUpdate(InventoryMessages.Update(InventoryChange.Add, added)));
        // Grant is the one add path that does not call SaveInventory (the bag is persisted on leave), so the overload
        // recompute has to be explicit here — otherwise a grant that crosses 50% leaves the pawn unpenalised.
        RefreshWeightPenaltyAfterInventoryChange(session);
        ZoneLog.Info($"grant object={session.ObjectId} item={request.ItemId} count={request.Count}");
        return null;
    }
}
