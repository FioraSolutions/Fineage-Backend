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

    public sealed class QuestListNetMsg
    {
        public QuestJournalEntryNet[] Active { get; set; } = System.Array.Empty<QuestJournalEntryNet>();
        public byte[] CompletedMask { get; set; } = System.Array.Empty<byte>();
        public int[] CompletedQuestIds { get; set; } = System.Array.Empty<int>();
        public int MaxActive { get; set; }
    }

    public sealed class RequestQuestAbortNetMsg
    {
        public int QuestId { get; set; }
    }
}
