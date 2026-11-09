using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeFriendInvite(FriendInviteNetMsg m) =>
            Write(w => w.WriteString(m.Name ?? ""));
        public static FriendInviteNetMsg DecodeFriendInvite(ReadOnlySpan<byte> p) =>
            new() { Name = new Reader(p).ReadString() };

        public static byte[] EncodeFriendAnswer(FriendAnswerNetMsg m) =>
            Write(w => w.WriteInt32(m.Response));
        public static FriendAnswerNetMsg DecodeFriendAnswer(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };

        public static byte[] EncodeFriendDelete(FriendDeleteNetMsg m) =>
            Write(w => w.WriteString(m.Name ?? ""));
        public static FriendDeleteNetMsg DecodeFriendDelete(ReadOnlySpan<byte> p) =>
            new() { Name = new Reader(p).ReadString() };

        public static byte[] EncodeBlockRequest(BlockRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Type);
            w.WriteString(m.Name ?? "");
        });
        public static BlockRequestNetMsg DecodeBlockRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new BlockRequestNetMsg { Type = r.ReadInt32(), Name = r.ReadString() };
        }

        public static byte[] EncodeFriendInviteAsk(FriendInviteAskNetMsg m) =>
            Write(w => w.WriteString(m.RequestorName ?? ""));
        public static FriendInviteAskNetMsg DecodeFriendInviteAsk(ReadOnlySpan<byte> p) =>
            new() { RequestorName = new Reader(p).ReadString() };

        public static byte[] EncodeFriendList(FriendListNetMsg m) => Write(w =>
        {
            var rows = m.Friends ?? System.Array.Empty<FriendRowNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i] ?? new FriendRowNet();
                w.WriteInt32(row.ObjectId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.Online);
            }
        });
        public static FriendListNetMsg DecodeFriendList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var rows = new FriendRowNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new FriendRowNet
                {
                    ObjectId = r.ReadInt32(),
                    Name = r.ReadString(),
                    Online = r.ReadInt32()
                };
            }
            return new FriendListNetMsg { Friends = rows };
        }

        public static byte[] EncodeFriendUpdate(FriendUpdateNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Added);
            w.WriteInt32(m.ObjectId);
            w.WriteString(m.Name ?? "");
            w.WriteInt32(m.Online);
        });
        public static FriendUpdateNetMsg DecodeFriendUpdate(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new FriendUpdateNetMsg
            {
                Added = r.ReadInt32(),
                ObjectId = r.ReadInt32(),
                Name = r.ReadString(),
                Online = r.ReadInt32()
            };
        }

        public static byte[] EncodeBlockList(BlockListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.BlockAll);
            var rows = m.Blocked ?? System.Array.Empty<FriendRowNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i] ?? new FriendRowNet();
                w.WriteInt32(row.ObjectId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.Online);
            }
        });
        public static BlockListNetMsg DecodeBlockList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int blockAll = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var rows = new FriendRowNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new FriendRowNet
                {
                    ObjectId = r.ReadInt32(),
                    Name = r.ReadString(),
                    Online = r.ReadInt32()
                };
            }
            return new BlockListNetMsg { BlockAll = blockAll, Blocked = rows };
        }
    }
}
