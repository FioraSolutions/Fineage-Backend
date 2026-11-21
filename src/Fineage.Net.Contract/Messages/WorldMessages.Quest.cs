namespace Fineage.Net.Contract
{
    public sealed class QuestJournalEntryNet
    {
        public int QuestId { get; set; }
        public string Name { get; set; } = "";
        public int State { get; set; }
        public int Cond { get; set; }
        public int ProgressFlags { get; set; }
        public int JournalWord { get; set; }
        public bool Repeatable { get; set; }
    }

    public sealed class QuestJournalRewardNet
    {
        public byte Kind { get; set; }
        public int ItemId { get; set; }
        public long Count { get; set; }
        public string Label { get; set; } = "";
    }

    public sealed class QuestJournalRewardSetNet
    {
        public int QuestId { get; set; }
        public QuestJournalRewardNet[] Rows { get; set; } = System.Array.Empty<QuestJournalRewardNet>();
    }

    public sealed class QuestListNetMsg
    {
        public QuestJournalEntryNet[] Active { get; set; } = System.Array.Empty<QuestJournalEntryNet>();
        public byte[] CompletedMask { get; set; } = System.Array.Empty<byte>();
        public int[] CompletedQuestIds { get; set; } = System.Array.Empty<int>();
        public int MaxActive { get; set; }
        public QuestJournalRewardSetNet[] RewardSets { get; set; } = System.Array.Empty<QuestJournalRewardSetNet>();
    }

    public sealed class RequestQuestAbortNetMsg
    {
        public int QuestId { get; set; }
    }
}
