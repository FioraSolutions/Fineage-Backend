namespace Fineage.Zone.Spawns;

/// <summary>One fixed Mobius-style spawn row (template id + x/y/z + respawn seconds).</summary>
public sealed record SpawnNpcDefinition(
    int ObjectId,
    int NpcId,
    string Name,
    int X,
    int Y,
    int Z,
    int Heading,
    bool Attackable,
    int MaxHp,
    float RunSpeed,
    float WalkSpeed,
    int RespawnDelaySeconds,
    int RespawnRandomSeconds);

public sealed record SpawnMapDefinition(
    string MapId,
    bool Enabled,
    IReadOnlyList<SpawnNpcDefinition> Npcs);
