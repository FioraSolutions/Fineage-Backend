namespace Fineage.Net.Contract;

public static partial class NetCodec
{
    public static byte[] EncodeCursedWeaponList(CursedWeaponListNetMsg m) => Write(w =>
    {
        var ids = m.ItemIds ?? [];
        w.WriteInt32(ids.Length);
        foreach (int id in ids)
            w.WriteInt32(id);
    });

    public static CursedWeaponListNetMsg DecodeCursedWeaponList(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        int count = r.ReadInt32();
        var ids = new int[count];
        for (int i = 0; i < count; i++)
            ids[i] = r.ReadInt32();
        return new CursedWeaponListNetMsg { ItemIds = ids };
    }

    public static byte[] EncodeCursedWeaponLocation(CursedWeaponLocationNetMsg m) => Write(w =>
    {
        var rows = m.Rows ?? [];
        w.WriteInt32(rows.Length);
        foreach (var row in rows)
        {
            w.WriteInt32(row.ItemId);
            w.WriteInt32(row.Activated);
            w.WriteInt32(row.X);
            w.WriteInt32(row.Y);
            w.WriteInt32(row.Z);
        }
    });

    public static CursedWeaponLocationNetMsg DecodeCursedWeaponLocation(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        int count = r.ReadInt32();
        var rows = new CursedWeaponLocationRowNet[count];
        for (int i = 0; i < count; i++)
        {
            rows[i] = new CursedWeaponLocationRowNet
            {
                ItemId = r.ReadInt32(),
                Activated = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32()
            };
        }

        return new CursedWeaponLocationNetMsg { Rows = rows };
    }
}