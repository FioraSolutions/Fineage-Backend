namespace Fineage.Net.Contract;

public static partial class NetCodec
{
    public static byte[] EncodeVehicleInfo(VehicleInfoNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.BoatObjectId);
        w.WriteInt32(m.X);
        w.WriteInt32(m.Y);
        w.WriteInt32(m.Z);
        w.WriteInt32(m.Heading);
    });

    public static VehicleInfoNetMsg DecodeVehicleInfo(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new VehicleInfoNetMsg
        {
            BoatObjectId = r.ReadInt32(),
            X = r.ReadInt32(),
            Y = r.ReadInt32(),
            Z = r.ReadInt32(),
            Heading = r.ReadInt32()
        };
    }

    public static byte[] EncodeVehicleDeparture(VehicleDepartureNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.BoatObjectId);
        w.WriteInt32(m.MoveSpeed);
        w.WriteInt32(m.RotationSpeed);
        w.WriteInt32(m.DestX);
        w.WriteInt32(m.DestY);
        w.WriteInt32(m.DestZ);
    });

    public static VehicleDepartureNetMsg DecodeVehicleDeparture(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new VehicleDepartureNetMsg
        {
            BoatObjectId = r.ReadInt32(),
            MoveSpeed = r.ReadInt32(),
            RotationSpeed = r.ReadInt32(),
            DestX = r.ReadInt32(),
            DestY = r.ReadInt32(),
            DestZ = r.ReadInt32()
        };
    }
}