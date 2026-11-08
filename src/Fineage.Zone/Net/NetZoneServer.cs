using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.World;
using NetHelloAck = Fineage.Net.Contract.HelloAckMsg;
using NetReject = Fineage.Net.Contract.RejectMsg;

namespace Fineage.Zone.Net;

/// <summary>
/// Fineage Net zone path. Reuses <see cref="ZoneWorld.TryEnterAsync"/> / <see cref="ZoneWorld.TryMoveAsync"/>;
/// Net packets are the only client-facing transport; presence (spawn / move / despawn) is fanned out via
/// <see cref="NetPresenceHub"/>.
/// </summary>
public sealed class NetZoneServer : INetPresenceBridge
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

    void INetPresenceBridge.FanoutMoveState(
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

    void INetPresenceBridge.FanoutKnownlistDiff(int selfObjectId, IReadOnlyList<int> added, IReadOnlyList<int> removed)
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

    void INetPresenceBridge.FanoutChangeMoveType(IReadOnlyList<int> watcherObjectIds, int moverObjectId, bool running, float runSpeed, float walkSpeed, bool inWater)
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

    void INetPresenceBridge.FanoutAttackStance(IReadOnlyList<int> watcherObjectIds, int objectId, bool inStance)
    {
        var id = _ids.AllocateOrGet(objectId);
        var payload = NetCodec.EncodeAttackStance(new AttackStanceNetMsg { EntityValue = id.Value, EntityGeneration = id.Generation });
        ushort opcode = inStance ? NetOpcode.AutoAttackStart : NetOpcode.AutoAttackStop;
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, opcode, payload);
    }

    void INetPresenceBridge.FanoutAutoSoulShot(IReadOnlyList<int> watcherObjectIds, int itemId, bool enabled)
    {
        var payload = NetCodec.EncodeAutoSoulShot(new AutoSoulShotNetMsg { ItemId = itemId, Enabled = enabled });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AutoSoulShot, payload);
    }

    void INetPresenceBridge.FanoutUserInfo(IReadOnlyList<int> watcherObjectIds, UserInfoNetMsg info)
    {
        var payload = NetCodec.EncodeUserInfo(info);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.UserInfo, payload);
    }

    void INetPresenceBridge.FanoutCharInfo(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        if (watcherObjectIds.Count == 0 || !_world.TryGetEntityById(objectId, out var entity) ||
            entity.Kind != ZoneEntityKind.Player)
            return;
        var payload = NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(entity, _ids.AllocateOrGet(objectId), PlayerSpawnPAtkSpd(entity)));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.EntitySpawn, payload);
    }

    void INetPresenceBridge.FanoutAttackState(IReadOnlyList<int> watcherObjectIds, AttackStateMsg attack)
    {
        var payload = NetCodec.EncodeAttackState(attack);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AttackState, payload);
    }

    void INetPresenceBridge.FanoutStatusUpdate(IReadOnlyList<int> watcherObjectIds, StatusUpdateNetMsg status)
    {
        var payload = NetCodec.EncodeStatusUpdate(status);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.StatusUpdate, payload);
    }

    void INetPresenceBridge.FanoutDie(IReadOnlyList<int> watcherObjectIds, EntityDieMsg die)
    {
        var payload = NetCodec.EncodeEntityDie(die);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.EntityDie, payload);
    }

    void INetPresenceBridge.FanoutDropItem(IReadOnlyList<int> watcherObjectIds, DropItemNetMsg drop)
    {
        var payload = NetCodec.EncodeDropItem(drop);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.DropItem, payload);
    }

    void INetPresenceBridge.FanoutEntityDespawn(IReadOnlyList<int> watcherObjectIds, EntityDespawnMsg despawn)
    {
        var payload = NetCodec.EncodeEntityDespawn(despawn);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.EntityDespawn, payload);
    }

    void INetPresenceBridge.FanoutInventoryUpdate(IReadOnlyList<int> watcherObjectIds, InventoryUpdateNetMsg update)
    {
        var payload = NetCodec.EncodeInventoryUpdate(update);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.InventoryUpdate, payload);
    }

    void INetPresenceBridge.FanoutEtcStatusUpdate(IReadOnlyList<int> watcherObjectIds, EtcStatusUpdateNetMsg status)
    {
        var payload = NetCodec.EncodeEtcStatusUpdate(status);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.EtcStatusUpdate, payload);
    }

    void INetPresenceBridge.FanoutCreatureSay(IReadOnlyList<int> watcherObjectIds, CreatureSayNetMsg say)
    {
        var payload = NetCodec.EncodeCreatureSay(say);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.CreatureSay, payload);
    }

    void INetPresenceBridge.FanoutSystemMessage(IReadOnlyList<int> watcherObjectIds, SystemMessageNetMsg message)
    {
        var payload = NetCodec.EncodeSystemMessage(message);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SystemMessage, payload);
    }

    void INetPresenceBridge.FanoutMagicSkillUse(IReadOnlyList<int> watcherObjectIds, MagicSkillUseNetMsg cast)
    {
        var payload = NetCodec.EncodeMagicSkillUse(cast);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.MagicSkillUse, payload);
    }

    void INetPresenceBridge.FanoutRevive(IReadOnlyList<int> watcherObjectIds, ReviveNetMsg revive)
    {
        var payload = NetCodec.EncodeRevive(revive);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.Revive, payload);
    }

    void INetPresenceBridge.FanoutTeleportToLocation(IReadOnlyList<int> watcherObjectIds, TeleportToLocationNetMsg teleport)
    {
        var payload = NetCodec.EncodeTeleportToLocation(teleport);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TeleportToLocation, payload);
    }

    void INetPresenceBridge.FanoutPlayerStats(IReadOnlyList<int> watcherObjectIds, PlayerStatsMsg stats)
    {
        var payload = NetCodec.EncodePlayerStats(stats);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PlayerStats, payload);
    }

    void INetPresenceBridge.FanoutAbnormalStatusUpdate(IReadOnlyList<int> watcherObjectIds, AbnormalStatusUpdateNetMsg update)
    {
        var payload = NetCodec.EncodeAbnormalStatusUpdate(update);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AbnormalStatusUpdate, payload);
    }

    void INetPresenceBridge.FanoutNpcHtml(IReadOnlyList<int> watcherObjectIds, NpcHtmlNetMsg html)
    {
        var payload = NetCodec.EncodeNpcHtml(html);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.NpcHtml, payload);
    }

    void INetPresenceBridge.FanoutBuyList(IReadOnlyList<int> watcherObjectIds, BuyListNetMsg list)
    {
        var payload = NetCodec.EncodeBuyList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.BuyList, payload);
    }

    void INetPresenceBridge.FanoutSellList(IReadOnlyList<int> watcherObjectIds, SellListNetMsg list)
    {
        var payload = NetCodec.EncodeSellList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SellList, payload);
    }

    void INetPresenceBridge.FanoutMultiSellList(IReadOnlyList<int> watcherObjectIds, MultiSellListNetMsg list)
    {
        var payload = NetCodec.EncodeMultiSellList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.MultiSellList, payload);
    }

    void INetPresenceBridge.FanoutShopBalance(IReadOnlyList<int> watcherObjectIds, ShopBalanceNetMsg balance)
    {
        var payload = NetCodec.EncodeShopBalance(balance);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ShopBalance, payload);
    }

    void INetPresenceBridge.FanoutWarehouseDepositList(IReadOnlyList<int> watcherObjectIds, WarehouseDepositListNetMsg list)
    {
        var payload = NetCodec.EncodeWarehouseDepositList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.WarehouseDepositList, payload);
    }

    void INetPresenceBridge.FanoutWarehouseWithdrawList(IReadOnlyList<int> watcherObjectIds, WarehouseWithdrawListNetMsg list)
    {
        var payload = NetCodec.EncodeWarehouseWithdrawList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.WarehouseWithdrawList, payload);
    }

    void INetPresenceBridge.FanoutSkillList(IReadOnlyList<int> watcherObjectIds, SkillListNetMsg list)
    {
        var payload = NetCodec.EncodeSkillList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SkillList, payload);
    }

    void INetPresenceBridge.FanoutChangeWaitType(IReadOnlyList<int> watcherObjectIds, ChangeWaitTypeNetMsg waitType)
    {
        var payload = NetCodec.EncodeChangeWaitType(waitType);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ChangeWaitType, payload);
    }

    void INetPresenceBridge.FanoutSocialAction(IReadOnlyList<int> watcherObjectIds, SocialActionNetMsg action)
    {
        var payload = NetCodec.EncodeSocialAction(action);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SocialAction, payload);
    }

    void INetPresenceBridge.FanoutTargetSelected(IReadOnlyList<int> watcherObjectIds, TargetSelectedNetMsg selected)
    {
        var payload = NetCodec.EncodeTargetSelected(selected);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TargetSelected, payload);
    }

    void INetPresenceBridge.FanoutTargetUnselected(IReadOnlyList<int> watcherObjectIds, TargetUnselectedNetMsg unselected)
    {
        var payload = NetCodec.EncodeTargetUnselected(unselected);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TargetUnselected, payload);
    }

    void INetPresenceBridge.FanoutAcquireSkillList(IReadOnlyList<int> watcherObjectIds, AcquireSkillListNetMsg list)
    {
        var payload = NetCodec.EncodeAcquireSkillList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AcquireSkillList, payload);
    }

    void INetPresenceBridge.FanoutAcquireSkillInfo(IReadOnlyList<int> watcherObjectIds, AcquireSkillInfoNetMsg info)
    {
        var payload = NetCodec.EncodeAcquireSkillInfo(info);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AcquireSkillInfo, payload);
    }

    void INetPresenceBridge.FanoutEnchantResult(IReadOnlyList<int> watcherObjectIds, EnchantResultNetMsg result)
    {
        var payload = NetCodec.EncodeEnchantResult(result);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.EnchantResult, payload);
    }

    void INetPresenceBridge.FanoutPledgeStatus(IReadOnlyList<int> watcherObjectIds, PledgeStatusNetMsg status)
    {
        var payload = NetCodec.EncodePledgeStatus(status);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PledgeStatus, payload);
    }

    void INetPresenceBridge.FanoutPledgeMemberList(IReadOnlyList<int> watcherObjectIds, PledgeMemberListNetMsg list)
    {
        var payload = NetCodec.EncodePledgeMemberList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PledgeMemberList, payload);
    }

    void INetPresenceBridge.FanoutClanAskJoin(IReadOnlyList<int> watcherObjectIds, ClanAskJoinNetMsg ask)
    {
        var payload = NetCodec.EncodeClanAskJoin(ask);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ClanAskJoin, payload);
    }

    void INetPresenceBridge.FanoutClanJoinResult(IReadOnlyList<int> watcherObjectIds, ClanJoinResultNetMsg result)
    {
        var payload = NetCodec.EncodeClanJoinResult(result);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ClanJoinResult, payload);
    }

    void INetPresenceBridge.FanoutQuestList(IReadOnlyList<int> watcherObjectIds, QuestListNetMsg list)
    {
        var payload = NetCodec.EncodeQuestList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.QuestList, payload);
    }

    void INetPresenceBridge.FanoutPartyAskJoin(IReadOnlyList<int> watcherObjectIds, PartyAskJoinNetMsg ask)
    {
        var payload = NetCodec.EncodePartyAskJoin(ask);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PartyAskJoin, payload);
    }

    void INetPresenceBridge.FanoutPartyJoinResult(IReadOnlyList<int> watcherObjectIds, PartyJoinResultNetMsg result)
    {
        var payload = NetCodec.EncodePartyJoinResult(result);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PartyJoinResult, payload);
    }

    void INetPresenceBridge.FanoutPartyMemberList(IReadOnlyList<int> watcherObjectIds, PartyMemberListNetMsg list)
    {
        var payload = NetCodec.EncodePartyMemberList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PartyMemberList, payload);
    }

    void INetPresenceBridge.FanoutTradeAsk(IReadOnlyList<int> watcherObjectIds, TradeAskNetMsg ask)
    {
        var payload = NetCodec.EncodeTradeAsk(ask);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TradeAsk, payload);
    }

    void INetPresenceBridge.FanoutTradeRequestResult(IReadOnlyList<int> watcherObjectIds, TradeRequestResultNetMsg result)
    {
        var payload = NetCodec.EncodeTradeRequestResult(result);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TradeRequestResult, payload);
    }

    void INetPresenceBridge.FanoutTradeStart(IReadOnlyList<int> watcherObjectIds, TradeStartNetMsg start)
    {
        var payload = NetCodec.EncodeTradeStart(start);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TradeStart, payload);
    }

    void INetPresenceBridge.FanoutTradeOfferUpdate(IReadOnlyList<int> watcherObjectIds, TradeOfferUpdateNetMsg update)
    {
        var payload = NetCodec.EncodeTradeOfferUpdate(update);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TradeOfferUpdate, payload);
    }

    void INetPresenceBridge.FanoutTradeConfirmState(IReadOnlyList<int> watcherObjectIds, TradeConfirmStateNetMsg state)
    {
        var payload = NetCodec.EncodeTradeConfirmState(state);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TradeConfirmState, payload);
    }

    void INetPresenceBridge.FanoutTradeFinished(IReadOnlyList<int> watcherObjectIds, TradeFinishedNetMsg finished)
    {
        var payload = NetCodec.EncodeTradeFinished(finished);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TradeFinished, payload);
    }

    void INetPresenceBridge.FanoutPrivateStoreInfo(IReadOnlyList<int> watcherObjectIds, PrivateStoreInfoNetMsg info)
    {
        var payload = NetCodec.EncodePrivateStoreInfo(info);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PrivateStoreInfo, payload);
    }

    void INetPresenceBridge.FanoutPrivateStoreListSell(IReadOnlyList<int> watcherObjectIds, PrivateStoreListSellNetMsg list)
    {
        var payload = NetCodec.EncodePrivateStoreListSell(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PrivateStoreListSell, payload);
    }

    void INetPresenceBridge.FanoutPrivateStoreListBuy(IReadOnlyList<int> watcherObjectIds, PrivateStoreListBuyNetMsg list)
    {
        var payload = NetCodec.EncodePrivateStoreListBuy(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PrivateStoreListBuy, payload);
    }

    void INetPresenceBridge.FanoutRecipeBookList(IReadOnlyList<int> watcherObjectIds, RecipeBookListNetMsg list)
    {
        var payload = NetCodec.EncodeRecipeBookList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.RecipeBookList, payload);
    }

    void INetPresenceBridge.FanoutRecipeItemMakeInfo(IReadOnlyList<int> watcherObjectIds, RecipeItemMakeInfoNetMsg info)
    {
        var payload = NetCodec.EncodeRecipeItemMakeInfo(info);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.RecipeItemMakeInfo, payload);
    }

    void INetPresenceBridge.FanoutRecipeShopSellList(IReadOnlyList<int> watcherObjectIds, RecipeShopSellListNetMsg list)
    {
        var payload = NetCodec.EncodeRecipeShopSellList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.RecipeShopSellList, payload);
    }

    void INetPresenceBridge.FanoutRecipeShopItemInfo(IReadOnlyList<int> watcherObjectIds, RecipeShopItemInfoNetMsg info)
    {
        var payload = NetCodec.EncodeRecipeShopItemInfo(info);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.RecipeShopItemInfo, payload);
    }

    void INetPresenceBridge.FanoutFriendInviteAsk(IReadOnlyList<int> watcherObjectIds, FriendInviteAskNetMsg ask)
    {
        var payload = NetCodec.EncodeFriendInviteAsk(ask);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.FriendInviteAsk, payload);
    }

    void INetPresenceBridge.FanoutFriendList(IReadOnlyList<int> watcherObjectIds, FriendListNetMsg list)
    {
        var payload = NetCodec.EncodeFriendList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.FriendList, payload);
    }

    void INetPresenceBridge.FanoutFriendUpdate(IReadOnlyList<int> watcherObjectIds, FriendUpdateNetMsg update)
    {
        var payload = NetCodec.EncodeFriendUpdate(update);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.FriendUpdate, payload);
    }

    void INetPresenceBridge.FanoutBlockList(IReadOnlyList<int> watcherObjectIds, BlockListNetMsg list)
    {
        var payload = NetCodec.EncodeBlockList(list);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.BlockList, payload);
    }

    void INetPresenceBridge.FanoutBossStatus(IReadOnlyList<int> watcherObjectIds, BossStatusNetMsg status)
    {
        var payload = NetCodec.EncodeBossStatus(status);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.BossStatus, payload);
    }

    void INetPresenceBridge.FanoutSiegeInfo(IReadOnlyList<int> watcherObjectIds, SiegeInfoNetMsg info)
    {
        var payload = NetCodec.EncodeSiegeInfo(info);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SiegeInfo, payload);
    }

    void INetPresenceBridge.FanoutOlympiadStatus(IReadOnlyList<int> watcherObjectIds, OlympiadStatusNetMsg status)
    {
        var payload = NetCodec.EncodeOlympiadStatus(status);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.OlympiadStatus, payload);
    }

    void INetPresenceBridge.FanoutOlympiadMatch(IReadOnlyList<int> watcherObjectIds, OlympiadMatchNetMsg match)
    {
        var payload = NetCodec.EncodeOlympiadMatch(match);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.OlympiadMatch, payload);
    }

    void INetPresenceBridge.FanoutOlympiadCountdown(IReadOnlyList<int> watcherObjectIds, OlympiadCountdownNetMsg countdown)
    {
        var payload = NetCodec.EncodeOlympiadCountdown(countdown);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.OlympiadCountdown, payload);
    }

    void INetPresenceBridge.FanoutOlympiadResult(IReadOnlyList<int> watcherObjectIds, OlympiadResultNetMsg result)
    {
        var payload = NetCodec.EncodeOlympiadResult(result);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.OlympiadResult, payload);
    }

    void INetPresenceBridge.FanoutSevenSignsStatus(IReadOnlyList<int> watcherObjectIds, SevenSignsStatusNetMsg status)
    {
        var payload = NetCodec.EncodeSevenSignsStatus(status);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SevenSignsStatus, payload);
    }

    void INetPresenceBridge.FanoutSevenSignsSky(IReadOnlyList<int> watcherObjectIds, SevenSignsSkyNetMsg sky)
    {
        var payload = NetCodec.EncodeSevenSignsSky(sky);
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SevenSignsSky, payload);
    }

    void INetPresenceBridge.FanoutDungeonEntered(IReadOnlyList<int> watcherObjectIds, DungeonEnteredNetMsg entered)
    {
        var payload = NetCodec.EncodeDungeonEntered(entered);
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
                            await OnHelloAsync(conn, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ZoneAuth:
                            self = await OnAuthAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            if (self.Value != 0 && zoneSession.Bound)
                            {
                                if (registeredObjectId != 0 && registeredObjectId != zoneSession.ObjectId)
                                    _presence.Unregister(registeredObjectId, conn);
                                _presence.Register(zoneSession.ObjectId, conn, self);
                                registeredObjectId = zoneSession.ObjectId;
                            }

                            break;
                        case NetOpcode.EnterWorld:
                            await OnEnterAsync(conn, zoneSession, self, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.MoveInput:
                            await OnMoveAsync(conn, zoneSession, self, envelope, ct).ConfigureAwait(false);
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
                            await OnReviveRequestAsync(conn, zoneSession, self, envelope, ct).ConfigureAwait(false);
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
                            await OnBypassRequestAsync(conn, zoneSession, self, envelope, ct).ConfigureAwait(false);
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
                            if (zoneSession.Bound && zoneSession.InWorld)
                            {
                                var vp = NetCodec.DecodeValidatePosition(envelope.Payload);
                                if (_world.ValidateClientPosition(zoneSession, vp.X, vp.Y, vp.Z, out int dxy, out int dz))
                                {
                                    Console.WriteLine(
                                        $"[zone] ValidatePosition drift obj={zoneSession.ObjectId} dxy={dxy} dz={dz} client={vp.X},{vp.Y},{vp.Z} server={zoneSession.X},{zoneSession.Y},{zoneSession.Z} -> PositionCorrection");
                                    await conn.SendAsync(
                                        NetOpcode.PositionCorrection,
                                        NetCodec.EncodePositionCorrection(new PositionCorrectionMsg
                                        {
                                            EntityValue = self.Value,
                                            EntityGeneration = self.Generation,
                                            X = zoneSession.X,
                                            Y = zoneSession.Y,
                                            Z = zoneSession.Z
                                        }),
                                        envelope.RequestSequence,
                                        ct).ConfigureAwait(false);
                                }
                            }

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
                            // Authoritative stop: correct to current session position.
                            if (zoneSession.Bound && zoneSession.InWorld)
                            {
                                _world.StopMove(zoneSession);
                                await conn.SendAsync(
                                    NetOpcode.PositionCorrection,
                                    NetCodec.EncodePositionCorrection(new PositionCorrectionMsg
                                    {
                                        EntityValue = self.Value,
                                        EntityGeneration = self.Generation,
                                        X = zoneSession.X,
                                        Y = zoneSession.Y,
                                        Z = zoneSession.Z
                                    }),
                                    envelope.RequestSequence,
                                    ct).ConfigureAwait(false);
                            }

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
            await LeaveNetAsync(zoneSession, self, registeredObjectId, conn).ConfigureAwait(false);
        }
    }

    async Task LeaveNetAsync(ZoneSession zoneSession, EntityId self, int registeredObjectId, NetConnection conn)
    {
        try
        {
            if (zoneSession.Bound && zoneSession.InWorld && self.Value != 0)
            {
                var peers = _world.SnapshotKnownPlayers(zoneSession);
                var despawn = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
                {
                    EntityValue = self.Value,
                    EntityGeneration = self.Generation
                });
                foreach (var peer in peers)
                    await _presence.SendAsync(peer.ObjectId, NetOpcode.EntityDespawn, despawn, CancellationToken.None)
                        .ConfigureAwait(false);
            }
        }
        catch
        {
            /* ignore */
        }

        if (registeredObjectId != 0)
            _presence.Unregister(registeredObjectId, conn);

        try
        {
            await _world.LeaveAsync(zoneSession, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            /* ignore */
        }
    }

    async Task OnHelloAsync(NetConnection conn, PacketEnvelope envelope, CancellationToken ct)
    {
        var hello = NetCodec.DecodeHello(envelope.Payload);
        uint granted = ProtocolCapabilities.FeatureSession | ProtocolCapabilities.FeatureWorldPresence;
        conn.HelloCompleted = true;
        conn.FeatureFlags = granted & (hello.FeatureFlags == 0 ? granted : hello.FeatureFlags);

        await conn.SendAsync(
            NetOpcode.HelloAck,
            NetCodec.EncodeHelloAck(new NetHelloAck
            {
                Major = PacketEnvelope.ProtocolMajor,
                Minor = PacketEnvelope.ProtocolMinor,
                FeatureFlags = conn.FeatureFlags,
                ServerName = "fineage-zone-net"
            }),
            ct: ct).ConfigureAwait(false);
    }

    async Task<EntityId> OnAuthAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);

        // A session authenticates once. Re-authing would silently swap a live character for another
        // handoff inside the same connection, so refuse it the way the retired V2 listener did.
        if (zoneSession.Bound)
        {
            await RejectAsync(conn, "already_authenticated", "This session already completed zone auth.", ct)
                .ConfigureAwait(false);
            return default;
        }

        var auth = NetCodec.DecodeZoneAuth(envelope.Payload);
        if (string.IsNullOrWhiteSpace(auth.Account))
        {
            await RejectAsync(conn, "bad_request", "Auth account is required.", ct).ConfigureAwait(false);
            return default;
        }

        var status = _handoff.TryConsume(auth.Token, _clock(), out var parsed);
        if (status == HandoffTokens.ValidateStatus.Expired)
        {
            await conn.SendAsync(
                NetOpcode.ZoneAuthResult,
                NetCodec.EncodeZoneAuthResult(new ZoneAuthResultMsg
                {
                    Ok = false,
                    Code = "handoff_expired",
                    Message = "Handoff token has expired."
                }),
                envelope.RequestSequence,
                ct).ConfigureAwait(false);
            return default;
        }

        if (status != HandoffTokens.ValidateStatus.Ok ||
            !string.Equals(parsed.Account, auth.Account, StringComparison.OrdinalIgnoreCase))
        {
            await conn.SendAsync(
                NetOpcode.ZoneAuthResult,
                NetCodec.EncodeZoneAuthResult(new ZoneAuthResultMsg
                {
                    Ok = false,
                    Code = "invalid_credentials",
                    Message = "Handoff token was rejected."
                }),
                envelope.RequestSequence,
                ct).ConfigureAwait(false);
            return default;
        }

        await _world.LeaveAsync(zoneSession, ct).ConfigureAwait(false);
        zoneSession.Bind(parsed.CharacterId, parsed.Account, parsed.CharacterName, parsed.ClassId, parsed.Race, parsed.Sex);
        var entityId = _ids.AllocateOrGet(parsed.CharacterId);

        await conn.SendAsync(
            NetOpcode.ZoneAuthResult,
            NetCodec.EncodeZoneAuthResult(new ZoneAuthResultMsg
            {
                Ok = true,
                EntityValue = entityId.Value,
                EntityGeneration = entityId.Generation
            }),
            envelope.RequestSequence,
            ct).ConfigureAwait(false);

        return entityId;
    }


    async Task OnEnterAsync(NetConnection conn, ZoneSession zoneSession, EntityId self, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound)
        {
            await RejectAsync(conn, "auth_required", "Auth with a handoff token first.", ct).ConfigureAwait(false);
            return;
        }

        bool ok = await _world.TryEnterAsync(zoneSession, ct).ConfigureAwait(false);
        if (!ok)
        {
            await RejectAsync(conn, "enter_failed", "Could not enter world.", ct).ConfigureAwait(false);
            return;
        }

        if (!_world.TryGetLivePlayer(zoneSession, out var entity))
        {
            await RejectAsync(conn, "enter_failed", "Enter world produced no live player entity.", ct).ConfigureAwait(false);
            return;
        }

        // Single source of truth: ZoneEntity after TryEnter (inventory, vitals, AutoSkills already applied).
        var packets = NetEnterWorldProjector.FromEntity(entity, self, _clock().ToUnixTimeMilliseconds(), PlayerSpawnPAtkSpd(entity));
        await conn.SendAsync(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(packets.Spawn), ct: ct)
            .ConfigureAwait(false);
        await conn.SendAsync(NetOpcode.PlayerStats, NetCodec.EncodePlayerStats(packets.Stats), ct: ct)
            .ConfigureAwait(false);
        // Mobius enter: UserInfo (full sheet + paperdoll) before ItemList so the status window and model are dressed.
        if (_world.TryBuildNetUserInfo(zoneSession, out var userInfo))
            await conn.SendAsync(NetOpcode.UserInfo, NetCodec.EncodeUserInfo(userInfo), ct: ct).ConfigureAwait(false);
        await conn.SendAsync(NetOpcode.ItemList, NetCodec.EncodeItemList(packets.Items), ct: ct)
            .ConfigureAwait(false);
        await conn.SendAsync(NetOpcode.SkillList, NetCodec.EncodeSkillList(packets.Skills), ct: ct)
            .ConfigureAwait(false);
        // Mobius EnterWorld: ShortCutInit after ItemList / SkillList so item and skill icons resolve.
        await conn.SendAsync(NetOpcode.ShortCutInit, NetCodec.EncodeShortCutInit(_world.NetShortcutInit(zoneSession)), ct: ct)
            .ConfigureAwait(false);
        // Mobius EnterWorld:401 sends EtcStatusUpdate after ItemList/ShortCutInit and after updateEffectIcons, so the
        // overload icon appears on an otherwise fully-dressed status bar.
        if (_world.TryBuildNetEtcStatus(zoneSession, out var etcStatus))
            await conn.SendAsync(NetOpcode.EtcStatusUpdate, NetCodec.EncodeEtcStatusUpdate(etcStatus), ct: ct)
                .ConfigureAwait(false);
        // Journal on enter so the quest tabs are filled before the player presses the menu button.
        await conn.SendAsync(NetOpcode.QuestList, NetCodec.EncodeQuestList(_world.NetQuestList(zoneSession)), ct: ct)
            .ConfigureAwait(false);
        // Mobius EnterWorld:561 — a returning clan member gets the pledge sheet and roster up front,
        // otherwise the clan window is blank until some later clan action fans a status out.
        if (_world.TryBuildNetClanSnapshot(zoneSession, out var clanStatus, out var clanMembers))
        {
            await conn.SendAsync(NetOpcode.PledgeStatus, NetCodec.EncodePledgeStatus(clanStatus), ct: ct)
                .ConfigureAwait(false);
            await conn.SendAsync(NetOpcode.PledgeMemberList, NetCodec.EncodePledgeMemberList(clanMembers), ct: ct)
                .ConfigureAwait(false);
        }

        var welcome = SayRules.Welcome(zoneSession.Name);
        await conn.SendAsync(
            NetOpcode.SystemMessage,
            NetCodec.EncodeSystemMessage(new SystemMessageNetMsg
            {
                Id = welcome.Id,
                Text = welcome.Text,
                Params = welcome.Params ?? []
            }),
            ct: ct).ConfigureAwait(false);

        var known = _world.SnapshotKnownEntities(zoneSession);
        foreach (var other in known)
        {
            var otherId = _ids.AllocateOrGet(other.ObjectId);
            if (other.Kind == ZoneEntityKind.GroundItem)
            {
                await conn.SendAsync(
                    NetOpcode.DropItem,
                    NetCodec.EncodeDropItem(new DropItemNetMsg
                    {
                        DropperObjectId = 0,
                        EntityValue = otherId.Value,
                        EntityGeneration = otherId.Generation,
                        ItemId = other.GroundItemId,
                        X = other.X,
                        Y = other.Y,
                        Z = other.Z,
                        Stackable = other.GroundStackable,
                        Count = other.GroundCount
                    }),
                    ct: ct).ConfigureAwait(false);
                continue;
            }

            var remote = NetEnterWorldProjector.BuildPresenceSpawn(other, otherId, PlayerSpawnPAtkSpd(other));
            await conn.SendAsync(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(remote), ct: ct)
                .ConfigureAwait(false);
        }

        var selfRemote = NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(entity, self, PlayerSpawnPAtkSpd(entity)));
        foreach (var peer in known)
        {
            if (peer.Kind == ZoneEntityKind.Player)
                await _presence.SendAsync(peer.ObjectId, NetOpcode.EntitySpawn, selfRemote, ct).ConfigureAwait(false);
        }
    }

    async Task OnMoveAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        EntityId self,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var move = NetCodec.DecodeMoveInput(envelope.Payload);
        var before = _world.SnapshotKnownIds(zoneSession);

        string? error = await _world.TryMoveAsync(
            zoneSession, move.DestX, move.DestY, move.DestZ, ct).ConfigureAwait(false);
        if (error != null)
        {
            await conn.SendAsync(
                NetOpcode.PositionCorrection,
                NetCodec.EncodePositionCorrection(new PositionCorrectionMsg
                {
                    EntityValue = self.Value,
                    EntityGeneration = self.Generation,
                    X = zoneSession.X,
                    Y = zoneSession.Y,
                    Z = zoneSession.Z
                }),
                envelope.RequestSequence,
                ct).ConfigureAwait(false);
            return;
        }

        var after = _world.SnapshotKnownIds(zoneSession);
        // MoveState to self + stayed peers is fanned by ZoneWorld.ReconcileKnownlist → NetBridge.
        await FanKnownlistDiffAsync(conn, zoneSession, self, before, after, ct).ConfigureAwait(false);
    }

    async Task FanKnownlistDiffAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        EntityId self,
        HashSet<int> before,
        HashSet<int> after,
        CancellationToken ct)
    {
        // Newly visible: EntitySpawn. Forgotten: EntityDespawn.
        foreach (int id in after)
        {
            if (before.Contains(id))
                continue;
            if (!_world.TryGetEntityById(id, out var newlySeen))
                continue;

            var otherId = _ids.AllocateOrGet(id);
            if (newlySeen.Kind == ZoneEntityKind.GroundItem)
            {
                await conn.SendAsync(
                    NetOpcode.DropItem,
                    NetCodec.EncodeDropItem(new DropItemNetMsg
                    {
                        DropperObjectId = 0,
                        EntityValue = otherId.Value,
                        EntityGeneration = otherId.Generation,
                        ItemId = newlySeen.GroundItemId,
                        X = newlySeen.X,
                        Y = newlySeen.Y,
                        Z = newlySeen.Z,
                        Stackable = newlySeen.GroundStackable,
                        Count = newlySeen.GroundCount
                    }),
                    ct: ct).ConfigureAwait(false);
                continue;
            }

            var spawn = NetCodec.EncodeEntitySpawn(
                NetEnterWorldProjector.BuildPresenceSpawn(newlySeen, otherId, PlayerSpawnPAtkSpd(newlySeen)));
            await conn.SendAsync(NetOpcode.EntitySpawn, spawn, ct: ct).ConfigureAwait(false);
            if (newlySeen.Kind == ZoneEntityKind.Player &&
                _world.TryGetLivePlayer(zoneSession, out var selfEntity))
            {
                var toPeer = NetCodec.EncodeEntitySpawn(
                    NetEnterWorldProjector.BuildRemoteSpawn(selfEntity, self, PlayerSpawnPAtkSpd(selfEntity)));
                await _presence.SendAsync(id, NetOpcode.EntitySpawn, toPeer, ct).ConfigureAwait(false);
            }
        }

        var despawnSelf = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = self.Value,
            EntityGeneration = self.Generation
        });
        foreach (int id in before)
        {
            if (after.Contains(id))
                continue;
            var forgottenId = _ids.AllocateOrGet(id);
            await conn.SendAsync(
                NetOpcode.EntityDespawn,
                NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
                {
                    EntityValue = forgottenId.Value,
                    EntityGeneration = forgottenId.Generation
                }),
                ct: ct).ConfigureAwait(false);
            if (_world.TryGetLivePlayerById(id, out _))
                await _presence.SendAsync(id, NetOpcode.EntityDespawn, despawnSelf, ct).ConfigureAwait(false);
        }
    }

    async Task OnReviveRequestAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        EntityId self,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var before = _world.SnapshotKnownIds(zoneSession);
        var revive = NetCodec.DecodeReviveRequest(envelope.Payload);
        string? error = await _world.TryReviveAsync(zoneSession, revive.Action, ct).ConfigureAwait(false);
        if (error != null)
        {
            await RejectAsync(conn, error, "Revive failed.", ct).ConfigureAwait(false);
            return;
        }

        // Revive / Teleport / StatusUpdate fan via NetBridge; AOI after village relocate needs spawn/despawn.
        var after = _world.SnapshotKnownIds(zoneSession);
        await FanKnownlistDiffAsync(conn, zoneSession, self, before, after, ct).ConfigureAwait(false);
    }

    async Task OnBypassRequestAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        EntityId self,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var before = _world.SnapshotKnownIds(zoneSession);
        var bypass = NetCodec.DecodeBypassRequest(envelope.Payload);
        string? error = await _world.TryBypassAsync(
            zoneSession,
            bypass.NpcObjectId,
            bypass.Command,
            ct).ConfigureAwait(false);
        if (error != null)
        {
            await RejectAsync(conn, error, "Bypass failed.", ct).ConfigureAwait(false);
            return;
        }

        // Teleport bypass relocates; refresh AOI for Net peers.
        var after = _world.SnapshotKnownIds(zoneSession);
        await FanKnownlistDiffAsync(conn, zoneSession, self, before, after, ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }

    static Task RejectAsync(NetConnection conn, string code, string message, CancellationToken ct)
    {
        NetTelemetry.PacketRejected("zone");
        return conn.SendAsync(NetOpcode.Reject, NetCodec.EncodeReject(new NetReject { Code = code, Message = message }), ct: ct);
    }
}
