namespace Fineage.Net.Contract;

/// <summary>Zone → watchers: Mobius <c>Ride</c> (0x86): mount or dismount visual on a player.</summary>
public sealed class RideNetMsg
{
    public int ObjectId { get; set; }

    /// <summary><see cref="Fineage.Protocol.MountProtocol.RideMount"/> or <see cref="Fineage.Protocol.MountProtocol.RideDismount"/>.</summary>
    public int Action { get; set; }
    /// <summary>0 none, 1 strider, 2 wyvern (Mobius mountType).</summary>
    public byte MountKind { get; set; }
    /// <summary>Mobius ride class id (npc id + 1_000_000); 0 on dismount.</summary>
    public int RideNpcId { get; set; }
}