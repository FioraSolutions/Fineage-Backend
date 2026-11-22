namespace Fineage.Net.Contract;

public static partial class NetCodec
{
    public static byte[] EncodeCommandChannelAskRequest(CommandChannelAskRequestNetMsg m) =>
        Write(w => w.WriteString(m.TargetName ?? ""));

    public static CommandChannelAskRequestNetMsg DecodeCommandChannelAskRequest(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new CommandChannelAskRequestNetMsg { TargetName = r.ReadString() };
    }

    public static byte[] EncodeCommandChannelAcceptRequest(CommandChannelAcceptRequestNetMsg m) =>
        Write(w => w.WriteInt32(m.Response));

    public static CommandChannelAcceptRequestNetMsg DecodeCommandChannelAcceptRequest(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new CommandChannelAcceptRequestNetMsg { Response = r.ReadInt32() };
    }

    public static byte[] EncodeCommandChannelOustRequest(CommandChannelOustRequestNetMsg m) =>
        Write(w => w.WriteString(m.TargetName ?? ""));

    public static CommandChannelOustRequestNetMsg DecodeCommandChannelOustRequest(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new CommandChannelOustRequestNetMsg { TargetName = r.ReadString() };
    }

    public static byte[] EncodeCommandChannelAskJoin(CommandChannelAskJoinNetMsg m) =>
        Write(w =>
        {
            w.WriteString(m.RequestorName ?? "");
            w.WriteInt32(m.RequestorObjectId);
        });

    public static byte[] EncodeCommandChannelInfo(CommandChannelInfoNetMsg m) =>
        Write(w =>
        {
            w.WriteString(m.LeaderName ?? "");
            w.WriteInt32(m.LootType);
            w.WriteInt32(m.MemberCount);
            var parties = m.Parties ?? [];
            w.WriteInt32(parties.Length);
            foreach (var row in parties)
            {
                w.WriteString(row.LeaderName ?? "");
                w.WriteInt32(row.LeaderObjectId);
                w.WriteInt32(row.MemberCount);
            }
        });
}