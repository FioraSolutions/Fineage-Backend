namespace Fineage.Zone.Interest;

/// <summary>
/// Who can see whom. <see cref="SingleCellAoi"/> is the whole zone.
/// A grid can replace it later; callers diff <see cref="GetVisible"/> into a knownlist.
/// Calls are serialized by <c>ZoneWorld</c>.
/// </summary>
public interface IAoi
{
    void Enter(int objectId, int x, int y);

    void Move(int objectId, int x, int y);

    void Leave(int objectId);

    /// <summary>Other ids visible to <paramref name="objectId"/>. Does not include itself.</summary>
    IReadOnlyCollection<int> GetVisible(int objectId);
}
