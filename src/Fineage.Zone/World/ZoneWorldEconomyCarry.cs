using System.Diagnostics;

namespace Fineage.Zone.World;

/// <summary>
/// #1112: a step whose economy batch could not be handed to the persist worker keeps its replies for the next step.
/// <para>
/// Inside a step the hand-off fails for two reasons, both temporary. A character in the batch can already have a commit
/// in flight (a logout commit steals every dirty bag, so under reconnect churn the travellers who just paid a fee are
/// reserved by someone else's logout). Or the worker queue can be full. Either way the batch is restored dirty and the
/// next step's commit carries it, so the change is not lost. Telling the client <c>economy_commit_failed</c> for that
/// was a lie: the teleport happened and the fee landed one step later. The reply now waits for the commit that really
/// carries the change. A reply that still has no commit after <see cref="EconomyReplyCarryBudget"/> is refused
/// explicitly, never dropped.
/// </para>
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>How long a reply may wait for its economy commit across steps before it is refused.</summary>
    internal TimeSpan EconomyReplyCarryBudget { get; set; } = TimeSpan.FromSeconds(10);

    internal EconomyReservation? EconomyReservationsForTests => _economyPersist?.Reservations;

    /// <summary>
    /// Tests: runs on the simulation thread after the commands stage applied, before its economy commit and replies —
    /// the window in which another character's logout commit can take this step's dirty bags.
    /// </summary>
    internal Action? BeforeEconomyReplyReleaseForTests { get; set; }

    /// <summary>Tests: a logout commit for <paramref name="leavingObjectId"/>, which steals every dirty bag.</summary>
    internal bool CommitEconomyAsLogoutForTests(int leavingObjectId) =>
        TryCommitDirtyEconomy(discardWithoutPublish: leavingObjectId);

    readonly List<ZoneCommandIngress.Applied> _economyCarried = [];
    readonly Dictionary<ZoneCommandIngress.Submission, long> _economyCarriedSince = new(ReferenceEqualityComparer.Instance);

    long _economyCarriedTotal;

    internal int EconomyCarriedReplyCount => _economyCarried.Count;

    /// <summary>Replies that ever waited for a later step's commit (shutdown summary and tests).</summary>
    internal long EconomyCarriedReplyTotal => _economyCarriedTotal;

    /// <summary>Simulation thread: this step's applied commands, after the replies carried from earlier steps.</summary>
    IReadOnlyList<ZoneCommandIngress.Applied> TakeAppliedWithCarried()
    {
        var applied = _ingress.TakeApplied();
        if (_economyCarried.Count == 0)
            return applied;

        var merged = new List<ZoneCommandIngress.Applied>(_economyCarried.Count + applied.Count);
        merged.AddRange(_economyCarried);
        merged.AddRange(applied);
        _economyCarried.Clear();
        return merged;
    }

    /// <summary>
    /// Simulation thread, after a step's batch was not queued: answer every reply whose character has no uncommitted
    /// economy, carry the rest to the next step, and refuse the ones that ran out of budget.
    /// </summary>
    void AnswerOrCarryUncommitted(IReadOnlyList<ZoneCommandIngress.Applied> applied, IReadOnlySet<int> uncommitted)
    {
        long now = Stopwatch.GetTimestamp();
        var answerNow = new List<ZoneCommandIngress.Applied>(applied.Count);
        List<ZoneCommandIngress.Applied>? refused = null;
        long newlyCarried = 0;
        foreach (var row in applied)
        {
            bool waitsOnEconomy = row.Error == null && row.Failure == null
                && uncommitted.Contains(row.Submission.Session.ObjectId);
            if (!waitsOnEconomy)
            {
                _economyCarriedSince.Remove(row.Submission);
                answerNow.Add(row);
                continue;
            }

            if (!_economyCarriedSince.TryGetValue(row.Submission, out long since))
            {
                _economyCarriedSince[row.Submission] = since = now;
                newlyCarried++;
            }
            if (Stopwatch.GetElapsedTime(since, now) <= EconomyReplyCarryBudget)
            {
                _economyCarried.Add(row);
                continue;
            }

            _economyCarriedSince.Remove(row.Submission);
            (refused ??= []).Add(row);
        }

        if (answerNow.Count > 0)
            ZoneCommandIngress.AnswerAfterFlush(answerNow, committed: true, uncommitted: null, EconomyCommitFailed);
        if (refused != null)
        {
            ZoneLog.Warn(
                $"economy reply refused after {EconomyReplyCarryBudget.TotalSeconds:0}s without a commit: {refused.Count} command(s), pending={_economyPersist?.PendingCount ?? 0}");
            ZoneCommandIngress.AnswerAfterFlush(refused, committed: false, uncommitted, EconomyCommitFailed);
        }

        _economyCarriedTotal += newlyCarried;
    }

    static HashSet<int> OnOtherCommit(List<ZoneCommandIngress.Applied> rows)
    {
        var ids = new HashSet<int>(rows.Count);
        foreach (var row in rows)
            ids.Add(row.Submission.Session.ObjectId);
        return ids;
    }

    /// <summary>A queued batch took the carried replies with it; they answer from its completion.</summary>
    void ForgetCarriedSince(IReadOnlyList<ZoneCommandIngress.Applied> applied)
    {
        if (_economyCarriedSince.Count == 0)
            return;
        foreach (var row in applied)
            _economyCarriedSince.Remove(row.Submission);
    }

    /// <summary>
    /// Shutdown, after the final ledger flush: a carried reply whose character has nothing left to commit landed and
    /// answers its own result; anything still dirty is refused. Nothing is left waiting.
    /// </summary>
    void AnswerCarriedEconomyRepliesOnShutdown()
    {
        ZoneLog.Info($"economy replies carried to a later step: total={_economyCarriedTotal} still-waiting={_economyCarried.Count}");
        if (_economyCarried.Count == 0)
            return;
        var rows = _economyCarried.ToList();
        _economyCarried.Clear();
        _economyCarriedSince.Clear();
        var stillDirty = HasDirtyEconomy() ? UncommittedEconomicCharacters() : [];
        using (_economyLock.Enter())
            stillDirty.UnionWith(_economyInFlight.Keys);
        ZoneCommandIngress.AnswerAfterFlush(rows, committed: stillDirty.Count == 0, stillDirty, EconomyCommitFailed);
    }
}
