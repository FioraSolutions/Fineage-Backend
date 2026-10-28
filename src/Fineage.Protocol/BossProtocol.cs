namespace Fineage.Protocol;

/// <summary>
/// Boss schedule wire. Opcodes are <see cref="MsgType.BossStatusRequest"/>,
/// <see cref="MsgType.AdminBossSchedule"/>, and <see cref="MsgType.BossStatus"/>.
/// Dungeon access owns 92 / 1092. These ids are the next free pair after that.
/// </summary>
public static class BossProtocol
{
    public const int KindRaid = 0;
    public const int KindGrand = 1;

    /// <summary>Shared status. Raid bosses use alive and dead only. Grand bosses also use waiting and fighting.</summary>
    public const int StatusAlive = 0;

    public const int StatusWaiting = 1;
    public const int StatusFighting = 2;

    /// <summary>
    /// Dead. Grand-boss scripts in Mobius sometimes store a private 1 for dead;
    /// this wire uses 3 so it does not collide with <see cref="StatusWaiting"/>.
    /// </summary>
    public const int StatusDead = 3;

    /// <summary><see cref="AdminBossScheduleMsg.Action"/>: mark the boss dead and roll the next window.</summary>
    public const int ActionMarkDead = 1;

    /// <summary><see cref="AdminBossScheduleMsg.Action"/>: spawn now and clear the respawn time.</summary>
    public const int ActionForceSpawn = 2;

    public const string UnknownBoss = "unknown_boss";
}
