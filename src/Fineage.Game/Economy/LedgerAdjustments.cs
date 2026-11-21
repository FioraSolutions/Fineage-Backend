namespace Fineage.Game.Economy;

/// <summary>Raised when <see cref="LedgerMove"/> aligns an account that was changed outside the journal.</summary>
public readonly record struct LedgerAdjustmentNotice(
    LedgerTxKind PendingMoveKind,
    LedgerAccount Account,
    IReadOnlyList<LedgerPosting> Postings);

/// <summary>Ledger alignment policy: logging, metrics, and optional strict refusal.</summary>
public static class LedgerAdjustments
{
    /// <summary>When true, any alignment adjustment throws instead of posting to the journal.</summary>
    public static bool Strict { get; set; }

    public static event Action<LedgerAdjustmentNotice>? Adjusted;

    internal static void Raise(LedgerTxKind pendingMoveKind, LedgerAccount account, IReadOnlyList<LedgerPosting> postings)
    {
        if (postings.Count == 0)
            return;
        var notice = new LedgerAdjustmentNotice(pendingMoveKind, account, postings);
        if (Strict)
        {
            throw new LedgerReconciliationException(
                $"ledger strict: {pendingMoveKind} would adjust {account} before posting ({Summarize(notice)})");
        }

        Adjusted?.Invoke(notice);
    }

    static string Summarize(LedgerAdjustmentNotice notice)
    {
        var parts = new List<string>();
        foreach (var posting in notice.Postings)
        {
            if (posting.Amount == 0)
                continue;
            parts.Add($"{posting.Account}:{posting.ItemId}×{posting.Amount}");
        }

        return parts.Count == 0 ? "empty" : string.Join(", ", parts);
    }
}