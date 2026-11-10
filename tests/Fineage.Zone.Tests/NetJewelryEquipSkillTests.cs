using Fineage.Accounts;
using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius jewelry item_skill (Ring of Queen Ant 6660 → skill 3562): GearSkills + SkillList fanout + combat mods.
/// </summary>
public sealed class NetJewelryEquipSkillTests
{
    const int RingOfQueenAnt = 6660;
    const int RingSkill = 3562;

    static StoredItem Bag(int itemId) => new(itemId, 1, -1);

    static SkillListMsg LastSkillList(NetBridgeRecorder rec) =>
        Assert.IsType<SkillListMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutSkillList)).Last()[1]);

    [Fact]
    public void Ring_of_queen_ant_raises_accuracy_and_crit_dmg()
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity
        {
            ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = 80, Inventory = bag
        };
        var world = new ZoneWorld(game: game);
        int accWithout = world.PlayerStats(player).Accuracy;
        double critWithout = world.PlayerStats(player).CritDmg;

        int mpWithout = player.MaxMp;
        // Seed class maxima then fold jewelry maxMp via RefreshPlayerMaxima.
        world.RefreshPlayerMaxima(player, fanout: false);
        mpWithout = player.MaxMp;

        bag.Equip(bag.Add(RingOfQueenAnt));
        var with = world.PlayerStats(player); // RefreshGearSkills
        world.RefreshPlayerMaxima(player, fanout: false);
        Assert.Contains(RingSkill, player.GearSkills.Keys);
        Assert.True(with.Accuracy > accWithout, $"acc {with.Accuracy} should exceed bare {accWithout}");
        Assert.True(with.CritDmg > critWithout, $"critDmg {with.CritDmg} should exceed bare {critWithout}");
        Assert.Equal(mpWithout + 21, player.MaxMp);
        var traits = world.PlayerDefenceTraits(player);
        Assert.NotNull(traits);
        Assert.True(traits!.ContainsKey("poison"), "Queen Ant should import POISON DefenceTrait");
        Assert.True(traits.ContainsKey("hold"), "Queen Ant should import HOLD DefenceTrait");

        // both_fingers fills LFinger first
        bag.Unequip(bag.GetEquipped(PaperdollSlot.LFinger)!);
        world.RefreshPlayerMaxima(player, fanout: false);
        Assert.Equal(accWithout, world.PlayerStats(player).Accuracy);
        Assert.Equal(critWithout, world.PlayerStats(player).CritDmg);
        Assert.DoesNotContain(RingSkill, player.GearSkills.Keys);
        Assert.Equal(mpWithout, player.MaxMp);
    }

    [Fact]
    public async Task Equipping_ring_of_queen_ant_fans_out_SkillList_with_passive()
    {
        var bench = new WorldBench();
        BenchPlayer actor = await bench.EnterAsync(
            "qaring",
            "Alden",
            [Bag(RingOfQueenAnt)],
            level: 80);
        bench.Rec.Calls.Clear();

        Assert.Null(await actor.Use(actor.Slot(RingOfQueenAnt)));
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSkillList)));
        var list = LastSkillList(bench.Rec);
        Assert.Contains(list.Skills, s => s.SkillId == RingSkill && s.Level == 1);

        bench.Rec.Calls.Clear();
        // both_fingers places into LFinger first
        Assert.Null(await actor.Unequip((int)PaperdollSlot.LFinger));
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSkillList)));
        var after = LastSkillList(bench.Rec);
        Assert.DoesNotContain(after.Skills, s => s.SkillId == RingSkill);
    }
}
