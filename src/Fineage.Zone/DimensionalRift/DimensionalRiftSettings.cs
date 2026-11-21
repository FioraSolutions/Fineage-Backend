namespace Fineage.Zone.DimensionalRift;

/// <summary>Mobius GeneralConfig dimensional rift keys (defaults from CT_0 Interlude General.ini).</summary>
public sealed class DimensionalRiftSettings
{
    public const int FragmentItemId = 7079;
    public const int WaitingRoomType = 0;
    public const byte WaitingRoomId = 0;
    public const byte RiftOuterRoomId = 1;

    public int MinPartySize { get; init; } = 5;
    public int MaxJumps { get; init; } = 4;
    public int SpawnDelayMs { get; init; } = 10_000;
    public int AutoJumpMinSec { get; init; } = 480;
    public int AutoJumpMaxSec { get; init; } = 600;
    public float BossRoomTimeMultiply { get; init; } = 1.5f;
    public int CostRecruit { get; init; } = 18;
    public int CostSoldier { get; init; } = 21;
    public int CostOfficer { get; init; } = 24;
    public int CostCaptain { get; init; } = 27;
    public int CostCommander { get; init; } = 30;
    public int CostHero { get; init; } = 33;

    public static DimensionalRiftSettings Default { get; } = new();

    public int EntryCost(int type) => type switch
    {
        1 => CostRecruit,
        2 => CostSoldier,
        3 => CostOfficer,
        4 => CostCaptain,
        5 => CostCommander,
        6 => CostHero,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}