using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;
using NetReject = Fineage.Net.Contract.RejectMsg;

namespace Fineage.Zone.Net;

/// <summary>
/// Fineage Net zone path. Reuses <see cref="ZoneWorld.TryEnterAsync"/> / <see cref="ZoneWorld.TryMoveAsync"/>;
/// Net packets are the only client-facing transport; presence (spawn / move / despawn) is fanned out via
/// <see cref="NetPresenceHub"/>.
/// </summary>
public sealed class NetZoneServer : IWorldEventPublisher
{
    readonly IHandoffStore _handoff;
    readonly ZoneWorld _world;
    readonly EntityIdMap _ids = new();
    readonly NetPresenceHub _presence;
    readonly Func<DateTimeOffset> _clock;
    readonly int _maxMessagesPerSecond;
    readonly TimeSpan _idleTimeout;
    readonly bool _adminCommands;
    readonly EnchantPacketHandler _enchant;
    readonly ClanPacketHandler _clan;
    readonly TradePacketHandler _trade;
    readonly PartyPacketHandler _party;
    readonly FriendPacketHandler _friends;
    readonly ShortcutActionPacketHandler _shortcutActions;
    readonly QuestPacketHandler _quests;
    readonly WarehousePacketHandler _warehouse;
    readonly ShopPacketHandler _shop;
    readonly NpcTalkPacketHandler _npcTalk;
    readonly PrivateStorePacketHandler _privateStore;
    readonly CombatPacketHandler _combat;
    readonly InventoryActionPacketHandler _inventoryActions;
    readonly SkillLearnPacketHandler _skillLearn;
    readonly RecipePacketHandler _recipes;
    readonly ChatPacketHandler _chat;
    readonly WorldEventPacketHandler _worldEvents;
    readonly MovementPacketHandler _movement;
    readonly CharacterActionPacketHandler _characterActions;
    readonly NetKnownlistFanout _knownlist;
    readonly SessionLifecyclePacketHandler _session;

    public NetZoneServer(
        IHandoffStore handoff,
        ZoneWorld world,
        IAccountStore? accounts = null,
        Func<DateTimeOffset>? clock = null,
        NetPresenceHub? presence = null,
        int? maxMessagesPerSecond = null,
        TimeSpan? idleTimeout = null,
        bool? adminCommands = null)
    {
        _handoff = handoff ?? throw new ArgumentNullException(nameof(handoff));
        _world = world;
        // Character durable state is loaded by ZoneWorld.TryEnterAsync via the shared store.
        _ = accounts;
        _presence = presence ?? new NetPresenceHub();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _maxMessagesPerSecond = maxMessagesPerSecond ?? ZoneNetSettings.MaxMessagesPerSecond;
        _idleTimeout = idleTimeout ?? TcpSessionSettings.IdleTimeout;
        _adminCommands = adminCommands ?? ZoneAdmin.Enabled;
        _enchant = new EnchantPacketHandler(_world, RejectAsync);
        _clan = new ClanPacketHandler(_world, _clock, RejectAsync);
        _trade = new TradePacketHandler(_world, RejectAsync);
        _party = new PartyPacketHandler(_world, RejectAsync);
        _friends = new FriendPacketHandler(_world, RejectAsync);
        _shortcutActions = new ShortcutActionPacketHandler(_world, RejectAsync);
        _quests = new QuestPacketHandler(_world, RejectAsync);
        _warehouse = new WarehousePacketHandler(_world, RejectAsync);
        _shop = new ShopPacketHandler(_world, RejectAsync);
        _npcTalk = new NpcTalkPacketHandler(_world, RejectAsync);
        _privateStore = new PrivateStorePacketHandler(_world, RejectAsync);
        _combat = new CombatPacketHandler(_world, RejectAsync);
        _inventoryActions = new InventoryActionPacketHandler(_world, RejectAsync);
        _skillLearn = new SkillLearnPacketHandler(_world, RejectAsync);
        _recipes = new RecipePacketHandler(_world, RejectAsync);
        _chat = new ChatPacketHandler(_world, _clock, RejectAsync);
        _worldEvents = new WorldEventPacketHandler(_world, _adminCommands, RejectAsync);
        _knownlist = new NetKnownlistFanout(_world, _ids, _presence, PlayerSpawnPAtkSpd);
        _movement = new MovementPacketHandler(_world, RejectAsync, _knownlist.FanDiffAsync);
        _characterActions = new CharacterActionPacketHandler(_world, RejectAsync, _knownlist.FanDiffAsync);
        _session = new SessionLifecyclePacketHandler(
            _handoff, _world, _ids, _presence, _clock, PlayerSpawnPAtkSpd, RejectAsync);
        _world.NetBridge = this;
    }

    int PlayerSpawnPAtkSpd(ZoneEntity entity) =>
        entity.Kind == ZoneEntityKind.Player ? _world.PlayerStats(entity).PAtkSpd : 0;

    /// <summary>Tests and selftest: wraps <paramref name="tokens"/> in a consuming in-memory store.</summary>
    public NetZoneServer(
        HandoffTokens tokens,
        ZoneWorld world,
        IAccountStore? accounts = null,
        Func<DateTimeOffset>? clock = null,
        NetPresenceHub? presence = null,
        int? maxMessagesPerSecond = null,
        TimeSpan? idleTimeout = null,
        bool? adminCommands = null)
        : this(new MemoryHandoffStore(tokens), world, accounts, clock, presence, maxMessagesPerSecond, idleTimeout, adminCommands)
    {
    }

    void IWorldEventPublisher.FanoutMoveState(
        IReadOnlyList<int> watcherObjectIds,
        int moverObjectId,
        int originX,
        int originY,
        int originZ,
        int destX,
        int destY,
        int destZ,
        bool running)
    {
        var mover = _ids.AllocateOrGet(moverObjectId);
        var payload = NetCodec.EncodeMoveState(new MoveStateMsg
        {
            EntityValue = mover.Value,
            EntityGeneration = mover.Generation,
            OriginX = originX,
            OriginY = originY,
            OriginZ = originZ,
            DestX = destX,
            DestY = destY,
            DestZ = destZ,
            Running = running
        });
        foreach (int watcher in watcherObjectIds)
        {
            if (!_presence.TryGet(watcher, out _, out _))
            {
                ZoneLog.Info($"[move] MoveState watcher={watcher} mover={moverObjectId} NOT REGISTERED in presence hub");
                continue;
            }
            _presence.TrySend(watcher, NetOpcode.MoveState, payload);
        }
    }

    void IWorldEventPublisher.FanoutKnownlistDiff(int selfObjectId, IReadOnlyList<int> added, IReadOnlyList<int> removed)
    {
        if (!_world.TryGetEntityById(selfObjectId, out var selfEntity))
            return;
        var self = _ids.AllocateOrGet(selfObjectId);
        var ct = CancellationToken.None;
        byte[]? selfSpawn = null;
        foreach (int id in added)
        {
            if (!_world.TryGetEntityById(id, out var seen))
                continue;
            var otherId = _ids.AllocateOrGet(id);
            if (seen.Kind == ZoneEntityKind.GroundItem)
            {
                _presence.TrySend(selfObjectId, NetOpcode.DropItem, NetCodec.EncodeDropItem(new DropItemNetMsg
                {
                    DropperObjectId = 0,
                    EntityValue = otherId.Value,
                    EntityGeneration = otherId.Generation,
                    ItemId = seen.GroundItemId,
                    X = seen.X,
                    Y = seen.Y,
                    Z = seen.Z,
                    Stackable = seen.GroundStackable,
                    Count = seen.GroundCount
                }));
                continue;
            }

            _presence.TrySendCritical(selfObjectId, NetOpcode.EntitySpawn,
                NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildPresenceSpawn(seen, otherId, PlayerSpawnPAtkSpd(seen))));
            if (seen.MoveActive)
                SendMoveState(selfObjectId, otherId, seen);
            if (seen.Kind == ZoneEntityKind.Player && seen.Session is { InWorld: true })
            {
                selfSpawn ??= NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(selfEntity, self, PlayerSpawnPAtkSpd(selfEntity)));
                _presence.TrySendCritical(id, NetOpcode.EntitySpawn, selfSpawn);
                if (selfEntity.MoveActive)
                    SendMoveState(id, self, selfEntity);
            }
        }

        if (removed.Count == 0)
            return;
        var despawnSelf = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = self.Value,
            EntityGeneration = self.Generation
        });
        foreach (int id in removed)
        {
            var forgotten = _ids.AllocateOrGet(id);
            _presence.TrySendCritical(selfObjectId, NetOpcode.EntityDespawn, NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
            {
                EntityValue = forgotten.Value,
                EntityGeneration = forgotten.Generation
            }));
            if (_world.TryGetLivePlayerById(id, out _))
                _presence.TrySendCritical(id, NetOpcode.EntityDespawn, despawnSelf);
        }
    }

    void SendMoveState(int watcherObjectId, EntityId mover, ZoneEntity e)
    {
        _presence.TrySend(watcherObjectId, NetOpcode.MoveState, NetCodec.EncodeMoveState(new MoveStateMsg
        {
            EntityValue = mover.Value,
            EntityGeneration = mover.Generation,
            OriginX = e.X,
            OriginY = e.Y,
            OriginZ = e.Z,
            DestX = e.MoveDestX,
            DestY = e.MoveDestY,
            DestZ = e.MoveDestZ,
            Running = e.Running
        }));
    }

    void IWorldEventPublisher.FanoutChangeMoveType(IReadOnlyList<int> watcherObjectIds, int moverObjectId, bool running, float runSpeed, float walkSpeed, bool inWater)
    {
        var mover = _ids.AllocateOrGet(moverObjectId);
        var payload = NetCodec.EncodeChangeMoveType(new ChangeMoveTypeNetMsg
        {
            EntityValue = mover.Value,
            EntityGeneration = mover.Generation,
            Running = running,
            RunSpeed = runSpeed,
            WalkSpeed = walkSpeed,
            InWater = inWater
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ChangeMoveType, payload);
    }

    void IWorldEventPublisher.FanoutAttackStance(IReadOnlyList<int> watcherObjectIds, int objectId, bool inStance)
    {
        var id = _ids.AllocateOrGet(objectId);
        var payload = NetCodec.EncodeAttackStance(new AttackStanceNetMsg { EntityValue = id.Value, EntityGeneration = id.Generation });
        ushort opcode = inStance ? NetOpcode.AutoAttackStart : NetOpcode.AutoAttackStop;
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, opcode, payload);
    }

    void IWorldEventPublisher.FanoutAutoSoulShot(IReadOnlyList<int> watcherObjectIds, int itemId, bool enabled)
    {
        var payload = NetCodec.EncodeAutoSoulShot(new AutoSoulShotNetMsg { ItemId = itemId, Enabled = enabled });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AutoSoulShot, payload);
    }

    void IWorldEventPublisher.FanoutUserInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.UserInfoMsg info)
    {
        var payload = NetCodec.EncodeUserInfo(ToNetUserInfo(info));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.UserInfo, payload);
    }

    internal static UserInfoNetMsg ToNetUserInfo(Fineage.Protocol.UserInfoMsg info)
    {
        var src = info.Paperdoll ?? [];
        var slots = new PaperdollSlotNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            slots[i] = new PaperdollSlotNet
            {
                ObjectId = src[i].ObjectId,
                ItemId = src[i].ItemId,
                EnchantLevel = src[i].EnchantLevel
            };
        }

        return new UserInfoNetMsg
        {
            EntityValue = (ulong)(uint)info.ObjectId,
            EntityGeneration = 1,
            Name = info.Name ?? "",
            ClassId = info.ClassId,
            Race = info.Race,
            Sex = info.Sex,
            X = info.X,
            Y = info.Y,
            Z = info.Z,
            RunSpeed = info.RunSpeed,
            WalkSpeed = info.WalkSpeed,
            Running = info.Running,
            Level = info.Level,
            Exp = info.Exp,
            Sp = info.Sp,
            CurHp = info.CurHp,
            MaxHp = info.MaxHp,
            CurMp = info.CurMp,
            MaxMp = info.MaxMp,
            CurCp = info.CurCp,
            MaxCp = info.MaxCp,
            Str = info.Str,
            Dex = info.Dex,
            Con = info.Con,
            Int = info.Int,
            Wit = info.Wit,
            Men = info.Men,
            PAtk = info.PAtk,
            MAtk = info.MAtk,
            PDef = info.PDef,
            MDef = info.MDef,
            PAtkSpd = info.PAtkSpd,
            MAtkSpd = info.MAtkSpd,
            Accuracy = info.Accuracy,
            Evasion = info.Evasion,
            CritRate = info.CritRate,
            WeaponGradePenalty = info.WeaponGradePenalty,
            ArmorGradePenalty = info.ArmorGradePenalty,
            ClanId = info.ClanId,
            Paperdoll = slots
        };
    }

    void IWorldEventPublisher.FanoutCharInfo(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        if (watcherObjectIds.Count == 0 || !_world.TryGetEntityById(objectId, out var entity) ||
            entity.Kind != ZoneEntityKind.Player)
            return;
        var payload = NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(entity, _ids.AllocateOrGet(objectId), PlayerSpawnPAtkSpd(entity)));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.EntitySpawn, payload);
    }

    void IWorldEventPublisher.FanoutAttackState(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AttackMsg attack)
    {
        var payload = NetCodec.EncodeAttackState(new AttackStateMsg
        {
            AttackerValue = (ulong)(uint)attack.AttackerObjectId,
            AttackerGeneration = 1,
            TargetValue = (ulong)(uint)attack.TargetObjectId,
            TargetGeneration = 1,
            Damage = attack.Damage,
            Flags = attack.Flags,
            AttackerX = attack.AttackerX,
            AttackerY = attack.AttackerY,
            AttackerZ = attack.AttackerZ,
            TargetX = attack.TargetX,
            TargetY = attack.TargetY,
            TargetZ = attack.TargetZ
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AttackState, payload);
    }

    void IWorldEventPublisher.FanoutStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.StatusUpdateMsg status)
    {
        var payload = NetCodec.EncodeStatusUpdate(new StatusUpdateNetMsg
        {
            EntityValue = (ulong)(uint)status.ObjectId,
            EntityGeneration = 1,
            AttributeIds = status.AttributeIds ?? [],
            Values = status.Values ?? []
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.StatusUpdate, payload);
    }

    void IWorldEventPublisher.FanoutDie(IReadOnlyList<int> watcherObjectIds, int objectId, int killerObjectId)
    {
        var payload = NetCodec.EncodeEntityDie(new EntityDieMsg
        {
            EntityValue = (ulong)(uint)objectId,
            EntityGeneration = 1,
            KillerObjectId = killerObjectId
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.EntityDie, payload);
    }

    void IWorldEventPublisher.FanoutDropItem(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.DropItemMsg drop)
    {
        var payload = NetCodec.EncodeDropItem(new DropItemNetMsg
        {
            DropperObjectId = drop.CharObjectId,
            EntityValue = (ulong)(uint)drop.ObjectId,
            EntityGeneration = 1,
            ItemId = drop.ItemId,
            X = drop.X,
            Y = drop.Y,
            Z = drop.Z,
            Stackable = drop.Stackable,
            Count = drop.Count
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.DropItem, payload);
    }

    void IWorldEventPublisher.FanoutEntityDespawn(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        var payload = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = (ulong)(uint)objectId,
            EntityGeneration = 1
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.EntityDespawn, payload);
    }

    void IWorldEventPublisher.FanoutInventoryUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.InventoryUpdateMsg update)
    {
        var src = update.Changes ?? [];
        var rows = new InventoryUpdateEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var row = src[i];
            rows[i] = new InventoryUpdateEntryNet
            {
                Change = row.Change,
                ObjectId = row.ObjectId,
                ItemId = row.ItemId,
                Count = row.Count,
                EnchantLevel = row.EnchantLevel,
                EquippedSlot = row.EquippedSlot
            };
        }
        var payload = NetCodec.EncodeInventoryUpdate(new InventoryUpdateNetMsg { Changes = rows });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.InventoryUpdate, payload);
    }

    void IWorldEventPublisher.FanoutEtcStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EtcStatusUpdateMsg status)
    {
        foreach (int watcher in watcherObjectIds)
        {
            var payload = NetCodec.EncodeEtcStatusUpdate(ToNetEtcStatus(status, watcher));
            _presence.TrySend(watcher, NetOpcode.EtcStatusUpdate, payload);
        }
    }

    internal static EtcStatusUpdateNetMsg ToNetEtcStatus(Fineage.Protocol.EtcStatusUpdateMsg status, int objectId) => new()
    {
        EntityValue = (ulong)(uint)objectId,
        EntityGeneration = 1,
        Charges = status.Charges,
        WeightPenalty = status.WeightPenalty,
        ChatBlocked = status.ChatBlocked,
        DangerArea = status.DangerArea,
        GradePenalty = status.GradePenalty,
        CharmOfCourage = status.CharmOfCourage,
        DeathPenalty = status.DeathPenalty
    };

    void IWorldEventPublisher.FanoutCreatureSay(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.CreatureSayMsg say)
    {
        var payload = NetCodec.EncodeCreatureSay(new CreatureSayNetMsg
        {
            ObjectId = say.ObjectId,
            Name = say.Name ?? "",
            Text = say.Text ?? "",
            ChatType = say.ChatType
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.CreatureSay, payload);
    }

    void IWorldEventPublisher.FanoutSystemMessage(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SystemMessageMsg message)
    {
        var payload = NetCodec.EncodeSystemMessage(new SystemMessageNetMsg
        {
            Id = message.Id,
            Params = message.Params ?? [],
            Text = message.Text ?? ""
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SystemMessage, payload);
    }

    void IWorldEventPublisher.FanoutMagicSkillUse(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.MagicSkillUseMsg cast)
    {
        var payload = NetCodec.EncodeMagicSkillUse(new MagicSkillUseNetMsg
        {
            CasterObjectId = cast.CasterObjectId,
            TargetObjectId = cast.TargetObjectId,
            SkillId = cast.SkillId,
            SkillLevel = cast.SkillLevel,
            HitTimeMs = cast.HitTimeMs,
            ReuseDelayMs = cast.ReuseDelayMs
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.MagicSkillUse, payload);
    }

    void IWorldEventPublisher.FanoutRevive(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        var payload = NetCodec.EncodeRevive(new ReviveNetMsg { ObjectId = objectId });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.Revive, payload);
    }

    void IWorldEventPublisher.FanoutTeleportToLocation(IReadOnlyList<int> watcherObjectIds, int objectId, int x, int y, int z, int heading)
    {
        var payload = NetCodec.EncodeTeleportToLocation(new TeleportToLocationNetMsg
        {
            ObjectId = objectId,
            X = x,
            Y = y,
            Z = z,
            Heading = heading
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TeleportToLocation, payload);
    }

    void IWorldEventPublisher.FanoutPlayerStats(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PlayerStatsMsg stats)
    {
        var payload = NetCodec.EncodePlayerStats(new Fineage.Net.Contract.PlayerStatsMsg
        {
            EntityValue = (ulong)(uint)stats.ObjectId,
            EntityGeneration = 1,
            Level = stats.Level,
            Exp = stats.Exp,
            Sp = stats.Sp,
            Hp = stats.Hp,
            MaxHp = stats.MaxHp,
            Mp = stats.Mp,
            MaxMp = stats.MaxMp,
            Cp = stats.Cp,
            MaxCp = stats.MaxCp
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PlayerStats, payload);
    }

    void IWorldEventPublisher.FanoutAbnormalStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AbnormalStatusUpdateMsg update)
    {
        var effects = update.Effects ?? [];
        var netEffects = new AbnormalEffectNet[effects.Length];
        for (int i = 0; i < effects.Length; i++)
        {
            var src = effects[i];
            netEffects[i] = new AbnormalEffectNet
            {
                Id = src.Id,
                Value = src.Value,
                DurationSeconds = src.DurationSeconds
            };
        }
        var payload = NetCodec.EncodeAbnormalStatusUpdate(new AbnormalStatusUpdateNetMsg
        {
            ObjectId = update.ObjectId,
            Effects = netEffects
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AbnormalStatusUpdate, payload);
    }

    void IWorldEventPublisher.FanoutNpcHtml(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.NpcHtmlMsg html)
    {
        var payload = NetCodec.EncodeNpcHtml(new NpcHtmlNetMsg
        {
            NpcObjectId = html.NpcObjectId,
            NpcId = html.NpcId,
            Html = html.Html ?? "",
            ItemId = html.ItemId
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.NpcHtml, payload);
    }

    void IWorldEventPublisher.FanoutBuyList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BuyListMsg list)
    {
        var payload = NetCodec.EncodeBuyList(ToNetBuyList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.BuyList, payload);
    }

    void IWorldEventPublisher.FanoutSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SellListMsg list)
    {
        var payload = NetCodec.EncodeSellList(ToNetSellList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SellList, payload);
    }

    void IWorldEventPublisher.FanoutMultiSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.MultiSellListMsg list)
    {
        var payload = NetCodec.EncodeMultiSellList(ToNetMultiSellList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.MultiSellList, payload);
    }

    void IWorldEventPublisher.FanoutShopBalance(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShopBalanceMsg balance)
    {
        var payload = NetCodec.EncodeShopBalance(new ShopBalanceNetMsg
        {
            Adena = balance.Adena,
            Weight = balance.Weight,
            MaxWeight = balance.MaxWeight,
            Slots = balance.Slots,
            MaxSlots = balance.MaxSlots,
            BaseTaxPercent = balance.BaseTaxPercent,
            CastleTaxPercent = balance.CastleTaxPercent
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ShopBalance, payload);
    }

    void IWorldEventPublisher.FanoutWarehouseDepositList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.WarehouseDepositListMsg list)
    {
        var payload = NetCodec.EncodeWarehouseDepositList(ToNetWarehouseDepositList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.WarehouseDepositList, payload);
    }

    void IWorldEventPublisher.FanoutWarehouseWithdrawList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.WarehouseWithdrawListMsg list)
    {
        var payload = NetCodec.EncodeWarehouseWithdrawList(ToNetWarehouseWithdrawList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.WarehouseWithdrawList, payload);
    }

    void IWorldEventPublisher.FanoutSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SkillListMsg list)
    {
        var payload = NetCodec.EncodeSkillList(NetEnterWorldProjector.ToNetSkillList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SkillList, payload);
    }

    void IWorldEventPublisher.FanoutChangeWaitType(IReadOnlyList<int> watcherObjectIds, int objectId, int waitType, int x, int y, int z)
    {
        var payload = NetCodec.EncodeChangeWaitType(new ChangeWaitTypeNetMsg
        {
            EntityValue = (ulong)(uint)objectId,
            EntityGeneration = 1,
            WaitType = waitType,
            X = x,
            Y = y,
            Z = z
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ChangeWaitType, payload);
    }

    void IWorldEventPublisher.FanoutSocialAction(IReadOnlyList<int> watcherObjectIds, int objectId, int actionId)
    {
        var payload = NetCodec.EncodeSocialAction(new SocialActionNetMsg
        {
            EntityValue = (ulong)(uint)objectId,
            EntityGeneration = 1,
            ActionId = actionId
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SocialAction, payload);
    }

    void IWorldEventPublisher.FanoutTargetSelected(IReadOnlyList<int> watcherObjectIds, int targetObjectId, int colorHint)
    {
        var payload = NetCodec.EncodeTargetSelected(new TargetSelectedNetMsg
        {
            TargetObjectId = targetObjectId,
            ColorHint = colorHint
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TargetSelected, payload);
    }

    void IWorldEventPublisher.FanoutTargetUnselected(IReadOnlyList<int> watcherObjectIds, int targetObjectId)
    {
        var payload = NetCodec.EncodeTargetUnselected(new TargetUnselectedNetMsg
        {
            TargetObjectId = targetObjectId
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TargetUnselected, payload);
    }

    void IWorldEventPublisher.FanoutAcquireSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AcquireSkillListMsg list)
    {
        var payload = NetCodec.EncodeAcquireSkillList(ToNetAcquireSkillList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AcquireSkillList, payload);
    }

    void IWorldEventPublisher.FanoutAcquireSkillInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AcquireSkillInfoMsg info)
    {
        var payload = NetCodec.EncodeAcquireSkillInfo(ToNetAcquireSkillInfo(info));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AcquireSkillInfo, payload);
    }

    static AcquireSkillListNetMsg ToNetAcquireSkillList(Fineage.Protocol.AcquireSkillListMsg list)
    {
        var src = list.Skills ?? [];
        var rows = new AcquireSkillEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            rows[i] = new AcquireSkillEntryNet
            {
                SkillId = src[i].SkillId,
                NextLevel = src[i].NextLevel,
                MaxLevel = src[i].MaxLevel,
                SpCost = src[i].SpCost,
                Requirements = src[i].Requirements
            };
        }

        return new AcquireSkillListNetMsg { SkillType = list.SkillType, Skills = rows };
    }

    static AcquireSkillInfoNetMsg ToNetAcquireSkillInfo(Fineage.Protocol.AcquireSkillInfoMsg info)
    {
        var src = info.Requirements ?? [];
        var reqs = new AcquireSkillRequirementNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            reqs[i] = new AcquireSkillRequirementNet
            {
                Type = src[i].Type,
                ItemId = src[i].ItemId,
                Count = src[i].Count,
                Unk = src[i].Unk
            };
        }

        return new AcquireSkillInfoNetMsg
        {
            SkillId = info.SkillId,
            Level = info.Level,
            SpCost = info.SpCost,
            SkillType = info.SkillType,
            Requirements = reqs
        };
    }

    void IWorldEventPublisher.FanoutEnchantResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EnchantResultMsg result)
    {
        var payload = NetCodec.EncodeEnchantResult(new EnchantResultNetMsg
        {
            Result = result.Result,
            EnchantLevel = result.EnchantLevel,
            ObjectId = result.ObjectId,
            CrystalItemId = result.CrystalItemId,
            CrystalCount = result.CrystalCount
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.EnchantResult, payload);
    }

    void IWorldEventPublisher.FanoutPledgeStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PledgeStatusChangedMsg status)
    {
        var payload = NetCodec.EncodePledgeStatus(ToNetPledgeStatus(status));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PledgeStatus, payload);
    }

    void IWorldEventPublisher.FanoutPledgeMemberList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PledgeMemberListMsg list)
    {
        var payload = NetCodec.EncodePledgeMemberList(ToNetPledgeMemberList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PledgeMemberList, payload);
    }

    void IWorldEventPublisher.FanoutClanAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ClanAskJoinMsg ask)
    {
        var payload = NetCodec.EncodeClanAskJoin(new ClanAskJoinNetMsg
        {
            RequestorObjectId = ask.RequestorObjectId,
            RequestorName = ask.RequestorName ?? "",
            ClanId = ask.ClanId,
            ClanName = ask.ClanName ?? "",
            PledgeType = ask.PledgeType
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ClanAskJoin, payload);
    }

    void IWorldEventPublisher.FanoutClanJoinResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ClanJoinResultMsg result)
    {
        var payload = NetCodec.EncodeClanJoinResult(new ClanJoinResultNetMsg { Response = result.Response });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ClanJoinResult, payload);
    }

    internal static PledgeStatusNetMsg ToNetPledgeStatus(Fineage.Protocol.PledgeStatusChangedMsg status) => new()
    {
        ClanId = status.ClanId,
        Name = status.Name ?? "",
        Level = status.Level,
        LeaderObjectId = status.LeaderObjectId,
        CrestId = status.CrestId,
        MemberCount = status.MemberCount,
        DissolvingExpiryUnixMs = status.DissolvingExpiryUnixMs,
        CharPenaltyExpiryUnixMs = status.CharPenaltyExpiryUnixMs
    };

    internal static PledgeMemberListNetMsg ToNetPledgeMemberList(Fineage.Protocol.PledgeMemberListMsg list)
    {
        var src = list.Members ?? [];
        var rows = new PledgeMemberNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            rows[i] = new PledgeMemberNet
            {
                ObjectId = src[i].ObjectId,
                Name = src[i].Name ?? "",
                ClassId = src[i].ClassId,
                Level = src[i].Level,
                Online = src[i].Online,
                PledgeType = src[i].PledgeType
            };
        }

        return new PledgeMemberListNetMsg
        {
            ClanId = list.ClanId,
            LeaderObjectId = list.LeaderObjectId,
            Members = rows
        };
    }

    void IWorldEventPublisher.FanoutQuestList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.QuestListMsg list)
    {
        var payload = NetCodec.EncodeQuestList(ToNetQuestList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.QuestList, payload);
    }

    internal static QuestListNetMsg ToNetQuestList(Fineage.Protocol.QuestListMsg list)
    {
        var src = list.Active ?? [];
        var active = new QuestJournalEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var entry = src[i];
            active[i] = new QuestJournalEntryNet
            {
                QuestId = entry.QuestId,
                Name = entry.Name ?? "",
                State = entry.State,
                Cond = entry.Cond,
                ProgressFlags = entry.ProgressFlags,
                JournalWord = entry.JournalWord,
                Repeatable = entry.Repeatable
            };
        }

        return new QuestListNetMsg
        {
            Active = active,
            CompletedMask = list.CompletedMask ?? [],
            CompletedQuestIds = list.CompletedQuestIds ?? [],
            MaxActive = list.MaxActive
        };
    }

    void IWorldEventPublisher.FanoutPartyAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyAskJoinMsg ask)
    {
        var payload = NetCodec.EncodePartyAskJoin(new PartyAskJoinNetMsg
        {
            RequestorObjectId = ask.RequestorObjectId,
            RequestorName = ask.RequestorName ?? "",
            ItemDistribution = ask.ItemDistribution
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PartyAskJoin, payload);
    }

    void IWorldEventPublisher.FanoutPartyJoinResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyJoinResultMsg result)
    {
        var payload = NetCodec.EncodePartyJoinResult(new PartyJoinResultNetMsg { Response = result.Response });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PartyJoinResult, payload);
    }

    void IWorldEventPublisher.FanoutPartyMemberList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyMemberListMsg list)
    {
        var src = list.Members ?? [];
        var members = new PartyMemberEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            members[i] = new PartyMemberEntryNet
            {
                ObjectId = src[i].ObjectId,
                Name = src[i].Name ?? "",
                ClassId = src[i].ClassId,
                Level = src[i].Level,
                CurHp = src[i].CurHp,
                MaxHp = src[i].MaxHp,
                CurMp = src[i].CurMp,
                MaxMp = src[i].MaxMp
            };
        }
        var payload = NetCodec.EncodePartyMemberList(new PartyMemberListNetMsg
        {
            LeaderObjectId = list.LeaderObjectId,
            PartyId = list.PartyId,
            Members = members
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PartyMemberList, payload);
    }

    void IWorldEventPublisher.FanoutTradeAsk(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeAskMsg ask)
    {
        var payload = NetCodec.EncodeTradeAsk(new TradeAskNetMsg
        {
            RequestorObjectId = ask.RequestorObjectId,
            RequestorName = ask.RequestorName ?? ""
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TradeAsk, payload);
    }

    void IWorldEventPublisher.FanoutTradeRequestResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeRequestResultMsg result)
    {
        var payload = NetCodec.EncodeTradeRequestResult(new TradeRequestResultNetMsg { Response = result.Response });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TradeRequestResult, payload);
    }

    void IWorldEventPublisher.FanoutTradeStart(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeStartMsg start)
    {
        var payload = NetCodec.EncodeTradeStart(new TradeStartNetMsg
        {
            PartnerObjectId = start.PartnerObjectId,
            PartnerName = start.PartnerName ?? ""
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TradeStart, payload);
    }

    void IWorldEventPublisher.FanoutTradeOfferUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeOfferUpdateMsg update)
    {
        var payload = NetCodec.EncodeTradeOfferUpdate(new TradeOfferUpdateNetMsg
        {
            IsOwn = update.IsOwn,
            InventoryObjectId = update.InventoryObjectId,
            ItemId = update.ItemId,
            Count = update.Count,
            EnchantLevel = update.EnchantLevel
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TradeOfferUpdate, payload);
    }

    void IWorldEventPublisher.FanoutTradeConfirmState(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeConfirmStateMsg state)
    {
        var payload = NetCodec.EncodeTradeConfirmState(new TradeConfirmStateNetMsg
        {
            SelfConfirmed = state.SelfConfirmed,
            PartnerConfirmed = state.PartnerConfirmed
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TradeConfirmState, payload);
    }

    void IWorldEventPublisher.FanoutTradeFinished(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeFinishedMsg finished)
    {
        var payload = NetCodec.EncodeTradeFinished(new TradeFinishedNetMsg { Response = finished.Response });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TradeFinished, payload);
    }

    void IWorldEventPublisher.FanoutPrivateStoreInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreInfoMsg info)
    {
        var payload = NetCodec.EncodePrivateStoreInfo(new PrivateStoreInfoNetMsg
        {
            ObjectId = info.ObjectId,
            StoreType = info.StoreType,
            Message = info.Message ?? ""
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PrivateStoreInfo, payload);
    }

    void IWorldEventPublisher.FanoutPrivateStoreListSell(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreListSellMsg list)
    {
        var payload = NetCodec.EncodePrivateStoreListSell(ToNetPrivateStoreListSell(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PrivateStoreListSell, payload);
    }

    void IWorldEventPublisher.FanoutPrivateStoreListBuy(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreListBuyMsg list)
    {
        var payload = NetCodec.EncodePrivateStoreListBuy(ToNetPrivateStoreListBuy(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PrivateStoreListBuy, payload);
    }

    static PrivateStoreListSellNetMsg ToNetPrivateStoreListSell(Fineage.Protocol.PrivateStoreListSellMsg list)
    {
        var src = list.Items ?? [];
        var items = new PrivateStoreSellRowNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            items[i] = new PrivateStoreSellRowNet
            {
                InventoryObjectId = src[i].InventoryObjectId,
                ItemId = src[i].ItemId,
                Count = src[i].Count,
                Price = src[i].Price,
                EnchantLevel = src[i].EnchantLevel
            };
        }

        return new PrivateStoreListSellNetMsg
        {
            StoreObjectId = list.StoreObjectId,
            StoreName = list.StoreName ?? "",
            Message = list.Message ?? "",
            Packaged = list.Packaged,
            BuyerAdena = list.BuyerAdena,
            Items = items
        };
    }

    static PrivateStoreListBuyNetMsg ToNetPrivateStoreListBuy(Fineage.Protocol.PrivateStoreListBuyMsg list)
    {
        var src = list.Items ?? [];
        var items = new PrivateStoreBuyRowNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            items[i] = new PrivateStoreBuyRowNet
            {
                ItemId = src[i].ItemId,
                Count = src[i].Count,
                Price = src[i].Price
            };
        }

        return new PrivateStoreListBuyNetMsg
        {
            StoreObjectId = list.StoreObjectId,
            StoreName = list.StoreName ?? "",
            Message = list.Message ?? "",
            StoreAdena = list.StoreAdena,
            Items = items
        };
    }

    void IWorldEventPublisher.FanoutRecipeBookList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeBookListMsg list)
    {
        var payload = NetCodec.EncodeRecipeBookList(new RecipeBookListNetMsg
        {
            Dwarven = list.Dwarven,
            RecipeIds = list.RecipeIds ?? []
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.RecipeBookList, payload);
    }

    void IWorldEventPublisher.FanoutRecipeItemMakeInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeItemMakeInfoMsg info)
    {
        var payload = NetCodec.EncodeRecipeItemMakeInfo(new RecipeItemMakeInfoNetMsg
        {
            RecipeId = info.RecipeId,
            Result = info.Result,
            CurrentMp = info.CurrentMp,
            MaxMp = info.MaxMp
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.RecipeItemMakeInfo, payload);
    }

    void IWorldEventPublisher.FanoutRecipeShopSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeShopSellListMsg list)
    {
        var src = list.Items ?? [];
        var items = new RecipeShopRowNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            items[i] = new RecipeShopRowNet
            {
                RecipeId = src[i].RecipeId,
                Price = src[i].Price
            };
        }
        var payload = NetCodec.EncodeRecipeShopSellList(new RecipeShopSellListNetMsg
        {
            ManufacturerObjectId = list.ManufacturerObjectId,
            CurrentMp = list.CurrentMp,
            MaxMp = list.MaxMp,
            BuyerAdena = list.BuyerAdena,
            Items = items
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.RecipeShopSellList, payload);
    }

    void IWorldEventPublisher.FanoutRecipeShopItemInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeShopItemInfoMsg info)
    {
        var payload = NetCodec.EncodeRecipeShopItemInfo(new RecipeShopItemInfoNetMsg
        {
            ManufacturerObjectId = info.ManufacturerObjectId,
            RecipeId = info.RecipeId,
            CurrentMp = info.CurrentMp,
            MaxMp = info.MaxMp,
            Result = info.Result
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.RecipeShopItemInfo, payload);
    }

    void IWorldEventPublisher.FanoutFriendInviteAsk(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendInviteAskMsg ask)
    {
        var payload = NetCodec.EncodeFriendInviteAsk(new FriendInviteAskNetMsg
        {
            RequestorName = ask.RequestorName ?? ""
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.FriendInviteAsk, payload);
    }

    void IWorldEventPublisher.FanoutFriendList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendListMsg list)
    {
        var payload = NetCodec.EncodeFriendList(ToNetFriendList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.FriendList, payload);
    }

    void IWorldEventPublisher.FanoutFriendUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendUpdateMsg update)
    {
        var payload = NetCodec.EncodeFriendUpdate(new FriendUpdateNetMsg
        {
            Added = update.Added,
            ObjectId = update.ObjectId,
            Name = update.Name ?? "",
            Online = update.Online
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.FriendUpdate, payload);
    }

    void IWorldEventPublisher.FanoutBlockList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BlockListMsg list)
    {
        var src = list.Blocked ?? [];
        var rows = new FriendRowNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            rows[i] = new FriendRowNet
            {
                ObjectId = src[i].ObjectId,
                Name = src[i].Name ?? "",
                Online = src[i].Online
            };
        }
        var payload = NetCodec.EncodeBlockList(new BlockListNetMsg
        {
            BlockAll = list.BlockAll,
            Blocked = rows
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.BlockList, payload);
    }

    static FriendListNetMsg ToNetFriendList(Fineage.Protocol.FriendListMsg list)
    {
        var src = list.Friends ?? [];
        var rows = new FriendRowNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            rows[i] = new FriendRowNet
            {
                ObjectId = src[i].ObjectId,
                Name = src[i].Name ?? "",
                Online = src[i].Online
            };
        }

        return new FriendListNetMsg { Friends = rows };
    }

    void IWorldEventPublisher.FanoutBossStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BossStatusMsg status)
    {
        var src = status.Bosses ?? [];
        var bosses = new BossStatusEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var row = src[i];
            bosses[i] = new BossStatusEntryNet
            {
                NpcId = row.NpcId,
                ObjectId = row.ObjectId,
                Kind = row.Kind,
                Status = row.Status,
                RespawnUnixMs = row.RespawnUnixMs,
                CurrentHp = row.CurrentHp,
                MaxHp = row.MaxHp,
                CurrentMp = row.CurrentMp,
                MaxMp = row.MaxMp,
                Enabled = row.Enabled,
                Spawned = row.Spawned
            };
        }
        var payload = NetCodec.EncodeBossStatus(new BossStatusNetMsg { Bosses = bosses });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.BossStatus, payload);
    }

    void IWorldEventPublisher.FanoutSiegeInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SiegeInfoMsg info)
    {
        var clans = info.Clans ?? [];
        var netClans = new SiegeClanNet[clans.Length];
        for (int i = 0; i < clans.Length; i++)
        {
            var row = clans[i];
            netClans[i] = new SiegeClanNet { ClanId = row.ClanId, Side = row.Side };
        }
        var payload = NetCodec.EncodeSiegeInfo(new SiegeInfoNetMsg
        {
            CastleId = info.CastleId,
            Name = info.Name ?? "",
            OwnerClanId = info.OwnerClanId,
            TaxPercent = info.TaxPercent,
            SiegeUnixMs = info.SiegeUnixMs,
            RegistrationDeadlineUnixMs = info.RegistrationDeadlineUnixMs,
            RegistrationClosed = info.RegistrationClosed,
            InProgress = info.InProgress,
            EndsUnixMs = info.EndsUnixMs,
            Clans = netClans
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SiegeInfo, payload);
    }

    void IWorldEventPublisher.FanoutOlympiadStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadStatusMsg status)
    {
        var payload = NetCodec.EncodeOlympiadStatus(new OlympiadStatusNetMsg
        {
            Period = status.Period,
            Cycle = status.Cycle,
            InCompetition = status.InCompetition,
            CompetitionStartUnixMs = status.CompetitionStartUnixMs,
            CompetitionEndUnixMs = status.CompetitionEndUnixMs,
            OlympiadEndUnixMs = status.OlympiadEndUnixMs,
            ValidationEndUnixMs = status.ValidationEndUnixMs,
            NextWeeklyUnixMs = status.NextWeeklyUnixMs,
            RegistrationOpen = status.RegistrationOpen,
            Noble = status.Noble,
            Points = status.Points,
            CompetitionsDone = status.CompetitionsDone,
            Won = status.Won,
            Lost = status.Lost,
            Drawn = status.Drawn,
            Queue = status.Queue,
            MatchId = status.MatchId,
            StadiumId = status.StadiumId,
            ClassId = status.ClassId
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.OlympiadStatus, payload);
    }

    void IWorldEventPublisher.FanoutOlympiadMatch(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadMatchMsg match)
    {
        var payload = NetCodec.EncodeOlympiadMatch(new OlympiadMatchNetMsg
        {
            MatchId = match.MatchId,
            StadiumId = match.StadiumId,
            ZoneId = match.ZoneId,
            Classed = match.Classed,
            Phase = match.Phase,
            PlayerOneId = match.PlayerOneId,
            PlayerTwoId = match.PlayerTwoId,
            PlayerOneName = match.PlayerOneName ?? "",
            PlayerTwoName = match.PlayerTwoName ?? "",
            InstanceId = match.InstanceId
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.OlympiadMatch, payload);
    }

    void IWorldEventPublisher.FanoutOlympiadCountdown(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadCountdownMsg countdown)
    {
        var payload = NetCodec.EncodeOlympiadCountdown(new OlympiadCountdownNetMsg
        {
            MatchId = countdown.MatchId,
            Kind = countdown.Kind,
            Seconds = countdown.Seconds
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.OlympiadCountdown, payload);
    }

    void IWorldEventPublisher.FanoutOlympiadResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadResultMsg result)
    {
        var payload = NetCodec.EncodeOlympiadResult(new OlympiadResultNetMsg
        {
            MatchId = result.MatchId,
            Outcome = result.Outcome,
            WinnerObjectId = result.WinnerObjectId,
            PointsOne = result.PointsOne,
            PointsTwo = result.PointsTwo,
            DeltaOne = result.DeltaOne,
            DeltaTwo = result.DeltaTwo,
            RewardItemId = result.RewardItemId,
            RewardCount = result.RewardCount,
            CompetitionsDoneOne = result.CompetitionsDoneOne,
            CompetitionsDoneTwo = result.CompetitionsDoneTwo
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.OlympiadResult, payload);
    }

    void IWorldEventPublisher.FanoutSevenSignsStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SevenSignsStatusMsg status)
    {
        var payload = NetCodec.EncodeSevenSignsStatus(new SevenSignsStatusNetMsg
        {
            Period = status.Period,
            Cycle = status.Cycle,
            PlayerCabal = status.PlayerCabal,
            PlayerSeal = status.PlayerSeal,
            PlayerStoneCount = status.PlayerStoneCount,
            PlayerAncientAdena = status.PlayerAncientAdena,
            PlayerContribution = status.PlayerContribution,
            DawnScore = status.DawnScore,
            DuskScore = status.DuskScore,
            DawnFestivalScore = status.DawnFestivalScore,
            DuskFestivalScore = status.DuskFestivalScore,
            DawnStonePoints = status.DawnStonePoints,
            DuskStonePoints = status.DuskStonePoints,
            AvariceOwner = status.AvariceOwner,
            GnosisOwner = status.GnosisOwner,
            StrifeOwner = status.StrifeOwner,
            DawnMembers = status.DawnMembers,
            DuskMembers = status.DuskMembers,
            WinningCabal = status.WinningCabal,
            NextChangeUnixMs = status.NextChangeUnixMs,
            SkyState = status.SkyState,
            DawnAvarice = status.DawnAvarice,
            DawnGnosis = status.DawnGnosis,
            DawnStrife = status.DawnStrife,
            DuskAvarice = status.DuskAvarice,
            DuskGnosis = status.DuskGnosis,
            DuskStrife = status.DuskStrife,
            PreviousWinner = status.PreviousWinner
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SevenSignsStatus, payload);
    }

    void IWorldEventPublisher.FanoutSevenSignsSky(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SevenSignsSkyMsg sky)
    {
        var payload = NetCodec.EncodeSevenSignsSky(new SevenSignsSkyNetMsg
        {
            State = sky.State,
            Period = sky.Period,
            WinningCabal = sky.WinningCabal
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SevenSignsSky, payload);
    }

    void IWorldEventPublisher.FanoutDungeonEntered(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.DungeonEnteredMsg entered)
    {
        var payload = NetCodec.EncodeDungeonEntered(new DungeonEnteredNetMsg
        {
            DungeonId = entered.DungeonId,
            X = entered.X,
            Y = entered.Y,
            Z = entered.Z,
            Inside = entered.Inside,
            Action = entered.Action
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.DungeonEntered, payload);
    }

    public async Task HandleAsync(NetConnection conn, CancellationToken ct)
    {
        var zoneSession = new ZoneSession();
        EntityId self = default;
        int registeredObjectId = 0;

        // Same read-loop guards the V2 listener had: a silent client and a flooding client both
        // close instead of occupying a slot forever. Touch on every frame, gate on the fixed window.
        using var idle = new SessionIdleWatchdog(ct, _idleTimeout);
        var rate = new MessageRateGate(_maxMessagesPerSecond);
        CancellationToken readCt = idle.Token;

        try
        {
            await foreach (var envelope in conn.Inbox.ReadAllAsync(readCt).ConfigureAwait(false))
            {
                if (!rate.TryConsume())
                {
                    await RejectAsync(conn, "rate_limit", "Too many messages per second.", readCt)
                        .ConfigureAwait(false);
                    break;
                }

                idle.Touch();
                try
                {
                    switch (envelope.Opcode)
                    {
                        case NetOpcode.Hello:
                            await _session.OnHelloAsync(conn, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ZoneAuth:
                            self = await _session.OnAuthAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            if (self.Value != 0 && zoneSession.Bound)
                            {
                                if (registeredObjectId != 0 && registeredObjectId != zoneSession.ObjectId)
                                    _presence.Unregister(registeredObjectId, conn);
                                _presence.Register(zoneSession.ObjectId, conn, self);
                                registeredObjectId = zoneSession.ObjectId;
                            }

                            break;
                        case NetOpcode.EnterWorld:
                            await _session.OnEnterAsync(conn, zoneSession, self, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.MoveInput:
                            await _movement.OnMoveAsync(conn, zoneSession, self, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.SelectTarget:
                            await _combat.OnSelectTargetAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.TargetCancelRequest:
                            await _combat.OnTargetCancelAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.AttackRequest:
                            await _combat.OnAttackAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PickupRequest:
                            await _inventoryActions.OnPickupAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.DropItemRequest:
                            await _inventoryActions.OnDropAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.DestroyItemRequest:
                            await _inventoryActions.OnDestroyAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.UseItem:
                            await _inventoryActions.OnUseItemAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.UnequipItem:
                            await _inventoryActions.OnUnequipAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.Say:
                            await _chat.OnSayAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.UseSkill:
                            await _combat.OnUseSkillAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ReviveRequest:
                            await _characterActions.OnReviveAsync(conn, zoneSession, self, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestAcquireSkillInfo:
                            await _skillLearn.OnAcquireAsync(conn, zoneSession, envelope, informing: true, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestAcquireSkill:
                            await _skillLearn.OnAcquireAsync(conn, zoneSession, envelope, informing: false, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.EnchantRequest:
                            await _enchant.OnRequestAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.EnchantConfirm:
                            await _enchant.OnConfirmAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.EnchantCancel:
                            await _enchant.OnCancelAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanCreateRequest:
                            await _clan.OnCreateAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanInviteRequest:
                            await _clan.OnInviteAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanAnswerJoinRequest:
                            await _clan.OnAnswerAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanLeaveRequest:
                            await _clan.OnLeaveAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanKickRequest:
                            await _clan.OnKickAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanDissolveRequest:
                            await _clan.OnDissolveAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanCancelDissolveRequest:
                            await _clan.OnCancelDissolveAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.NpcTalk:
                            await _npcTalk.OnTalkAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.BypassRequest:
                            await _characterActions.OnBypassAsync(conn, zoneSession, self, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestBuyItem:
                            await _shop.OnBuyAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestSellItem:
                            await _shop.OnSellAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestMultiSellChoose:
                            await _shop.OnMultiSellAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.WarehouseDepositRequest:
                            await _warehouse.OnDepositAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.WarehouseWithdrawRequest:
                            await _warehouse.OnWithdrawAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestQuestList:
                            await _quests.OnListAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestQuestAbort:
                            await _quests.OnAbortAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PartyInviteRequest:
                            await _party.OnInviteAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PartyAnswerJoinRequest:
                            await _party.OnAnswerAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PartyLeaveRequest:
                            await _party.OnLeaveAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PartyKickRequest:
                            await _party.OnKickAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.TradeRequest:
                            await _trade.OnRequestAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.TradeAnswerRequest:
                            await _trade.OnAnswerAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.TradeAddItemRequest:
                            await _trade.OnAddItemAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.TradeDoneRequest:
                            await _trade.OnDoneAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ValidatePosition:
                            await _movement.OnValidatePositionAsync(conn, zoneSession, self, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestActionUse:
                            await _shortcutActions.OnActionUseAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestAutoSoulShot:
                            await _shortcutActions.OnAutoSoulShotAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestShortCutReg:
                            await _shortcutActions.OnShortCutRegAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestShortCutDel:
                            await _shortcutActions.OnShortCutDelAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.BossStatusRequest:
                            await _worldEvents.OnBossStatusAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.AdminBossSchedule:
                            await _worldEvents.OnAdminBossAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.SiegeListRequest:
                            await _worldEvents.OnSiegeListAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.SiegeRegisterRequest:
                            await _worldEvents.OnSiegeRegisterAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.SiegeDefenderAnswerRequest:
                            await _worldEvents.OnSiegeAnswerAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.AdminCaptureCastle:
                            await _worldEvents.OnAdminCaptureCastleAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.OlympiadStatusRequest:
                            await _worldEvents.OnOlympiadStatusAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.OlympiadRegisterRequest:
                            await _worldEvents.OnOlympiadRegisterAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.OlympiadUnregisterRequest:
                            await _worldEvents.OnOlympiadUnregisterAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.OlympiadLeaveRequest:
                            await _worldEvents.OnOlympiadLeaveAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.AdminReportOlympiadResult:
                            await _worldEvents.OnAdminReportOlympiadAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.SevenSignsStatusRequest:
                            await _worldEvents.OnSevenSignsStatusAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.SevenSignsActionRequest:
                            await _worldEvents.OnSevenSignsActionAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.DungeonEnterRequest:
                            await _worldEvents.OnDungeonAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.SetPrivateStoreSell:
                            await _privateStore.OnSetSellAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PrivateStoreQuit:
                            await _privateStore.OnQuitAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PrivateStoreListRequest:
                            await _privateStore.OnListAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PrivateStoreBuy:
                            await _privateStore.OnBuyAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.SetPrivateStoreBuy:
                            await _privateStore.OnSetBuyAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PrivateStoreSellTo:
                            await _privateStore.OnSellToAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RecipeBookOpen:
                            await _recipes.OnBookOpenAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RecipeMakeSelf:
                            await _recipes.OnMakeSelfAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.SetRecipeShop:
                            await _recipes.OnSetShopAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RecipeShopMakeInfo:
                            await _recipes.OnShopMakeInfoAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RecipeShopMakeItem:
                            await _recipes.OnShopMakeItemAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.FriendInvite:
                            await _friends.OnInviteAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.FriendAnswer:
                            await _friends.OnAnswerAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.FriendListRequest:
                            await _friends.OnListRequestAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.FriendDelete:
                            await _friends.OnDeleteAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.BlockRequest:
                            await _friends.OnBlockAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.StopMove:
                            await _movement.OnStopMoveAsync(conn, zoneSession, self, envelope, ct).ConfigureAwait(false);
                            break;
                        default:
                            await RejectAsync(conn, "unknown_opcode", $"opcode 0x{envelope.Opcode:X4}", ct)
                                .ConfigureAwait(false);
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ZoneLog.Info($"handler_error session={zoneSession.WireSessionId} opcode=0x{envelope.Opcode:X4}: {ex}");
                    await RejectAsync(conn, "handler_error", "An internal error occurred.", ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await _session.LeaveAsync(zoneSession, self, registeredObjectId, conn).ConfigureAwait(false);
        }
    }

    static Task RejectAsync(NetConnection conn, string code, string message, CancellationToken ct)
    {
        NetTelemetry.PacketRejected("zone");
        return conn.SendAsync(NetOpcode.Reject, NetCodec.EncodeReject(new NetReject { Code = code, Message = message }), ct: ct);
    }

    static BuyListNetMsg ToNetBuyList(Fineage.Protocol.BuyListMsg buy)
    {
        var src = buy.Items ?? [];
        var items = new BuyListItemNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            items[i] = new BuyListItemNet
            {
                ItemId = src[i].ItemId,
                Price = src[i].Price,
                Count = src[i].Count
            };
        }

        return new BuyListNetMsg
        {
            NpcObjectId = buy.NpcObjectId,
            ListId = buy.ListId,
            BaseTaxPercent = buy.BaseTaxPercent,
            CastleTaxPercent = buy.CastleTaxPercent,
            Items = items
        };
    }

    static SellListNetMsg ToNetSellList(Fineage.Protocol.SellListMsg sell)
    {
        var src = sell.Items ?? [];
        var items = new SellListItemNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            items[i] = new SellListItemNet
            {
                ObjectId = src[i].ObjectId,
                ItemId = src[i].ItemId,
                Count = src[i].Count,
                Price = src[i].Price,
                EnchantLevel = src[i].EnchantLevel
            };
        }

        return new SellListNetMsg { NpcObjectId = sell.NpcObjectId, Items = items };
    }

    static MultiSellListNetMsg ToNetMultiSellList(Fineage.Protocol.MultiSellListMsg multi)
    {
        var src = multi.Entries ?? [];
        var entries = new MultiSellEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            entries[i] = new MultiSellEntryNet
            {
                EntryId = src[i].EntryId,
                Ingredients = ToNetMultiSellParts(src[i].Ingredients),
                Products = ToNetMultiSellParts(src[i].Products)
            };
        }

        return new MultiSellListNetMsg
        {
            NpcObjectId = multi.NpcObjectId,
            ListId = multi.ListId,
            MaintainEnchantment = multi.MaintainEnchantment,
            InventoryOnly = multi.InventoryOnly,
            CastleTaxPercent = multi.CastleTaxPercent,
            Entries = entries
        };
    }

    static MultiSellPartNet[] ToNetMultiSellParts(Fineage.Protocol.MultiSellPartMsg[]? parts)
    {
        parts ??= [];
        var rows = new MultiSellPartNet[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            rows[i] = new MultiSellPartNet
            {
                ItemId = parts[i].ItemId,
                Count = parts[i].Count,
                EnchantLevel = parts[i].EnchantLevel
            };
        }

        return rows;
    }

    static WarehouseDepositListNetMsg ToNetWarehouseDepositList(Fineage.Protocol.WarehouseDepositListMsg list) => new()
    {
        NpcObjectId = list.NpcObjectId,
        FeePerLine = list.FeePerLine,
        SlotsUsed = list.SlotsUsed,
        SlotsMax = list.SlotsMax,
        Adena = list.Adena,
        Items = ToNetWarehouseItemRows(list.Items)
    };

    static WarehouseWithdrawListNetMsg ToNetWarehouseWithdrawList(Fineage.Protocol.WarehouseWithdrawListMsg list) => new()
    {
        NpcObjectId = list.NpcObjectId,
        SlotsUsed = list.SlotsUsed,
        SlotsMax = list.SlotsMax,
        InventorySlotsUsed = list.InventorySlotsUsed,
        InventorySlotsMax = list.InventorySlotsMax,
        CurrentWeight = list.CurrentWeight,
        MaxWeight = list.MaxWeight,
        Items = ToNetWarehouseItemRows(list.Items)
    };

    static ItemListEntryNet[] ToNetWarehouseItemRows(Fineage.Protocol.ItemListEntryMsg[]? items)
    {
        items ??= [];
        var rows = new ItemListEntryNet[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            rows[i] = new ItemListEntryNet
            {
                ObjectId = items[i].ObjectId,
                ItemId = items[i].ItemId,
                Count = items[i].Count,
                EnchantLevel = items[i].EnchantLevel,
                EquippedSlot = items[i].EquippedSlot
            };
        }

        return rows;
    }
}
