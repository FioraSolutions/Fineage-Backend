namespace Fineage.Zone.Bosses;

/// <summary>One boss the world should materialize. <see cref="IgnoreEnabled"/> is the admin force-spawn path.</summary>
public readonly record struct BossSpawnOrder(
    int NpcId,
    int ObjectId,
    int CurrentHp,
    int CurrentMp,
    bool IgnoreEnabled);

/// <summary>Schedule view. <c>Spawned</c> is filled by the world, not the store.</summary>
public readonly record struct BossView(
    int NpcId,
    int ObjectId,
    BossKind Kind,
    BossLifeStatus Status,
    long RespawnUnixMs,
    int CurrentHp,
    int MaxHp,
    int CurrentMp,
    int MaxMp,
    bool Enabled);

/// <summary>
/// Raid and grand boss respawn schedule.
/// Raid windows follow Mobius <c>RaidBossSpawnManager.updateStatus</c>
/// (<c>Rnd.get(respawnMinDelay, respawnMaxDelay)</c> after the NPC.ini multipliers).
/// Grand windows follow the boss scripts
/// (<c>interval + getRandom(-random, random)</c> hours from GrandBoss.ini).
/// A zero raid delay is not armed; the next process start spawns the boss once the saved time is in the past.
/// </summary>
