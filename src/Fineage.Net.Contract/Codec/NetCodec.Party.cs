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
            return new PartyMemberListNetMsg { LeaderObjectId = leader, PartyId = partyId, Members = members };
        }
    }
}
