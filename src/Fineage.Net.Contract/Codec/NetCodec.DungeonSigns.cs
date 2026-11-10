using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
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
