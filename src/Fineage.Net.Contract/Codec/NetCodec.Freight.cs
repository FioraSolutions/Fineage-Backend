using System;

namespace Fineage.Net.Contract;

public static partial class NetCodec
{
    public static byte[] EncodePackageToList(PackageToListNetMsg m) => Write(w =>
    {
        var rows = m.Characters ?? Array.Empty<PackageToListEntryNet>();
        w.WriteInt32(rows.Length);
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i] ?? new PackageToListEntryNet();
            w.WriteInt32(row.ObjectId);
            w.WriteString(row.Name ?? "");
        }
    });

    public static PackageToListNetMsg DecodePackageToList(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        int n = r.ReadInt32();
        if (n < 0) n = 0;
        var rows = new PackageToListEntryNet[n];
        for (int i = 0; i < n; i++)
            rows[i] = new PackageToListEntryNet { ObjectId = r.ReadInt32(), Name = r.ReadString() };
        return new PackageToListNetMsg { Characters = rows };
    }

    public static byte[] EncodePackageSendableList(PackageSendableListNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.RecipientObjectId);
        w.WriteInt32(m.Adena);
        WriteItemListEntries(w, m.Items);
    });

    public static PackageSendableListNetMsg DecodePackageSendableList(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        var m = new PackageSendableListNetMsg
        {
            RecipientObjectId = r.ReadInt32(),
            Adena = r.ReadInt32(),
            Items = ReadItemListEntries(ref r)
        };
        return m;
    }

    public static byte[] EncodePackageSendRequest(PackageSendRequestNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.RecipientObjectId);
        var lines = m.Lines ?? Array.Empty<ShopObjectCountNet>();
        w.WriteInt32(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            var row = lines[i] ?? new ShopObjectCountNet();
            w.WriteInt32(row.ObjectId);
            w.WriteInt32(row.Count);
        }
    });

    public static PackageSendRequestNetMsg DecodePackageSendRequest(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        int recipient = r.ReadInt32();
        int n = r.ReadInt32();
        if (n < 0) n = 0;
        var lines = new ShopObjectCountNet[n];
        for (int i = 0; i < n; i++)
            lines[i] = new ShopObjectCountNet { ObjectId = r.ReadInt32(), Count = r.ReadInt32() };
        return new PackageSendRequestNetMsg { RecipientObjectId = recipient, Lines = lines };
    }

    public static byte[] EncodePackageSendableListRequest(PackageSendableListRequestNetMsg m) =>
        Write(w => w.WriteInt32(m.RecipientObjectId));

    public static PackageSendableListRequestNetMsg DecodePackageSendableListRequest(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new PackageSendableListRequestNetMsg { RecipientObjectId = r.ReadInt32() };
    }
}