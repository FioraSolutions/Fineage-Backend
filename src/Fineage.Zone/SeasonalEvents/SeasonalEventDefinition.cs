using Fineage.Zone.Drops;

namespace Fineage.Zone.SeasonalEvents;

/// <summary>Mobius <c>LongTimeEvent</c> config.xml for one seasonal script folder.</summary>
public sealed class SeasonalEventDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public long StartUnixMs { get; init; }
    public long EndUnixMs { get; init; }
    public string OnEnterMessage { get; init; } = "";
    public string OnEndMessage { get; init; } = "";
    public IReadOnlyList<SeasonalNpcSpawn> Spawns { get; init; } = [];
    public IReadOnlyList<DropGroupDefinition> DropGroups { get; init; } = [];

    public bool IsActiveAt(long nowUnixMs) =>
        EndUnixMs > StartUnixMs && nowUnixMs >= StartUnixMs && nowUnixMs <= EndUnixMs;
}

public sealed class SeasonalNpcSpawn
{
    public int NpcId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public int Heading { get; init; }
}
