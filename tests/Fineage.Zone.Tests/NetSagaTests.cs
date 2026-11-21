using Fineage.Accounts;
using Fineage.Game.Quests;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Saga of the Duelist (73) through zone: owned summons, despawn timers, the Halisha guard and the class change.</summary>
public sealed class NetSagaTests
{
    const int Saga = 73;
    const int Gladiator = 2;
    const int Duelist = 88;
    const int Master = 30849;     // npc[0]
    const int Spirit = 31653;     // npc[7]
    const int FirstFoe = 27289;   // mob[0]
    const int Archon = 27222;     // mob[1]
    const int Ticket = 7096;      // items[10]
    const int FoeProof = 7333;    // items[6]
    const int Marks = 7488;       // items[3]

    static void Seed(BenchPlayer p, int cond, params (string Key, string Value)[] vars)
    {
        var row = p.Player.Quests.Add(Saga, QuestPhase.Started);
        row.Set("cond", cond.ToString());
        foreach (var (k, v) in vars)
            row.Set(k, v);
    }

    static long Now(WorldBench bench) => bench.Now().ToUnixTimeMilliseconds();

    [Fact]
    public async Task Master_changes_the_gladiator_into_a_duelist()
    {
        var bench = new WorldBench();
        var p = await bench.EnterAsync(classId: Gladiator, level: 76, items: [new StoredItem(Ticket, 1, -1)]);
        var master = bench.World.SpawnFolkForTests(Master, p.Player.X + 30, p.Player.Y, p.Player.Z);
        Seed(p, 19);

        Assert.Null(await p.QuestEvent(master.ObjectId, Saga, "0-2"));

        Assert.Equal(Duelist, p.Player.ClassId);
        Assert.Equal(Duelist, p.SavedRecord().ClassId);
        Assert.Equal(QuestPhase.Completed, p.Player.Quests.Find(Saga)!.Phase);
        Assert.Equal(0, p.Inventory.CountOf(Ticket));
        Assert.Equal(1, p.Inventory.CountOf(QuestCatalog.SagaRewardItem));
    }

    [Fact]
    public async Task Spirit_raises_one_owned_foe_that_leaves_after_five_minutes()
    {
        var bench = new WorldBench();
        var p = await bench.EnterAsync(classId: Gladiator, level: 76);
        var spirit = bench.World.SpawnFolkForTests(Spirit, p.Player.X + 30, p.Player.Y, p.Player.Z);
        Seed(p, 8, ("spawned", "0"));

        Assert.Null(await p.QuestEvent(spirit.ObjectId, Saga, "7-1"));
        Assert.Equal(1, bench.World.CountNpcTemplateForTest(FirstFoe));
        Assert.Equal("1", p.Player.Quests.Find(Saga)!.Get("spawned"));
        Assert.Contains(bench.World.NpcSaysForTest, line => line.StartsWith(p.Player.Name + "!", StringComparison.Ordinal));

        Assert.Null(await p.QuestEvent(spirit.ObjectId, Saga, "7-1"));
        Assert.Equal(1, bench.World.CountNpcTemplateForTest(FirstFoe));

        bench.World.TickQuestSpawnsForTest(Now(bench) + 299_000);
        Assert.Equal(1, bench.World.CountNpcTemplateForTest(FirstFoe));
        bench.World.TickQuestSpawnsForTest(Now(bench) + 301_000);
        Assert.Equal(0, bench.World.CountNpcTemplateForTest(FirstFoe));
        Assert.Equal("0", p.Player.Quests.Find(Saga)!.Get("spawned"));
    }

    [Fact]
    public async Task Only_the_owner_alone_gets_the_foe_proof()
    {
        var bench = new WorldBench();
        var p = await bench.EnterAsync(classId: Gladiator, level: 76);
        var other = await bench.EnterAsync(classId: Gladiator, level: 76, clearRecorder: false);
        var spirit = bench.World.SpawnFolkForTests(Spirit, p.Player.X + 30, p.Player.Y, p.Player.Z);
        Seed(p, 8, ("spawned", "0"));

        Assert.Null(await p.QuestEvent(spirit.ObjectId, Saga, "7-1"));
        var foe = bench.World.FindNpcTemplateForTest(FirstFoe)!;
        bench.World.QuestKillEntityForTest(other.Player, foe);
        Assert.Equal(8, p.Player.Quests.Find(Saga)!.Cond);
        Assert.Equal("0", p.Player.Quests.Find(Saga)!.Get("spawned"));

        Assert.Null(await p.QuestEvent(spirit.ObjectId, Saga, "7-1"));
        foe = bench.World.FindNpcTemplateForTest(FirstFoe)!;
        bench.World.QuestKillEntityForTest(p.Player, foe);
        Assert.Equal(9, p.Player.Quests.Find(Saga)!.Cond);
        Assert.Equal(1, p.Inventory.CountOf(FoeProof));
    }

    [Fact]
    public async Task Archon_of_halisha_leaves_when_a_stranger_hits_it()
    {
        var bench = new WorldBench();
        var p = await bench.EnterAsync(classId: Gladiator, level: 76, items: [new StoredItem(Marks, 700, -1)]);
        var other = await bench.EnterAsync(classId: Gladiator, level: 76, clearRecorder: false);
        Seed(p, 15);

        bench.World.QuestKillForTest(p.Player, 21646);
        Assert.Equal(680, p.Inventory.CountOf(Marks));
        var archon = bench.World.FindNpcTemplateForTest(Archon)!;
        Assert.Equal(p.Player.X, archon.X);
        Assert.Equal("1", p.Player.Quests.Find(Saga)!.Get("spawned"));

        bench.World.QuestAttackForTest(other.Player, archon);
        Assert.Equal("0", p.Player.Quests.Find(Saga)!.Get("spawned"));
        bench.World.TickQuestSpawnsForTest();
        Assert.Equal(0, bench.World.CountNpcTemplateForTest(Archon));
    }

    [Fact]
    public async Task Buff_on_solo_guard_archon_from_a_stranger_makes_it_leave()
    {
        var bench = new WorldBench();
        var p = await bench.EnterAsync(classId: Gladiator, level: 76, items: [new StoredItem(Marks, 700, -1)]);
        var other = await bench.EnterAsync(classId: Gladiator, level: 76, clearRecorder: false);
        Seed(p, 15);
        bench.World.QuestKillForTest(p.Player, 21646);
        var archon = bench.World.FindNpcTemplateForTest(Archon)!;
        Assert.True(bench.World.ApplyOwnedSkillSeeForTest(other.Player, archon));
        bench.World.TickQuestSpawnsForTest();
        Assert.Equal(0, bench.World.CountNpcTemplateForTest(Archon));
    }

    [Fact]
    public async Task Plain_quest_spawns_leave_after_ten_quiet_minutes()
    {
        var bench = new WorldBench();
        var p = await bench.EnterAsync(level: 40);
        bench.World.QuestSpawnNearForTest(p.Player, 30001, p.Player.X, p.Player.Y, p.Player.Z, [27088]);
        var mob = bench.World.FindNpcTemplateForTest(27088)!;

        bench.World.TickQuestSpawnsForTest(Now(bench) + (ZoneWorld.DefaultQuestDespawnSeconds + 1) * 1000L);
        if (mob.AggroTargetId != 0)
        {
            // Still fighting: it waits, then leaves once nobody is engaged.
            Assert.Equal(1, bench.World.CountNpcTemplateForTest(27088));
            bench.World.SetAggroTarget(mob, 0);
            bench.World.TickQuestSpawnsForTest(Now(bench) + (ZoneWorld.DefaultQuestDespawnSeconds + 120) * 1000L);
        }

        Assert.Equal(0, bench.World.CountNpcTemplateForTest(27088));
    }
}
