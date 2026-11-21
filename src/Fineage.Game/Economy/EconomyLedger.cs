namespace Fineage.Game.Economy;

public interface IEconomyLedger
{
    /// <summary>
    /// Validates balances and pieces, appends atomically and returns the stored result. A repeated id with the same
    /// transaction returns the original result without a second posting; with a different one it is a conflict.
    /// </summary>
    ValueTask<LedgerResult> SubmitAsync(LedgerTransaction tx, CancellationToken ct);
}

/// <summary>
/// The auditable economy ledger, in process: double-entry postings that sum to zero per item, balances and piece
/// ownership projected from the journal, and an idempotency index by <see cref="LedgerTxId"/> over the last
/// <c>idempotencyTicks</c> ticks; an id older than that is refused as stale, so a late retry can never post twice.
/// Starting replays the journal and refuses one whose hash chain or postings do not hold.
/// </summary>
public sealed class EconomyLedger : IEconomyLedger
{
    readonly object _sync = new();
    readonly ILedgerJournal _journal;
    readonly long _idempotencyTicks;
    readonly Dictionary<LedgerTxId, LedgerEntry> _byId = [];
    readonly Queue<LedgerEntry> _recent = new();
    readonly Dictionary<LedgerAccount, LedgerHoldings> _balances = [];
    readonly Dictionary<int, LedgerAccount> _owners = [];
    string _head = LedgerChain.Genesis;
    long _position;
    long _latestTick = long.MinValue;

    public EconomyLedger(ILedgerJournal journal, long idempotencyTicks)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        if (idempotencyTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(idempotencyTicks), idempotencyTicks, "The idempotency window must be positive.");
        _idempotencyTicks = idempotencyTicks;
        var entries = journal.ReadAll();
        if (LedgerChain.FirstBroken(entries) is { } broken)
            throw new InvalidOperationException($"economy ledger journal is broken at position {broken}");
        foreach (var entry in entries)
        {
            var rejection = Apply(entry.Transaction);
            if (rejection != LedgerRejection.None)
                throw new InvalidOperationException(
                    $"economy ledger journal entry {entry.Position} ({entry.Transaction.Id}) does not replay: {rejection}");
            Remember(entry);
            _head = entry.Hash;
            _position = entry.Position;
        }
    }

    public long Position
    {
        get
        {
            lock (_sync)
                return _position;
        }
    }

    public string Head
    {
        get
        {
            lock (_sync)
                return _head;
        }
    }

    public ValueTask<LedgerResult> SubmitAsync(LedgerTransaction tx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Submit(tx));
    }

    public LedgerResult Submit(LedgerTransaction tx)
    {
        ArgumentNullException.ThrowIfNull(tx);
        lock (_sync)
        {
            if (_byId.TryGetValue(tx.Id, out var stored))
            {
                return LedgerChain.Encode(stored.Transaction).AsSpan().SequenceEqual(LedgerChain.Encode(tx))
                    ? new LedgerResult(tx.Id, LedgerOutcome.Duplicate, LedgerRejection.None, stored.Position)
                    : LedgerResult.Reject(tx.Id, LedgerRejection.Conflict);
            }

            if (_latestTick != long.MinValue && tx.Id.Tick < _latestTick - _idempotencyTicks)
                return LedgerResult.Reject(tx.Id, LedgerRejection.Stale);

            var rejection = Apply(tx);
            if (rejection != LedgerRejection.None)
                return LedgerResult.Reject(tx.Id, rejection);

            var entry = new LedgerEntry(_position + 1, tx, LedgerChain.Hash(_head, tx));
            _journal.Append(entry);
            Remember(entry);
            _position = entry.Position;
            _head = entry.Hash;
            return new LedgerResult(tx.Id, LedgerOutcome.Committed, LedgerRejection.None, entry.Position);
        }
    }

    /// <summary>A copy of what <paramref name="account"/> holds by the journal.</summary>
    public LedgerHoldings HoldingsOf(LedgerAccount account)
    {
        lock (_sync)
            return _balances.TryGetValue(account, out var holdings) ? holdings.Copy() : new LedgerHoldings();
    }

    public LedgerAccount? OwnerOf(int instanceId)
    {
        lock (_sync)
            return _owners.TryGetValue(instanceId, out var owner) ? owner : null;
    }

    /// <summary>The entry committed under <paramref name="id"/> while it is inside the idempotency window.</summary>
    public LedgerEntry? Find(LedgerTxId id)
    {
        lock (_sync)
            return _byId.GetValueOrDefault(id);
    }

    /// <summary>Writes buffered entries to durable storage. Call outside the step.</summary>
    public void Flush() => _journal.Flush();

    void Remember(LedgerEntry entry)
    {
        _byId.Add(entry.Transaction.Id, entry);
        _recent.Enqueue(entry);
        _latestTick = Math.Max(_latestTick, entry.Transaction.Id.Tick);
        while (_recent.Peek().Transaction.Id.Tick < _latestTick - _idempotencyTicks)
            _byId.Remove(_recent.Dequeue().Transaction.Id);
    }

    LedgerRejection Apply(LedgerTransaction tx)
    {
        if (tx.Postings.Count == 0)
            return LedgerRejection.Empty;

        var sums = new Dictionary<int, long>();
        var seen = new HashSet<(int Instance, bool Credit)>();
        foreach (var posting in tx.Postings)
        {
            if (posting.ItemId <= 0 || posting.Amount == 0 || posting.Amount == long.MinValue)
                return LedgerRejection.BadPosting;
            if (posting.InstanceIds.Count != 0)
            {
                if (posting.Account.IsSystem)
                    return LedgerRejection.BadPosting;
                if (posting.InstanceIds.Count != Math.Abs(posting.Amount))
                    return LedgerRejection.InstanceMismatch;
                foreach (int instance in posting.InstanceIds)
                {
                    if (!seen.Add((instance, posting.Amount > 0)))
                        return LedgerRejection.InstanceMismatch;
                }
            }

            sums[posting.ItemId] = checked(sums.GetValueOrDefault(posting.ItemId) + posting.Amount);
        }

        if (sums.Values.Any(sum => sum != 0))
            return LedgerRejection.Unbalanced;

        var working = new Dictionary<LedgerAccount, LedgerHoldings>();
        var released = new HashSet<int>();
        var claimed = new Dictionary<int, LedgerAccount>();
        LedgerHoldings Working(LedgerAccount account)
        {
            if (!working.TryGetValue(account, out var holdings))
            {
                holdings = _balances.TryGetValue(account, out var current) ? current.Copy() : new LedgerHoldings();
                working[account] = holdings;
            }

            return holdings;
        }

        foreach (var posting in tx.Postings.Where(posting => posting.Amount < 0))
        {
            var holdings = Working(posting.Account);
            if (posting.Account.IsSystem)
            {
                holdings.Add(posting.ItemId, posting.Amount);
                continue;
            }

            if (posting.InstanceIds.Count == 0)
            {
                if (holdings.HoldsPiecesOf(posting.ItemId))
                    return LedgerRejection.InstanceMismatch;
            }
            else
            {
                foreach (int instance in posting.InstanceIds)
                {
                    if (!holdings.Instances.TryGetValue(instance, out int item) || item != posting.ItemId)
                        return LedgerRejection.InstanceNotOwned;
                    holdings.RemovePiece(instance);
                    released.Add(instance);
                }
            }

            if (holdings.CountOf(posting.ItemId) + posting.Amount < 0)
                return LedgerRejection.Overdraft;
            holdings.Add(posting.ItemId, posting.Amount);
        }

        foreach (var posting in tx.Postings.Where(posting => posting.Amount > 0))
        {
            var holdings = Working(posting.Account);
            if (posting.Account.IsSystem)
            {
                holdings.Add(posting.ItemId, posting.Amount);
                continue;
            }

            bool pieces = holdings.HoldsPiecesOf(posting.ItemId);
            if (posting.InstanceIds.Count == 0)
            {
                if (pieces)
                    return LedgerRejection.InstanceMismatch;
                holdings.Add(posting.ItemId, posting.Amount);
                continue;
            }

            if (!pieces && holdings.CountOf(posting.ItemId) != 0)
                return LedgerRejection.InstanceMismatch;
            foreach (int instance in posting.InstanceIds)
            {
                if (_owners.ContainsKey(instance) && !released.Contains(instance))
                    return LedgerRejection.InstanceTaken;
                holdings.AddPiece(posting.ItemId, instance);
                claimed[instance] = posting.Account;
            }
        }

        foreach (var (account, holdings) in working)
            _balances[account] = holdings;
        foreach (int instance in released)
            _owners.Remove(instance);
        foreach (var (instance, account) in claimed)
            _owners[instance] = account;
        return LedgerRejection.None;
    }
}
