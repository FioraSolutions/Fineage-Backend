namespace Fineage.Zone.Interest;

/// <summary>
/// One interest cell for the whole zone. Right while a zone is one village.
/// </summary>
public sealed class SingleCellAoi : IAoi
{
    readonly HashSet<int> _ids = [];

    public void Enter(int objectId, int x, int y) => _ids.Add(objectId);

    public void Move(int objectId, int x, int y)
    {
    }

    public void Leave(int objectId) => _ids.Remove(objectId);

    public IReadOnlyCollection<int> GetVisible(int objectId)
    {
        var visible = new List<int>(_ids.Count);
        foreach (int id in _ids)
        {
            if (id != objectId)
                visible.Add(id);
        }

        return visible;
    }
}
