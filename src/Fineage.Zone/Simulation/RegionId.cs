namespace Fineage.Zone.Simulation;

/// <summary>
/// One simulation region: an instance layer plus one grid cell of the world plane. Ascending
/// (<see cref="InstanceId"/>, <see cref="X"/>, <see cref="Y"/>) is the deterministic order every region stage runs in.
/// </summary>
public readonly record struct RegionId(int InstanceId, int X, int Y) : IComparable<RegionId>
{
    public int CompareTo(RegionId other)
    {
        int byInstance = InstanceId.CompareTo(other.InstanceId);
        if (byInstance != 0)
            return byInstance;
        int byX = X.CompareTo(other.X);
        return byX != 0 ? byX : Y.CompareTo(other.Y);
    }

    public override string ToString() => $"{InstanceId}:{X}_{Y}";
}
