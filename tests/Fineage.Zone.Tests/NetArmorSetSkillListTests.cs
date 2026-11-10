using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius ArmorSetListener: completing a set grants temporary skills and fans out SkillList
/// so the client shows the set passive icon.
/// </summary>
public sealed class NetArmorSetSkillListTests
{
    // Dark Crystal Robe set (armor-sets.json id 44): chest 2407 + head 512 + gloves 5767 + feet 5779
    // → skills 3006 (Equip Set Items) + 3535 (Dark Crystal Robe Set).
    const int Chest = 2407;
    const int Head = 512;
    const int Gloves = 5767;
    const int Feet = 5779;
    const int SetMarker = 3006;
    const int SetPassive = 3535;

    static StoredItem Bag(int itemId) => new(itemId, 1, -1);

    static SkillListMsg LastSkillList(NetBridgeRecorder rec) =>
        Assert.IsType<SkillListMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutSkillList)).Last()[1]);

    [Fact]
    public void BuildSkills_includes_gear_set_passives_when_complete()
    {
        var game = Fineage.Game.GameData.LoadDefault();
        var bag = new Fineage.Game.PlayerInventory(game.Items);
        var player = new ZoneEntity
        {
            ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 94, Level = 80, Inventory = bag
        };
        var world = new ZoneWorld(game: game);
        bag.Equip(bag.Add(Chest));
        bag.Equip(bag.Add(Gloves));
        bag.Equip(bag.Add(Feet));
        world.PlayerStats(player); // RefreshGearSkills via PlayerStatMods
        var incomplete = NetEnterWorldProjector.BuildSkills(player, 0);
        Assert.DoesNotContain(incomplete.Skills, s => s.SkillId == SetPassive);

        bag.Equip(bag.Add(Head));
        world.PlayerStats(player);
        var complete = NetEnterWorldProjector.BuildSkills(player, 0);
        Assert.Contains(complete.Skills, s => s.SkillId == SetMarker && s.Level == 1);
        Assert.Contains(complete.Skills, s => s.SkillId == SetPassive && s.Level == 1);
    }

    [Fact]
    public async Task Completing_dark_crystal_robe_set_fans_out_SkillList_with_set_passives()
    {
        var bench = new WorldBench();
        BenchPlayer actor = await bench.EnterAsync(
            "dcset",
            "Mage",
            [Bag(Chest), Bag(Head), Bag(Gloves), Bag(Feet)],
            classId: 94,
            race: 0,
            level: 80);

        Assert.Null(await actor.Use(actor.Slot(Chest)));
        Assert.Null(await actor.Use(actor.Slot(Gloves)));
        Assert.Null(await actor.Use(actor.Slot(Feet)));
        bench.Rec.Calls.Clear();

        Assert.Null(await actor.Use(actor.Slot(Head)));
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSkillList)));
        var list = LastSkillList(bench.Rec);
        Assert.Contains(list.Skills, s => s.SkillId == SetMarker);
        Assert.Contains(list.Skills, s => s.SkillId == SetPassive);

        bench.Rec.Calls.Clear();
        Assert.Null(await actor.Unequip((int)Fineage.Game.PaperdollSlot.Head));
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSkillList)));
        var after = LastSkillList(bench.Rec);
        Assert.DoesNotContain(after.Skills, s => s.SkillId == SetPassive);
        Assert.DoesNotContain(after.Skills, s => s.SkillId == SetMarker);
    }
}
