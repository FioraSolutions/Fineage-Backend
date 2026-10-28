using Fineage.Game;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Combat;
using Fineage.Zone.Interest;
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
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Heading { get; init; }
    public int ClassId { get; init; }
    public int NpcId { get; init; }
    public bool Attackable { get; init; }
    public int CurHp { get; set; }
    public int MaxHp { get; init; }
    public int CurMp { get; set; }
    public int MaxMp { get; init; }
    public int Level { get; init; } = 1;
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
    /// <summary>When true, regen rates double (classic sitting rest). No sit opcode yet.</summary>
    public bool Sitting { get; set; }
    public float RunSpeed { get; init; }
    public float WalkSpeed { get; init; }
    public bool Running { get; init; }
    public ZoneSession? Session { get; init; }
    public PlayerInventory? Inventory { get; init; }
    /// <summary>
    /// Player is dead. Equip and unequip are rejected. Combat sets this; spawn leaves it false.
    /// CurHp is not the signal: a living player can sit at low HP while regen heals them.
    /// </summary>
    public bool Dead { get; set; }
    public int GroundItemId { get; set; }
    public int GroundCount { get; set; } = 1;
    public bool GroundStackable { get; set; }
    public List<AbnormalEffectMsg> AbnormalEffects { get; } = [];
    public HashSet<int> Knownlist { get; } = [];
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
    readonly IAccountStore? _accounts;
    readonly bool _expertisePenalty;
    readonly Dictionary<int, ZoneEntity> _byId = [];
    readonly object _gate = new();

    public ZoneWorld(
        IAoi? aoi = null,
        GameData? game = null,
        ItemObjectIdAllocator? itemIds = null,
        Random? spawnRandom = null,
        IAccountStore? accounts = null,
        bool? expertisePenalty = null)
    {
        _aoi = aoi ?? new SingleCellAoi();
        _game = game ?? GameData.LoadDefault();
        _itemIds = itemIds ?? new ItemObjectIdAllocator();
        _spawnRandom = spawnRandom ?? Random.Shared;
        _accounts = accounts;
        _expertisePenalty = expertisePenalty ?? ZoneGearSettings.ExpertisePenalty;
        foreach (var npc in TalkingIsland.Gremlins)
        {
            var entity = new ZoneEntity
            {
                ObjectId = npc.ObjectId,
                Kind = ZoneEntityKind.Npc,
                Name = npc.Name,
                X = npc.X,
                Y = npc.Y,
                Z = npc.Z,
                Heading = npc.Heading,
                NpcId = npc.NpcId,
                Attackable = npc.Attackable,
                MaxHp = TalkingIsland.GremlinMaxHp,
                CurHp = TalkingIsland.GremlinMaxHp,
                RunSpeed = npc.RunSpeed,
                WalkSpeed = npc.WalkSpeed
            };
            _byId.Add(entity.ObjectId, entity);
            _aoi.Enter(entity.ObjectId, entity.X, entity.Y);
        }
    }

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
        var entity = new ZoneEntity
        {
            ObjectId = session.ObjectId,
            Kind = ZoneEntityKind.Player,
            Name = session.Name,
            X = spawn.X,
            Y = spawn.Y,
            Z = spawn.Z,
            ClassId = session.ClassId,
            RunSpeed = 120f,
            WalkSpeed = 80f,
            Running = true,
            Session = session,
            Inventory = inventory,
            MaxHp = vitals.maxHp,
            CurHp = vitals.hp,
            MaxMp = vitals.maxMp,
            CurMp = vitals.mp,
            Level = vitals.level,
            Race = vitals.race,
            Sex = vitals.sex
        };
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

        toSelf.Add(Pack(session, MsgType.SystemMessage, SayRules.Welcome(session.Name)));
        known = entity.Knownlist.Count;
        return true;
    }

    string? TryMove(ZoneSession session, MoveToMsg move, List<Pending> toSelf, List<Pending> toOthers)
    {
        if (!IsLive(session))
            return "world";

        var entity = _byId[session.ObjectId];
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
        SavePresence(session, entity.X, entity.Y, entity.Z, touchAccess: false);

        entity.Knownlist.Clear();
        _aoi.Leave(session.ObjectId);
        _byId.Remove(session.ObjectId);
        session.InWorld = false;

        PartyPlayerLeftWorld(session.ObjectId, toOthers);

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

        return ClassSpawn.Pick(session.ClassId, _spawnRandom);
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
            entity.ArmorGradePenalty);
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
            : InventoryMessages.ItemIds(entity.Inventory)
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
        WalkSpeed = entity.WalkSpeed
    };

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
