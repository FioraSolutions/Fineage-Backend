using Fineage.Zone;

namespace Fineage.Zone.Drops;

/// <summary>Deterministic droplist rolls for acceptance/selftest (seed matches gremlin death tests).</summary>
public static class MobiusDropTestSeed
{
    public const int DefaultSeed = 42;
    public const int TrainingHallGremlinNpcId = 18342;

    public static DropStack ExpectedGremlinDeathDrop(int seed = DefaultSeed, DropCatalog? catalog = null)
    {
        catalog ??= DropCatalog.LoadDefault();
        // Talking Island 20001 has no Mobius dropLists. Training-hall 18342 is the generated row.
        if (!catalog.TryGet(TrainingHallGremlinNpcId, out var definition))
            throw new InvalidOperationException("Training-hall gremlin droplist missing from datapack.");

        var rolls = DropRoller.RollDeath(definition, new Random(seed));
        if (rolls.Count != 1)
            throw new InvalidOperationException($"expected one gremlin death stack, got {rolls.Count}");
        return rolls[0];
    }
}
