using Fineage.Game;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Interest;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// A <see cref="ZoneWorld"/> with in-memory players and every <see cref="INetPresenceBridge"/> call recorded.
/// Feature rules assert on the recorded Net fanout instead of reading MessagePack frames, so a test states
/// what the client receives without dragging in the retired V2 wire.
/// </summary>
internal sealed class WorldBench
{
    public WorldBench(
        GameData? game = null,
        Func<DateTimeOffset>? clock = null,
        Fineage.Zone.Geo.GeoEngine? geo = null,
        IAoi? aoi = null,
        Random? deathRandom = null,
        Fineage.Game.DeathRuleCatalog? deathRules = null,
        Fineage.Game.Services.ItemWeights? weights = null,
        Fineage.Game.IEnchantRandom? enchantRandom = null,
        bool? expertisePenalty = null,
        Fineage.Zone.Spawns.SpawnCatalog? spawns = null,
        string? mapId = null,
        Fineage.Game.Shops.BuyListCatalog? buyLists = null,
        Fineage.Game.Shops.MultiSellCatalog? multisells = null,
        Fineage.Zone.NpcHtml.NpcHtmlCatalog? html = null,
        Fineage.Zone.NpcHtml.BypassRegistry? bypasses = null,
        Fineage.Game.Services.TeleportCatalog? teleports = null,
        int? teleportOffset = null,
        Fineage.Zone.Bosses.BossSchedule? bosses = null)
    {
        Game = game ?? GameData.LoadDefault();
        Store = JsonAccountStore.InMemory();
        Now = clock ?? (() => DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000));
        World = new ZoneWorld(
            aoi ?? new RangeAoi(1_000),
            Game,
            accounts: Store,
            clock: Now,
            geo: geo,
            deathRules: deathRules,
            deathRandom: deathRandom,
            weights: weights,
            enchantRandom: enchantRandom,
            expertisePenalty: expertisePenalty,
            spawnCatalog: spawns,
            activeMapId: mapId,
            buyLists: buyLists,
            multisells: multisells,
            html: html,
            bypasses: bypasses,
            teleports: teleports,
            teleportOffset: teleportOffset,
            bosses: bosses);
        (Bridge, Rec) = NetBridgeRecorder.Create();
        World.NetBridge = Bridge;
    }

    public GameData Game { get; }
    public JsonAccountStore Store { get; }
    public ZoneWorld World { get; }
    public INetPresenceBridge Bridge { get; }
    public NetBridgeRecorder Rec { get; }
    public Func<DateTimeOffset> Now { get; }

    int _n;

    /// <summary>Creates a character with the starter kit, optional gear override, and enters the world.</summary>
    public async Task<BenchPlayer> EnterAsync(
        string? account = null,
        string? name = null,
        StoredItem[]? items = null,
        int classId = HumanClasses.Fighter,
        int race = 0,
        int sex = 0,
        int level = 1,
        int x = 46045,
        int y = 41251,
        int z = -3440,
        bool clearRecorder = true)
    {
        _n++;
        string acct = account ?? "bench" + _n;
        string who = name ?? "Hero" + _n;

        var login = new LoginService(Store, new HandoffTokens(HandoffTokens.DefaultSecret), Now);
        if (!Store.AccountExists(acct))
            Assert.True(login.Register(acct, "pass1234").Ok);

        // Created through the store rather than the lobby on purpose: the lobby refuses a race that does not match
        // the class, but equip-condition tests need such a character to prove the item rule itself denies the equip.
        var existing = Store.FindByName(acct, who);
        int characterId;
        if (existing != null)
        {
            // Re-entry of a character that already logged out: keep whatever it persisted.
            characterId = existing.Id;
            if (items is { Length: > 0 })
                Assert.True(Store.TryReplaceItems(acct, characterId, items));
            Assert.True(Store.TrySavePresence(acct, characterId, x, y, z, null));
        }
        else
        {
            // An empty array means "start from the class kit"; a non-empty one is the whole bag.
            var kit = items is { Length: > 0 }
                ? items
                : CharacterInventory.Rows(Game.CreateStarterInventory(classId));
            var status = Store.TryAddCharacter(
                acct, who, classId, race, sex, out var created, kit,
                hasPosition: true, x: x, y: y, z: z, level: level);
            Assert.Equal(CreateCharacterStatus.Ok, status);
            characterId = created!.Id;
        }

        var play = login.Select(acct, characterId);
        Assert.True(play.Ok && play.Value != null, play.Message);
        HandoffTicket ticket = play.Value!;

        var session = new ZoneSession(new MemoryStream());
        session.Bind(ticket.CharacterId, ticket.Account, ticket.CharacterName, ticket.ClassId, ticket.Race, ticket.Sex);
        Assert.True(await World.TryEnterAsync(session, CancellationToken.None));
        Assert.True(World.TryGetLivePlayer(session, out var player));
        if (clearRecorder)
            Rec.Calls.Clear();

        return new BenchPlayer(World, Store, session, player, ticket);
    }
}

/// <summary>One entered player plus the store, for save/restore assertions.</summary>
internal sealed class BenchPlayer
{
    public BenchPlayer(ZoneWorld world, JsonAccountStore store, ZoneSession session, ZoneEntity player, HandoffTicket ticket)
    {
        World = world;
        Store = store;
        Session = session;
        Player = player;
        Ticket = ticket;
    }

    public ZoneWorld World { get; }
    public JsonAccountStore Store { get; }
    public ZoneSession Session { get; }
    public ZoneEntity Player { get; }
    public HandoffTicket Ticket { get; }

    public int ObjectId => Session.ObjectId;
    public PlayerInventory Inventory => Player.Inventory!;

    /// <summary>Object id of the single bag row holding <paramref name="itemId"/>.</summary>
    public int Slot(int itemId) => Inventory.Items.Single(item => item.ItemId == itemId).ObjectId;

    public int[] Slots(int itemId) => Inventory.Items.Where(item => item.ItemId == itemId).Select(item => item.ObjectId).ToArray();

    /// <summary>Item rows as persisted, so a test can prove the write happened before leave.</summary>
    public IReadOnlyList<StoredItem> Saved() => Store.FindById(Ticket.Account, Ticket.CharacterId)!.Items!;

    /// <summary>The whole persisted record, for progress and journal assertions.</summary>
    public CharacterRecord SavedRecord() => Store.FindById(Ticket.Account, Ticket.CharacterId)!;

    public Task<string?> Use(int objectId) =>
        World.TryUseItemAsync(Session, new UseItemMsg { ObjectId = objectId }, CancellationToken.None);

    public Task<string?> Unequip(int paperdollSlot) =>
        World.TryUnequipItemAsync(Session, new UnequipItemMsg { PaperdollSlot = paperdollSlot }, CancellationToken.None);

    /// <summary>Teleports without the walk, for tests that only care about being in talk range.</summary>
    public void MoveTo(int x, int y, int z)
    {
        Player.X = x;
        Player.Y = y;
        Player.Z = z;
        Session.X = x;
        Session.Y = y;
        Session.Z = z;
    }

    public Task<string?> Talk(int npcObjectId) =>
        World.TryNpcTalkAsync(Session, npcObjectId, CancellationToken.None);

    public Task<string?> QuestEvent(int npcObjectId, int questId, string eventName) =>
        World.TryQuestEventAsync(Session, npcObjectId, questId, eventName, CancellationToken.None);

    public Task<string?> AbortQuest(int questId) =>
        World.TryQuestAbortAsync(Session, questId, CancellationToken.None);

    public Task<string?> BypassAsync(int npcObjectId, string command) =>
        World.TryBypassAsync(Session, npcObjectId, command, CancellationToken.None);

    public Task<string?> Buy(int npcObjectId, int listId, params (int ItemId, int Count)[] rows)
    {
        var items = rows.Select(row => new ShopCountMsg { ItemId = row.ItemId, Count = row.Count }).ToArray();
        return World.TryBuyAsync(
            Session,
            new RequestBuyItemMsg { NpcObjectId = npcObjectId, ListId = listId, Items = items },
            CancellationToken.None);
    }

    public Task<string?> Sell(int npcObjectId, params (int ObjectId, int Count)[] rows)
    {
        var items = rows.Select(row => new ShopObjectCountMsg { ObjectId = row.ObjectId, Count = row.Count }).ToArray();
        return World.TrySellAsync(
            Session,
            new RequestSellItemMsg { NpcObjectId = npcObjectId, Items = items },
            CancellationToken.None);
    }

    public Task<string?> MultiSell(int npcObjectId, int listId, int entryId, int amount) =>
        World.TryMultiSellAsync(
            Session,
            new RequestMultiSellChooseMsg
            {
                NpcObjectId = npcObjectId,
                ListId = listId,
                EntryId = entryId,
                Amount = amount
            },
            CancellationToken.None);

    /// <summary>Picks a gatekeeper destination, the same call the teleport page's link lands on.</summary>
    public Task<string?> Teleport(int npcObjectId, string listName, int locationId) =>
        World.TryTeleportSelectAsync(
            Session,
            new TeleportSelectRequestMsg { NpcObjectId = npcObjectId, ListName = listName, LocationId = locationId },
            CancellationToken.None);

    public Task<string?> Deposit(int npcObjectId, params (int ObjectId, int Count)[] rows)
    {
        var lines = rows.Select(row => new WarehouseCountMsg { ObjectId = row.ObjectId, Count = row.Count }).ToArray();
        return World.TryWarehouseDepositAsync(
            Session,
            new WarehouseDepositRequestMsg { NpcObjectId = npcObjectId, Lines = lines },
            CancellationToken.None);
    }

    public Task<string?> Withdraw(int npcObjectId, params (int ObjectId, int Count)[] rows)
    {
        var lines = rows.Select(row => new WarehouseCountMsg { ObjectId = row.ObjectId, Count = row.Count }).ToArray();
        return World.TryWarehouseWithdrawAsync(
            Session,
            new WarehouseWithdrawRequestMsg { NpcObjectId = npcObjectId, Lines = lines },
            CancellationToken.None);
    }

    public Task<string?> SupportMagic(int npcObjectId) =>
        World.TrySupportMagicAsync(Session, npcObjectId, CancellationToken.None);

    public Task<string?> GiveBlessing(int npcObjectId) =>
        World.TryGiveBlessingAsync(Session, npcObjectId, CancellationToken.None);
}
