using Fineage.Zone;

namespace Fineage.Zone.Drops;

/// <summary>Deterministic droplist rolls for acceptance/selftest (seed matches gremlin death tests).</summary>
public static class MobiusDropTestSeed
{
    public const int DefaultSeed = 42;

    public static DropStack ExpectedGremlinDeathDrop(int seed = DefaultSeed, DropCatalog? catalog = null)
    {
        catalog ??= DropCatalog.LoadDefault();
        if (!catalog.TryGet(TalkingIsland.GremlinNpcId, out var definition))
            throw new InvalidOperationException("Gremlin droplist missing from datapack.");

        var rolls = DropRoller.RollDeath(definition, new Random(seed));
        if (rolls.Count != 1)
            throw new InvalidOperationException($"expected one gremlin death stack, got {rolls.Count}");
        return rolls[0];
    }
}
