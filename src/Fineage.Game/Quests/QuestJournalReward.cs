namespace Fineage.Game.Quests;

public enum QuestJournalRewardKind : byte
{
    Item = 0,
    Adena = 1,
    Exp = 2,
    Sp = 3,
}

public sealed record QuestJournalReward(QuestJournalRewardKind Kind, int ItemId, long Count, string Label);

public sealed record QuestJournalRewardSet(int QuestId, IReadOnlyList<QuestJournalReward> Rows);