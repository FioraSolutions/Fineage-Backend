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

/// <summary>Interlude <c>VehicleCheckLocation</c> (0x5B).</summary>
public sealed class VehicleCheckLocationNetMsg
{
    public int BoatObjectId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Heading { get; set; }
}

/// <summary>Interlude <c>GetOnVehicle</c> (server 0x5C).</summary>
public sealed class GetOnVehicleNetMsg
{
    public int PlayerObjectId { get; set; }
    public int BoatObjectId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

/// <summary>Interlude <c>GetOffVehicle</c> (server 0x5D).</summary>
public sealed class GetOffVehicleNetMsg
{
    public int PlayerObjectId { get; set; }
    public int BoatObjectId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

/// <summary>Interlude <c>MoveToLocationInVehicle</c> (0x71).</summary>
public sealed class MoveToLocationInVehicleNetMsg
{
    public int PlayerObjectId { get; set; }
    public int BoatObjectId { get; set; }
    public int DestX { get; set; }
    public int DestY { get; set; }
    public int DestZ { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }
    public int OriginZ { get; set; }
}

/// <summary>Interlude <c>StopMoveInVehicle</c> (0x72).</summary>
public sealed class StopMoveInVehicleNetMsg
{
    public int PlayerObjectId { get; set; }
    public int BoatObjectId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Heading { get; set; }
}

/// <summary>Interlude <c>ValidateLocationInVehicle</c> (0x73).</summary>
public sealed class ValidateLocationInVehicleNetMsg
{
    public int PlayerObjectId { get; set; }
    public int BoatObjectId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Heading { get; set; }
}

/// <summary>Interlude <c>VehicleStarted</c> (0xC0).</summary>
public sealed class VehicleStartedNetMsg
{
    public int BoatObjectId { get; set; }
    public int State { get; set; }
}

/// <summary>Interlude <c>RequestGetOnVehicle</c> (client 0x42).</summary>
public sealed class RequestGetOnVehicleNetMsg
{
    public int BoatObjectId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

/// <summary>Interlude <c>RequestGetOffVehicle</c> (client 0x43).</summary>
public sealed class RequestGetOffVehicleNetMsg
{
    public int BoatObjectId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

/// <summary>Interlude <c>RequestMoveToLocationInVehicle</c> (client 0x5C).</summary>
public sealed class RequestMoveToLocationInVehicleNetMsg
{
    public int BoatObjectId { get; set; }
    public int DestX { get; set; }
    public int DestY { get; set; }
    public int DestZ { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }
    public int OriginZ { get; set; }
}

/// <summary>Interlude <c>CannotMoveAnymoreInVehicle</c> (client 0x5D).</summary>
public sealed class CannotMoveAnymoreInVehicleNetMsg
{
    public int BoatObjectId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Heading { get; set; }
}