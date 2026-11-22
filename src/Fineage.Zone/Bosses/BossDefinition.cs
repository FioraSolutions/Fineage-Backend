namespace Fineage.Zone.Bosses;

/// <summary>
/// One raid or grand boss row. Respawn bounds are milliseconds and already include
/// the Mobius min/max expansion (raid delay ± random, grand interval ± random).
/// </summary>
public sealed record BossDefinition(
    int NpcId,
    BossKind Kind,
    bool Enabled,
    int ObjectId,
    string Name,
    string Title,
    string NpcType,
    int X,
    int Y,
    int Z,
    int Heading,
    bool Attackable,
    int MaxHp,
    int MaxMp,
    float RunSpeed,
    float WalkSpeed,
    long RespawnMinMillis,
    long RespawnMaxMillis,
    BossLifeStatus InitialStatus)
{
    /// <summary>False for rift and script bosses whose spawn list delay is 0.</summary>
    public bool HasRespawnWindow => RespawnMinMillis > 0 || RespawnMaxMillis > 0;

    /// <summary>
    /// Mobius world spawn list row: an imported point. Instance, rift, and script bosses
    /// (Four Sepulchers Halisha, Frintezza, Anakazel, Dr Chaos) are stored at 0,0,0.
    /// A zero raid delay still has a world point (Venom) and comes back on process start.
    /// </summary>
    public bool HasWorldSpawnPoint => X != 0 || Y != 0 || Z != 0;
}
