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

        public static byte[] EncodeClanInviteRequest(ClanInviteRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.TargetObjectId);
            if (m.PledgeType != 0)
                w.WriteInt32(m.PledgeType);
        });

        public static ClanInviteRequestNetMsg DecodeClanInviteRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var m = new ClanInviteRequestNetMsg { TargetObjectId = r.ReadInt32() };
            if (r.Remaining >= 4)
                m.PledgeType = r.ReadInt32();
            return m;
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
            w.WriteInt32(m.Reputation);
            w.WriteInt32(m.LargeCrestId);
            w.WriteInt32(m.AllyId);
            w.WriteString(m.AllyName ?? "");
            w.WriteInt32(m.AllyCrestId);
            w.WriteInt32(m.CastleId);
            w.WriteInt32(m.HallId);
            w.WriteByte(m.AtWar ? (byte)1 : (byte)0);
            w.WriteInt32(m.NewLeaderObjectId);
            var subs = m.SubPledges ?? System.Array.Empty<SubPledgeNet>();
            w.WriteInt32(subs.Length);
            foreach (var sub in subs)
            {
                w.WriteInt32(sub.Type);
                w.WriteString(sub.Name ?? "");
                w.WriteInt32(sub.LeaderObjectId);
            }
            w.WriteInt32Array(m.RankPrivileges ?? System.Array.Empty<int>());
            var skills = m.Skills ?? System.Array.Empty<ClanSkillNet>();
            w.WriteInt32(skills.Length);
            foreach (var skill in skills)
            {
                w.WriteInt32(skill.SkillId);
                w.WriteInt32(skill.Level);
            }
        });

        public static PledgeStatusNetMsg DecodePledgeStatus(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var m = new PledgeStatusNetMsg
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
            if (r.Remaining <= 0)
                return m;
            m.Reputation = r.ReadInt32();
            m.LargeCrestId = r.ReadInt32();
            m.AllyId = r.ReadInt32();
            m.AllyName = r.ReadString();
            m.AllyCrestId = r.ReadInt32();
            m.CastleId = r.ReadInt32();
            m.HallId = r.ReadInt32();
            m.AtWar = r.ReadByte() != 0;
            m.NewLeaderObjectId = r.ReadInt32();
            int subCount = ReadCount(ref r, 16);
            var subs = new SubPledgeNet[subCount];
            for (int i = 0; i < subCount; i++)
                subs[i] = new SubPledgeNet { Type = r.ReadInt32(), Name = r.ReadString(), LeaderObjectId = r.ReadInt32() };
            m.SubPledges = subs;
            m.RankPrivileges = r.ReadInt32Array();
            int skillCount = ReadCount(ref r, 256);
            var skills = new ClanSkillNet[skillCount];
            for (int i = 0; i < skillCount; i++)
                skills[i] = new ClanSkillNet { SkillId = r.ReadInt32(), Level = r.ReadInt32() };
            m.Skills = skills;
            return m;
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
            var extras = m.Extras ?? System.Array.Empty<PledgeMemberExtraNet>();
            w.WriteInt32(extras.Length);
            foreach (var extra in extras)
            {
                w.WriteInt32(extra.PowerGrade);
                w.WriteInt32(extra.SponsorObjectId);
                w.WriteInt32(extra.ApprenticeObjectId);
                w.WriteInt32(extra.PledgeClass);
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

            var list = new PledgeMemberListNetMsg { ClanId = clanId, LeaderObjectId = leader, Members = rows };
            if (r.Remaining >= 4)
            {
                int extraCount = ReadCount(ref r, 4096);
                var extras = new PledgeMemberExtraNet[extraCount];
                for (int i = 0; i < extraCount; i++)
                {
                    extras[i] = new PledgeMemberExtraNet
                    {
                        PowerGrade = r.ReadInt32(),
                        SponsorObjectId = r.ReadInt32(),
                        ApprenticeObjectId = r.ReadInt32(),
                        PledgeClass = r.ReadInt32()
                    };
                }
                list.Extras = extras;
            }
            return list;
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
