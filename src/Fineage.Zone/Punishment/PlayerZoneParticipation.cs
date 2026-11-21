namespace Fineage.Zone.Punishment;

/// <summary>Tracks players inside festival arenas and dimensional rifts for teleport guards.</summary>
public sealed class PlayerZoneParticipation
{
    readonly HashSet<int> _festival = [];
    readonly HashSet<int> _rift = [];

    public void SetFestival(int objectId, bool inside)
    {
        if (inside)
            _festival.Add(objectId);
        else
            _festival.Remove(objectId);
    }

    public void SetDimensionalRift(int objectId, bool inside)
    {
        if (inside)
            _rift.Add(objectId);
        else
            _rift.Remove(objectId);
    }

    public bool InFestival(int objectId) => _festival.Contains(objectId);

    public bool InDimensionalRift(int objectId) => _rift.Contains(objectId);
}