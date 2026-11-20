using System;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeQuestDirection(QuestDirectionNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.QuestId);
            w.WriteByte(m.Show ? (byte)1 : (byte)0);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
        });

        public static QuestDirectionNetMsg DecodeQuestDirection(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new QuestDirectionNetMsg
            {
                QuestId = r.ReadInt32(),
                Show = r.ReadByte() != 0,
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32()
            };
        }

        public static byte[] EncodeQuestNpcMarks(QuestNpcMarksNetMsg m) => Write(w =>
        {
            var rows = m.Marks ?? Array.Empty<QuestNpcMarkNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                w.WriteInt32(rows[i].NpcId);
                w.WriteByte(rows[i].Mark);
            }
        });

        public static QuestNpcMarksNetMsg DecodeQuestNpcMarks(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var rows = new QuestNpcMarkNet[n];
            for (int i = 0; i < n; i++)
                rows[i] = new QuestNpcMarkNet { NpcId = r.ReadInt32(), Mark = r.ReadByte() };
            return new QuestNpcMarksNetMsg { Marks = rows };
        }

        public static byte[] EncodeTutorialUi(TutorialUiNetMsg m) => Write(w =>
        {
            w.WriteByte((byte)m.Kind);
            w.WriteString(m.Html ?? "");
            w.WriteInt32(m.EventId);
            w.WriteByte(m.On ? (byte)1 : (byte)0);
        });

        public static TutorialUiNetMsg DecodeTutorialUi(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TutorialUiNetMsg
            {
                Kind = (TutorialUiKind)r.ReadByte(),
                Html = r.ReadString(),
                EventId = r.ReadInt32(),
                On = r.ReadByte() != 0
            };
        }
    }
}