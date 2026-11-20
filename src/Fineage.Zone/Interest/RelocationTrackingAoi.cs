namespace Fineage.Zone.Interest;

/// <summary>
/// The world's AOI: every position change goes through <see cref="Enter"/> or <see cref="Move"/>, so this is where the
/// region directory learns which entities to check at the end-of-tick handoff.
/// </summary>
sealed class RelocationTrackingAoi(IAoi inner, Action<int> relocated) : IAoi
{
    public void Enter(int objectId, int x, int y)
    {
        inner.Enter(objectId, x, y);
        relocated(objectId);
    }

    public void Move(int objectId, int x, int y)
    {
        inner.Move(objectId, x, y);
        relocated(objectId);
    }

    public void Leave(int objectId) => inner.Leave(objectId);

    public IReadOnlyCollection<int> GetVisible(int objectId) => inner.GetVisible(objectId);

    public bool StillKnown(int objectId, int otherId) => inner.StillKnown(objectId, otherId);
}
