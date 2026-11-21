namespace Fineage.Zone.World;

/// <summary>What a siege NPC is (Mobius instance types <c>Door</c>, <c>ControlTower</c>, <c>FlameTower</c>, <c>SiegeFlag</c>, <c>Defender</c>).</summary>
enum SiegeNpcRole
{
    None = 0,
    Door,
    ControlTower,
    FlameTower,
    Flag,
    Guard
}

/// <summary>Siege payload of an NPC entity: which castle it belongs to and, for doors and flags, the extra state.</summary>
sealed class SiegeNpcInfo
{
    public required SiegeNpcRole Role { get; init; }
    public required int CastleId { get; init; }
    /// <summary>Clan hall id for agit gates (castle doors use <see cref="CastleId"/> only).</summary>
    public int HallId { get; init; }
    /// <summary>Mobius Doors.xml id (doors only).</summary>
    public int DoorId { get; init; }
    public bool Wall { get; init; }
    /// <summary>Base door HP before the chamberlain upgrade ratio.</summary>
    public int BaseHp { get; init; }
    /// <summary>Owner clan of a headquarters flag.</summary>
    public int FlagClanId { get; init; }
    /// <summary>Last "your base is being attacked" notice (unix ms), so a flag under fire does not spam its clan.</summary>
    public long LastAttackNoticeMs { get; set; }
}

sealed partial class ZoneEntity
{
    /// <summary>Set on castle gates / walls, towers, headquarters and siege guards; null on every other NPC.</summary>
    public SiegeNpcInfo? SiegeNpc { get; set; }
}
