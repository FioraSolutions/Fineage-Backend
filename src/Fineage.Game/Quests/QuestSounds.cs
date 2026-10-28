namespace Fineage.Game.Quests;

/// <summary>Client sound names for the quest lifecycle. Played by the engine, not copied from a script.</summary>
public static class QuestSounds
{
    public const string Accept = "ItemSound.quest_accept";
    public const string Middle = "ItemSound.quest_middle";
    public const string Finish = "ItemSound.quest_finish";
    public const string ItemGet = "ItemSound.quest_itemget";
    public const string GiveUp = "ItemSound.quest_giveup";

    public static bool Known(string? name) => name is Accept or Middle or Finish or ItemGet or GiveUp;
}
