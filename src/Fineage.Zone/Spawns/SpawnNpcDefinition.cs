namespace Fineage.Zone.Spawns;

/// <summary>
/// One fixed Mobius-style spawn row. Name, attackable, hp, and speeds come from the row
/// when the JSON sets them (Cedric gremlins). Otherwise they come from <c>data/npcs.json</c>.
/// Title and type always come from the template.
/// </summary>
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
    int RespawnRandomSeconds,
    string Title = "",
    string NpcType = "");

public sealed record SpawnMapDefinition(
    string MapId,
    bool Enabled,
    IReadOnlyList<SpawnNpcDefinition> Npcs);
