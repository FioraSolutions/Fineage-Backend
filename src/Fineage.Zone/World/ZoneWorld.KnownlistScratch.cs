namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly HashSet<int> _knownlistDiffBefore = new();
    readonly HashSet<int> _knownlistDiffAfter = new();
    readonly HashSet<int> _reconcileBefore = new();
    readonly HashSet<int> _reconcileAfter = new();
    bool _knownlistDiffActive;

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