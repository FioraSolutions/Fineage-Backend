using System;

namespace Fineage.Net.Contract;

public static partial class NetCodec
{
    public static byte[] EncodeMonRaceInfo(MonRaceInfoNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.CodeA);
        w.WriteInt32(m.CodeB);
        var runners = m.Runners ?? Array.Empty<MonRaceRunnerNetMsg>();
        w.WriteInt32(runners.Length);
        foreach (var r in runners)
        {
            w.WriteInt32(r.ObjectId);
            w.WriteInt32(r.NpcIdPlusDisplay);
            w.WriteInt32(r.OriginX);
            w.WriteInt32(r.OriginY);
            w.WriteInt32(r.OriginZ);
            w.WriteInt32(r.EndX);
            w.WriteInt32(r.EndY);
            w.WriteInt32(r.EndZ);
            w.WriteDouble(r.CollisionHeight);
            w.WriteDouble(r.CollisionRadius);
            w.WriteInt32(r.Unknown120);
            var speeds = r.Speeds ?? new byte[20];
            for (int i = 0; i < 20; i++)
                w.WriteByte(i < speeds.Length ? speeds[i] : (byte)0);
            w.WriteInt32(0);
        }
    });
}