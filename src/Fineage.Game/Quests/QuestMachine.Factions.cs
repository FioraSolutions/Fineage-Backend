namespace Fineage.Game.Quests;

/// <summary>The Ketra / Varka alliance marks a player holds, as a value a snapshot can carry.</summary>
[Flags]
public enum FactionAlliance : byte
{
    None = 0,
    Ketra = 1,
    Varka = 2
}

/// <summary>
/// Ketra Orc / Varka Silenos alliance rules (Mobius <c>ai/others/VarkaKetra</c> behaviour): the alliance mark of a side
/// keeps that side's monsters passive, and killing one of them costs a mark level and drops the side's quests.
/// </summary>
public sealed partial class QuestMachine
{
    public static readonly IReadOnlySet<int> KetraMobs = new HashSet<int>
    {
        21324, 21325, 21327, 21328, 21329, 21331, 21332, 21334, 21335, 21336, 21338, 21339, 21340, 21342, 21343, 21344,
        21345, 21346, 21347, 21348, 21349, 25299, 25302, 25305, 25306
    };

    public static readonly IReadOnlySet<int> VarkaMobs = new HashSet<int>
    {
        21350, 21351, 21353, 21354, 21355, 21357, 21358, 21360, 21361, 21362, 21364, 21365, 21366, 21368, 21369, 21370,
        21371, 21372, 21373, 21374, 21375, 25309, 25312, 25315, 25316
    };

    static readonly int[] KetraMarks = [7211, 7212, 7213, 7214, 7215];
    static readonly int[] VarkaMarks = [7221, 7222, 7223, 7224, 7225];
    static readonly int[] KetraQuests = [605, 606, 607, 608, 609, 610];
    static readonly int[] VarkaQuests = [611, 612, 613, 614, 615, 616];

    static int[]? MarksOf(int npcId) => KetraMobs.Contains(npcId) ? KetraMarks : VarkaMobs.Contains(npcId) ? VarkaMarks : null;

    static bool HoldsAny(PlayerInventory inventory, int[] ids)
    {
        foreach (int id in ids)
        {
            if (inventory.CountOf(id) > 0)
                return true;
        }

        return false;
    }

    /// <summary>Mobius <c>onNpcHate</c>: a Ketra / Varka monster does not aggro a player allied with its side.</summary>
    public static bool IsAlliedWith(int npcId, PlayerInventory? inventory) =>
        inventory != null && MarksOf(npcId) is { } marks && HoldsAny(inventory, marks);

    /// <summary>The sides whose marks <paramref name="inventory"/> holds.</summary>
    public static FactionAlliance AllianceOf(PlayerInventory? inventory) =>
        inventory == null
            ? FactionAlliance.None
            : (HoldsAny(inventory, KetraMarks) ? FactionAlliance.Ketra : FactionAlliance.None) |
              (HoldsAny(inventory, VarkaMarks) ? FactionAlliance.Varka : FactionAlliance.None);

    /// <summary><see cref="IsAlliedWith(int, PlayerInventory?)"/> for a player known by its <see cref="AllianceOf"/>.</summary>
    public static bool IsAlliedWith(int npcId, FactionAlliance alliance) =>
        (KetraMobs.Contains(npcId) && alliance.HasFlag(FactionAlliance.Ketra)) ||
        (VarkaMobs.Contains(npcId) && alliance.HasFlag(FactionAlliance.Varka));

    /// <summary>
    /// The player helped kill a monster of the side they are allied with: the mark drops one level (level 1 is lost)
    /// and every started quest of that side ends with its quest items. Empty when nothing applies.
    /// </summary>
    public IReadOnlyList<QuestOutcome> BreakAlliance(QuestJournal journal, PlayerInventory inventory, int npcId)
    {
        if (MarksOf(npcId) is not { } marks || !HoldsAny(inventory, marks))
            return [];
        bool ketra = marks == KetraMarks;
        var outcomes = new List<QuestOutcome>();
        if (_catalog.Find(ketra ? 605 : 611) is { } alliance)
        {
            var plan = new Plan(journal.Find(alliance.Id));
            for (int i = 0; i < marks.Length; i++)
            {
                int have = inventory.CountOf(marks[i]);
                if (have <= 0)
                    continue;
                AddDelta(inventory, plan, marks[i], -have);
                if (i > 0)
                    AddDelta(inventory, plan, marks[i - 1], 1);
                break;
            }

            outcomes.Add(Commit(journal, inventory, alliance, plan, "kill", ""));
        }

        foreach (int questId in ketra ? KetraQuests : VarkaQuests)
        {
            var row = journal.Find(questId);
            if (row == null || row.Phase != QuestPhase.Started || _catalog.Find(questId) is not { } quest)
                continue;
            var plan = new Plan(row) { Delete = true };
            plan.Sounds.Add(QuestSounds.GiveUp);
            WipeQuestItems(quest, inventory, plan);
            outcomes.Add(Commit(journal, inventory, quest, plan, "kill", ""));
        }

        return outcomes;
    }
}
