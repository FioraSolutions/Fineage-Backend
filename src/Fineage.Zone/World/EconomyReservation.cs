namespace Fineage.Zone.World;

/// <summary>Characters with a durable economy commit in flight cannot start another until it completes.</summary>
internal sealed class EconomyReservation
{
    readonly object _gate = new();
    readonly HashSet<int> _reserved = [];

    public bool TryReserve(IEnumerable<int> characterIds)
    {
        var ids = characterIds.Where(id => id > 0).Distinct().ToList();
        lock (_gate)
        {
            foreach (int id in ids)
            {
                if (_reserved.Contains(id))
                    return false;
            }

            foreach (int id in ids)
                _reserved.Add(id);
            return true;
        }
    }

    public void Release(IEnumerable<int> characterIds)
    {
        lock (_gate)
        {
            foreach (int id in characterIds)
                _reserved.Remove(id);
        }
    }

    public bool IsReserved(int characterId)
    {
        if (characterId <= 0)
            return false;
        lock (_gate)
            return _reserved.Contains(characterId);
    }
}