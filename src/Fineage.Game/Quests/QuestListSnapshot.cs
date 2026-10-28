namespace Fineage.Game.Quests;

public sealed record QuestJournalEntry(
    int QuestId,
    string Name,
    int State,
    int Cond,
    int ProgressFlags,
    int JournalWord,
    bool Repeatable);

/// <summary>
/// Active (started) quests plus the one-time completion mask.
/// The mask is 128 bytes: bit <c>questId % 8</c> of byte <c>questId / 8</c>, for ids 0..1023.
/// Ids outside that range are still listed in <see cref="CompletedQuestIds"/>.
/// </summary>
public sealed class QuestListSnapshot
{
    public required QuestJournalEntry[] Active { get; init; }
    public required byte[] CompletedMask { get; init; }
    public required int[] CompletedQuestIds { get; init; }
    public required int MaxActive { get; init; }
}
