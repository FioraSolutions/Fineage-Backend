namespace Fineage.Zone.World;

/// <summary>Immutable sealed batch ready for the fan-out dispatcher (no live world references).</summary>
internal sealed class SealedWorldFanoutBatch(WorldFanoutBatch source, WorldEventSequence sequence, long enqueuedAtTimestamp)
{
    public WorldEventSequence Sequence { get; } = sequence;
    public long EnqueuedAtTimestamp { get; } = enqueuedAtTimestamp;
    public int Coalesced => source.Coalesced;
    public int Count => source.Count;

    public WorldFanoutBatch.FanoutSendCursor OpenCursor() => source.OpenCursor();

    internal void ReleaseScratchResources() => source.ReleaseAudiences();
}
