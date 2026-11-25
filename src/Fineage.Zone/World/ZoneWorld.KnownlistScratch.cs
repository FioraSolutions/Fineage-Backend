namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly HashSet<int> _knownlistDiffBefore = new();
    readonly HashSet<int> _knownlistDiffAfter = new();
    bool _knownlistDiffActive;

    /// <summary>
    /// Knownlist reconcile scratch: the before/after sets of one move. Region bodies of different regions run at the
    /// same time on worker threads (<c>auto-attacks</c>, <c>pending-hits</c>, …) and every one of them reconciles its
    /// movers, so this scratch is per thread. A shared pair let two bodies clear and fill it under each other and threw
    /// <c>Collection was modified</c> mid-diff in the parallel executor.
    /// </summary>
    [ThreadStatic] static HashSet<int>? t_reconcileBefore;

    /// <inheritdoc cref="t_reconcileBefore"/>
    [ThreadStatic] static HashSet<int>? t_reconcileAfter;

    static HashSet<int> ReconcileBeforeScratch => t_reconcileBefore ??= [];

    static HashSet<int> ReconcileAfterScratch => t_reconcileAfter ??= [];

    /// <summary>On the simulation thread before a move command applies: seeds <see cref="KnownlistDiffBefore"/>.</summary>
    internal void BeginKnownlistDiff(ZoneSession session)
    {
        _knownlistDiffActive = true;
        _knownlistDiffBefore.Clear();
        _knownlistDiffAfter.Clear();
        if (TryGetLivePlayer(session, out var self))
            _knownlistDiffBefore.UnionWith(self.Knownlist);
    }

    internal void EndKnownlistDiff() => _knownlistDiffActive = false;

    internal bool KnownlistDiffActive => _knownlistDiffActive;

    internal HashSet<int> KnownlistDiffBefore => _knownlistDiffBefore;

    internal HashSet<int> KnownlistDiffAfter => _knownlistDiffAfter;
}