namespace Fineage.Net.Contract;

public static partial class NetCodec
{
    public static byte[] EncodePlayerActionState(PlayerActionStateNetMsg m) => Write(w =>
    {
        w.WriteByte(m.PetOut ? (byte)1 : (byte)0);
        w.WriteByte(m.MountablePet ? (byte)1 : (byte)0);
        w.WriteByte(m.Mounted ? (byte)1 : (byte)0);
        w.WriteByte(m.CanRide ? (byte)1 : (byte)0);
        w.WriteByte(m.MountKind);
        w.WriteInt32(m.PetControlObjectId);
    });

    public static PlayerActionStateNetMsg DecodePlayerActionState(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new PlayerActionStateNetMsg
        {
            PetOut = r.ReadByte() != 0,
            MountablePet = r.ReadByte() != 0,
            Mounted = r.ReadByte() != 0,
            CanRide = r.ReadByte() != 0,
            MountKind = r.ReadByte(),
            PetControlObjectId = r.ReadInt32()
        };
    }
}