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
            var rewardSets = m.RewardSets ?? Array.Empty<QuestJournalRewardSetNet>();
            w.WriteInt32(rewardSets.Length);
            for (int s = 0; s < rewardSets.Length; s++)
            {
                var set = rewardSets[s] ?? new QuestJournalRewardSetNet();
                w.WriteInt32(set.QuestId);
                var rows = set.Rows ?? Array.Empty<QuestJournalRewardNet>();
                w.WriteInt32(rows.Length);
                for (int r = 0; r < rows.Length; r++)
                {
                    var row = rows[r] ?? new QuestJournalRewardNet();
                    w.WriteByte(row.Kind);
                    w.WriteInt32(row.ItemId);
                    w.WriteInt64(row.Count);
                    w.WriteString(row.Label ?? "");
                }
            }
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
            int maxActive = r.ReadInt32();
            int setCount = r.Remaining >= 4 ? r.ReadInt32() : 0;
            if (setCount < 0) setCount = 0;
            var rewardSets = new QuestJournalRewardSetNet[setCount];
            for (int s = 0; s < setCount; s++)
            {
                int questId = r.ReadInt32();
                int rowCount = r.ReadInt32();
                if (rowCount < 0) rowCount = 0;
                var rows = new QuestJournalRewardNet[rowCount];
                for (int i = 0; i < rowCount; i++)
                {
                    rows[i] = new QuestJournalRewardNet
                    {
                        Kind = r.ReadByte(),
                        ItemId = r.ReadInt32(),
                        Count = r.ReadInt64(),
                        Label = r.ReadString()
                    };
                }
                rewardSets[s] = new QuestJournalRewardSetNet { QuestId = questId, Rows = rows };
            }
            return new QuestListNetMsg
            {
                Active = active,
                CompletedMask = mask,
                CompletedQuestIds = done,
                MaxActive = maxActive,
                RewardSets = rewardSets
            };
        }
    }
}
