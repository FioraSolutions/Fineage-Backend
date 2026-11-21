namespace Fineage.Protocol;

/// <summary>Mobius <c>RequestActionUse</c> 38 (mount / dismount) and <c>Ride</c> packet mount types.</summary>
public static class MountProtocol
{
    /// <summary>Interlude action panel: mount the summoned strider / dismount when already riding.</summary>
    public const int RideAction = 38;

    /// <summary>Mobius <c>Ride.ACTION_MOUNT</c>.</summary>
    public const int RideMount = 1;

    /// <summary>Mobius <c>Ride.ACTION_DISMOUNT</c>.</summary>
    public const int RideDismount = 0;

    /// <summary>Client ride class id = template npc id + this offset (Mobius Ride packet).</summary>
    public const int RideNpcOffset = 1_000_000;

    public const string Blocked = "mount_blocked";

    // Mobius SystemMessageId (strider mount refusals)
    public const int SysMsgStriderDeadRider = 1004;
    public const int SysMsgDeadStrider = 1005;
    public const int SysMsgStriderInBattle = 1006;
    public const int SysMsgRiderInBattle = 1007;
    public const int SysMsgMountWhileStanding = 1008;
}