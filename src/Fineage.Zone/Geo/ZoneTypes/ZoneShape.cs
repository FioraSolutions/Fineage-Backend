using Fineage.Zone.Geo;

namespace Fineage.Zone.Geo.ZoneTypes;

/// <summary>
/// One zone footprint. NPoly uses the same even-odd test as <see cref="PeaceZones"/>; cuboids and cylinders
/// are the other two Mobius shapes.
/// </summary>
public sealed class ZoneShape
{
    public string Name { get; init; } = "";
    public int Id { get; init; }
    public string Kind { get; init; } = "";
    public int MinZ { get; init; }
    public int MaxZ { get; init; }
    public int MinX { get; init; }
    public int MaxX { get; init; }
    public int MinY { get; init; }
    public int MaxY { get; init; }
    public int Radius { get; init; }
    public (int X, int Y)[] Nodes { get; init; } = [];
    PeaceZone? _poly;

    public bool Contains(int x, int y, int z)
    {
        if (z < MinZ || z > MaxZ || x < MinX || x > MaxX || y < MinY || y > MaxY)
            return false;
        if (Kind == "Cuboid")
            return true;
        if (Kind == "Cylinder")
        {
            long dx = (long)x - Nodes[0].X;
            long dy = (long)y - Nodes[0].Y;
            long r = Radius;
            return dx * dx + dy * dy <= r * r;
        }

        _poly ??= new PeaceZone
        {
            Name = Name,
            MinZ = MinZ,
            MaxZ = MaxZ,
            Nodes = Nodes,
            MinX = MinX,
            MaxX = MaxX,
            MinY = MinY,
            MaxY = MaxY
        };
        return PeaceZones.Hit([_poly], x, y, z);
    }
}
