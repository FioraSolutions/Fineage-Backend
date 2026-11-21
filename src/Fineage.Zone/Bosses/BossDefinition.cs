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
    /// Mobius <c>raidboss_spawnlist</c> / <c>grandboss_data</c> rows the world schedule owns: imported
    /// coordinates and a respawn window. Instance, rift, and script bosses sit at 0,0,0 or delay 0.
    /// </summary>
    public bool IsWorldScheduledSpawn => HasRespawnWindow && (X != 0 || Y != 0 || Z != 0);
}
