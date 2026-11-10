using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
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
    }
}
