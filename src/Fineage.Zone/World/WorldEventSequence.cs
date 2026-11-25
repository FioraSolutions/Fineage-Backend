namespace Fineage.Zone.World;

/// <summary>Monotonic fan-out batch identity: tick, phase within the tick, and per-phase sequence.</summary>
internal readonly record struct WorldEventSequence(long Tick, byte Phase, ulong Sequence)
{
    public static WorldEventSequence First(long tick, byte phase) => new(tick, phase, 0);

    public WorldEventSequence Next() => this with { Sequence = Sequence + 1 };
}