namespace Fineage.Net.Contract;

/// <summary>
/// Zone → owner: which pet/mount actions the Interlude client may show (summon window + action 38).
/// Sent on enter world, pet spawn/dismiss, mount/dismount, and peace/siege moves when relevant.
/// </summary>
public sealed class PlayerActionStateNetMsg
{
    public bool PetOut { get; set; }
    /// <summary>Summoned pet's template is mountable (<see cref="SummonInfoNetMsg.Mountable"/>).</summary>
    public bool MountablePet { get; set; }
    public bool Mounted { get; set; }
    /// <summary>Ride (38) allowed now: mountable pet out, fed, not dead, not in peace/siege, not in battle.</summary>
    public bool CanRide { get; set; }
    /// <summary>0 none, 1 strider, 2 wyvern (Mobius mountType while mounted).</summary>
    public byte MountKind { get; set; }
    /// <summary>Pet collar object id when a pet is out (quests / UI); 0 when none.</summary>
    public int PetControlObjectId { get; set; }
}