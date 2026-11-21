using Fineage.Game.Services;

namespace Fineage.Zone.World;

/// <summary>Player riding state (Mobius <c>Player.getMountType</c> / <c>getMountObjectID</c>).</summary>
sealed class MountState
{
    public PetMountKind Kind { get; init; }
    public int NpcId { get; init; }
    /// <summary>Collar object id when mounted from a pet; 0 for wyvern-manager mounts.</summary>
    public int ControlObjectId { get; init; }
    public float RunSpeed { get; init; }
    public float WalkSpeed { get; init; }
    /// <summary>True when the wyvern came from a castle manager (no collar to restore).</summary>
    public bool WyvernRent { get; init; }
}

sealed partial class ZoneEntity
{
    public MountState? Mount { get; set; }

    public bool IsMounted => Mount != null;

    public PetMountKind MountKind => Mount?.Kind ?? PetMountKind.None;
}