using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeBossStatusRequest(BossStatusRequestNetMsg m) => Write(w => w.WriteInt32(m.NpcId));

        public static BossStatusRequestNetMsg DecodeBossStatusRequest(ReadOnlySpan<byte> p) =>
            new() { NpcId = new Reader(p).ReadInt32() };

        public static byte[] EncodeAdminBossSchedule(AdminBossScheduleNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcId);
            w.WriteInt32(m.Action);
        });

        public static AdminBossScheduleNetMsg DecodeAdminBossSchedule(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AdminBossScheduleNetMsg { NpcId = r.ReadInt32(), Action = r.ReadInt32() };
        }

        public static byte[] EncodeBossStatus(BossStatusNetMsg m) => Write(w =>
        {
            var rows = m.Bosses ?? System.Array.Empty<BossStatusEntryNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i] ?? new BossStatusEntryNet();
                w.WriteInt32(row.NpcId);
                w.WriteInt32(row.ObjectId);
                w.WriteInt32(row.Kind);
                w.WriteInt32(row.Status);
                w.WriteInt64(row.RespawnUnixMs);
                w.WriteInt32(row.CurrentHp);
                w.WriteInt32(row.MaxHp);
                w.WriteInt32(row.CurrentMp);
                w.WriteInt32(row.MaxMp);
                w.WriteByte(row.Enabled ? (byte)1 : (byte)0);
                w.WriteByte(row.Spawned ? (byte)1 : (byte)0);
            }
        });

        public static BossStatusNetMsg DecodeBossStatus(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var rows = new BossStatusEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new BossStatusEntryNet
                {
                    NpcId = r.ReadInt32(),
                    ObjectId = r.ReadInt32(),
                    Kind = r.ReadInt32(),
                    Status = r.ReadInt32(),
                    RespawnUnixMs = r.ReadInt64(),
                    CurrentHp = r.ReadInt32(),
                    MaxHp = r.ReadInt32(),
                    CurrentMp = r.ReadInt32(),
                    MaxMp = r.ReadInt32(),
                    Enabled = r.ReadByte() != 0,
                    Spawned = r.ReadByte() != 0
                };
            }
            return new BossStatusNetMsg { Bosses = rows };
        }

        public static byte[] EncodeSiegeListRequest(SiegeListRequestNetMsg m) => Write(w => w.WriteInt32(m.CastleId));

        public static SiegeListRequestNetMsg DecodeSiegeListRequest(ReadOnlySpan<byte> p) =>
            new() { CastleId = new Reader(p).ReadInt32() };

        public static byte[] EncodeSiegeRegisterRequest(SiegeRegisterRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CastleId);
            w.WriteInt32(m.Attacker);
            w.WriteInt32(m.Joining);
        });

        public static SiegeRegisterRequestNetMsg DecodeSiegeRegisterRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SiegeRegisterRequestNetMsg
            {
                CastleId = r.ReadInt32(),
                Attacker = r.ReadInt32(),
                Joining = r.ReadInt32()
            };
        }

        public static byte[] EncodeSiegeDefenderAnswerRequest(SiegeDefenderAnswerRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CastleId);
            w.WriteInt32(m.ClanId);
            w.WriteInt32(m.Approved);
        });

        public static SiegeDefenderAnswerRequestNetMsg DecodeSiegeDefenderAnswerRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SiegeDefenderAnswerRequestNetMsg
            {
                CastleId = r.ReadInt32(),
                ClanId = r.ReadInt32(),
                Approved = r.ReadInt32()
            };
        }

        public static byte[] EncodeAdminCaptureCastle(AdminCaptureCastleNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CastleId);
            w.WriteInt32(m.ClanId);
        });

        public static AdminCaptureCastleNetMsg DecodeAdminCaptureCastle(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AdminCaptureCastleNetMsg { CastleId = r.ReadInt32(), ClanId = r.ReadInt32() };
        }

        public static byte[] EncodeSiegeInfo(SiegeInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CastleId);
            w.WriteString(m.Name ?? "");
            w.WriteInt32(m.OwnerClanId);
            w.WriteInt32(m.TaxPercent);
            w.WriteInt64(m.SiegeUnixMs);
            w.WriteInt64(m.RegistrationDeadlineUnixMs);
            w.WriteByte(m.RegistrationClosed ? (byte)1 : (byte)0);
            w.WriteByte(m.InProgress ? (byte)1 : (byte)0);
            w.WriteInt64(m.EndsUnixMs);
            var clans = m.Clans ?? System.Array.Empty<SiegeClanNet>();
            w.WriteInt32(clans.Length);
            for (int i = 0; i < clans.Length; i++)
            {
                var row = clans[i] ?? new SiegeClanNet();
                w.WriteInt32(row.ClanId);
                w.WriteInt32(row.Side);
            }
        });

        public static SiegeInfoNetMsg DecodeSiegeInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var info = new SiegeInfoNetMsg
            {
                CastleId = r.ReadInt32(),
                Name = r.ReadString(),
                OwnerClanId = r.ReadInt32(),
                TaxPercent = r.ReadInt32(),
                SiegeUnixMs = r.ReadInt64(),
                RegistrationDeadlineUnixMs = r.ReadInt64(),
                RegistrationClosed = r.ReadByte() != 0,
                InProgress = r.ReadByte() != 0,
                EndsUnixMs = r.ReadInt64()
            };
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var clans = new SiegeClanNet[n];
            for (int i = 0; i < n; i++)
                clans[i] = new SiegeClanNet { ClanId = r.ReadInt32(), Side = r.ReadInt32() };
            info.Clans = clans;
            return info;
        }
    }
}
