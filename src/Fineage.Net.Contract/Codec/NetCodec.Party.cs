using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodePartyInviteRequest(PartyInviteRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.TargetObjectId);
            w.WriteInt32(m.ItemDistribution);
        });
        public static PartyInviteRequestNetMsg DecodePartyInviteRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyInviteRequestNetMsg { TargetObjectId = r.ReadInt32(), ItemDistribution = r.ReadInt32() };
        }

        public static byte[] EncodePartyAnswerJoinRequest(PartyAnswerJoinRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteInt32(m.Response);
        });
        public static PartyAnswerJoinRequestNetMsg DecodePartyAnswerJoinRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyAnswerJoinRequestNetMsg { RequestorObjectId = r.ReadInt32(), Response = r.ReadInt32() };
        }

        public static byte[] EncodePartyLeaveRequest() => Array.Empty<byte>();
        public static void DecodePartyLeaveRequest(ReadOnlySpan<byte> _) { }

        public static byte[] EncodePartyKickRequest(PartyKickRequestNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));
        public static PartyKickRequestNetMsg DecodePartyKickRequest(ReadOnlySpan<byte> p) =>
            new() { TargetObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodePartyChangeLeaderRequest(PartyChangeLeaderRequestNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));
        public static PartyChangeLeaderRequestNetMsg DecodePartyChangeLeaderRequest(ReadOnlySpan<byte> p) =>
            new() { TargetObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodePartyAskJoin(PartyAskJoinNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteString(m.RequestorName ?? "");
            w.WriteInt32(m.ItemDistribution);
        });
        public static PartyAskJoinNetMsg DecodePartyAskJoin(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyAskJoinNetMsg
            {
                RequestorObjectId = r.ReadInt32(),
                RequestorName = r.ReadString(),
                ItemDistribution = r.ReadInt32()
            };
        }

        public static byte[] EncodePartyJoinResult(PartyJoinResultNetMsg m) => Write(w => w.WriteInt32(m.Response));
        public static PartyJoinResultNetMsg DecodePartyJoinResult(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };

        public static byte[] EncodePartyMemberList(PartyMemberListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.LeaderObjectId);
            w.WriteInt32(m.PartyId);
            var members = m.Members ?? Array.Empty<PartyMemberEntryNet>();
            w.WriteInt32(members.Length);
            for (int i = 0; i < members.Length; i++)
            {
                var row = members[i] ?? new PartyMemberEntryNet();
                w.WriteInt32(row.ObjectId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.ClassId);
                w.WriteInt32(row.Level);
                w.WriteInt32(row.CurHp);
                w.WriteInt32(row.MaxHp);
                w.WriteInt32(row.CurMp);
                w.WriteInt32(row.MaxMp);
            }
            // Backward-compatible CP tail: older decoders stop after the rows.
            w.WriteInt32(members.Length);
            for (int i = 0; i < members.Length; i++)
            {
                var row = members[i] ?? new PartyMemberEntryNet();
                w.WriteInt32(row.CurCp);
                w.WriteInt32(row.MaxCp);
            }
        });
        public static PartyMemberListNetMsg DecodePartyMemberList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int leader = r.ReadInt32();
            int partyId = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var members = new PartyMemberEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                members[i] = new PartyMemberEntryNet
                {
                    ObjectId = r.ReadInt32(),
                    Name = r.ReadString(),
                    ClassId = r.ReadInt32(),
                    Level = r.ReadInt32(),
                    CurHp = r.ReadInt32(),
                    MaxHp = r.ReadInt32(),
                    CurMp = r.ReadInt32(),
                    MaxMp = r.ReadInt32()
                };
            }
            if (r.Remaining >= 4)
            {
                int tail = r.ReadInt32();
                for (int i = 0; i < tail && i < n && r.Remaining >= 8; i++)
                {
                    members[i].CurCp = r.ReadInt32();
                    members[i].MaxCp = r.ReadInt32();
                }
            }
            return new PartyMemberListNetMsg { LeaderObjectId = leader, PartyId = partyId, Members = members };
        }

        public static byte[] EncodePartyMemberUpdate(PartyMemberUpdateNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ObjectId);
            w.WriteInt32(m.Level);
            w.WriteInt32(m.ClassId);
            w.WriteInt32(m.CurHp);
            w.WriteInt32(m.MaxHp);
            w.WriteInt32(m.CurMp);
            w.WriteInt32(m.MaxMp);
            w.WriteInt32(m.CurCp);
            w.WriteInt32(m.MaxCp);
        });
        public static PartyMemberUpdateNetMsg DecodePartyMemberUpdate(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyMemberUpdateNetMsg
            {
                ObjectId = r.ReadInt32(),
                Level = r.ReadInt32(),
                ClassId = r.ReadInt32(),
                CurHp = r.ReadInt32(),
                MaxHp = r.ReadInt32(),
                CurMp = r.ReadInt32(),
                MaxMp = r.ReadInt32(),
                CurCp = r.ReadInt32(),
                MaxCp = r.ReadInt32()
            };
        }

        /// <summary>PartySpelled shares the AbnormalStatusUpdate body (objectId + effect rows).</summary>
        public static byte[] EncodePartySpelled(AbnormalStatusUpdateNetMsg m) => EncodeAbnormalStatusUpdate(m);
        public static AbnormalStatusUpdateNetMsg DecodePartySpelled(ReadOnlySpan<byte> p) => DecodeAbnormalStatusUpdate(p);

        public static byte[] EncodePartyMemberPosition(PartyMemberPositionNetMsg m) => Write(w =>
        {
            var rows = m.Members ?? Array.Empty<PartyMemberPositionEntryNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i] ?? new PartyMemberPositionEntryNet();
                w.WriteInt32(row.ObjectId);
                w.WriteInt32(row.X);
                w.WriteInt32(row.Y);
                w.WriteInt32(row.Z);
            }
        });
        public static PartyMemberPositionNetMsg DecodePartyMemberPosition(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = Math.Max(0, r.ReadInt32());
            var rows = new PartyMemberPositionEntryNet[n];
            for (int i = 0; i < n; i++)
                rows[i] = new PartyMemberPositionEntryNet { ObjectId = r.ReadInt32(), X = r.ReadInt32(), Y = r.ReadInt32(), Z = r.ReadInt32() };
            return new PartyMemberPositionNetMsg { Members = rows };
        }
    }
}
