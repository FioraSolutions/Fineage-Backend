namespace Fineage.Zone.World;

sealed partial class ZoneEntity
{
    /// <summary>Players and npcs this entity currently knows. Membership changes drop <see cref="NetWatcherAudience"/>.</summary>
    public WatcherSet Knownlist { get; }

    /// <summary>Audience id for <c>KnownNetPlayersOf</c>, held while the knownlist and self-session bit stay the same.</summary>
    internal AudienceSnapshotId NetWatcherAudience;

    internal bool NetWatcherSelf;

    /// <summary>Bumped when the cached audience is dropped, so a list captured earlier is not reused.</summary>
    internal int NetWatcherEpoch;

    public ZoneEntity()
    {
        Knownlist = new WatcherSet();
        Knownlist.Changed = DropNetWatcherAudience;
    }

    internal void ReplaceNetWatcherAudience(AudienceSnapshotId id, bool self)
    {
        var previous = NetWatcherAudience;
        NetWatcherEpoch++;
        NetWatcherAudience = id;
        NetWatcherSelf = self;
        if (!previous.IsEmpty)
            AudienceSnapshots.Release(previous);
    }

    internal void DropNetWatcherAudience()
    {
        NetWatcherEpoch++;
        if (NetWatcherAudience.IsEmpty)
            return;
        var id = NetWatcherAudience;
        NetWatcherAudience = default;
        NetWatcherSelf = false;
        AudienceSnapshots.Release(id);
    }
}
