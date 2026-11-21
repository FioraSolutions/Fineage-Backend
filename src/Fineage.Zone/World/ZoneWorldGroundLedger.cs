using Fineage.Game;
using Fineage.Game.Economy;

namespace Fineage.Zone.World;

/// <summary>
/// Ground stacks on the economy ledger (step 11c). Each stack is its own account, <see cref="LedgerAccount.Ground"/>
/// by object id, so the journal shows where a stack came from and who took it. A piece keeps its object id through
/// the ground (<see cref="ZoneEntity.GroundPiece"/>).
/// </summary>
public sealed partial class ZoneWorld
{
    static LedgerHoldings GroundHoldingsOf(ZoneEntity ground) => ground.GroundPiece
        ? new LedgerHoldings().AddPiece(ground.GroundItemId, ground.ObjectId)
        : new LedgerHoldings().Add(ground.GroundItemId, ground.GroundCount);

    static IReadOnlyList<int> GroundPieces(ZoneEntity ground) => ground.GroundPiece ? [ground.ObjectId] : [];

    static IReadOnlyList<int> PiecesOf(IEnumerable<ItemInstance> items) =>
        items.Where(item => !item.Template.Stackable).Select(item => item.ObjectId).ToList();

    PetState? SummonedPet(int controlObjectId)
    {
        foreach (var player in Players())
        {
            if (player.SummonObjectId != 0
                && _byId.TryGetValue(player.SummonObjectId, out var summon)
                && summon.Summon?.Pet is { } pet
                && pet.ControlObjectId == controlObjectId)
                return pet;
        }

        return null;
    }

    /// <summary>Posts a stack that <see cref="SpawnGroundStack"/> just placed as coming from <paramref name="from"/>.</summary>
    static void PostOntoGround(LedgerMove move, LedgerAccount from, ZoneEntity ground)
    {
        var account = LedgerAccount.Ground(ground.ObjectId);
        move.Observe(account, new LedgerHoldings());
        var pieces = GroundPieces(ground);
        move.Post(from, ground.GroundItemId, -ground.GroundCount, from.IsSystem ? [] : pieces);
        move.Post(account, ground.GroundItemId, ground.GroundCount, pieces);
    }

    /// <summary>Observes a stack before it leaves the ground and posts it out; the caller posts where it went.</summary>
    static void PostOffGround(LedgerMove move, ZoneEntity ground)
    {
        var account = LedgerAccount.Ground(ground.ObjectId);
        move.Observe(account, GroundHoldingsOf(ground));
        move.Post(account, ground.GroundItemId, -ground.GroundCount, GroundPieces(ground));
    }

    /// <summary>
    /// Puts a ground stack into <paramref name="bag"/>: a piece under its own object id and enchant, a larger
    /// non-stackable stack as new pieces, a stackable one onto the stack. Returns the rows that changed.
    /// </summary>
    static IReadOnlyList<ItemInstance> AddGroundStack(PlayerInventory bag, ZoneEntity ground)
    {
        if (ground.GroundPiece)
            return [bag.AddRestored(ground.ObjectId, ground.GroundItemId, 1, ground.GroundEnchant)];
        if (ground.GroundStackable)
            return [bag.Add(ground.GroundItemId, ground.GroundCount)];
        return bag.AddPieces(ground.GroundItemId, ground.GroundCount);
    }
}
