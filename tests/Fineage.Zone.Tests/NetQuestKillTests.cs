using Fineage.Game.Quests;
using Fineage.Net.Contract;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius Quest.onKill reaches the killer on Net: quest drop lands in the bag (InventoryUpdate).</summary>
public sealed class NetQuestKillTests
{
    [Fact]
    public async Task Spore_fungus_kill_drops_spore_sacs_for_collect_spores()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        killer.Quests.Add(313, QuestPhase.Started).Replace(QuestPhase.Started, new Dictionary<string, string> { ["cond"] = "1" });

        for (int i = 0; i < 80; i++)
            kit.World.QuestKillForTest(killer, 20509);

        Assert.Equal(10, killer.Inventory!.CountOf(1118));
        Assert.Equal(2, killer.Quests.Find(313)!.Cond);
        Assert.NotEmpty(rec.Of(nameof(IWorldEventPublisher.FanoutInventoryUpdate)));
    }
}

/// <summary>Mobius getRandomPartyMemberState: Q316 kill credit goes to one eligible party member within ALT_PARTY_RANGE.</summary>
public sealed class NetQuestPartyKillTests
{
    static void Start316(Fineage.Zone.World.ZoneEntity p) =>
        p.Quests.Add(316, QuestPhase.Started).Replace(QuestPhase.Started, new Dictionary<string, string> { ["cond"] = "1" });

    [Fact]
    public async Task Party_member_with_the_quest_gets_the_fang_when_killer_has_none()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        var (_, mate) = await kit.EnterAsync();
        mate.X = killer.X + 500; mate.Y = killer.Y;
        kit.World.JoinPartyForTest(killer, mate);
        Start316(mate);

        kit.World.QuestKillForTest(killer, 20040);
        // Mobius giveItemRandomly count=1 (chance 10/5 ≈ always); older pack wrongly dropped 2.
        Assert.Equal(1, mate.Inventory!.CountOf(1042));
        Assert.Equal(0, killer.Inventory!.CountOf(1042));
    }

    [Fact]
    public async Task Out_of_range_or_not_in_party_gets_nothing()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        var (_, far) = await kit.EnterAsync();
        var (_, stranger) = await kit.EnterAsync();
        far.X = killer.X + Fineage.Zone.World.ZoneWorld.QuestPartyRange + 10; far.Y = killer.Y;
        kit.World.JoinPartyForTest(killer, far);
        Start316(far);
        Start316(stranger);

        kit.World.QuestKillForTest(killer, 20040);
        Assert.Equal(0, far.Inventory!.CountOf(1042));
        Assert.Equal(0, stranger.Inventory!.CountOf(1042));
    }

    [Fact]
    public async Task Varool_fang_skips_members_who_already_hold_one()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        var (_, mate) = await kit.EnterAsync();
        mate.X = killer.X; mate.Y = killer.Y;
        kit.World.JoinPartyForTest(killer, mate);
        Start316(killer);
        Start316(mate);

        kit.World.QuestKillForTest(killer, 27020);
        kit.World.QuestKillForTest(killer, 27020);
        Assert.Equal(1, killer.Inventory!.CountOf(1043));
        Assert.Equal(1, mate.Inventory!.CountOf(1043));
        kit.World.QuestKillForTest(killer, 27020);
        Assert.Equal(1, killer.Inventory!.CountOf(1043));
        Assert.Equal(1, mate.Inventory!.CountOf(1043));
    }
}

/// <summary>Mobius Quest.onAttack: Q234 Fate's Whisper reddens the Pipette Knife on a hit against Baium.</summary>
public sealed class NetQuestAttackTests
{
    [Fact]
    public async Task Hitting_baium_with_the_pipette_knife_gives_the_red_knife()
    {
        var kit = new NetTestKit();
        var (_, hero) = await kit.EnterAsync();
        hero.Quests.Add(234, QuestPhase.Started).Replace(QuestPhase.Started, new Dictionary<string, string> { ["cond"] = "7" });
        var knife = hero.Inventory!.Add(4665);
        var baium = new ZoneEntity { Kind = ZoneEntityKind.Npc, NpcId = 29020, ObjectId = 0 };

        kit.World.QuestAttackForTest(hero, baium);
        Assert.Equal(0, hero.Inventory.CountOf(4673));

        hero.Inventory.Equip(knife);
        kit.World.QuestAttackForTest(hero, baium);
        Assert.Equal(1, hero.Inventory.CountOf(4673));
        Assert.Equal(0, hero.Inventory.CountOf(4665));
    }
}

/// <summary>Ketra / Varka: raid heads go to every party member (executeForEachPlayer); killing an ally costs the mark.</summary>
public sealed class NetKetraVarkaTests
{
    [Fact]
    public async Task Shadith_head_goes_to_every_party_member_on_the_quest()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        var (_, mate) = await kit.EnterAsync();
        mate.X = killer.X + 300; mate.Y = killer.Y;
        kit.World.JoinPartyForTest(killer, mate);
        foreach (var p in new[] { killer, mate })
            p.Quests.Add(607, QuestPhase.Started).Replace(QuestPhase.Started, new Dictionary<string, string> { ["cond"] = "1" });

        kit.World.QuestKillForTest(killer, 25309);
        Assert.Equal(1, killer.Inventory!.CountOf(7235));
        Assert.Equal(1, mate.Inventory!.CountOf(7235));
        Assert.Equal(2, mate.Quests.Find(607)!.Cond);
    }

    [Fact]
    public async Task Killing_a_ketra_orc_while_allied_lowers_the_mark_for_the_party()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        var (_, mate) = await kit.EnterAsync();
        mate.X = killer.X; mate.Y = killer.Y;
        kit.World.JoinPartyForTest(killer, mate);
        mate.Inventory!.Add(7212);
        mate.Quests.Add(606, QuestPhase.Started).Replace(QuestPhase.Started, new Dictionary<string, string> { ["cond"] = "1" });

        kit.World.QuestKillForTest(killer, 21324);
        Assert.Equal(0, mate.Inventory.CountOf(7212));
        Assert.Equal(1, mate.Inventory.CountOf(7211));
        Assert.Null(mate.Quests.Find(606));
    }
}

/// <summary>Quest 350: Soul Crystal absorb at half HP, then the kill steps the crystal up (Mobius levelSoulCrystals).</summary>
public sealed class NetSoulCrystalTests
{
    static ZoneEntity Mob(int npcId, int hp) =>
        new() { Kind = ZoneEntityKind.Npc, NpcId = npcId, ObjectId = 0, Attackable = true, MaxHp = hp, CurHp = hp };

    static void Start350(ZoneEntity p) =>
        p.Quests.Add(350, QuestPhase.Started).Replace(QuestPhase.Started, new Dictionary<string, string> { ["cond"] = "1" });

    [Fact]
    public async Task Absorbed_at_half_hp_levels_the_crystal()
    {
        var kit = new NetTestKit();
        var (_, hero) = await kit.EnterAsync();
        kit.World.SoulCrystalRandom = _ => 0;
        Start350(hero);
        hero.Inventory!.Add(4629);
        var orc = Mob(20583, 1000);
        orc.CurHp = 400;
        kit.World.SoulAbsorbForTest(hero, orc);
        kit.World.SoulKillForTest(hero, orc);
        Assert.Equal(0, hero.Inventory.CountOf(4629));
        Assert.Equal(1, hero.Inventory.CountOf(4630));
    }

    [Fact]
    public async Task No_absorb_or_too_early_absorb_does_nothing()
    {
        var kit = new NetTestKit();
        var (_, hero) = await kit.EnterAsync();
        kit.World.SoulCrystalRandom = _ => 0;
        Start350(hero);
        hero.Inventory!.Add(4629);
        kit.World.SoulKillForTest(hero, Mob(20583, 1000));
        var early = Mob(20583, 1000);
        kit.World.SoulAbsorbForTest(hero, early); // full HP
        kit.World.SoulKillForTest(hero, early);
        Assert.Equal(1, hero.Inventory.CountOf(4629));
    }

    [Fact]
    public async Task Without_the_quest_or_with_two_crystals_nothing_happens()
    {
        var kit = new NetTestKit();
        var (_, hero) = await kit.EnterAsync();
        kit.World.SoulCrystalRandom = _ => 0;
        hero.Inventory!.Add(4629);
        var orc = Mob(20583, 1000);
        orc.CurHp = 100;
        kit.World.SoulAbsorbForTest(hero, orc);
        kit.World.SoulKillForTest(hero, orc);
        Assert.Equal(1, hero.Inventory.CountOf(4629));

        Start350(hero);
        hero.Inventory.Add(4640);
        kit.World.SoulAbsorbForTest(hero, orc);
        kit.World.SoulKillForTest(hero, orc);
        Assert.Equal(1, hero.Inventory.CountOf(4629));
        Assert.Equal(1, hero.Inventory.CountOf(4640));
    }

    [Fact]
    public async Task Baium_levels_every_party_member_at_ten()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        var (_, mate) = await kit.EnterAsync();
        mate.X = killer.X + 200; mate.Y = killer.Y; mate.Z = killer.Z;
        kit.World.JoinPartyForTest(killer, mate);
        kit.World.SoulCrystalRandom = _ => 0;
        foreach (var p in new[] { killer, mate })
        {
            Start350(p);
            p.Inventory!.Add(4639); // red 10
        }

        kit.World.SoulKillForTest(killer, Mob(29020, 100000));
        Assert.Equal(1, killer.Inventory!.CountOf(5577));
        Assert.Equal(1, mate.Inventory!.CountOf(5577));
    }
}
