namespace Fineage.Game.Quests;

/// <summary>One persisted variable. Keys are at most 20 characters, values at most 255.</summary>
public sealed record QuestSaveVar(string Key, string Value);

/// <summary>
/// One quest row for a character. <see cref="State"/> is <see cref="QuestPhase"/>.
/// A missing list on the character means quests were never saved.
/// </summary>
public sealed record QuestSaveRow(int QuestId, int State, IReadOnlyList<QuestSaveVar> Vars);
