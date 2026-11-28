using System.Reflection;
using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>
/// #1116: the start marks of quests a character has no row for are cached per actor. These tests pin that the cached
/// answer is exactly the full-catalog walk it replaced, on the shipped pack, for fresh and mid-progress journals.
/// </summary>
public sealed class QuestNpcMarkCacheTests
{
    static readonly GameData Data = GameData.LoadDefault();

    static readonly MethodInfo FindStep = typeof(QuestMachine).GetMethod("Find", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("QuestMachine.Find (private static) not found");

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 20, 18)]
    [InlineData(2, 40, 31)]
    [InlineData(3, 62, 44)]
    [InlineData(4, 76, 53)]
    public void Cached_marks_equal_the_full_catalog_walk(int race, int level, int classId)
    {
        var machine = new QuestMachine(Data.Quests, Data.Items);
        var actor = new QuestActor(race, level, classId);
        var quests = Data.Quests.Quests.Take(40).ToList();

        var journals = new List<QuestJournal> { new() };
        var progressed = new QuestJournal();
        for (int i = 0; i < quests.Count; i++)
        {
            var row = progressed.Add(quests[i].Id, (i % 3) switch
            {
                0 => QuestPhase.Started,
                1 => QuestPhase.Completed,
                _ => QuestPhase.Created
            });
            if (row.Phase == QuestPhase.Started)
                row.Set(QuestProgress.CondVar, (1 + i % 4).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        journals.Add(progressed);

        foreach (var journal in journals)
        {
            // Twice: the second call is served from the per-actor cache.
            for (int pass = 0; pass < 2; pass++)
                Assert.Equal(Reference(journal, actor), machine.CollectNpcMarks(journal, actor));
        }
    }

    [Fact]
    public void The_actor_random_source_does_not_change_or_grow_the_cache()
    {
        var machine = new QuestMachine(Data.Quests, Data.Items);
        var journal = new QuestJournal();
        var plain = machine.CollectNpcMarks(journal, new QuestActor(0, 10, 0));
        var rolled = machine.CollectNpcMarks(journal, new QuestActor(0, 10, 0) { Roll = () => 0.0 });
        Assert.Equal(plain, rolled);
    }

    /// <summary>The walk CollectNpcMarks did before the cache: every catalog quest, every keyed step group.</summary>
    static List<(int NpcId, byte Mark)> Reference(QuestJournal journal, QuestActor actor)
    {
        var marks = new Dictionary<int, byte>();
        foreach (var quest in Data.Quests.Quests)
        {
            var state = journal.Find(quest.Id);
            foreach (var (key, group) in quest.DialogsByKey)
            {
                if (key.NpcId <= 0)
                    continue;
                if (FindStep.Invoke(null, [quest, key.Hook, key.NpcId, key.Event, state, actor, null, null, null]) == null)
                    continue;
                foreach (var dialog in group)
                {
                    byte mark = state switch
                    {
                        { Phase: QuestPhase.Started } when Completes(dialog, state) => 3,
                        { Phase: QuestPhase.Started } => 2,
                        _ => 1
                    };
                    marks[key.NpcId] = Math.Max(marks.GetValueOrDefault(key.NpcId), mark);
                }
            }
        }

        return marks.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    static bool Completes(QuestDialog dialog, QuestProgress progress) =>
        dialog.When == QuestWhen.Started
        && (dialog.Cond is not int cond || cond == progress.Cond)
        && dialog.Ops.Any(op => op is CompleteOp);
}
