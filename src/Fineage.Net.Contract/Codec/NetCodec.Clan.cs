using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeClanCreateRequest(ClanCreateRequestNetMsg m) => Write(w => w.WriteString(m.Name ?? ""));

        public static ClanCreateRequestNetMsg DecodeClanCreateRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ClanCreateRequestNetMsg { Name = r.ReadString() };
        }

        public static byte[] EncodeClanInviteRequest(ClanInviteRequestNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));

        public static ClanInviteRequestNetMsg DecodeClanInviteRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ClanInviteRequestNetMsg { TargetObjectId = r.ReadInt32() };
        }

        public static byte[] EncodeClanAnswer(ClanAnswerNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteInt32(m.Response);
        });

        public static ClanAnswerNetMsg DecodeClanAnswer(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ClanAnswerNetMsg { RequestorObjectId = r.ReadInt32(), Response = r.ReadInt32() };
        }

        public static byte[] EncodeClanKickRequest(ClanKickRequestNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));

        public static ClanKickRequestNetMsg DecodeClanKickRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ClanKickRequestNetMsg { TargetObjectId = r.ReadInt32() };
        }

        public static byte[] EncodePledgeStatus(PledgeStatusNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ClanId);
            w.WriteString(m.Name ?? "");
            w.WriteInt32(m.Level);
            w.WriteInt32(m.LeaderObjectId);
            w.WriteInt32(m.CrestId);
            w.WriteInt32(m.MemberCount);
            w.WriteInt64(m.DissolvingExpiryUnixMs);
            w.WriteInt64(m.CharPenaltyExpiryUnixMs);
        });

        public static PledgeStatusNetMsg DecodePledgeStatus(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PledgeStatusNetMsg
            {
                ClanId = r.ReadInt32(),
                Name = r.ReadString(),
                Level = r.ReadInt32(),
                LeaderObjectId = r.ReadInt32(),
                CrestId = r.ReadInt32(),
                MemberCount = r.ReadInt32(),
                DissolvingExpiryUnixMs = r.ReadInt64(),
                CharPenaltyExpiryUnixMs = r.ReadInt64()
            };
        }

        public static byte[] EncodePledgeMemberList(PledgeMemberListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ClanId);
            w.WriteInt32(m.LeaderObjectId);
            PledgeMemberNet[] rows = m.Members ?? System.Array.Empty<PledgeMemberNet>();
            w.WriteInt32(rows.Length);
            foreach (PledgeMemberNet row in rows)
            {
                w.WriteInt32(row.ObjectId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.ClassId);
                w.WriteInt32(row.Level);
                w.WriteByte(row.Online ? (byte)1 : (byte)0);
                w.WriteInt32(row.PledgeType);
            }
        });

        public static PledgeMemberListNetMsg DecodePledgeMemberList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int clanId = r.ReadInt32();
            int leader = r.ReadInt32();
            int n = r.ReadInt32();
            var rows = new PledgeMemberNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new PledgeMemberNet
                {
                    ObjectId = r.ReadInt32(),
                    Name = r.ReadString(),
                    ClassId = r.ReadInt32(),
                    Level = r.ReadInt32(),
                    Online = r.ReadByte() != 0,
                    PledgeType = r.ReadInt32()
                };
            }

            return new PledgeMemberListNetMsg { ClanId = clanId, LeaderObjectId = leader, Members = rows };
        }

        public static byte[] EncodeClanAskJoin(ClanAskJoinNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteString(m.RequestorName ?? "");
            w.WriteInt32(m.ClanId);
            w.WriteString(m.ClanName ?? "");
            w.WriteInt32(m.PledgeType);
        });

        public static ClanAskJoinNetMsg DecodeClanAskJoin(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ClanAskJoinNetMsg
            {
                RequestorObjectId = r.ReadInt32(),
                RequestorName = r.ReadString(),
                ClanId = r.ReadInt32(),
                ClanName = r.ReadString(),
                PledgeType = r.ReadInt32()
            };
        }

        public static byte[] EncodeClanJoinResult(ClanJoinResultNetMsg m) => Write(w => w.WriteInt32(m.Response));

        public static ClanJoinResultNetMsg DecodeClanJoinResult(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ClanJoinResultNetMsg { Response = r.ReadInt32() };
        }
    }
}
