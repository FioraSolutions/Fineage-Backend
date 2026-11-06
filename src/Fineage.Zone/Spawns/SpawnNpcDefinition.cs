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
    string NpcType = "",
    int Level = 1,
    long Exp = 0,
    int Sp = 0,
    double PAtk = 0,
    double PDef = 0,
    int AttackSpeed = 0,
    double Accuracy = 0,
    double Evasion = 0,
    int Critical = 0,
    int RandomDamage = 0,
    int Dex = 30,
    int ShieldDef = 0,
    int ShieldRate = 0,
    double MAtk = 0,
    double MDef = 0,
    int AttackRange = 0,
    bool Aggressive = false,
    int AggroRange = 0,
    int ClanHelpRange = 0,
    IReadOnlyList<string>? Clans = null,
    IReadOnlyList<int>? IgnoreNpcIds = null,
    double HpRegen = 0,
    double MpRegen = 0,
    int MaxMp = 0,
    int SoulShot = 0,
    int SoulShotChance = 0,
    int SpiritShot = 0,
    int SpiritShotChance = 0,
    IReadOnlyDictionary<string, double>? DefenceTraits = null,
    IReadOnlyList<Fineage.Zone.Npcs.NpcAiSkill>? AiSkills = null,
    int MinSkillChance = 7,
    int MaxSkillChance = 15);

public sealed record SpawnMapDefinition(
    string MapId,
    bool Enabled,
    IReadOnlyList<SpawnNpcDefinition> Npcs);
