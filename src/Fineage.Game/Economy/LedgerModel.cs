namespace Fineage.Game.Economy;

public enum LedgerOwnerKind : byte
{
    System = 0,
    Character = 1,
    Castle = 2
}

public enum LedgerContainer : byte
{
    System = 0,
    Inventory = 1,
    Treasury = 2
}

/// <summary>Sources and sinks. Their balances may go negative: a negative balance is what they minted.</summary>
public enum SystemAccount
{
    NpcShop = 1,

    /// <summary>
    /// Counterpart of <see cref="LedgerTxKind.Adjustment"/>: value that reached or left an account through a move the
    /// ledger does not record yet. Its postings are the audit trail of the moves still to migrate.
    /// </summary>
    Untracked = 2
}

public readonly record struct LedgerAccount(LedgerOwnerKind Owner, int OwnerId, LedgerContainer Container)
    : IComparable<LedgerAccount>
{
    public static LedgerAccount Of(SystemAccount account) =>
        new(LedgerOwnerKind.System, (int)account, LedgerContainer.System);

    public static LedgerAccount Inventory(int characterId) =>
        new(LedgerOwnerKind.Character, characterId, LedgerContainer.Inventory);

    public static LedgerAccount Treasury(int castleId) =>
        new(LedgerOwnerKind.Castle, castleId, LedgerContainer.Treasury);

    public bool IsSystem => Owner == LedgerOwnerKind.System;

    public int CompareTo(LedgerAccount other)
    {
        int byOwner = Owner.CompareTo(other.Owner);
        if (byOwner != 0)
            return byOwner;
        int byId = OwnerId.CompareTo(other.OwnerId);
        return byId != 0 ? byId : Container.CompareTo(other.Container);
    }

    public override string ToString() => IsSystem
        ? $"system:{(SystemAccount)OwnerId}"
        : $"{Owner.ToString().ToLowerInvariant()}:{OwnerId}/{Container.ToString().ToLowerInvariant()}";
}

public enum LedgerTxKind
{
    Adjustment = 1,
    ShopBuy = 2,
    ShopSell = 3,
    ShopExchange = 4,
    TradeSettle = 5
}

/// <summary>
/// Idempotency key: the tick the move happened at and its order among the tick's moves. Tick numbers never repeat
/// across restarts (fixed steps since the Unix epoch), so an id names one move for good.
/// </summary>
public readonly record struct LedgerTxId(long Tick, int Sequence) : IComparable<LedgerTxId>
{
    public int CompareTo(LedgerTxId other)
    {
        int byTick = Tick.CompareTo(other.Tick);
        return byTick != 0 ? byTick : Sequence.CompareTo(other.Sequence);
    }

    public override string ToString() => $"{Tick}.{Sequence}";
}

/// <summary>
/// One side of a move. <see cref="InstanceIds"/> names the pieces of a non-stackable item that move, one per unit,
/// or is empty for stackable value.
/// </summary>
public sealed record LedgerPosting(LedgerAccount Account, int ItemId, long Amount, IReadOnlyList<int> InstanceIds)
{
    public LedgerPosting(LedgerAccount account, int itemId, long amount)
        : this(account, itemId, amount, [])
    {
    }
}

/// <summary>
/// A move between owners. <see cref="CausedBy"/> is the character that caused it (0 for none; a trade's requestor);
/// <see cref="Reference"/> names what it went through: the NPC template for shop moves, the partner for trades, 0 for
/// adjustments.
/// </summary>
public sealed record LedgerTransaction(
    LedgerTxId Id,
    LedgerTxKind Kind,
    int CausedBy,
    int Reference,
    long WallUnixMs,
    IReadOnlyList<LedgerPosting> Postings);

public enum LedgerOutcome
{
    Committed,
    Duplicate,
    Rejected
}

public enum LedgerRejection
{
    None,
    Empty,
    BadPosting,
    Unbalanced,
    Overdraft,
    InstanceMismatch,
    InstanceNotOwned,
    InstanceTaken,
    Conflict,
    Stale
}

public readonly record struct LedgerResult(LedgerTxId Id, LedgerOutcome Outcome, LedgerRejection Rejection, long Position)
{
    public bool Ok => Outcome != LedgerOutcome.Rejected;

    public static LedgerResult Reject(LedgerTxId id, LedgerRejection rejection) =>
        new(id, LedgerOutcome.Rejected, rejection, 0);
}

/// <summary>A committed transaction at its journal position, chained to the previous entry by <see cref="Hash"/>.</summary>
public sealed record LedgerEntry(long Position, LedgerTransaction Transaction, string Hash);
