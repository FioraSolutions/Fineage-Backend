using Fineage.Game.Services;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class NpcServiceRulesTests
{
    static readonly DateTimeOffset Wednesday = new(2026, 10, 28, 21, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset MondayNight = new(2026, 10, 26, 20, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset TuesdayMorning = new(2026, 10, 27, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Normal_teleport_is_free_through_level_40_and_half_price_on_monday_and_tuesday_evenings()
    {
        Assert.Equal(40, TeleportRules.FreeLevel);
        Assert.Equal(0, TeleportRules.Quote("NORMAL", 1, 18000, Wednesday));
        Assert.Equal(0, TeleportRules.Quote("NORMAL", 40, 18000, Wednesday));
        Assert.Equal(18000, TeleportRules.Quote("NORMAL", 41, 18000, Wednesday));
        Assert.Equal(9000, TeleportRules.Quote("NORMAL", 41, 18000, MondayNight));
        Assert.Equal(415, TeleportRules.Quote("NORMAL", 50, 830, new DateTimeOffset(2026, 10, 27, 23, 0, 0, TimeSpan.Zero)));
        Assert.Equal(18000, TeleportRules.Quote("NORMAL", 41, 18000, TuesdayMorning));
        Assert.Equal(18000, TeleportRules.Quote("NORMAL", 10, 18000, Wednesday, subClassActive: true));
        Assert.Equal(830, TeleportRules.Quote("OTHER", 1, 830, Wednesday));
        Assert.Equal(1000, TeleportRules.Quote("NOBLES_ADENA", 1, 1000, Wednesday));
    }

    [Fact]
    public void Roxxy_list_matches_the_mobius_gatekeeper()
    {
        var catalog = TeleportCatalog.LoadDefault();
        Assert.True(catalog.GatekeeperCount >= 100);
        var normal = catalog.Find(30006, "normal");
        Assert.NotNull(normal);
        Assert.Equal("NORMAL", normal!.Type);
        var gludin = normal.Find(0);
        Assert.Equal("The Village of Gludin", gludin!.Name);
        Assert.Equal(-80749, gludin.X);
        Assert.Equal(149834, gludin.Y);
        Assert.Equal(18000, gludin.FeeCount);
        Assert.Equal(57, gludin.FeeItemId);
        Assert.Equal(830, normal.Find(5)!.FeeCount);
        Assert.Equal("Elven Ruins", normal.Find(5)!.Name);

        var ruins = TeleportCatalog.LoadDefault().Find(30427, "OTHER");
        Assert.NotNull(ruins);
        Assert.Equal(48586, ruins!.Find(0)!.X);
        Assert.Equal(0, ruins.Find(0)!.FeeCount);
        Assert.True(TeleportRules.IsNobles(catalog.Find(30006, "NOBLES_TOKEN")!.Type));
    }

    [Fact]
    public void Newbie_sets_follow_the_mobius_windows()
    {
        var buffs = NewbieBuffCatalog.LoadDefault();
        Assert.Equal(8, buffs.LowestLevel);
        Assert.Equal(25, buffs.HighestLevel);
        Assert.True(buffs.IsMage(10));
        Assert.False(buffs.IsMage(49));
        Assert.False(buffs.IsMage(0));

        Assert.Equal(NewbieBuffRefusal.LevelLow, buffs.Evaluate(true, 0, 0, 7, out _));
        Assert.Equal(NewbieBuffRefusal.LevelHigh, buffs.Evaluate(true, 0, 0, 26, out _));
        Assert.Equal(NewbieBuffRefusal.NotNewbie, buffs.Evaluate(false, 0, 0, 16, out _));
        Assert.Equal(NewbieBuffRefusal.ThirdClass, buffs.Evaluate(true, 0, 3, 16, out _));
        Assert.Equal(NewbieBuffRefusal.NoneAvailable, buffs.Evaluate(true, 0, 0, 25, out var none));
        Assert.Empty(none);

        Assert.Equal(NewbieBuffRefusal.None, buffs.Evaluate(true, 0, 0, 8, out var wind));
        Assert.Equal(4322, Assert.Single(wind).SkillId);

        Assert.Equal(NewbieBuffRefusal.None, buffs.Evaluate(true, 0, 0, 16, out var fighter));
        Assert.Contains(fighter, buff => buff.SkillId == 4327);
        Assert.Contains(fighter, buff => buff.SkillId == 4338);
        Assert.DoesNotContain(fighter, buff => buff.SkillId == 4331);
        Assert.Equal(3600, fighter[0].DurationSeconds);
        Assert.Equal(3, fighter[0].AbnormalLevel);

        Assert.Equal(NewbieBuffRefusal.None, buffs.Evaluate(true, 10, 0, 16, out var mage));
        Assert.Contains(mage, buff => buff.SkillId == 4331);
        Assert.DoesNotContain(mage, buff => buff.SkillId == 4327);

        Assert.Equal(NewbieBuffRefusal.None, buffs.Evaluate(true, 49, 0, 16, out var orcMystic));
        Assert.Contains(orcMystic, buff => buff.SkillId == 4324);

        Assert.True(buffs.CanBless(39, 1));
        Assert.False(buffs.CanBless(40, 0));
        Assert.False(buffs.CanBless(20, 2));
        Assert.Equal(5182, buffs.Blessing.SkillId);
        Assert.Equal(7200, buffs.Blessing.DurationSeconds);
    }

    [Fact]
    public void Warehouse_slots_fee_and_load_follow_mobius_defaults()
    {
        Assert.Equal(120, WarehouseRules.WarehouseSlots(WarehouseRules.RaceDwarf));
        Assert.Equal(100, WarehouseRules.WarehouseSlots(0));
        Assert.Equal(100, WarehouseRules.InventorySlots(WarehouseRules.RaceDwarf));
        Assert.Equal(80, WarehouseRules.InventorySlots(0));
        Assert.Equal(30, WarehouseRules.DepositFee(1));
        Assert.Equal(60, WarehouseRules.DepositFee(2));
        Assert.True(WarehouseRules.FeeCovered(100, 50, 30));
        Assert.False(WarehouseRules.FeeCovered(100, 80, 30));
        Assert.Equal(0, WarehouseRules.SlotsFor(stackable: true, alreadyStored: true, count: 5));
        Assert.Equal(1, WarehouseRules.SlotsFor(stackable: true, alreadyStored: false, count: 5));
        Assert.Equal(3, WarehouseRules.SlotsFor(stackable: false, alreadyStored: false, count: 3));

        var bonuses = ClassCatalog.Load(Path.GetDirectoryName(TeleportCatalog.FindFile("classes.json"))!).StatBonuses;
        var fighter = ClassCatalog.Load(Path.GetDirectoryName(TeleportCatalog.FindFile("classes.json"))!).TryGet(0, out var template);
        Assert.True(fighter && template != null);
        double bonus = bonuses.Bonus(StatSlot.Con, template!.Con);
        Assert.Equal((int)Math.Floor(bonus * 69000), WarehouseRules.MaxLoad(bonus));
        Assert.True(WarehouseRules.MaxLoad(bonus) > 69000);
    }
}
