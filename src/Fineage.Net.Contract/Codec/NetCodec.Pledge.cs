using System;
using System.IO;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        /// <summary>Largest blob a pledge command may carry (large crest is 2176; headroom so the size check can answer).</summary>
        public const int MaxPledgeBlobBytes = 8192;

        static int ReadCount(ref Reader r, int max)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > max)
                throw new InvalidDataException("count");
            return n;
        }

        static void WriteBlob(Writer w, byte[]? data)
        {
            data ??= Array.Empty<byte>();
            w.WriteInt32(data.Length);
            foreach (byte b in data)
                w.WriteByte(b);
        }

        static byte[] ReadBlob(ref Reader r, int max)
        {
            int n = ReadCount(ref r, max);
            var data = new byte[n];
            for (int i = 0; i < n; i++)
                data[i] = r.ReadByte();
            return data;
        }

        public static byte[] EncodePledgeCommand(PledgeCommandNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Kind);
            w.WriteInt32(m.TargetObjectId);
            w.WriteInt32(m.IntArg1);
            w.WriteInt32(m.IntArg2);
            w.WriteInt64(m.Amount);
            w.WriteString(m.Name ?? "");
            w.WriteString(m.Text ?? "");
            WriteBlob(w, m.Blob);
        });

        public static PledgeCommandNetMsg DecodePledgeCommand(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PledgeCommandNetMsg
            {
                Kind = r.ReadInt32(),
                TargetObjectId = r.ReadInt32(),
                IntArg1 = r.ReadInt32(),
                IntArg2 = r.ReadInt32(),
                Amount = r.ReadInt64(),
                Name = r.ReadString(),
                Text = r.ReadString(),
                Blob = ReadBlob(ref r, MaxPledgeBlobBytes)
            };
        }

        public static byte[] EncodeCrestData(CrestDataNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Kind);
            w.WriteInt32(m.CrestId);
            WriteBlob(w, m.Data);
        });

        public static CrestDataNetMsg DecodeCrestData(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new CrestDataNetMsg { Kind = r.ReadInt32(), CrestId = r.ReadInt32(), Data = ReadBlob(ref r, MaxPledgeBlobBytes) };
        }

        static void WriteWars(Writer w, PledgeWarNet[]? rows)
        {
            rows ??= Array.Empty<PledgeWarNet>();
            w.WriteInt32(rows.Length);
            foreach (var row in rows)
            {
                w.WriteInt32(row.ClanId);
                w.WriteString(row.Name ?? "");
                w.WriteByte(row.Mutual ? (byte)1 : (byte)0);
            }
        }

        static PledgeWarNet[] ReadWars(ref Reader r)
        {
            int n = ReadCount(ref r, 4096);
            var rows = new PledgeWarNet[n];
            for (int i = 0; i < n; i++)
                rows[i] = new PledgeWarNet { ClanId = r.ReadInt32(), Name = r.ReadString(), Mutual = r.ReadByte() != 0 };
            return rows;
        }

        public static byte[] EncodePledgeWarList(PledgeWarListNetMsg m) => Write(w =>
        {
            WriteWars(w, m.Declared);
            WriteWars(w, m.UnderAttack);
        });

        public static PledgeWarListNetMsg DecodePledgeWarList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var declared = ReadWars(ref r);
            var attacked = ReadWars(ref r);
            return new PledgeWarListNetMsg { Declared = declared, UnderAttack = attacked };
        }

        public static byte[] EncodeRelationChanged(RelationChangedNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ObjectId);
            w.WriteInt32(m.Relation);
            w.WriteByte(m.AutoAttackable ? (byte)1 : (byte)0);
            w.WriteInt32(m.Karma);
            w.WriteByte(m.PvpFlag);
        });

        public static RelationChangedNetMsg DecodeRelationChanged(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new RelationChangedNetMsg
            {
                ObjectId = r.ReadInt32(),
                Relation = r.ReadInt32(),
                AutoAttackable = r.ReadByte() != 0,
                Karma = r.ReadInt32(),
                PvpFlag = r.ReadByte()
            };
        }

        public static byte[] EncodePledgeInfo(PledgeInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ClanId);
            w.WriteString(m.Name ?? "");
            w.WriteInt32(m.CrestId);
            w.WriteInt32(m.AllyId);
            w.WriteString(m.AllyName ?? "");
            w.WriteInt32(m.AllyCrestId);
        });

        public static PledgeInfoNetMsg DecodePledgeInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PledgeInfoNetMsg
            {
                ClanId = r.ReadInt32(),
                Name = r.ReadString(),
                CrestId = r.ReadInt32(),
                AllyId = r.ReadInt32(),
                AllyName = r.ReadString(),
                AllyCrestId = r.ReadInt32()
            };
        }

        public static byte[] EncodeAllyInfo(AllyInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.AllyId);
            w.WriteString(m.Name ?? "");
            w.WriteInt32(m.CrestId);
            w.WriteInt32(m.LeaderClanId);
            var clans = m.Clans ?? Array.Empty<AllyClanNet>();
            w.WriteInt32(clans.Length);
            foreach (var c in clans)
            {
                w.WriteInt32(c.ClanId);
                w.WriteString(c.Name ?? "");
                w.WriteInt32(c.Level);
                w.WriteString(c.LeaderName ?? "");
                w.WriteInt32(c.MemberCount);
                w.WriteInt32(c.OnlineCount);
                w.WriteInt32(c.CrestId);
            }
        });

        public static AllyInfoNetMsg DecodeAllyInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var m = new AllyInfoNetMsg { AllyId = r.ReadInt32(), Name = r.ReadString(), CrestId = r.ReadInt32(), LeaderClanId = r.ReadInt32() };
            int n = ReadCount(ref r, 64);
            var clans = new AllyClanNet[n];
            for (int i = 0; i < n; i++)
            {
                clans[i] = new AllyClanNet
                {
                    ClanId = r.ReadInt32(),
                    Name = r.ReadString(),
                    Level = r.ReadInt32(),
                    LeaderName = r.ReadString(),
                    MemberCount = r.ReadInt32(),
                    OnlineCount = r.ReadInt32(),
                    CrestId = r.ReadInt32()
                };
            }
            m.Clans = clans;
            return m;
        }

        public static byte[] EncodeAllyAskJoin(AllyAskJoinNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteString(m.RequestorName ?? "");
            w.WriteString(m.AllyName ?? "");
        });

        public static AllyAskJoinNetMsg DecodeAllyAskJoin(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AllyAskJoinNetMsg { RequestorObjectId = r.ReadInt32(), RequestorName = r.ReadString(), AllyName = r.ReadString() };
        }

        public static byte[] EncodeClanHallList(ClanHallListNetMsg m) => Write(w =>
        {
            var halls = m.Halls ?? Array.Empty<ClanHallNet>();
            w.WriteInt32(halls.Length);
            foreach (var h in halls)
            {
                w.WriteInt32(h.HallId);
                w.WriteString(h.Name ?? "");
                w.WriteString(h.Location ?? "");
                w.WriteInt32(h.Grade);
                w.WriteInt64(h.Lease);
                w.WriteInt32(h.OwnerClanId);
                w.WriteString(h.OwnerClanName ?? "");
                w.WriteInt64(h.AuctionEndUnixMs);
                w.WriteInt64(h.StartingBid);
                w.WriteInt64(h.HighestBid);
                w.WriteInt32(h.BidCount);
                w.WriteInt64(h.OwnBid);
            }
        });

        public static ClanHallListNetMsg DecodeClanHallList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = ReadCount(ref r, 512);
            var halls = new ClanHallNet[n];
            for (int i = 0; i < n; i++)
            {
                halls[i] = new ClanHallNet
                {
                    HallId = r.ReadInt32(),
                    Name = r.ReadString(),
                    Location = r.ReadString(),
                    Grade = r.ReadInt32(),
                    Lease = r.ReadInt64(),
                    OwnerClanId = r.ReadInt32(),
                    OwnerClanName = r.ReadString(),
                    AuctionEndUnixMs = r.ReadInt64(),
                    StartingBid = r.ReadInt64(),
                    HighestBid = r.ReadInt64(),
                    BidCount = r.ReadInt32(),
                    OwnBid = r.ReadInt64()
                };
            }
            return new ClanHallListNetMsg { Halls = halls };
        }

        /// <summary>
        /// Deco byte count in Mobius <c>AgitDecoInfo.writeImpl</c>
        /// (L2J_Mobius_CT_0_Interlude/.../serverpackets/AgitDecoInfo.java:44-186):
        /// HP, MP, MP, EXP, teleport, crystal 0, curtains, item-create, support, support, front, item-create.
        /// </summary>
        public const int AgitDecoByteCount = 12;

        public static byte[] EncodeAgitDecoInfo(AgitDecoInfoNetMsg m) => Write(w =>
        {
            var deco = m.DecoBytes ?? [];
            if (deco.Length != AgitDecoByteCount)
                throw new InvalidDataException("agit_deco_bytes");
            w.WriteInt32(m.HallId);
            foreach (byte b in deco)
                w.WriteByte(b);
        });

        public static AgitDecoInfoNetMsg DecodeAgitDecoInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int hallId = r.ReadInt32();
            var deco = new byte[AgitDecoByteCount];
            for (int i = 0; i < AgitDecoByteCount; i++)
                deco[i] = r.ReadByte();
            return new AgitDecoInfoNetMsg { HallId = hallId, DecoBytes = deco };
        }
    }
}
