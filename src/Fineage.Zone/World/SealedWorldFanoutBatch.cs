namespace Fineage.Zone.World;

/// <summary>Immutable sealed batch ready for the fan-out dispatcher (no live world references).</summary>
internal sealed class SealedWorldFanoutBatch(
    WorldFanoutBatch source,
    WorldEventSequence sequence,
    long enqueuedAtTimestamp,
    CommandSubmitMark[]? commandSubmits = null)
{
    public WorldEventSequence Sequence { get; } = sequence;
    public long EnqueuedAtTimestamp { get; } = enqueuedAtTimestamp;
    public CommandSubmitMark[]? CommandSubmits { get; } = commandSubmits;
    public int Coalesced => source.Coalesced;
    public int Count => source.Count;

    /// <summary>Recipient work this batch hands to the delivery worker; the dispatcher budgets its backlog by it.</summary>
    public long SendWork => source.SendWork;

    /// <summary>Visual writes already shed inside the batch to keep it inside its own send-work budget.</summary>
    public int TakeDroppedVisuals() => source.TakeDroppedVisuals();

    /// <summary>
    /// Sheds visual writes until the batch carries at most <paramref name="limit"/> send work. The dispatcher calls
    /// this on an enqueue that would cross the hard backlog: the batch then hands over less work instead of growing
    /// the backlog.
    /// </summary>
    public int ShedVisualsToBudget(long limit) => source.ShedVisualsToBudget(limit);

    /// <summary>True when every write left in the batch is a visual one (animations and markers only).</summary>
    public bool HasOnlyVisualWrites() => source.HasOnlyVisualWrites();

    public WorldFanoutBatch.FanoutSendCursor OpenCursor() => source.OpenCursor();

    internal void ReleaseScratchResources() => source.ReleaseAudiences();
}
