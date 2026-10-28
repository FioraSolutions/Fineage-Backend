using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Combat;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Zone.Drops;
using Fineage.Zone.Bosses;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Dungeons;
using Fineage.Zone.Siege;
using MessagePack;

namespace Fineage.Zone.World;

public enum ZoneEntityKind
{
    Npc = 0,
    GroundItem = 1,
    Player = 2
}

sealed class ZoneEntity
{
    public int ObjectId { get; init; }
    public ZoneEntityKind Kind { get; init; }
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>Mobius instance type (Merchant, Guard, Monster, …).</summary>
    public string NpcType { get; init; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Heading { get; init; }
    public int ClassId { get; init; }
    public int NpcId { get; init; }
    public bool Attackable { get; init; }
    public int CurHp { get; set; }
    public int MaxHp { get; set; }
    public int CurMp { get; set; }
    public int MaxMp { get; set; }
    public int Level { get; set; } = 1;
    public long Exp { get; set; }
    public int Sp { get; set; }
    public QuestJournal Quests { get; set; } = new();
    public int Race { get; init; }
    public int Sex { get; init; }
    public bool Hero { get; set; }
    public bool InClan { get; set; }
    public bool ClanLeader { get; set; }
    public int PledgeClass { get; set; }
    public int CastleId { get; set; }
    public int HideoutId { get; set; }
    public bool PvpFlagged { get; set; }
    public int PkKills { get; set; }
    public int ExpertiseBonus { get; set; }
    /// <summary>Weapon expertise penalty, 0–4. Updated when gear changes and on enter.</summary>
    public int WeaponGradePenalty { get; set; }
    /// <summary>Armor expertise penalty, 0–4. Updated when gear changes and on enter.</summary>
    public int ArmorGradePenalty { get; set; }
    /// <summary>When true, regen rates double (classic sitting rest).</summary>
    public bool Sitting { get; set; }
    public float RunSpeed { get; init; }
    public float WalkSpeed { get; init; }
    public bool Running { get; set; } = true;
    public ZoneSession? Session { get; init; }
    public PlayerInventory? Inventory { get; init; }
    /// <summary>
    /// Player is dead. Equip and unequip are rejected. Combat sets this; spawn leaves it false.
    /// CurHp is not the signal: a living player can sit at low HP while regen heals them.
    /// </summary>
    public bool Dead { get; set; }
    /// <summary>Scroll object id selected for the enchant window. 0 when the window is closed.</summary>
    public int ActiveEnchantObjectId { get; set; }
    public int GroundItemId { get; set; }
    public int GroundCount { get; set; } = 1;
    public bool GroundStackable { get; set; }
    /// <summary>Mobius spoil: who activated spoil on this attackable (0 = none).</summary>
    public int SpoilerObjectId { get; set; }
    /// <summary>Pre-rolled corpse drops after death when spoiled; cleared by sweeper.</summary>
    public List<Drops.DropStack> SweepItems { get; } = [];
    /// <summary>Dead NPC kept in world until sweeper takes <see cref="SweepItems"/>.</summary>
    public bool CorpseAwaitingSweep { get; set; }
    public List<ZoneAbnormalEntry> Abnormals { get; } = [];
    /// <summary>Known skill id to level (auto-get tree on enter).</summary>
    public Dictionary<int, int> KnownSkills { get; } = [];
    /// <summary>Skill id to unix-ms when reuse expires.</summary>
    public Dictionary<int, long> SkillReuseUntilMs { get; } = [];
    public HashSet<int> Knownlist { get; } = [];
    public int ClanId { get; set; }
    /// <summary>Attack stance end, unix milliseconds. Leave and dismiss are refused until this passes.</summary>
    public long CombatUntilUnixMs { get; set; }
    /// <summary>Catacomb or necropolis id while inside a Seven Signs gate. 0 is outside. Runtime only, same as Mobius <c>isIn7sDungeon</c>.</summary>
    public int DungeonId { get; set; }
}

/// <summary>
/// One zone. Players and NPCs are shared entities. Each player keeps a knownlist
/// of what their client has been told; AOI decides membership.
/// </summary>
public sealed partial class ZoneWorld
{
    public const int MaxSayLength = ChatProtocol.MaxTextLength;
    static readonly TimeSpan FanoutTimeout = TimeSpan.FromSeconds(2);

    readonly IAoi _aoi;
    readonly GameData _game;
    readonly ItemObjectIdAllocator _itemIds;
    readonly Random _spawnRandom;
    readonly Random _dropRandom;
    readonly IAccountStore? _accounts;
    readonly bool _expertisePenalty;
    readonly IEnchantRandom _enchantRandom;
    readonly EnchantRates _enchantRates;
    readonly QuestMachine _quests;
    readonly SevenSignsEngine _sevenSigns;
    readonly DungeonCatalog _dungeons;
    readonly DungeonAccess _dungeonAccess;
    readonly SiegeSettings _siegeSettings;
    readonly SiegeBoard _siege;
    readonly NpcHtmlCatalog _html;
    readonly BypassRegistry _bypasses;
    readonly Dictionary<int, ZoneEntity> _byId = [];
    readonly DropCatalog _dropCatalog;
    readonly DropRates _dropRates;
    readonly object _gate = new();

    public ZoneWorld(
        IAoi? aoi = null,
        GameData? game = null,
        ItemObjectIdAllocator? itemIds = null,
        Random? spawnRandom = null,
        Random? dropRandom = null,
        IAccountStore? accounts = null,
        bool? expertisePenalty = null,
        SpawnCatalog? spawnCatalog = null,
        DropCatalog? dropCatalog = null,
        DropRates? dropRates = null,
        string? activeMapId = null,
        Func<DateTimeOffset>? clock = null,
        IEnchantRandom? enchantRandom = null,
        EnchantRates? enchantRates = null,
        SevenSignsEngine? sevenSigns = null,
        bool includeEnabledMaps = false,
        DungeonCatalog? dungeons = null,
        DungeonAccessOptions? dungeonAccess = null,
        SiegeSettings? siege = null,
        BossSchedule? bosses = null,
        Random? bossRandom = null,
        NpcHtmlCatalog? html = null,
        BypassRegistry? bypasses = null)
    {
        _aoi = aoi ?? new SingleCellAoi();
        _game = game ?? GameData.LoadDefault();
        _itemIds = itemIds ?? new ItemObjectIdAllocator();
        _spawnRandom = spawnRandom ?? Random.Shared;
        _dropRandom = dropRandom ?? spawnRandom ?? Random.Shared;
        _accounts = accounts;
        _expertisePenalty = expertisePenalty ?? ZoneGearSettings.ExpertisePenalty;
        _spawnCatalog = spawnCatalog ?? SpawnCatalog.LoadDefault();
        _dropCatalog = dropCatalog ?? DropCatalog.LoadDefault();
        _dropRates = dropRates ?? DropRates.Retail;
        _activeMapId = string.IsNullOrWhiteSpace(activeMapId) ? SpawnCatalog.DefaultMapId : activeMapId.Trim();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _enchantRandom = enchantRandom ?? SharedEnchantRandom.Instance;
        _enchantRates = enchantRates ?? EnchantRates.Default;
        _enchantRates.Validate();
        _includeEnabledMaps = includeEnabledMaps;
        _quests = new QuestMachine(_game.Quests, _game.Items);
        _sevenSigns = sevenSigns ?? new SevenSignsEngine(new SystemGameClock());
        _dungeons = dungeons ?? DungeonCatalog.LoadDefault();
        _dungeonAccess = new DungeonAccess(_dungeons, dungeonAccess ?? DungeonAccessOptions.FromEnvironment());
        _siegeSettings = siege ?? SiegeSettings.FromEnvironment();
        _siege = new SiegeBoard(_clock().ToUnixTimeMilliseconds(), _siegeSettings);
        _bosses = bosses ?? BossSchedule.LoadDefault(_clock, bossRandom);
        _html = html ?? NpcHtmlCatalog.LoadDefault();
        _bypasses = bypasses ?? BypassRegistry.CreateDefault(_html);
        BootstrapSpawns();
        BootstrapBosses();
        BootstrapQuestNpcs();
        LoadClans();
        LoadSiege();
    }

    /// <summary>Bypass handlers. A later <see cref="BypassRegistry.Register"/> replaces a stub.</summary>
    public BypassRegistry Bypasses => _bypasses;

    public IReadOnlyList<int> NpcObjectIds
    {
        get
        {
            lock (_gate)
            {
                var ids = new List<int>();
                foreach (var entity in _byId.Values)
                {
                    if (entity.Kind == ZoneEntityKind.Npc)
                        ids.Add(entity.ObjectId);
                }

                ids.Sort();
                return ids;
            }
        }
    }

    /// <summary>Players with an active zone session in the world (for OTEL <c>fineage.zone.online</c>).</summary>
    public int OnlinePlayerCount
    {
        get
        {
            lock (_gate)
            {
                int count = 0;
                foreach (var entity in _byId.Values)
                {
                    if (entity.Kind == ZoneEntityKind.Player && entity.Session is { InWorld: true })
                        count++;
                }

                return count;
            }
        }
    }

    public async Task<bool> TryEnterAsync(ZoneSession session, CancellationToken ct)
    {
        var toSelf = new List<Pending>();
        var toOthers = new List<Pending>();
        bool ok;
        int known;
        lock (_gate)
            ok = TryEnter(session, toSelf, toOthers, out known);
        if (!ok)
            return false;

        ZoneLog.EnterWorld(session, known);
        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        await FlushOthersAsync(toOthers, ct).ConfigureAwait(false);
        return true;
    }

    public async Task LeaveAsync(ZoneSession session, CancellationToken ct)
    {
        var toOthers = new List<Pending>();
        bool removed;
        lock (_gate)
            removed = Unlink(session, toOthers);
        if (!removed)
            return;

        _sevenSigns.Save();
        _bosses.Save();
        ZoneLog.LeaveWorld(session);
        await FlushOthersAsync(toOthers, ct).ConfigureAwait(false);
    }

    public async Task<string?> TryMoveAsync(ZoneSession session, MoveToMsg move, CancellationToken ct)
    {
        var toSelf = new List<Pending>();
        var toOthers = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryMove(session, move, toSelf, toOthers);
        if (error != null)
            return error;

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        await FlushOthersAsync(toOthers, ct).ConfigureAwait(false);
        return null;
    }

    bool TryEnter(ZoneSession session, List<Pending> toSelf, List<Pending> toOthers, out int known)
    {
        known = 0;
        if (session.ObjectId <= 0)
            return false;
        if (_byId.TryGetValue(session.ObjectId, out var occupied) && occupied.Kind != ZoneEntityKind.Player)
            return false;

        if (session.InWorld)
            Unlink(session, toOthers);
        else if (_byId.TryGetValue(session.ObjectId, out var current) &&
                 current.Session != null &&
                 !ReferenceEquals(current.Session, session))
            Unlink(current.Session, toOthers);

        var spawn = SpawnFor(session);
        var inventory = LoadInventory(session);
        var vitals = LoadVitals(session);
        RememberPenalties(session);
        var entity = new ZoneEntity
        {
            ObjectId = session.ObjectId,
            Kind = ZoneEntityKind.Player,
            Name = session.Name,
            X = spawn.X,
            Y = spawn.Y,
            Z = spawn.Z,
            ClassId = session.ClassId,
            RunSpeed = MoveSpeed(session.ClassId).run,
            WalkSpeed = MoveSpeed(session.ClassId).walk,
            Running = true,
            Session = session,
            Inventory = inventory,
            MaxHp = vitals.maxHp,
            CurHp = vitals.hp,
            MaxMp = vitals.maxMp,
            CurMp = vitals.mp,
            Level = vitals.level,
            Race = vitals.race,
            Sex = vitals.sex,
            ClanId = ClanIdFor(session.ObjectId)
        };
        LoadQuestProgress(session, entity);
        RefreshKnownSkills(entity);
        SyncClanStanding(entity);
        RefreshGradePenalty(entity);
        _byId[entity.ObjectId] = entity;
        _aoi.Enter(entity.ObjectId, entity.X, entity.Y);
        session.InWorld = true;
        session.X = entity.X;
        session.Y = entity.Y;
        session.Z = entity.Z;
        SavePresence(session, entity.X, entity.Y, entity.Z, touchAccess: true);

        toSelf.Add(Pack(session, MsgType.UserInfo, UserInfo(entity)));
        toSelf.Add(Pack(session, MsgType.ItemList, InventoryMessages.ItemList(inventory)));
        toSelf.Add(Pack(session, MsgType.SkillList, SkillListFor(entity)));
        toSelf.Add(Pack(session, MsgType.PlayerSpawned, Spawned(entity)));
        foreach (var other in OrderedVisible(entity.ObjectId))
        {
            entity.Knownlist.Add(other.ObjectId);
            AddPresence(toSelf, session, other);
            if (other.Session != null)
            {
                other.Knownlist.Add(entity.ObjectId);
                toOthers.Add(Pack(other.Session, MsgType.CharInfo, CharInfo(entity)));
            }
        }

        AnnounceClanEnter(entity, toSelf, toOthers);
        toSelf.Add(Pack(session, MsgType.SystemMessage, SayRules.Welcome(session.Name)));
        known = entity.Knownlist.Count;
        return true;
    }

    string? TryMove(ZoneSession session, MoveToMsg move, List<Pending> toSelf, List<Pending> toOthers)
    {
        if (!IsLive(session))
            return "world";

        var entity = _byId[session.ObjectId];
        if (entity.Sitting)
            return ActionProtocol.Sitting;
        if (!TalkingIsland.WithinStep(entity.X, entity.Y, move.DestX, move.DestY))
            return "range";

        var ack = new MoveToMsg
        {
            ObjectId = entity.ObjectId,
            DestX = move.DestX,
            DestY = move.DestY,
            DestZ = move.DestZ,
            OriginX = entity.X,
            OriginY = entity.Y,
            OriginZ = entity.Z
        };

        var before = new HashSet<int>(entity.Knownlist);
        entity.X = move.DestX;
        entity.Y = move.DestY;
        entity.Z = move.DestZ;
        session.X = entity.X;
        session.Y = entity.Y;
        session.Z = entity.Z;
        _aoi.Move(entity.ObjectId, entity.X, entity.Y);

        var after = new HashSet<int>();
        foreach (int id in _aoi.GetVisible(entity.ObjectId))
            after.Add(id);

        toSelf.Add(Pack(session, MsgType.MoveToAck, ack));

        var entered = SortedExcept(after, before);
        var stayed = SortedShared(after, before);
        var left = SortedExcept(before, after);

        foreach (int id in entered)
            LinkSeen(entity, id, toSelf, toOthers);
        foreach (int id in stayed)
        {
            if (_byId.TryGetValue(id, out var other) && other.Session != null)
                toOthers.Add(Pack(other.Session, MsgType.MoveToAck, ack));
        }

        foreach (int id in left)
            Forget(entity, id, toSelf, toOthers);

        return null;
    }

    void LinkSeen(ZoneEntity self, int otherId, List<Pending> toSelf, List<Pending> toOthers)
    {
        if (self.Session == null || !_byId.TryGetValue(otherId, out var other))
            return;

        self.Knownlist.Add(otherId);
        AddPresence(toSelf, self.Session, other);
        if (other.Session != null)
        {
            other.Knownlist.Add(self.ObjectId);
            toOthers.Add(Pack(other.Session, MsgType.CharInfo, CharInfo(self)));
        }
    }

    void Forget(ZoneEntity self, int otherId, List<Pending> toSelf, List<Pending> toOthers)
    {
        if (self.Session == null)
            return;

        self.Knownlist.Remove(otherId);
        toSelf.Add(Pack(self.Session, MsgType.DeleteObject, new DeleteObjectMsg { ObjectId = otherId }));
        if (_byId.TryGetValue(otherId, out var other))
        {
            other.Knownlist.Remove(self.ObjectId);
            if (other.Session != null)
                toOthers.Add(Pack(other.Session, MsgType.DeleteObject, new DeleteObjectMsg { ObjectId = self.ObjectId }));
        }
    }

    bool Unlink(ZoneSession session, List<Pending> toOthers)
    {
        if (session.ObjectId == 0 || !_byId.TryGetValue(session.ObjectId, out var entity))
        {
            session.InWorld = false;
            return false;
        }

        if (!ReferenceEquals(entity.Session, session))
        {
            session.InWorld = false;
            return false;
        }

        var watchers = new List<ZoneSession>();
        foreach (int id in entity.Knownlist)
        {
            if (!_byId.TryGetValue(id, out var other))
                continue;
            other.Knownlist.Remove(session.ObjectId);
            if (other.Session != null)
                watchers.Add(other.Session);
        }

        if (entity.Inventory != null)
            SaveInventory(session, entity.Inventory);
        SaveVitals(session, entity);
        SaveQuestProgress(session, entity);
        SavePresence(session, entity.X, entity.Y, entity.Z, touchAccess: false);

        entity.Knownlist.Clear();
        _aoi.Leave(session.ObjectId);
        _byId.Remove(session.ObjectId);
        session.InWorld = false;

        PartyPlayerLeftWorld(session.ObjectId, toOthers);
        ClanPlayerLeftWorld(session.ObjectId, session.Name, toOthers);

        byte[] body = MessagePackSerializer.Serialize(new DeleteObjectMsg { ObjectId = session.ObjectId });
        foreach (var watcher in watchers)
            toOthers.Add(new Pending(watcher, MsgType.DeleteObject, body));
        return true;
    }

    ClassSpawn.Point SpawnFor(ZoneSession session)
    {
        if (_accounts != null)
        {
            var record = _accounts.FindById(session.Account, session.ObjectId);
            if (record is { HasPosition: true })
                return new ClassSpawn.Point(record.X, record.Y, record.Z);
        }

        if (!PlayerClasses.TryGet(session.ClassId, out var template) || template == null || template.Spawns.Count == 0)
            return new ClassSpawn.Point(TalkingIslandSpawn.X, TalkingIslandSpawn.Y, TalkingIslandSpawn.Z);
        var picked = template.Spawns[_spawnRandom.Next(template.Spawns.Count)];
        return new ClassSpawn.Point(picked.X, picked.Y, picked.Z);
    }

    ClassCatalog PlayerClasses => _game.Classes ?? ClassTemplates.Catalog;

    (float run, float walk) MoveSpeed(int classId)
    {
        if (PlayerClasses.TryGet(classId, out var template) && template != null)
            return (template.RunSpeed, template.WalkSpeed);
        return (120f, 80f);
    }

    void SavePresence(ZoneSession session, int x, int y, int z, bool touchAccess)
    {
        if (_accounts == null)
            return;
        _accounts.TrySavePresence(
            session.Account,
            session.ObjectId,
            x,
            y,
            z,
            touchAccess ? DateTimeOffset.UtcNow : null);
    }

    PlayerInventory LoadInventory(ZoneSession session)
    {
        if (_accounts == null)
            return _game.CreateStarterInventory(session.ClassId, _itemIds);

        var record = _accounts.FindById(session.Account, session.ObjectId);
        if (record == null)
            return _game.CreateStarterInventory(session.ClassId, _itemIds);
        if (record.Items == null)
        {
            var granted = _game.CreateStarterInventory(session.ClassId, _itemIds);
            _accounts.TryReplaceItems(session.Account, session.ObjectId, CharacterInventory.Rows(granted));
            return granted;
        }

        return CharacterInventory.Restore(_game.Items, _itemIds, record.Items);
    }

    void SaveInventory(ZoneSession session, PlayerInventory inventory)
    {
        if (_accounts == null)
            return;
        _accounts.TryReplaceItems(session.Account, session.ObjectId, CharacterInventory.Rows(inventory));
    }

    bool IsLive(ZoneSession session) =>
        session.InWorld &&
        session.ObjectId != 0 &&
        _byId.TryGetValue(session.ObjectId, out var entity) &&
        ReferenceEquals(entity.Session, session);

    List<ZoneEntity> OrderedVisible(int objectId)
    {
        var visible = new List<ZoneEntity>();
        foreach (int id in _aoi.GetVisible(objectId))
        {
            if (_byId.TryGetValue(id, out var entity))
                visible.Add(entity);
        }

        visible.Sort(static (a, b) =>
        {
            int kind = a.Kind.CompareTo(b.Kind);
            return kind != 0 ? kind : a.ObjectId.CompareTo(b.ObjectId);
        });
        return visible;
    }

    List<ZoneEntity> Players()
    {
        var players = new List<ZoneEntity>();
        foreach (var entity in _byId.Values)
        {
            if (entity.Kind == ZoneEntityKind.Player && entity.Session is { InWorld: true })
                players.Add(entity);
        }

        return players;
    }

    static List<int> SortedExcept(HashSet<int> source, HashSet<int> other)
    {
        var ids = new List<int>();
        foreach (int id in source)
        {
            if (!other.Contains(id))
                ids.Add(id);
        }

        ids.Sort();
        return ids;
    }

    static List<int> SortedShared(HashSet<int> source, HashSet<int> other)
    {
        var ids = new List<int>();
        foreach (int id in source)
        {
            if (other.Contains(id))
                ids.Add(id);
        }

        ids.Sort();
        return ids;
    }

    static void AddPresence(List<Pending> target, ZoneSession to, ZoneEntity entity)
    {
        if (entity.Kind == ZoneEntityKind.Npc)
            target.Add(Pack(to, MsgType.NpcInfo, NpcInfo(entity)));
        else if (entity.Kind == ZoneEntityKind.GroundItem)
            target.Add(Pack(to, MsgType.SpawnItem, SpawnItem(entity)));
        else
            target.Add(Pack(to, MsgType.CharInfo, CharInfo(entity)));
    }

    static PlayerSpawnedMsg Spawned(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        Name = entity.Name,
        X = entity.X,
        Y = entity.Y,
        Z = entity.Z,
        RunSpeed = entity.RunSpeed,
        WalkSpeed = entity.WalkSpeed,
        Running = entity.Running
    };

    static UserInfoMsg UserInfo(ZoneEntity entity)
    {
        if (entity.Inventory == null)
            throw new InvalidOperationException($"Player {entity.ObjectId} has no inventory.");
        return InventoryMessages.UserInfo(
            entity.ObjectId,
            entity.Name,
            entity.ClassId,
            entity.X,
            entity.Y,
            entity.Z,
            entity.RunSpeed,
            entity.WalkSpeed,
            entity.Running,
            entity.Inventory,
            entity.WeaponGradePenalty,
            entity.ArmorGradePenalty,
            entity.ClanId,
            ClanProtocol.CrestId);
    }

    static CharInfoMsg CharInfo(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        Name = entity.Name,
        X = entity.X,
        Y = entity.Y,
        Z = entity.Z,
        ClassId = entity.ClassId,
        Paperdoll = entity.Inventory == null
            ? new int[InventoryMessages.PaperdollSlotCount]
            : InventoryMessages.ItemIds(entity.Inventory),
        ClanId = entity.ClanId,
        ClanCrestId = entity.ClanId == 0 ? 0 : ClanProtocol.CrestId,
        Enchant = entity.Inventory == null
            ? new int[InventoryMessages.PaperdollSlotCount]
            : InventoryMessages.EnchantLevels(entity.Inventory)
    };

    static NpcInfoMsg NpcInfo(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        NpcId = entity.NpcId,
        Name = entity.Name,
        X = entity.X,
        Y = entity.Y,
        Z = entity.Z,
        Heading = entity.Heading,
        Attackable = entity.Attackable,
        RunSpeed = entity.RunSpeed,
        WalkSpeed = entity.WalkSpeed,
        Title = entity.Title,
        NpcType = entity.NpcType
    };

    internal NpcInfoMsg NpcInfoByTemplateForTests(int npcId)
    {
        lock (_gate)
        {
            ZoneEntity? found = null;
            foreach (var entity in _byId.Values)
            {
                if (entity.Kind != ZoneEntityKind.Npc || entity.NpcId != npcId)
                    continue;
                if (found == null || entity.ObjectId < found.ObjectId)
                    found = entity;
            }

            if (found == null)
                throw new KeyNotFoundException($"NPC template {npcId} is not spawned.");
            return NpcInfo(found);
        }
    }

    internal static SpawnItemMsg SpawnItem(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        ItemId = entity.GroundItemId,
        X = entity.X,
        Y = entity.Y,
        Z = entity.Z,
        Stackable = entity.GroundStackable,
        Count = entity.GroundCount
    };

    static Pending Pack<T>(ZoneSession to, MsgType type, T message) =>
        new(to, type, MessagePackSerializer.Serialize(message));

    static async Task FlushSelfAsync(ZoneSession self, List<Pending> messages, CancellationToken ct)
    {
        foreach (var message in messages)
        {
            if (ReferenceEquals(message.To, self))
                await self.SendFrameAsync((ushort)message.Type, message.Body, ct).ConfigureAwait(false);
            else
                await TrySendAsync(message, ct).ConfigureAwait(false);
        }
    }

    static async Task FlushOthersAsync(List<Pending> messages, CancellationToken ct)
    {
        foreach (var message in messages)
            await TrySendAsync(message, ct).ConfigureAwait(false);
    }

    static async Task TrySendAsync(Pending message, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(FanoutTimeout);
        try
        {
            await message.To.SendFrameAsync((ushort)message.Type, message.Body, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            ZoneLog.Info($"TX drop object={message.To.ObjectId} {ex.GetType().Name}");
        }
    }

    readonly record struct Pending(ZoneSession To, MsgType Type, byte[] Body);
}
