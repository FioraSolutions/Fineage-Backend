using Fineage.Game;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Interest;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Class, race, sex, hero, clan, and expertise-penalty equip rules on the zone.</summary>
public sealed class ZoneEquipRestrictionTests
{
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
        var (store, world, tokens) = Open();
        await using var human = await EnterAsync(store, world, tokens, "race", "Alden", items: [Bag(425), Bag(7682), Bag(8923)]);
        Assert.Null(await human.Use(425));
        var (_, user) = await human.ExpectGearAsync();
        Assert.Equal(425, user.Paperdoll[10].ItemId);

        Assert.Null(await human.Use(8923));
        (_, user) = await human.ExpectGearAsync();
        Assert.Equal(8923, user.Paperdoll[15].ItemId);

        Assert.Equal(InventoryProtocol.SexRestricted, await human.Use(7682));
        await human.ExpectQuietAsync();
        AssertSlot(store, human, 7682, -1);

        await using var outsider = await EnterAsync(store, world, tokens, "raceb", "Bram", race: 99, items: [Bag(425)]);
        Assert.Equal(InventoryProtocol.RaceRestricted, await outsider.Use(425));
        await outsider.ExpectQuietAsync();
        AssertSlot(store, outsider, 425, -1);

        await using var woman = await EnterAsync(store, world, tokens, "racec", "Cira", sex: 1, items: [Bag(7682), Bag(8923)]);
        Assert.Null(await woman.Use(7682));
        (_, user) = await woman.ExpectGearAsync();
        Assert.Equal(7682, user.Paperdoll[16].ItemId);
        Assert.Equal(InventoryProtocol.SexRestricted, await woman.Use(8923));
        AssertSlot(store, woman, 8923, -1);
    }

    [Fact]
    public async Task Hero_clan_castle_and_class_conditions_reject_without_equipping()
    {
        var (store, world, tokens) = Open(WithClassLock());
        await using var actor = await EnterAsync(store, world, tokens, "hero", "Alden", items:
        [
            Bag(6611), Bag(7850), Bag(7860), Bag(6834), Bag(6841), Bag(Locked)
        ]);

        Assert.Equal(InventoryProtocol.HeroRestricted, await actor.Use(6611));
        Assert.Equal(InventoryProtocol.PledgeRestricted, await actor.Use(7850));
        Assert.Equal(InventoryProtocol.CastleRestricted, await actor.Use(6834));
        Assert.Equal(InventoryProtocol.CastleRestricted, await actor.Use(6841));
        Assert.Equal(InventoryProtocol.ClassRestricted, await actor.Use(Locked));
        await actor.ExpectQuietAsync();
        foreach (int itemId in new[] { 6611, 7850, 7860, 6834, 6841, Locked })
            AssertSlot(store, actor, itemId, -1);

        Assert.True(world.TrySetEquipStanding(actor.Ticket.CharacterId, new PlayerEquipStanding(Hero: true)));
        Assert.Null(await actor.Use(6611));
        var (_, user) = await actor.ExpectGearAsync();
        Assert.Equal(6611, user.Paperdoll[7].ItemId);
        Assert.Equal(4, user.WeaponGradePenalty);
        Assert.Equal(0, user.ArmorGradePenalty);
        Assert.Equal(4, user.GradePenalty);

        Assert.True(world.TrySetEquipStanding(actor.Ticket.CharacterId, new PlayerEquipStanding(
            Hero: true, InClan: true, PledgeClass: 1, CastleId: 6)));
        Assert.Null(await actor.Use(7850));
        (_, user) = await actor.ExpectGearAsync();
        Assert.Equal(7850, user.Paperdoll[6].ItemId);
        Assert.Equal(InventoryProtocol.PledgeRestricted, await actor.Use(7860));
        Assert.Equal(InventoryProtocol.PledgeRestricted, await actor.Use(6834));

        Assert.True(world.TrySetEquipStanding(actor.Ticket.CharacterId, new PlayerEquipStanding(
            Hero: true, InClan: true, PledgeClass: 2, CastleId: 6)));
        Assert.Null(await actor.Use(6834));
        (_, user) = await actor.ExpectGearAsync();
        Assert.Equal(6834, user.Paperdoll[15].ItemId);

        Assert.True(world.TrySetEquipStanding(actor.Ticket.CharacterId, new PlayerEquipStanding(
            Hero: true, InClan: true, PledgeClass: 4, CastleId: 6)));
        Assert.Null(await actor.Use(7860));
        (_, user) = await actor.ExpectGearAsync();
        Assert.Equal(7860, user.Paperdoll[6].ItemId);

        Assert.True(world.TrySetEquipStanding(actor.Ticket.CharacterId, new PlayerEquipStanding(
            Hero: true, InClan: true, ClanLeader: true, CastleId: 3)));
        Assert.Null(await actor.Use(6841));
        (_, user) = await actor.ExpectGearAsync();
        Assert.Equal(6841, user.Paperdoll[15].ItemId);

        Assert.True(world.TrySetEquipStanding(actor.Ticket.CharacterId, new PlayerEquipStanding(Hero: false)));
        Assert.Null(await actor.Use(6611));
        (_, user) = await actor.ExpectGearAsync();
        Assert.Equal(0, user.Paperdoll[7].ItemId);
    }

    [Fact]
    public async Task Pvp_level_pk_clan_hall_and_exact_expertise_reject_without_equipping()
    {
        var (store, world, tokens) = Open(RuleCatalog());
        await using var actor = await EnterAsync(store, world, tokens, "flags", "Alden", level: 30, items:
        [
            Bag(PvpOnly), Bag(PeaceOnly), Bag(ExactGrade), Bag(MinLevel), Bag(MaxPk), Bag(Hall)
        ]);

        Assert.Equal(InventoryProtocol.PvpRestricted, await actor.Use(PvpOnly));
        Assert.Null(await actor.Use(PeaceOnly));
        await actor.ExpectGearAsync();
        Assert.Null(await actor.Use(PeaceOnly));
        await actor.ExpectGearAsync();
        Assert.True(world.TrySetEquipStanding(actor.Ticket.CharacterId, new PlayerEquipStanding(PvpFlagged: true)));
        Assert.Equal(InventoryProtocol.PvpRestricted, await actor.Use(PeaceOnly));
        Assert.Null(await actor.Use(PvpOnly));
        await actor.ExpectGearAsync();

        Assert.Equal(InventoryProtocol.GradeRestricted, await actor.Use(ExactGrade));
        Assert.Equal(InventoryProtocol.LevelRestricted, await actor.Use(MinLevel));
        Assert.Null(await actor.Use(MaxPk));
        await actor.ExpectGearAsync();
        Assert.Null(await actor.Use(MaxPk));
        await actor.ExpectGearAsync();
        Assert.True(world.TrySetEquipStanding(actor.Ticket.CharacterId, new PlayerEquipStanding(PvpFlagged: true, PkKills: 2)));
        Assert.Equal(InventoryProtocol.PkRestricted, await actor.Use(MaxPk));
        Assert.Equal(InventoryProtocol.ClanHallRestricted, await actor.Use(Hall));
        Assert.True(world.TrySetEquipStanding(actor.Ticket.CharacterId, new PlayerEquipStanding(InClan: true, HideoutId: 4, PvpFlagged: true)));
        Assert.Null(await actor.Use(Hall));
        await actor.ExpectGearAsync();

        foreach (int itemId in new[] { ExactGrade, MinLevel })
            AssertSlot(store, actor, itemId, -1);
    }

    [Theory]
    [InlineData(19, 1, 0, 1)]
    [InlineData(20, 0, 0, 0)]
    [InlineData(1, 4, 2, 4)]
    [InlineData(76, 0, 0, 0)]
    public async Task Higher_grade_equips_and_UserInfo_carries_the_expertise_penalty(int level, int weapon, int armor, int skill)
    {
        var (store, world, tokens) = Open(GradeCatalog());
        var items = level == 1
            ? new[] { Bag(DSword), Bag(CArmor), Bag(SSword) }
            : new[] { Bag(DSword), Bag(SSword) };
        await using var actor = await EnterAsync(store, world, tokens, "grade" + level, "Alden" + level, level: level, items: items);
        Assert.Equal(0, actor.User.WeaponGradePenalty);
        Assert.Equal(0, actor.User.GradePenalty);

        int blade = level is 19 or 20 ? DSword : SSword;
        Assert.Null(await actor.Use(blade));
        var (_, user) = await actor.ExpectGearAsync();
        Assert.Equal(blade, user.Paperdoll[7].ItemId);
        if (level == 1)
        {
            Assert.Null(await actor.Use(CArmor));
            (_, user) = await actor.ExpectGearAsync();
            Assert.Equal(CArmor, user.Paperdoll[10].ItemId);
        }

        Assert.Equal(weapon, user.WeaponGradePenalty);
        Assert.Equal(armor, user.ArmorGradePenalty);
        Assert.Equal(skill, user.GradePenalty);
        AssertSlot(store, actor, blade, 7);
    }

    [Fact]
    public async Task Arrows_do_not_add_a_penalty_and_the_config_can_turn_the_penalty_off()
    {
        var (store, world, tokens) = Open(GradeCatalog());
        await using var actor = await EnterAsync(store, world, tokens, "arrow", "Alden", items: [Bag(Bow), Stack(SArrow, 20)]);
        Assert.Null(await actor.Use(Bow));
        await actor.ExpectGearAsync();
        Assert.Null(await actor.Use(SArrow));
        var (_, user) = await actor.ExpectGearAsync();
        Assert.Equal(SArrow, user.Paperdoll[8].ItemId);
        Assert.Equal(0, user.WeaponGradePenalty);
        Assert.Equal(0, user.ArmorGradePenalty);
        Assert.Equal(0, user.GradePenalty);

        var (quietStore, quietWorld, quietTokens) = Open(GradeCatalog(), expertisePenalty: false);
        await using var open = await EnterAsync(quietStore, quietWorld, quietTokens, "open", "Bram", items: [Bag(SSword)]);
        Assert.Null(await open.Use(SSword));
        (_, user) = await open.ExpectGearAsync();
        Assert.Equal(SSword, user.Paperdoll[7].ItemId);
        Assert.Equal(0, user.WeaponGradePenalty);
        Assert.Equal(0, user.ArmorGradePenalty);
        Assert.Equal(0, user.GradePenalty);
    }

    [Fact]
    public async Task Enter_world_reports_the_penalty_for_gear_already_worn()
    {
        var (store, world, tokens) = Open(GradeCatalog());
        await using var actor = await EnterAsync(store, world, tokens, "worn", "Alden", items: [new StoredItem(SSword, 1, 7)]);
        Assert.Equal(SSword, actor.User.Paperdoll[7].ItemId);
        Assert.Equal(4, actor.User.WeaponGradePenalty);
        Assert.Equal(0, actor.User.ArmorGradePenalty);
        Assert.Equal(4, actor.User.GradePenalty);
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
        new ItemTemplate(PvpOnly, "PvP", ItemKind.Weapon, BodySlot.RightHand, "icon.test", WeaponType.Sword, Equip: new EquipRequirement.PvpIs(true)),
        new ItemTemplate(PeaceOnly, "Peace", ItemKind.Armor, BodySlot.Head, "icon.test", Equip: new EquipRequirement.PvpIs(false)),
        new ItemTemplate(ExactGrade, "Exact", ItemKind.Weapon, BodySlot.RightHand, "icon.test", WeaponType.Sword, Equip: new EquipRequirement.ExpertiseIs(0)),
        new ItemTemplate(MinLevel, "Level", ItemKind.Armor, BodySlot.Gloves, "icon.test", Equip: new EquipRequirement.MinLevel(40)),
        new ItemTemplate(MaxPk, "Pk", ItemKind.Armor, BodySlot.Feet, "icon.test", Equip: new EquipRequirement.PkAtMost(0)),
        new ItemTemplate(Hall, "Hall", ItemKind.Armor, BodySlot.Neck, "icon.test", Equip: new EquipRequirement.ClanHalls([-1]))
    ]), new StarterKits([]));

    static GameData GradeCatalog() => GameData.From(new ItemCatalog(
    [
        new ItemTemplate(DSword, "D", ItemKind.Weapon, BodySlot.RightHand, "icon.test", WeaponType.Sword, Crystal: CrystalGrade.D),
        new ItemTemplate(CArmor, "C", ItemKind.Armor, BodySlot.Chest, "icon.test", ArmorType: ArmorType.Heavy, Crystal: CrystalGrade.C),
        new ItemTemplate(SSword, "S", ItemKind.Weapon, BodySlot.RightHand, "icon.test", WeaponType.Sword, Crystal: CrystalGrade.S),
        new ItemTemplate(Bow, "Bow", ItemKind.Weapon, BodySlot.LrHand, "icon.test", WeaponType.Bow),
        new ItemTemplate(SArrow, "Arrow", ItemKind.Etc, BodySlot.LeftHand, "icon.test", EtcType: EtcItemType.Arrow, Stackable: true, Crystal: CrystalGrade.S)
    ]), new StarterKits([]));

    static (JsonAccountStore Store, ZoneWorld World, HandoffTokens Tokens) Open(GameData? game = null, bool? expertisePenalty = null)
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var store = JsonAccountStore.InMemory();
        var world = new ZoneWorld(new RangeAoi(1_000), game, accounts: store, expertisePenalty: expertisePenalty);
        return (store, world, tokens);
    }

    static async Task<GearSession> EnterAsync(
        JsonAccountStore store,
        ZoneWorld world,
        HandoffTokens tokens,
        string account,
        string name,
        StoredItem[] items,
        int level = 1,
        int race = 0,
        int sex = 0)
    {
        var (hash, salt, iterations) = PasswordHasher.Hash("pass1234");
        Assert.Equal(AddAccountStatus.Created, store.TryAddAccount(account, hash, salt, iterations, null));
        var status = store.TryAddCharacter(
            account, name, HumanClasses.Fighter, race, sex, out var created, items,
            level: level, hasPosition: true);
        Assert.Equal(CreateCharacterStatus.Ok, status);
        var ticket = tokens.Issue(created!, DateTimeOffset.UtcNow);

        var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.CharacterId, ticket.Account, ticket.CharacterName, ticket.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        stream.Position = 0;
        UserInfoMsg? user = null;
        ItemListMsg? list = null;
        while (true)
        {
            var frame = await FrameIO.ReadAsync(stream);
            if (frame == null)
                break;
            if (frame.Value.Type == (ushort)MsgType.UserInfo)
                user = MessagePackSerializer.Deserialize<UserInfoMsg>(frame.Value.Body);
            else if (frame.Value.Type == (ushort)MsgType.ItemList)
                list = MessagePackSerializer.Deserialize<ItemListMsg>(frame.Value.Body);
        }

        Assert.NotNull(user);
        Assert.NotNull(list);
        stream.SetLength(0);
        return new GearSession(world, store, session, stream, ticket, user, list);
    }

    static StoredItem Bag(int itemId) => new(itemId, 1, -1);

    static StoredItem Stack(int itemId, int count) => new(itemId, count, -1);

    static void AssertSlot(JsonAccountStore store, GearSession actor, int itemId, int slot)
    {
        var saved = store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId);
        Assert.Contains(saved!.Items!, item => item.ItemId == itemId && item.EquippedSlot == slot);
    }

    sealed class GearSession : IAsyncDisposable
    {
        public GearSession(ZoneWorld world, JsonAccountStore store, ZoneSession session, MemoryStream stream, HandoffTicket ticket, UserInfoMsg user, ItemListMsg items)
        {
            World = world;
            Store = store;
            Session = session;
            Stream = stream;
            Ticket = ticket;
            User = user;
            Items = items;
        }

        public ZoneWorld World { get; }
        public JsonAccountStore Store { get; }
        public ZoneSession Session { get; }
        public MemoryStream Stream { get; }
        public HandoffTicket Ticket { get; }
        public UserInfoMsg User { get; }
        public ItemListMsg Items { get; }

        public int ObjectId(int itemId) => Items.Items.Single(item => item.ItemId == itemId).ObjectId;

        public Task<string?> Use(int itemId)
        {
            Stream.SetLength(0);
            Stream.Position = 0;
            return World.TryUseItemAsync(Session, new UseItemMsg { ObjectId = ObjectId(itemId) }, CancellationToken.None);
        }

        public async Task<(InventoryUpdateMsg Update, UserInfoMsg User)> ExpectGearAsync()
        {
            Stream.Position = 0;
            var frames = new List<(ushort Type, byte[] Body)>();
            while (true)
            {
                var frame = await FrameIO.ReadAsync(Stream);
                if (frame == null)
                    break;
                frames.Add(frame.Value);
            }

            Assert.Equal(2, frames.Count);
            return (
                MessagePackSerializer.Deserialize<InventoryUpdateMsg>(frames[0].Body),
                MessagePackSerializer.Deserialize<UserInfoMsg>(frames[1].Body));
        }

        public async Task ExpectQuietAsync()
        {
            Stream.Position = 0;
            Assert.Null(await FrameIO.ReadAsync(Stream));
        }

        public async ValueTask DisposeAsync()
        {
            await World.LeaveAsync(Session, CancellationToken.None);
            await Stream.DisposeAsync();
        }
    }
}
