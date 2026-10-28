namespace Fineage.Game.Quests;

/// <summary>
/// Quest lifecycle. Values match the stored state byte: created, started, completed.
/// </summary>
public enum QuestPhase
{
    Created = 0,
    Started = 1,
    Completed = 2
}
