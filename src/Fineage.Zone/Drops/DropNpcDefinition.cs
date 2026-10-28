namespace Fineage.Zone.Drops;

public sealed class DropNpcDefinition
{
    public required int NpcId { get; init; }
    public required DropDeathDefinition Death { get; init; }
    public DropCorpseDefinition? Corpse { get; init; }
}

public sealed class DropDeathDefinition
{
    public required IReadOnlyList<DropGroupDefinition> Groups { get; init; }
}

public sealed class DropGroupDefinition
{
    /// <summary>Percent-style weight (0–100). Group must pass this roll to award one item inside.</summary>
    public required double Chance { get; init; }
    public required IReadOnlyList<DropItemDefinition> Items { get; init; }
}

public sealed class DropCorpseDefinition
{
    public required IReadOnlyList<DropItemDefinition> Items { get; init; }
}

public sealed class DropItemDefinition
{
    public required int ItemId { get; init; }
    public required int Min { get; init; }
    public required int Max { get; init; }
    /// <summary>Relative weight inside a death group, or independent percent for corpse lines.</summary>
    public required double Chance { get; init; }
}
