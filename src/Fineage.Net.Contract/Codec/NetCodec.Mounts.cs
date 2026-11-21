namespace Fineage.Net.Contract;

public static partial class NetCodec
{
    public static byte[] EncodeRide(RideNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.ObjectId);
        w.WriteInt32(m.Action);
        w.WriteByte(m.MountKind);
        w.WriteInt32(m.RideNpcId);
    });

    public static RideNetMsg DecodeRide(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new RideNetMsg
        {
            ObjectId = r.ReadInt32(),
            Action = r.ReadInt32(),
            MountKind = r.ReadByte(),
            RideNpcId = r.ReadInt32()
        };
    }
}