using Fineage.Protocol;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public const int MaxGrantCount = 10_000;

    public Task<string?> TryGrantItemAsync(ZoneSession session, int itemId, int count, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = TryGrantItem(session, itemId, count);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    string? TryGrantItem(ZoneSession session, int itemId, int count)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (GearRules.GrantError(itemId, count, MaxGrantCount, _game.Items.TryGet(itemId, out _)) is { } grantError)
            return grantError;

        var added = player.Inventory.Add(itemId, count);
        // Same reasoning as GiveItem: the Net client would otherwise never learn the row appeared.
        NetBridge?.FanoutInventoryUpdate(
            [session.ObjectId],
            InventoryMessages.Update(InventoryChange.Add, added));
        // Grant is the one add path that does not call SaveInventory (the bag is persisted on leave), so the overload
        // recompute has to be explicit here — otherwise a grant that crosses 50% leaves the pawn unpenalised.
        RefreshWeightPenaltyAfterInventoryChange(session);
        ZoneLog.Info($"grant object={session.ObjectId} item={itemId} count={count}");
        return null;
    }
}
