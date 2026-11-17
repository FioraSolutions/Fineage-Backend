using System.Text.Json;
using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Third-class sagas 70–100: the shared saga expansion, walked from the master's start to the class change.</summary>
public sealed class SagaQuestTests
{
    static readonly GameData Data = GameData.LoadDefault();

    sealed record Saga(int Id, int ClassId, int PrevClass, int[] Npc, int[] Items, int[] Mob);

    static readonly Dictionary<int, Saga> Sagas = LoadSagas();

    static Dictionary<int, Saga> LoadSagas()
    {
        string dir = Path.GetDirectoryName(typeof(SagaQuestTests).Assembly.Location)!;
        string? path = null;
        for (var d = new DirectoryInfo(dir); d != null && path == null; d = d.Parent)
        {
            string candidate = Path.Combine(d.FullName, "data", "quests", QuestCatalog.SagaFileName);
            if (File.Exists(candidate))
                path = candidate;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path!));
        var map = new Dictionary<int, Saga>();
        foreach (var s in doc.RootElement.GetProperty("sagas").EnumerateArray())
        {
            static int[] Ints(JsonElement e) => e.EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var saga = new Saga(s.GetProperty("id").GetInt32(), s.GetProperty("classId").GetInt32(),
                s.GetProperty("prevClass").GetInt32(), Ints(s.GetProperty("npc")), Ints(s.GetProperty("items")), Ints(s.GetProperty("mob")));
            map[saga.Id] = saga;
        }

        return map;
    }

    public static IEnumerable<object[]> SagaIds() => Enumerable.Range(70, 31).Select(id => new object[] { id });

    static PlayerInventory Bag(params (int ItemId, int Count)[] seed)
    {
        var bag = new PlayerInventory(Data.Items);
        foreach (var (id, n) in seed)
        {
            if (id > 0)
                bag.AddPieces(id, n);
        }

        return bag;
    }

    /// <summary>Greedy walker: every roll succeeds, owned spawns are fought solo by their owner.</summary>
    static int Walk(Saga saga, PlayerInventory bag, QuestJournal j, QuestActor me, int rounds = 400)
    {
        var quest = Data.Quests.Find(saga.Id)!;
        var m = new QuestMachine(Data.Quests, Data.Items, () => 0.0);
        var talks = quest.Dialogs.Where(d => d.Hook == QuestHook.Talk).Select(d => d.NpcId).Distinct().ToArray();
        var events = quest.Dialogs.Where(d => d.Hook == QuestHook.Event).Select(d => (d.NpcId, d.Event)).Distinct().ToArray();
        var kills = quest.Dialogs.Where(d => d.Hook == QuestHook.Kill && d.Owned == null && !d.Party).Select(d => d.NpcId).Distinct().ToArray();
        var party = quest.Dialogs.Where(d => d.Hook == QuestHook.Kill && d.Party).Select(d => d.NpcId).Distinct().ToArray();
        var owned = quest.Dialogs.Where(d => d.Owned != null).Select(d => (d.NpcId, d.Hook)).Distinct().ToArray();
        for (int round = 0; round < rounds; round++)
        {
            int Check(QuestOutcome o) => o.Extras.ClassChange;
            foreach (var (npc, ev) in events)
            {
                int cls = Check(m.AdvEvent(j, bag, saga.Id, npc, ev, me));
                if (cls != 0)
                    return cls;
            }

            foreach (int npc in talks)
            {
                int cls = Check(m.Talk(j, bag, saga.Id, npc, me));
                if (cls != 0)
                    return cls;
            }

            foreach (int npc in kills)
                m.OnKill(j, bag, npc, me);
            foreach (int npc in party)
                m.OnPartyKill(j, bag, saga.Id, npc, me);
            foreach (var (npc, hook) in owned)
                m.OnOwned(j, bag, npc, me, hook, solo: true);
        }

        return 0;
    }

    [Theory]
    [MemberData(nameof(SagaIds))]
    public void Saga_ends_in_the_third_class(int questId)
    {
        var saga = Sagas[questId];
        var me = new QuestActor(0, 76, saga.PrevClass);
        var bag = Bag((saga.Items[0], 1), (saga.Items[2], 1), (saga.Items[11], 1));
        var j = new QuestJournal();
        Assert.Equal(saga.ClassId, Walk(saga, bag, j, me));
        Assert.Equal(QuestPhase.Completed, j.Find(questId)!.Phase);
        Assert.Equal(1, bag.CountOf(QuestCatalog.SagaRewardItem));
        Assert.True(bag.CountOf(QuestCatalog.AdenaItemId) >= QuestCatalog.SagaRewardAdena);
        Assert.Equal(0, bag.CountOf(saga.Items[10]));
    }

    [Fact]
    public void Every_second_class_with_a_third_has_a_saga()
    {
        // Interlude 2nd classes 2..57 (non-dwarf/dwarf) map one-to-one onto 88..118.
        var prev = Sagas.Values.Select(s => s.PrevClass).ToHashSet();
        var next = Sagas.Values.Select(s => s.ClassId).ToHashSet();
        Assert.Equal(31, prev.Count);
        Assert.Equal(Enumerable.Range(88, 31).ToHashSet(), next);
    }

    [Fact]
    public void Wrong_class_or_low_level_cannot_start()
    {
        var saga = Sagas[73];
        var m = new QuestMachine(Data.Quests, Data.Items);
        var j = new QuestJournal();
        var bag = Bag();
        m.AdvEvent(j, bag, 73, saga.Npc[0], "accept", new QuestActor(0, 76, 0));
        Assert.Null(j.Find(73));
        var low = m.AdvEvent(j, bag, 73, saga.Npc[0], "0-1", new QuestActor(0, 75, saga.PrevClass));
        Assert.Equal(Page(saga, "0-02.htm"), low.Html);
        m.AdvEvent(j, bag, 73, saga.Npc[0], "accept", new QuestActor(0, 76, saga.PrevClass));
        Assert.Equal(1, j.Find(73)!.Cond);
        Assert.Equal(1, bag.CountOf(saga.Items[10]));
    }

    static void Seed(QuestJournal j, params (string Key, string Value)[] vars)
    {
        var row = j.Add(70, QuestPhase.Started);
        foreach (var (k, v) in vars)
            row.Set(k, v);
    }

    static string Page(Saga saga, string file)
    {
        string dir = Path.GetDirectoryName(typeof(SagaQuestTests).Assembly.Location)!;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            var hit = Directory.Exists(Path.Combine(d.FullName, "data", "quests"))
                ? Directory.GetDirectories(Path.Combine(d.FullName, "data", "quests"), $"{saga.Id:00000}_*").FirstOrDefault()
                : null;
            if (hit != null)
                return File.ReadAllText(Path.Combine(hit, file));
        }

        return "";
    }

    [Fact]
    public void Halisha_marks_raise_the_archon_once()
    {
        var saga = Sagas[70];
        var me = new QuestActor(0, 76, saga.PrevClass);
        var m = new QuestMachine(Data.Quests, Data.Items, () => 0.0);
        var j = new QuestJournal();
        var bag = Bag((saga.Items[3], 700));
        Seed(j, ("cond", "15"));
        var raise = m.OnPartyKill(j, bag, 70, 21646, me);
        Assert.Single(raise.Extras.Summons);
        Assert.Equal(saga.Mob[1], raise.Extras.Summons[0].NpcId);
        Assert.True(raise.Extras.Summons[0].AtPlayer);
        Assert.Equal(680, bag.CountOf(saga.Items[3]));
        Assert.Empty(m.OnPartyKill(j, bag, 70, 21646, me).Extras.Summons);
        Assert.Equal(680, bag.CountOf(saga.Items[3]));
        // The summon timed out: marks drop again, and the next kill can raise it again.
        Assert.True(m.ApplySpawnVars(j, 70, raise.Extras.Summons[0].GoneVars));
        bag.AddPieces(saga.Items[3], 20);
        Assert.Single(m.OnPartyKill(j, bag, 70, 21646, me).Extras.Summons);
    }

    [Fact]
    public void Owned_kill_by_someone_else_gives_nothing()
    {
        var saga = Sagas[70];
        var me = new QuestActor(0, 76, saga.PrevClass);
        var m = new QuestMachine(Data.Quests, Data.Items);
        var j = new QuestJournal();
        var bag = Bag();
        Seed(j, ("cond", "8"), ("spawned", "1"));
        m.OnKill(j, bag, saga.Mob[0], me);
        m.OnOwned(j, bag, saga.Mob[0], me, QuestHook.Kill, solo: false);
        Assert.Equal(8, j.Find(70)!.Cond);
        m.OnOwned(j, bag, saga.Mob[0], me, QuestHook.Kill, solo: true);
        Assert.Equal(9, j.Find(70)!.Cond);
        Assert.Equal(1, bag.CountOf(saga.Items[6]));
    }

    [Fact]
    public void Relog_resets_summon_vars()
    {
        var m = new QuestMachine(Data.Quests, Data.Items);
        var j = new QuestJournal();
        Seed(j, ("cond", "8"), ("spawned", "1"));
        Assert.True(m.ResetSpawnVars(j));
        Assert.Equal("0", j.Find(70)!.Vars["spawned"]);
    }
}
