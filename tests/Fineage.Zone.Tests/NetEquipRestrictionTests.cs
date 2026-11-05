using Fineage.Game;
using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Class, race, sex, hero, clan, castle, PvP, level, PK, clan-hall, and exact-Expertise equip conditions, plus the
/// grade penalty a higher crystal brings. Ported off the V2 wire: the deny code is the world's own return value and
/// the paperdoll / penalty sheet is read from the recorded <see cref="INetPresenceBridge"/> traffic.
/// </summary>
public sealed class NetEquipRestrictionTests
{
    const int HumanSword = 425;
    const int FemaleOnly = 7682;
    const int MaleOnly = 8923;
    const int Locked = 90001;

    const int PvpOnly = 90002;
    const int PeaceOnly = 90003;
    const int ExactGrade = 90004;
    const int MinLevel = 90005;
    const int MaxPk = 90006;
    const int Hall = 90007;

    const int DSword = 90008;
    const int CArmor = 90009;
    const int SSword = 90010;
    const int Bow = 90011;
    const int SArrow = 90012;

    [Fact]
    public async Task Race_and_sex_conditions_reject_without_changing_paperdoll()
    {
        var bench = new WorldBench();
        BenchPlayer human = await bench.EnterAsync("racea", "Alden", [Bag(HumanSword), Bag(FemaleOnly), Bag(MaleOnly)]);

        Assert.Null(await human.Use(human.Slot(HumanSword)));
        var (_, user) = LastGear(bench.Rec);
        Assert.Equal(HumanSword, user.Paperdoll[10].ItemId);

        Assert.Null(await human.Use(human.Slot(MaleOnly)));
        (_, user) = LastGear(bench.Rec);
        Assert.Equal(MaleOnly, user.Paperdoll[15].ItemId);

        bench.Rec.Calls.Clear();
        Assert.Equal(InventoryProtocol.SexRestricted, await human.Use(human.Slot(FemaleOnly)));
        Assert.Empty(bench.Rec.Calls);
        AssertSavedSlot(human, FemaleOnly, -1);

        // A race outside the item's list is refused by the item condition itself, not by the lobby.
        BenchPlayer outsider = await bench.EnterAsync("raceb", "Bram", [Bag(HumanSword)], race: 99);
        Assert.Equal(InventoryProtocol.RaceRestricted, await outsider.Use(outsider.Slot(HumanSword)));
        AssertSavedSlot(outsider, HumanSword, -1);

        BenchPlayer woman = await bench.EnterAsync("racec", "Cira", [Bag(FemaleOnly), Bag(MaleOnly)], sex: 1);
        Assert.Null(await woman.Use(woman.Slot(FemaleOnly)));
        (_, user) = LastGear(bench.Rec);
        Assert.Equal(FemaleOnly, user.Paperdoll[16].ItemId);
        Assert.Equal(InventoryProtocol.SexRestricted, await woman.Use(woman.Slot(MaleOnly)));
        AssertSavedSlot(woman, MaleOnly, -1);
    }

    [Fact]
    public async Task Hero_clan_castle_and_class_conditions_reject_without_equipping()
    {
        var bench = new WorldBench(WithClassLock());
        BenchPlayer actor = await bench.EnterAsync("hero", "Alden",
            [Bag(6611), Bag(7850), Bag(7860), Bag(6834), Bag(6841), Bag(Locked)]);

        Assert.Equal(InventoryProtocol.HeroRestricted, await actor.Use(actor.Slot(6611)));
        Assert.Equal(InventoryProtocol.PledgeRestricted, await actor.Use(actor.Slot(7850)));
        Assert.Equal(InventoryProtocol.CastleRestricted, await actor.Use(actor.Slot(6834)));
        Assert.Equal(InventoryProtocol.CastleRestricted, await actor.Use(actor.Slot(6841)));
        Assert.Equal(InventoryProtocol.ClassRestricted, await actor.Use(actor.Slot(Locked)));
        foreach (int itemId in new[] { 6611, 7850, 7860, 6834, 6841, Locked })
            AssertSavedSlot(actor, itemId, -1);

        Assert.True(bench.World.TrySetEquipStanding(actor.ObjectId, new PlayerEquipStanding(Hero: true)));
        Assert.Null(await actor.Use(actor.Slot(6611)));
        var (_, user) = LastGear(bench.Rec);
        Assert.Equal(6611, user.Paperdoll[7].ItemId);
        Assert.Equal(4, user.WeaponGradePenalty);
        Assert.Equal(0, user.ArmorGradePenalty);
        // Mobius refreshExpertisePenalty sends EtcStatusUpdate: the icon has to agree with the UserInfo penalty.
        var etc = LastEtcStatus(bench.Rec);
        Assert.True(etc.GradePenalty);

        Assert.True(bench.World.TrySetEquipStanding(actor.ObjectId, new PlayerEquipStanding(
            Hero: true, InClan: true, PledgeClass: 1, CastleId: 6)));
        Assert.Null(await actor.Use(actor.Slot(7850)));
        (_, user) = LastGear(bench.Rec);
        Assert.Equal(7850, user.Paperdoll[6].ItemId);
        Assert.Equal(InventoryProtocol.PledgeRestricted, await actor.Use(actor.Slot(7860)));
        Assert.Equal(InventoryProtocol.PledgeRestricted, await actor.Use(actor.Slot(6834)));

        Assert.True(bench.World.TrySetEquipStanding(actor.ObjectId, new PlayerEquipStanding(
            Hero: true, InClan: true, PledgeClass: 2, CastleId: 6)));
        Assert.Null(await actor.Use(actor.Slot(6834)));
        (_, user) = LastGear(bench.Rec);
        Assert.Equal(6834, user.Paperdoll[15].ItemId);

        Assert.True(bench.World.TrySetEquipStanding(actor.ObjectId, new PlayerEquipStanding(
            Hero: true, InClan: true, PledgeClass: 4, CastleId: 6)));
        Assert.Null(await actor.Use(actor.Slot(7860)));
        (_, user) = LastGear(bench.Rec);
        Assert.Equal(7860, user.Paperdoll[6].ItemId);

        Assert.True(bench.World.TrySetEquipStanding(actor.ObjectId, new PlayerEquipStanding(
            Hero: true, InClan: true, ClanLeader: true, CastleId: 3)));
        Assert.Null(await actor.Use(actor.Slot(6841)));
        (_, user) = LastGear(bench.Rec);
        Assert.Equal(6841, user.Paperdoll[15].ItemId);

        Assert.True(bench.World.TrySetEquipStanding(actor.ObjectId, new PlayerEquipStanding()));
        Assert.Null(await actor.Use(actor.Slot(6611)));
        (_, user) = LastGear(bench.Rec);
        Assert.Equal(0, user.Paperdoll[7].ItemId);
    }

    [Fact]
    public async Task Pvp_level_pk_clan_hall_and_exact_expertise_reject_without_equipping()
    {
        var bench = new WorldBench(RuleCatalog());
        BenchPlayer actor = await bench.EnterAsync("flags", "Alden",
            [Bag(PvpOnly), Bag(PeaceOnly), Bag(ExactGrade), Bag(MinLevel), Bag(MaxPk), Bag(Hall)], level: 30);

        Assert.Equal(InventoryProtocol.PvpRestricted, await actor.Use(actor.Slot(PvpOnly)));
        Assert.Null(await actor.Use(actor.Slot(PeaceOnly)));
        Assert.Null(await actor.Use(actor.Slot(PeaceOnly)));
        Assert.True(bench.World.TrySetEquipStanding(actor.ObjectId, new PlayerEquipStanding(PvpFlagged: true)));
        Assert.Equal(InventoryProtocol.PvpRestricted, await actor.Use(actor.Slot(PeaceOnly)));
        Assert.Null(await actor.Use(actor.Slot(PvpOnly)));

        Assert.Equal(InventoryProtocol.GradeRestricted, await actor.Use(actor.Slot(ExactGrade)));
        Assert.Equal(InventoryProtocol.LevelRestricted, await actor.Use(actor.Slot(MinLevel)));
        Assert.Null(await actor.Use(actor.Slot(MaxPk)));
        Assert.Null(await actor.Use(actor.Slot(MaxPk)));
        Assert.True(bench.World.TrySetEquipStanding(actor.ObjectId,
            new PlayerEquipStanding(PvpFlagged: true, PkKills: 2)));
        Assert.Equal(InventoryProtocol.PkRestricted, await actor.Use(actor.Slot(MaxPk)));
        Assert.Equal(InventoryProtocol.ClanHallRestricted, await actor.Use(actor.Slot(Hall)));
        Assert.True(bench.World.TrySetEquipStanding(actor.ObjectId,
            new PlayerEquipStanding(InClan: true, HideoutId: 4, PvpFlagged: true)));
        Assert.Null(await actor.Use(actor.Slot(Hall)));

        foreach (int itemId in new[] { ExactGrade, MinLevel })
            AssertSavedSlot(actor, itemId, -1);
    }

    [Theory]
    [InlineData(19, 1, 0, 1)]
    [InlineData(20, 0, 0, 0)]
    [InlineData(1, 4, 2, 4)]
    [InlineData(76, 0, 0, 0)]
    public async Task Higher_grade_equips_and_UserInfo_carries_the_expertise_penalty(
        int level, int weapon, int armor, int skill)
    {
        var bench = new WorldBench(GradeCatalog());
        StoredItem[] items = level == 1
            ? [Bag(DSword), Bag(CArmor), Bag(SSword)]
            : [Bag(DSword), Bag(SSword)];
        BenchPlayer actor = await bench.EnterAsync("grade" + level, "Alden" + level, items, level: level);

        var enter = EnterSheet(bench, actor);
        Assert.Equal(0, enter.WeaponGradePenalty);
        Assert.Equal(0, enter.ArmorGradePenalty);

        int blade = level is 19 or 20 ? DSword : SSword;
        Assert.Null(await actor.Use(actor.Slot(blade)));
        var (_, user) = LastGear(bench.Rec);
        Assert.Equal(blade, user.Paperdoll[7].ItemId);
        if (level == 1)
        {
            Assert.Null(await actor.Use(actor.Slot(CArmor)));
            (_, user) = LastGear(bench.Rec);
            Assert.Equal(CArmor, user.Paperdoll[10].ItemId);
        }

        Assert.Equal(weapon, user.WeaponGradePenalty);
        Assert.Equal(armor, user.ArmorGradePenalty);
        // The V2 sheet carries the folded level; the Net EtcStatusUpdate carries only the flag, and is sent only
        // when the penalty actually moved (Mobius refreshExpertisePenalty fires on a change).
        bool moved = weapon > 0 || armor > 0;
        Assert.Equal(moved, bench.Rec.Of(nameof(INetPresenceBridge.FanoutEtcStatusUpdate)).Count > 0);
        if (moved)
            Assert.Equal(skill > 0, LastEtcStatus(bench.Rec).GradePenalty);
        AssertSavedSlot(actor, blade, 7);
    }

    [Fact]
    public async Task Arrows_do_not_add_a_penalty_and_the_config_can_turn_the_penalty_off()
    {
        var bench = new WorldBench(GradeCatalog());
        BenchPlayer actor = await bench.EnterAsync("arrow", "Alden", [Bag(Bow), Stack(SArrow, 20)]);
        Assert.Null(await actor.Use(actor.Slot(Bow)));
        Assert.Null(await actor.Use(actor.Slot(SArrow)));
        var (_, user) = LastGear(bench.Rec);
        Assert.Equal(SArrow, user.Paperdoll[8].ItemId);
        Assert.Equal(0, user.WeaponGradePenalty);
        Assert.Equal(0, user.ArmorGradePenalty);
        Assert.DoesNotContain(bench.Rec.Of(nameof(INetPresenceBridge.FanoutEtcStatusUpdate)),
            args => args[1] is EtcStatusUpdateNetMsg { GradePenalty: true });

        var quiet = new WorldBench(GradeCatalog(), expertisePenalty: false);
        BenchPlayer open = await quiet.EnterAsync("open", "Bram", [Bag(SSword)]);
        Assert.Null(await open.Use(open.Slot(SSword)));
        (_, user) = LastGear(quiet.Rec);
        Assert.Equal(SSword, user.Paperdoll[7].ItemId);
        Assert.Equal(0, user.WeaponGradePenalty);
        Assert.Equal(0, user.ArmorGradePenalty);
    }

    [Fact]
    public async Task Enter_world_reports_the_penalty_for_gear_already_worn()
    {
        var bench = new WorldBench(GradeCatalog());
        BenchPlayer actor = await bench.EnterAsync("worn", "Alden", [new StoredItem(SSword, 1, 7)]);

        var worn = EnterSheet(bench, actor);
        Assert.Equal(SSword, worn.Paperdoll[7].ItemId);
        Assert.Equal(4, worn.WeaponGradePenalty);
        Assert.Equal(0, worn.ArmorGradePenalty);
        Assert.Equal(4, bench.World.TryGetLivePlayer(actor.Session, out var live) ? live.GradePenaltyLevel : -1);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("NO", false)]
    public void Expertise_penalty_defaults_on(string? text, bool enabled)
    {
        Assert.Equal(enabled, ZoneGearSettings.ParseExpertisePenalty(text));
    }

    static UserInfoNetMsg LastUserInfoAtEnter(NetBridgeRecorder rec) =>
        Assert.IsType<UserInfoNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutUserInfo)).First()[1]);

    /// <summary>The sheet <c>NetZoneServer</c> sends on enter — built on demand, never fanned out.</summary>
    static UserInfoNetMsg EnterSheet(WorldBench bench, BenchPlayer actor)
    {
        Assert.True(bench.World.TryBuildNetUserInfo(actor.Session, out var info));
        return info;
    }

    static (InventoryUpdateNetMsg Update, UserInfoNetMsg User) LastGear(NetBridgeRecorder rec) =>
        (Assert.IsType<InventoryUpdateNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate)).Last()[1]),
         Assert.IsType<UserInfoNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutUserInfo)).Last()[1]));

    static EtcStatusUpdateNetMsg LastEtcStatus(NetBridgeRecorder rec) =>
        Assert.IsType<EtcStatusUpdateNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutEtcStatusUpdate)).Last()[1]);

    static void AssertSavedSlot(BenchPlayer actor, int itemId, int slot) =>
        Assert.Contains(actor.Saved(), item => item.ItemId == itemId && item.EquippedSlot == slot);

    static StoredItem Bag(int itemId) => new(itemId, 1, -1);

    static StoredItem Stack(int itemId, int count) => new(itemId, count, -1);

    static GameData WithClassLock()
    {
        var data = GameData.LoadDefault();
        var templates = data.Items.Templates.ToList();
        templates.Add(new ItemTemplate(
            Locked, "Class Blade", ItemKind.Weapon, BodySlot.RightHand, "icon.test", WeaponType.Sword,
            Equip: new EquipRequirement.Classes([5])));
        return GameData.From(new ItemCatalog(templates), data.StarterKits);
    }

    static GameData RuleCatalog() => GameData.From(new ItemCatalog(
    [
        new ItemTemplate(PvpOnly, "PvP", ItemKind.Weapon, BodySlot.RightHand, "icon.test", WeaponType.Sword,
            Equip: new EquipRequirement.PvpIs(true)),
        new ItemTemplate(PeaceOnly, "Peace", ItemKind.Armor, BodySlot.Head, "icon.test",
            Equip: new EquipRequirement.PvpIs(false)),
        new ItemTemplate(ExactGrade, "Exact", ItemKind.Weapon, BodySlot.RightHand, "icon.test", WeaponType.Sword,
            Equip: new EquipRequirement.ExpertiseIs(0)),
        new ItemTemplate(MinLevel, "Level", ItemKind.Armor, BodySlot.Gloves, "icon.test",
            Equip: new EquipRequirement.MinLevel(40)),
        new ItemTemplate(MaxPk, "Pk", ItemKind.Armor, BodySlot.Feet, "icon.test",
            Equip: new EquipRequirement.PkAtMost(0)),
        new ItemTemplate(Hall, "Hall", ItemKind.Armor, BodySlot.Neck, "icon.test",
            Equip: new EquipRequirement.ClanHalls([-1]))
    ]), new StarterKits([]));

    static GameData GradeCatalog() => GameData.From(new ItemCatalog(
    [
        new ItemTemplate(DSword, "D", ItemKind.Weapon, BodySlot.RightHand, "icon.test", WeaponType.Sword,
            Crystal: CrystalGrade.D),
        new ItemTemplate(CArmor, "C", ItemKind.Armor, BodySlot.Chest, "icon.test", ArmorType: ArmorType.Heavy,
            Crystal: CrystalGrade.C),
        new ItemTemplate(SSword, "S", ItemKind.Weapon, BodySlot.RightHand, "icon.test", WeaponType.Sword,
            Crystal: CrystalGrade.S),
        new ItemTemplate(Bow, "Bow", ItemKind.Weapon, BodySlot.LrHand, "icon.test", WeaponType.Bow),
        new ItemTemplate(SArrow, "Arrow", ItemKind.Etc, BodySlot.LeftHand, "icon.test", EtcType: EtcItemType.Arrow,
            Stackable: true, Crystal: CrystalGrade.S)
    ]), new StarterKits([]));
}
