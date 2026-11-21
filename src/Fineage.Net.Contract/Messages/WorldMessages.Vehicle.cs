namespace Fineage.Net.Contract;

/// <summary>Interlude <c>VehicleInfo</c> (0x59) — boat position snapshot on spawn / stop / enter range.</summary>
public sealed class VehicleInfoNetMsg
{
    public int BoatObjectId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Heading { get; set; }
}

/// <summary>Interlude <c>VehicleDeparture</c> (0x5A) — boat starts moving toward a route point.</summary>
public sealed class VehicleDepartureNetMsg
{
    public int BoatObjectId { get; set; }
    public int MoveSpeed { get; set; }
    public int RotationSpeed { get; set; }
    public int DestX { get; set; }
    public int DestY { get; set; }
    public int DestZ { get; set; }
}