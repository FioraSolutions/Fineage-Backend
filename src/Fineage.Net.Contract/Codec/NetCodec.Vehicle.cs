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

    public static byte[] EncodeVehicleCheckLocation(VehicleCheckLocationNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.BoatObjectId);
        w.WriteInt32(m.X);
        w.WriteInt32(m.Y);
        w.WriteInt32(m.Z);
        w.WriteInt32(m.Heading);
    });

    public static VehicleCheckLocationNetMsg DecodeVehicleCheckLocation(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new VehicleCheckLocationNetMsg
        {
            BoatObjectId = r.ReadInt32(),
            X = r.ReadInt32(),
            Y = r.ReadInt32(),
            Z = r.ReadInt32(),
            Heading = r.ReadInt32()
        };
    }

    public static byte[] EncodeGetOnVehicle(GetOnVehicleNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.PlayerObjectId);
        w.WriteInt32(m.BoatObjectId);
        w.WriteInt32(m.X);
        w.WriteInt32(m.Y);
        w.WriteInt32(m.Z);
    });

    public static GetOnVehicleNetMsg DecodeGetOnVehicle(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new GetOnVehicleNetMsg
        {
            PlayerObjectId = r.ReadInt32(),
            BoatObjectId = r.ReadInt32(),
            X = r.ReadInt32(),
            Y = r.ReadInt32(),
            Z = r.ReadInt32()
        };
    }

    public static byte[] EncodeGetOffVehicle(GetOffVehicleNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.PlayerObjectId);
        w.WriteInt32(m.BoatObjectId);
        w.WriteInt32(m.X);
        w.WriteInt32(m.Y);
        w.WriteInt32(m.Z);
    });

    public static GetOffVehicleNetMsg DecodeGetOffVehicle(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new GetOffVehicleNetMsg
        {
            PlayerObjectId = r.ReadInt32(),
            BoatObjectId = r.ReadInt32(),
            X = r.ReadInt32(),
            Y = r.ReadInt32(),
            Z = r.ReadInt32()
        };
    }

    public static byte[] EncodeMoveToLocationInVehicle(MoveToLocationInVehicleNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.PlayerObjectId);
        w.WriteInt32(m.BoatObjectId);
        w.WriteInt32(m.DestX);
        w.WriteInt32(m.DestY);
        w.WriteInt32(m.DestZ);
        w.WriteInt32(m.OriginX);
        w.WriteInt32(m.OriginY);
        w.WriteInt32(m.OriginZ);
    });

    public static MoveToLocationInVehicleNetMsg DecodeMoveToLocationInVehicle(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new MoveToLocationInVehicleNetMsg
        {
            PlayerObjectId = r.ReadInt32(),
            BoatObjectId = r.ReadInt32(),
            DestX = r.ReadInt32(),
            DestY = r.ReadInt32(),
            DestZ = r.ReadInt32(),
            OriginX = r.ReadInt32(),
            OriginY = r.ReadInt32(),
            OriginZ = r.ReadInt32()
        };
    }

    public static byte[] EncodeStopMoveInVehicle(StopMoveInVehicleNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.PlayerObjectId);
        w.WriteInt32(m.BoatObjectId);
        w.WriteInt32(m.X);
        w.WriteInt32(m.Y);
        w.WriteInt32(m.Z);
        w.WriteInt32(m.Heading);
    });

    public static StopMoveInVehicleNetMsg DecodeStopMoveInVehicle(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new StopMoveInVehicleNetMsg
        {
            PlayerObjectId = r.ReadInt32(),
            BoatObjectId = r.ReadInt32(),
            X = r.ReadInt32(),
            Y = r.ReadInt32(),
            Z = r.ReadInt32(),
            Heading = r.ReadInt32()
        };
    }

    public static byte[] EncodeValidateLocationInVehicle(ValidateLocationInVehicleNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.PlayerObjectId);
        w.WriteInt32(m.BoatObjectId);
        w.WriteInt32(m.X);
        w.WriteInt32(m.Y);
        w.WriteInt32(m.Z);
        w.WriteInt32(m.Heading);
    });

    public static ValidateLocationInVehicleNetMsg DecodeValidateLocationInVehicle(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new ValidateLocationInVehicleNetMsg
        {
            PlayerObjectId = r.ReadInt32(),
            BoatObjectId = r.ReadInt32(),
            X = r.ReadInt32(),
            Y = r.ReadInt32(),
            Z = r.ReadInt32(),
            Heading = r.ReadInt32()
        };
    }

    public static byte[] EncodeVehicleStarted(VehicleStartedNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.BoatObjectId);
        w.WriteInt32(m.State);
    });

    public static VehicleStartedNetMsg DecodeVehicleStarted(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new VehicleStartedNetMsg
        {
            BoatObjectId = r.ReadInt32(),
            State = r.ReadInt32()
        };
    }

    public static byte[] EncodeRequestGetOnVehicle(RequestGetOnVehicleNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.BoatObjectId);
        w.WriteInt32(m.X);
        w.WriteInt32(m.Y);
        w.WriteInt32(m.Z);
    });

    public static RequestGetOnVehicleNetMsg DecodeRequestGetOnVehicle(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new RequestGetOnVehicleNetMsg
        {
            BoatObjectId = r.ReadInt32(),
            X = r.ReadInt32(),
            Y = r.ReadInt32(),
            Z = r.ReadInt32()
        };
    }

    public static byte[] EncodeRequestGetOffVehicle(RequestGetOffVehicleNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.BoatObjectId);
        w.WriteInt32(m.X);
        w.WriteInt32(m.Y);
        w.WriteInt32(m.Z);
    });

    public static RequestGetOffVehicleNetMsg DecodeRequestGetOffVehicle(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new RequestGetOffVehicleNetMsg
        {
            BoatObjectId = r.ReadInt32(),
            X = r.ReadInt32(),
            Y = r.ReadInt32(),
            Z = r.ReadInt32()
        };
    }

    public static byte[] EncodeRequestMoveToLocationInVehicle(RequestMoveToLocationInVehicleNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.BoatObjectId);
        w.WriteInt32(m.DestX);
        w.WriteInt32(m.DestY);
        w.WriteInt32(m.DestZ);
        w.WriteInt32(m.OriginX);
        w.WriteInt32(m.OriginY);
        w.WriteInt32(m.OriginZ);
    });

    public static RequestMoveToLocationInVehicleNetMsg DecodeRequestMoveToLocationInVehicle(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new RequestMoveToLocationInVehicleNetMsg
        {
            BoatObjectId = r.ReadInt32(),
            DestX = r.ReadInt32(),
            DestY = r.ReadInt32(),
            DestZ = r.ReadInt32(),
            OriginX = r.ReadInt32(),
            OriginY = r.ReadInt32(),
            OriginZ = r.ReadInt32()
        };
    }

    public static byte[] EncodeCannotMoveAnymoreInVehicle(CannotMoveAnymoreInVehicleNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.BoatObjectId);
        w.WriteInt32(m.X);
        w.WriteInt32(m.Y);
        w.WriteInt32(m.Z);
        w.WriteInt32(m.Heading);
    });

    public static CannotMoveAnymoreInVehicleNetMsg DecodeCannotMoveAnymoreInVehicle(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new CannotMoveAnymoreInVehicleNetMsg
        {
            BoatObjectId = r.ReadInt32(),
            X = r.ReadInt32(),
            Y = r.ReadInt32(),
            Z = r.ReadInt32(),
            Heading = r.ReadInt32()
        };
    }
}