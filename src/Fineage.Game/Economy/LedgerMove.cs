namespace Fineage.Game.Economy;

/// <summary>Thrown when the ledger and the state it mirrors disagree about a move.</summary>
public sealed class LedgerReconciliationException(string message) : InvalidOperationException(message);

/// <summary>Hands out <see cref="LedgerTxId"/>s in order within each tick.</summary>
public sealed class LedgerTxSequence
{
    long _tick = long.MinValue;
    int _next;

    public LedgerTxId Next(long tick)
    {
        if (tick != _tick)
        {
            _tick = tick;
            _next = 0;
        }

        return new LedgerTxId(tick, _next++);
    }
}

/// <summary>
/// Records one move next to the state that still decides it (inventories, treasuries) and reconciles the two.
/// <see cref="Observe"/> each account the move touches before changing it: when the journal does not explain what
/// the account holds (a move the ledger does not record yet), an <see cref="LedgerTxKind.Adjustment"/> against
/// <see cref="SystemAccount.Untracked"/> brings it in line first. <see cref="Commit"/> submits the move and checks
/// that every observed account now holds exactly what the state shows. A rejection or a mismatch throws
/// <see cref="LedgerReconciliationException"/>; it is never corrected silently.
/// </summary>
public sealed class LedgerMove
{
    readonly EconomyLedger _ledger;
    readonly Func<LedgerTxId> _nextId;
    readonly LedgerTxKind _kind;
    readonly long _wallUnixMs;
    readonly int _causedBy;
    readonly int _reference;
    readonly List<LedgerAccount> _observed = [];
    readonly List<LedgerPosting> _postings = [];

    public LedgerMove(
        EconomyLedger ledger,
        Func<LedgerTxId> nextId,
        LedgerTxKind kind,
        long wallUnixMs,
        int causedBy,
        int reference)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _nextId = nextId ?? throw new ArgumentNullException(nameof(nextId));
        if (kind == LedgerTxKind.Adjustment)
            throw new ArgumentException("adjustments are made by Observe", nameof(kind));
        _kind = kind;
        _wallUnixMs = wallUnixMs;
        _causedBy = causedBy;
        _reference = reference;
    }

    public void Observe(LedgerAccount account, LedgerHoldings before)
    {
        ArgumentNullException.ThrowIfNull(before);
        if (account.IsSystem)
            throw new ArgumentException("system accounts have no state to observe", nameof(account));
        if (_observed.Contains(account))
            return;
        _observed.Add(account);

        var postings = Alignment(account, before);
        if (postings.Count == 0)
            return;
        var adjustment = new LedgerTransaction(_nextId(), LedgerTxKind.Adjustment, _causedBy, 0, _wallUnixMs, postings);
        var result = _ledger.Submit(adjustment);
        if (!result.Ok)
            throw new LedgerReconciliationException($"adjustment {adjustment.Id} for {account} rejected: {result.Rejection}");
    }

    public void Post(LedgerAccount account, int itemId, long amount, IReadOnlyList<int>? instanceIds = null)
    {
        if (amount == 0)
            return;
        _postings.Add(new LedgerPosting(account, itemId, amount, instanceIds ?? []));
    }

    /// <summary>Submits the move, then checks each observed account against <paramref name="after"/>.</summary>
    public LedgerResult Commit(Func<LedgerAccount, LedgerHoldings> after)
    {
        ArgumentNullException.ThrowIfNull(after);
        foreach (var posting in _postings)
        {
            if (!posting.Account.IsSystem && !_observed.Contains(posting.Account))
                throw new InvalidOperationException($"{_kind} posts to {posting.Account} without observing it first");
        }

        var tx = new LedgerTransaction(_nextId(), _kind, _causedBy, _reference, _wallUnixMs, Compact(_postings));
        var result = _ledger.Submit(tx);
        if (!result.Ok)
            throw new LedgerReconciliationException($"{_kind} {tx.Id} rejected: {result.Rejection}");

        foreach (var account in _observed)
        {
            var journal = _ledger.HoldingsOf(account);
            var state = after(account);
            if (!journal.SameAs(state))
                throw new LedgerReconciliationException(
                    $"{_kind} {tx.Id}: {account} holds {state} but the journal says {journal}");
        }

        return result;
    }

    List<LedgerPosting> Alignment(LedgerAccount account, LedgerHoldings observed)
    {
        var journal = _ledger.HoldingsOf(account);
        var postings = new List<LedgerPosting>();
        var untracked = new SortedDictionary<int, long>();
        var counted = new Dictionary<int, long>();
        var nowhere = LedgerAccount.Of(SystemAccount.Untracked);

        foreach (var (instance, item) in observed.Instances)
        {
            if (journal.Instances.TryGetValue(instance, out int held) && held == item)
                continue;
            if (_ledger.OwnerOf(instance) is { } elsewhere && elsewhere != account)
            {
                int elsewhereItem = _ledger.HoldingsOf(elsewhere).Instances[instance];
                postings.Add(new LedgerPosting(elsewhere, elsewhereItem, -1, [instance]));
                untracked[elsewhereItem] = untracked.GetValueOrDefault(elsewhereItem) + 1;
            }

            postings.Add(new LedgerPosting(account, item, 1, [instance]));
            untracked[item] = untracked.GetValueOrDefault(item) - 1;
            counted[item] = counted.GetValueOrDefault(item) + 1;
        }

        foreach (var (instance, item) in journal.Instances)
        {
            if (observed.Instances.TryGetValue(instance, out int kept) && kept == item)
                continue;
            postings.Add(new LedgerPosting(account, item, -1, [instance]));
            untracked[item] = untracked.GetValueOrDefault(item) + 1;
            counted[item] = counted.GetValueOrDefault(item) - 1;
        }

        foreach (int item in observed.Counts.Keys.Union(journal.Counts.Keys).Order())
        {
            long rest = observed.CountOf(item) - journal.CountOf(item) - counted.GetValueOrDefault(item);
            if (rest == 0)
                continue;
            postings.Add(new LedgerPosting(account, item, rest));
            untracked[item] = untracked.GetValueOrDefault(item) - rest;
        }

        foreach (var (item, amount) in untracked)
        {
            if (amount != 0)
                postings.Add(new LedgerPosting(nowhere, item, amount));
        }

        return postings;
    }

    static List<LedgerPosting> Compact(List<LedgerPosting> postings)
    {
        var merged = new List<LedgerPosting>();
        var plain = new Dictionary<(LedgerAccount, int), int>();
        foreach (var posting in postings)
        {
            if (posting.InstanceIds.Count != 0)
            {
                merged.Add(posting);
                continue;
            }

            if (plain.TryGetValue((posting.Account, posting.ItemId), out int at))
                merged[at] = merged[at] with { Amount = merged[at].Amount + posting.Amount };
            else
            {
                plain[(posting.Account, posting.ItemId)] = merged.Count;
                merged.Add(posting);
            }
        }

        merged.RemoveAll(posting => posting.Amount == 0);
        return merged;
    }
}
