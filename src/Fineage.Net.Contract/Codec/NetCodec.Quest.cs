using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeRequestQuestList() => Array.Empty<byte>();
        public static void DecodeRequestQuestList(ReadOnlySpan<byte> _) { }

        public static byte[] EncodeRequestQuestAbort(RequestQuestAbortNetMsg m) => Write(w => w.WriteInt32(m.QuestId));
        public static RequestQuestAbortNetMsg DecodeRequestQuestAbort(ReadOnlySpan<byte> p) =>
            new() { QuestId = new Reader(p).ReadInt32() };

        public static byte[] EncodeQuestList(QuestListNetMsg m) => Write(w =>
        {
            var active = m.Active ?? Array.Empty<QuestJournalEntryNet>();
            w.WriteInt32(active.Length);
            for (int i = 0; i < active.Length; i++)
            {
                var row = active[i] ?? new QuestJournalEntryNet();
                w.WriteInt32(row.QuestId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.State);
                w.WriteInt32(row.Cond);
                w.WriteInt32(row.ProgressFlags);
                w.WriteInt32(row.JournalWord);
                w.WriteByte(row.Repeatable ? (byte)1 : (byte)0);
            }
            var mask = m.CompletedMask ?? Array.Empty<byte>();
            w.WriteInt32(mask.Length);
            for (int i = 0; i < mask.Length; i++)
                w.WriteByte(mask[i]);
            var done = m.CompletedQuestIds ?? Array.Empty<int>();
            w.WriteInt32(done.Length);
            for (int i = 0; i < done.Length; i++)
                w.WriteInt32(done[i]);
            w.WriteInt32(m.MaxActive);
        });

        public static QuestListNetMsg DecodeQuestList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var active = new QuestJournalEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                active[i] = new QuestJournalEntryNet
                {
                    QuestId = r.ReadInt32(),
                    Name = r.ReadString(),
                    State = r.ReadInt32(),
                    Cond = r.ReadInt32(),
                    ProgressFlags = r.ReadInt32(),
                    JournalWord = r.ReadInt32(),
                    Repeatable = r.ReadByte() != 0
                };
            }
            int maskLen = r.ReadInt32();
            if (maskLen < 0) maskLen = 0;
            var mask = new byte[maskLen];
            for (int i = 0; i < maskLen; i++)
                mask[i] = r.ReadByte();
            int doneLen = r.ReadInt32();
            if (doneLen < 0) doneLen = 0;
            var done = new int[doneLen];
            for (int i = 0; i < doneLen; i++)
                done[i] = r.ReadInt32();
            return new QuestListNetMsg
            {
                Active = active,
                CompletedMask = mask,
                CompletedQuestIds = done,
                MaxActive = r.ReadInt32()
            };
        }
    }
}
