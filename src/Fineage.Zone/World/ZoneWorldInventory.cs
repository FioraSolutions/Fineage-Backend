using Fineage.Game.Economy;
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
        using (_affinity.Step())
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

        RecordBagChange(
            player,
            LedgerTxKind.GmGrant,
            GmGrantSource,
            () =>
            {
                var added = player.Inventory.Add(itemId, count);
                NetBridge?.FanoutInventoryUpdate(
                    [session.ObjectId],
                    InventoryMessages.Update(InventoryChange.Add, added));
                RefreshWeightPenaltyAfterInventoryChange(session);
                ZoneLog.Info($"grant object={session.ObjectId} item={itemId} count={count}");
                return count;
            },
            _ => itemId);
        return null;
    }
}
