namespace Fineage.Game.Quests;

/// <summary>
/// Later combat entry point. Zone melee does not call this.
/// A listener applies datapack steps whose hook is <c>kill</c> for the dead npc template.
/// </summary>
public interface IQuestKillHook
{
    QuestOutcome OnKill(QuestJournal journal, PlayerInventory inventory, int npcId);
}
