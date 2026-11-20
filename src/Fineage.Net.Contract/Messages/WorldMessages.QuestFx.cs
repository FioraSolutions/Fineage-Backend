namespace Fineage.Net.Contract
{
    public sealed class QuestDirectionNetMsg
    {
        public int QuestId { get; set; }
        public bool Show { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    public sealed class QuestNpcMarkNet
    {
        public int NpcId { get; set; }
        public byte Mark { get; set; }
    }

    public sealed class QuestNpcMarksNetMsg
    {
        public QuestNpcMarkNet[] Marks { get; set; } = System.Array.Empty<QuestNpcMarkNet>();
    }

    public enum TutorialUiKind : byte
    {
        ShowHtml = 1,
        CloseHtml = 2,
        QuestionMark = 3,
        EnableClientEvent = 4,
    }

    public sealed class TutorialUiNetMsg
    {
        public TutorialUiKind Kind { get; set; }
        public string Html { get; set; } = "";
        public int EventId { get; set; }
        public bool On { get; set; }
    }
}