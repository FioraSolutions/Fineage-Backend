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

        public static byte[] EncodeOlympiadRegisterRequest(OlympiadRegisterRequestNetMsg m) =>
            Write(w => w.WriteInt32(m.Classed));

        public static OlympiadRegisterRequestNetMsg DecodeOlympiadRegisterRequest(ReadOnlySpan<byte> p) =>
            new() { Classed = new Reader(p).ReadInt32() };

        public static byte[] EncodeAdminReportOlympiadResult(AdminReportOlympiadResultNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.WinnerObjectId);
        });

        public static AdminReportOlympiadResultNetMsg DecodeAdminReportOlympiadResult(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AdminReportOlympiadResultNetMsg { MatchId = r.ReadInt32(), WinnerObjectId = r.ReadInt32() };
        }

        public static byte[] EncodeOlympiadStatus(OlympiadStatusNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Period);
            w.WriteInt32(m.Cycle);
            w.WriteByte(m.InCompetition ? (byte)1 : (byte)0);
            w.WriteInt64(m.CompetitionStartUnixMs);
            w.WriteInt64(m.CompetitionEndUnixMs);
            w.WriteInt64(m.OlympiadEndUnixMs);
            w.WriteInt64(m.ValidationEndUnixMs);
            w.WriteInt64(m.NextWeeklyUnixMs);
            w.WriteByte(m.RegistrationOpen ? (byte)1 : (byte)0);
            w.WriteByte(m.Noble ? (byte)1 : (byte)0);
            w.WriteInt32(m.Points);
            w.WriteInt32(m.CompetitionsDone);
            w.WriteInt32(m.Won);
            w.WriteInt32(m.Lost);
            w.WriteInt32(m.Drawn);
            w.WriteInt32(m.Queue);
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.StadiumId);
            w.WriteInt32(m.ClassId);
        });

        public static OlympiadStatusNetMsg DecodeOlympiadStatus(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new OlympiadStatusNetMsg
            {
                Period = r.ReadInt32(),
                Cycle = r.ReadInt32(),
                InCompetition = r.ReadByte() != 0,
                CompetitionStartUnixMs = r.ReadInt64(),
                CompetitionEndUnixMs = r.ReadInt64(),
                OlympiadEndUnixMs = r.ReadInt64(),
                ValidationEndUnixMs = r.ReadInt64(),
                NextWeeklyUnixMs = r.ReadInt64(),
                RegistrationOpen = r.ReadByte() != 0,
                Noble = r.ReadByte() != 0,
                Points = r.ReadInt32(),
                CompetitionsDone = r.ReadInt32(),
                Won = r.ReadInt32(),
                Lost = r.ReadInt32(),
                Drawn = r.ReadInt32(),
                Queue = r.ReadInt32(),
                MatchId = r.ReadInt32(),
                StadiumId = r.ReadInt32(),
                ClassId = r.ReadInt32()
            };
        }

        public static byte[] EncodeOlympiadMatch(OlympiadMatchNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.StadiumId);
            w.WriteInt32(m.ZoneId);
            w.WriteByte(m.Classed ? (byte)1 : (byte)0);
            w.WriteInt32(m.Phase);
            w.WriteInt32(m.PlayerOneId);
            w.WriteInt32(m.PlayerTwoId);
            w.WriteString(m.PlayerOneName ?? "");
            w.WriteString(m.PlayerTwoName ?? "");
            w.WriteInt32(m.InstanceId);
        });

        public static OlympiadMatchNetMsg DecodeOlympiadMatch(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new OlympiadMatchNetMsg
            {
                MatchId = r.ReadInt32(),
                StadiumId = r.ReadInt32(),
                ZoneId = r.ReadInt32(),
                Classed = r.ReadByte() != 0,
                Phase = r.ReadInt32(),
                PlayerOneId = r.ReadInt32(),
                PlayerTwoId = r.ReadInt32(),
                PlayerOneName = r.ReadString(),
                PlayerTwoName = r.ReadString(),
                InstanceId = r.ReadInt32()
            };
        }

        public static byte[] EncodeOlympiadCountdown(OlympiadCountdownNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.Kind);
            w.WriteInt32(m.Seconds);
        });

        public static OlympiadCountdownNetMsg DecodeOlympiadCountdown(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new OlympiadCountdownNetMsg
            {
                MatchId = r.ReadInt32(),
                Kind = r.ReadInt32(),
                Seconds = r.ReadInt32()
            };
        }

        public static byte[] EncodeOlympiadResult(OlympiadResultNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.Outcome);
            w.WriteInt32(m.WinnerObjectId);
            w.WriteInt32(m.PointsOne);
            w.WriteInt32(m.PointsTwo);
            w.WriteInt32(m.DeltaOne);
            w.WriteInt32(m.DeltaTwo);
            w.WriteInt32(m.RewardItemId);
            w.WriteInt32(m.RewardCount);
            w.WriteInt32(m.CompetitionsDoneOne);
            w.WriteInt32(m.CompetitionsDoneTwo);
        });

        public static OlympiadResultNetMsg DecodeOlympiadResult(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new OlympiadResultNetMsg
            {
                MatchId = r.ReadInt32(),
                Outcome = r.ReadInt32(),
                WinnerObjectId = r.ReadInt32(),
                PointsOne = r.ReadInt32(),
                PointsTwo = r.ReadInt32(),
                DeltaOne = r.ReadInt32(),
                DeltaTwo = r.ReadInt32(),
                RewardItemId = r.ReadInt32(),
                RewardCount = r.ReadInt32(),
                CompetitionsDoneOne = r.ReadInt32(),
                CompetitionsDoneTwo = r.ReadInt32()
            };
        }

        public static byte[] EncodeDungeonEnterRequest(DungeonEnterRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.DungeonId);
            w.WriteInt32(m.Action);
        });

        public static DungeonEnterRequestNetMsg DecodeDungeonEnterRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DungeonEnterRequestNetMsg { DungeonId = r.ReadInt32(), Action = r.ReadInt32() };
        }

        public static byte[] EncodeDungeonEntered(DungeonEnteredNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.DungeonId);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteByte(m.Inside ? (byte)1 : (byte)0);
            w.WriteInt32(m.Action);
        });

        public static DungeonEnteredNetMsg DecodeDungeonEntered(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DungeonEnteredNetMsg
            {
                DungeonId = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                Inside = r.ReadByte() != 0,
                Action = r.ReadInt32()
            };
        }

        public static byte[] EncodeSevenSignsActionRequest(SevenSignsActionRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Action);
            w.WriteInt32(m.Cabal);
            w.WriteInt32(m.Seal);
            w.WriteInt32(m.BlueStones);
            w.WriteInt32(m.GreenStones);
            w.WriteInt32(m.RedStones);
        });

        public static SevenSignsActionRequestNetMsg DecodeSevenSignsActionRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SevenSignsActionRequestNetMsg
            {
                Action = r.ReadInt32(),
                Cabal = r.ReadInt32(),
                Seal = r.ReadInt32(),
                BlueStones = r.ReadInt32(),
                GreenStones = r.ReadInt32(),
                RedStones = r.ReadInt32()
            };
        }

        public static byte[] EncodeSevenSignsStatus(SevenSignsStatusNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Period);
            w.WriteInt32(m.Cycle);
            w.WriteInt32(m.PlayerCabal);
            w.WriteInt32(m.PlayerSeal);
            w.WriteInt32(m.PlayerStoneCount);
            w.WriteInt32(m.PlayerAncientAdena);
            w.WriteInt32(m.PlayerContribution);
            w.WriteInt32(m.DawnScore);
            w.WriteInt32(m.DuskScore);
            w.WriteInt32(m.DawnFestivalScore);
            w.WriteInt32(m.DuskFestivalScore);
            w.WriteInt64(m.DawnStonePoints);
            w.WriteInt64(m.DuskStonePoints);
            w.WriteInt32(m.AvariceOwner);
            w.WriteInt32(m.GnosisOwner);
            w.WriteInt32(m.StrifeOwner);
            w.WriteInt32(m.DawnMembers);
            w.WriteInt32(m.DuskMembers);
            w.WriteInt32(m.WinningCabal);
            w.WriteInt64(m.NextChangeUnixMs);
            w.WriteInt32(m.SkyState);
            w.WriteInt32(m.DawnAvarice);
            w.WriteInt32(m.DawnGnosis);
            w.WriteInt32(m.DawnStrife);
            w.WriteInt32(m.DuskAvarice);
            w.WriteInt32(m.DuskGnosis);
            w.WriteInt32(m.DuskStrife);
            w.WriteInt32(m.PreviousWinner);
        });

        public static SevenSignsStatusNetMsg DecodeSevenSignsStatus(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SevenSignsStatusNetMsg
            {
                Period = r.ReadInt32(),
                Cycle = r.ReadInt32(),
                PlayerCabal = r.ReadInt32(),
                PlayerSeal = r.ReadInt32(),
                PlayerStoneCount = r.ReadInt32(),
                PlayerAncientAdena = r.ReadInt32(),
                PlayerContribution = r.ReadInt32(),
                DawnScore = r.ReadInt32(),
                DuskScore = r.ReadInt32(),
                DawnFestivalScore = r.ReadInt32(),
                DuskFestivalScore = r.ReadInt32(),
                DawnStonePoints = r.ReadInt64(),
                DuskStonePoints = r.ReadInt64(),
                AvariceOwner = r.ReadInt32(),
                GnosisOwner = r.ReadInt32(),
                StrifeOwner = r.ReadInt32(),
                DawnMembers = r.ReadInt32(),
                DuskMembers = r.ReadInt32(),
                WinningCabal = r.ReadInt32(),
                NextChangeUnixMs = r.ReadInt64(),
                SkyState = r.ReadInt32(),
                DawnAvarice = r.ReadInt32(),
                DawnGnosis = r.ReadInt32(),
                DawnStrife = r.ReadInt32(),
                DuskAvarice = r.ReadInt32(),
                DuskGnosis = r.ReadInt32(),
                DuskStrife = r.ReadInt32(),
                PreviousWinner = r.ReadInt32()
            };
        }

        public static byte[] EncodeSevenSignsSky(SevenSignsSkyNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.State);
            w.WriteInt32(m.Period);
            w.WriteInt32(m.WinningCabal);
        });

        public static SevenSignsSkyNetMsg DecodeSevenSignsSky(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SevenSignsSkyNetMsg
            {
                State = r.ReadInt32(),
                Period = r.ReadInt32(),
                WinningCabal = r.ReadInt32()
            };
        }
    }
}
