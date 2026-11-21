namespace Fineage.Game.Economy;

public enum LedgerOwnerKind : byte
{
    System = 0,
    Character = 1,
    Castle = 2,

    /// <summary>The world itself: what lies on the ground.</summary>
    World = 3,

    /// <summary>A pet, by the object id of its control item, which outlives every summoning.</summary>
    Pet = 4,

    Clan = 5
}

public enum LedgerContainer : byte
{
    System = 0,
    Inventory = 1,
    Treasury = 2,

    /// <summary>One stack on the ground, by its object id.</summary>
    Ground = 3,

    /// <summary>
    /// A private or clan warehouse. Warehouse stacks have no durable piece id yet, so the ledger holds them as a
    /// count per item: a piece deposited leaves its instance id behind and a piece withdrawn gets a new one.
    /// </summary>
    Warehouse = 4,

    /// <summary>Freight packages addressed to this character (Mobius <c>ItemLocation.FREIGHT</c>).</summary>
    Freight = 5
}

/// <summary>Sources and sinks. Their balances may go negative: a negative balance is what they minted.</summary>
public enum SystemAccount
{
    NpcShop = 1,

    /// <summary>
    /// Counterpart of <see cref="LedgerTxKind.Adjustment"/>: value that reached or left an account through a move the
    /// ledger does not record yet. Its postings are the audit trail of the moves still to migrate.
    /// </summary>
    Untracked = 2,

    /// <summary>Monster loot comes from here.</summary>
    DropSource = 3,

    /// <summary>Items destroyed from a bag, ground stacks that expired, herbs used on pickup, party adena remainders.</summary>
    Destroyed = 4,

    /// <summary>Service fees paid to NPCs: the warehouse deposit fee.</summary>
    Fees = 5,

    /// <summary>Counterpart of quest rewards, quest items handed out and taken back, and the newbie helper gift.</summary>
    Quests = 6,

    /// <summary>Takes crafting materials and hands out what was made.</summary>
    Crafting = 7,

    /// <summary>Door, wall and other castle upgrades paid from a lord's bag.</summary>
    CastleMaintenance = 8,

    /// <summary>Manor seed sales and crop procurements.</summary>
    Manor = 9,

    /// <summary>Circlets and crowns granted to castle members.</summary>
    CastleGrants = 10,

    /// <summary>Lottery ticket sales and jackpot payouts.</summary>
    Lottery = 11,

    /// <summary>Seven Signs fees, contributions and period rewards.</summary>
    SevenSigns = 12,

    /// <summary>Olympiad gate passes and noble grants.</summary>
    Olympiad = 13,

    /// <summary>Fishing and other non-combat gathering.</summary>
    Gathering = 14,

    /// <summary>Character creation starter kits.</summary>
    StarterKit = 15,

    /// <summary>GM or admin item grants into a player's bag.</summary>
    GmGrants = 16,

    /// <summary>Potions, shots, arrows, skill costs and other item consumption.</summary>
    Consumed = 17,

    /// <summary>Monster race ticket sales and payouts.</summary>
    MonsterRace = 18,

    /// <summary>Clan hall function leases and hall rent.</summary>
    ClanHall = 19
}

public readonly record struct LedgerAccount(LedgerOwnerKind Owner, int OwnerId, LedgerContainer Container)
    : IComparable<LedgerAccount>
{
    public static LedgerAccount Of(SystemAccount account) =>
        new(LedgerOwnerKind.System, (int)account, LedgerContainer.System);

    public static LedgerAccount Inventory(int characterId) =>
        new(LedgerOwnerKind.Character, characterId, LedgerContainer.Inventory);

    public static LedgerAccount Treasury(int castleId) =>
        new(LedgerOwnerKind.Castle, castleId, LedgerContainer.Treasury);

    public static LedgerAccount Ground(int groundObjectId) =>
        new(LedgerOwnerKind.World, groundObjectId, LedgerContainer.Ground);

    public static LedgerAccount Pet(int controlObjectId) =>
        new(LedgerOwnerKind.Pet, controlObjectId, LedgerContainer.Inventory);

    public static LedgerAccount Warehouse(int characterId) =>
        new(LedgerOwnerKind.Character, characterId, LedgerContainer.Warehouse);

    public static LedgerAccount ClanWarehouse(int clanId) =>
        new(LedgerOwnerKind.Clan, clanId, LedgerContainer.Warehouse);

    public static LedgerAccount Freight(int characterId) =>
        new(LedgerOwnerKind.Character, characterId, LedgerContainer.Freight);

    public bool IsSystem => Owner == LedgerOwnerKind.System;

    public int CompareTo(LedgerAccount other)
    {
        int byOwner = Owner.CompareTo(other.Owner);
        if (byOwner != 0)
            return byOwner;
        int byId = OwnerId.CompareTo(other.OwnerId);
        return byId != 0 ? byId : Container.CompareTo(other.Container);
    }

    public override string ToString() => IsSystem
        ? $"system:{(SystemAccount)OwnerId}"
        : $"{Owner.ToString().ToLowerInvariant()}:{OwnerId}/{Container.ToString().ToLowerInvariant()}";
}

public enum LedgerTxKind
{
    Adjustment = 1,
    ShopBuy = 2,
    ShopSell = 3,
    ShopExchange = 4,
    TradeSettle = 5,

    /// <summary>Monster loot onto the ground: <c>CausedBy</c> the killer, <c>Reference</c> the npc id.</summary>
    Loot = 6,

    /// <summary>A player drops from the bag onto the ground.</summary>
    Drop = 7,

    /// <summary>A player's items fall on death: <c>Reference</c> is the killer.</summary>
    DeathDrop = 8,

    /// <summary>A ground stack is taken: <c>Reference</c> is the stack's object id.</summary>
    Pickup = 9,

    /// <summary>A player destroys an item from the bag.</summary>
    Destroy = 10,

    /// <summary>A ground stack expires: <c>Reference</c> is the stack's object id.</summary>
    Expire = 11,

    /// <summary>Bag into a private or clan warehouse, fee included: <c>Reference</c> is the warehouse keeper's npc id.</summary>
    WarehouseDeposit = 12,

    /// <summary>Private or clan warehouse into the bag: <c>Reference</c> is the warehouse keeper's npc id.</summary>
    WarehouseWithdraw = 13,

    /// <summary>
    /// A sale at a private store, either way: <c>CausedBy</c> the visitor who bought or sold, <c>Reference</c> the
    /// store's owner.
    /// </summary>
    StoreSale = 14,

    /// <summary>What a quest step gave and took: <c>Reference</c> is the quest id (0 when a kill moved several).</summary>
    QuestReward = 15,

    /// <summary>
    /// Materials into crafting and the product out, in one move whether the roll succeeded or not; a manufacture
    /// also pays the maker's fee. <c>CausedBy</c> is the customer, <c>Reference</c> the recipe id.
    /// </summary>
    Craft = 16,

    /// <summary>Adena from a character's bag into a castle treasury: <c>Reference</c> is the castle id.</summary>
    TreasuryDeposit = 17,

    /// <summary>Adena from a castle treasury into a character's bag: <c>Reference</c> is the castle id.</summary>
    TreasuryWithdraw = 18,

    /// <summary>A castle door or wall upgrade: <c>Reference</c> is the door id.</summary>
    CastleUpgrade = 19,

    /// <summary>A circlet or crown item granted to a castle member: <c>Reference</c> is the castle id.</summary>
    CastleGrant = 20,

    /// <summary>Seeds bought from a manor manager: <c>Reference</c> is the castle id.</summary>
    ManorSeedBuy = 21,

    /// <summary>
    /// Crop procure revenue credited to the castle treasury at period close: <c>Reference</c> is the castle id;
    /// <c>CausedBy</c> is 0.
    /// </summary>
    ManorCropProcure = 22,

    /// <summary>Manor period approval cost drawn from the treasury: <c>Reference</c> is the castle id; <c>CausedBy</c> is 0.</summary>
    ManorPeriodCost = 23,

    /// <summary>A lottery ticket purchase: <c>Reference</c> is the draw number.</summary>
    LotteryBuy = 24,

    /// <summary>A claimed lottery prize: <c>Reference</c> is the ticket serial.</summary>
    LotteryPrize = 25,

    /// <summary>Seven Signs cabal registration fee.</summary>
    SevenSignsJoin = 26,

    /// <summary>Seal stones contributed to the cabal score.</summary>
    SevenSignsContribute = 27,

    /// <summary>Weekly Seven Signs ancient adena reward.</summary>
    SevenSignsReward = 28,

    /// <summary>Seal stones traded for ancient adena at a priest.</summary>
    SevenSignsExchange = 29,

    /// <summary>Olympiad noble gate passes or board grants: <c>Reference</c> is item id.</summary>
    OlympiadGrant = 30,

    /// <summary>A fish or other gather reward: <c>Reference</c> is item id.</summary>
    FishingReward = 31,

    /// <summary>Spoil sweep loot into a bag: <c>Reference</c> is the npc template id.</summary>
    SweepLoot = 32,

    /// <summary>Enchant scroll used and item changed or crystallized: <c>Reference</c> is scroll item id.</summary>
    Enchant = 33,

    /// <summary>Pet food consumed from the pet's bag.</summary>
    PetFeed = 34,

    /// <summary>Items moved between owner inventory and pet inventory: <c>Reference</c> is control item id.</summary>
    PetItemTransfer = 35,

    /// <summary>Gatekeeper or teleporter fare: <c>Reference</c> is the destination id.</summary>
    TeleportFee = 36,

    /// <summary>Dungeon entrance fare: <c>Reference</c> is the dungeon id.</summary>
    DungeonEntryFee = 37,

    /// <summary>Clan level increase costs: <c>Reference</c> is the new clan level.</summary>
    ClanLevelUp = 38,

    /// <summary>Clan skill acquisition item costs: <c>Reference</c> is skill id.</summary>
    ClanSkillAcquire = 39,

    /// <summary>Trainer skill books consumed at learn time: <c>Reference</c> is skill id.</summary>
    TrainerSkillLearn = 40,

    /// <summary>Henna draw fee and dye consumption: <c>Reference</c> is dye id.</summary>
    HennaDraw = 41,

    /// <summary>Henna removal fee and dye return: <c>Reference</c> is dye id.</summary>
    HennaRemove = 42,

    /// <summary>Class transfer proof items: <c>Reference</c> is new class id.</summary>
    ClassChange = 43,

    /// <summary>Starter kit granted on first enter: <c>Reference</c> is class id.</summary>
    StarterKit = 44,

    /// <summary>Admin grant into the bag: <c>Reference</c> is item id.</summary>
    GmGrant = 45,

    /// <summary>Item consumed from a bag: <c>Reference</c> is item id (or skill id for skill costs).</summary>
    Consume = 46,

    /// <summary>Bag into another character's freight on the same account: <c>Reference</c> is the recipient's character id.</summary>
    FreightDeposit = 47,

    /// <summary>Freight into the bag at any warehouse keeper: <c>Reference</c> is the keeper's npc id.</summary>
    FreightWithdraw = 48,

    /// <summary>Monster race ticket purchase: <c>Reference</c> is race number.</summary>
    MonsterRaceBet = 49,

    /// <summary>Monster race ticket payout: <c>Reference</c> is ticket object id.</summary>
    MonsterRacePayout = 50,

    /// <summary>Wedding ceremony fee at Andromeda: <c>Reference</c> is couple id.</summary>
    WeddingCeremonyFee = 51,

    /// <summary><c>.gotolove</c> teleport fee: <c>Reference</c> is partner object id.</summary>
    WeddingTeleportFee = 52,

    /// <summary>Married divorce adena transfer: payer inventory → partner inventory.</summary>
    WeddingDivorcePenalty = 53,

    /// <summary>Clan hall function lease: <c>Reference</c> is function type.</summary>
    ClanHallFunction = 54,

    /// <summary>Auctionable hall rent: <c>Reference</c> is hall id.</summary>
    ClanHallLease = 55
}

/// <summary>
/// Idempotency key: the tick the move happened at and its order among the tick's moves. Tick numbers never repeat
/// across restarts (fixed steps since the Unix epoch), so an id names one move for good.
/// </summary>
public readonly record struct LedgerTxId(long Tick, int Sequence) : IComparable<LedgerTxId>
{
    public int CompareTo(LedgerTxId other)
    {
        int byTick = Tick.CompareTo(other.Tick);
        return byTick != 0 ? byTick : Sequence.CompareTo(other.Sequence);
    }

    public override string ToString() => $"{Tick}.{Sequence}";
}

/// <summary>
/// One side of a move. <see cref="InstanceIds"/> names the pieces of a non-stackable item that move, one per unit,
/// or is empty for stackable value.
/// </summary>
public sealed record LedgerPosting(LedgerAccount Account, int ItemId, long Amount, IReadOnlyList<int> InstanceIds)
{
    public LedgerPosting(LedgerAccount account, int itemId, long amount)
        : this(account, itemId, amount, [])
    {
    }
}

/// <summary>
/// A move between owners. <see cref="CausedBy"/> is the character that caused it (0 for none; a trade's requestor);
/// <see cref="Reference"/> names what it went through: the NPC template for shop moves, the partner for trades, 0 for
/// adjustments.
/// </summary>
public sealed record LedgerTransaction(
    LedgerTxId Id,
    LedgerTxKind Kind,
    int CausedBy,
    int Reference,
    long WallUnixMs,
    IReadOnlyList<LedgerPosting> Postings);

public enum LedgerOutcome
{
    Committed,
    Duplicate,
    Rejected
}

public enum LedgerRejection
{
    None,
    Empty,
    BadPosting,
    Unbalanced,
    Overdraft,
    InstanceMismatch,
    InstanceNotOwned,
    InstanceTaken,
    Conflict,
    Stale
}

public readonly record struct LedgerResult(LedgerTxId Id, LedgerOutcome Outcome, LedgerRejection Rejection, long Position)
{
    public bool Ok => Outcome != LedgerOutcome.Rejected;

    public static LedgerResult Reject(LedgerTxId id, LedgerRejection rejection) =>
        new(id, LedgerOutcome.Rejected, rejection, 0);
}

/// <summary>A committed transaction at its journal position, chained to the previous entry by <see cref="Hash"/>.</summary>
public sealed record LedgerEntry(long Position, LedgerTransaction Transaction, string Hash);
