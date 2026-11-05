using Fineage.Login;
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
/// V2 wire flushes go to a discarded stream so Net packets are the client-facing truth.
/// Presence (spawn / move / despawn) is fanned out via <see cref="NetPresenceHub"/>.
/// </summary>
public sealed class NetZoneServer : INetPresenceBridge
{
    readonly HandoffTokens _tokens;
    readonly ZoneWorld _world;
    readonly EntityIdMap _ids = new();
    readonly NetPresenceHub _presence;
    readonly Func<DateTimeOffset> _clock;
    readonly int _maxMessagesPerSecond;
    readonly TimeSpan _idleTimeout;

    public NetZoneServer(
        HandoffTokens tokens,
        ZoneWorld world,
        IAccountStore? accounts = null,
        Func<DateTimeOffset>? clock = null,
        NetPresenceHub? presence = null,
        int? maxMessagesPerSecond = null,
        TimeSpan? idleTimeout = null)
    {
        _tokens = tokens;
        _world = world;
        // Character durable state is loaded by ZoneWorld.TryEnterAsync via the shared store.
        _ = accounts;
        _presence = presence ?? new NetPresenceHub();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _maxMessagesPerSecond = maxMessagesPerSecond ?? ZoneNetSettings.MaxMessagesPerSecond;
        _idleTimeout = idleTimeout ?? TcpSessionSettings.IdleTimeout;
        _world.NetBridge = this;
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
            _ = _presence.SendAsync(watcher, NetOpcode.MoveState, payload, CancellationToken.None);
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
                _ = _presence.SendAsync(selfObjectId, NetOpcode.DropItem, NetCodec.EncodeDropItem(new DropItemNetMsg
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
                }), ct);
                continue;
            }

            _ = _presence.SendAsync(selfObjectId, NetOpcode.EntitySpawn,
                NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildPresenceSpawn(seen, otherId)), ct);
            if (seen.MoveActive)
                SendMoveState(selfObjectId, otherId, seen);
            if (seen.Kind == ZoneEntityKind.Player && seen.Session is { InWorld: true })
            {
                selfSpawn ??= NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(selfEntity, self));
                _ = _presence.SendAsync(id, NetOpcode.EntitySpawn, selfSpawn, ct);
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
            _ = _presence.SendAsync(selfObjectId, NetOpcode.EntityDespawn, NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
            {
                EntityValue = forgotten.Value,
                EntityGeneration = forgotten.Generation
            }), ct);
            if (_world.TryGetLivePlayerById(id, out _))
                _ = _presence.SendAsync(id, NetOpcode.EntityDespawn, despawnSelf, ct);
        }
    }

    void SendMoveState(int watcherObjectId, EntityId mover, ZoneEntity e)
    {
        _ = _presence.SendAsync(watcherObjectId, NetOpcode.MoveState, NetCodec.EncodeMoveState(new MoveStateMsg
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
        }), CancellationToken.None);
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
            _ = _presence.SendAsync(watcher, NetOpcode.ChangeMoveType, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutAttackStance(IReadOnlyList<int> watcherObjectIds, int objectId, bool inStance)
    {
        var id = _ids.AllocateOrGet(objectId);
        var payload = NetCodec.EncodeAttackStance(new AttackStanceNetMsg { EntityValue = id.Value, EntityGeneration = id.Generation });
        ushort opcode = inStance ? NetOpcode.AutoAttackStart : NetOpcode.AutoAttackStop;
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, opcode, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutAutoSoulShot(IReadOnlyList<int> watcherObjectIds, int itemId, bool enabled)
    {
        var payload = NetCodec.EncodeAutoSoulShot(new AutoSoulShotNetMsg { ItemId = itemId, Enabled = enabled });
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.AutoSoulShot, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutUserInfo(IReadOnlyList<int> watcherObjectIds, UserInfoNetMsg info)
    {
        var payload = NetCodec.EncodeUserInfo(info);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.UserInfo, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutCharInfo(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        if (watcherObjectIds.Count == 0 || !_world.TryGetEntityById(objectId, out var entity) ||
            entity.Kind != ZoneEntityKind.Player)
            return;
        var payload = NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(entity, _ids.AllocateOrGet(objectId)));
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.EntitySpawn, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutAttackState(IReadOnlyList<int> watcherObjectIds, AttackStateMsg attack)
    {
        var payload = NetCodec.EncodeAttackState(attack);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.AttackState, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutStatusUpdate(IReadOnlyList<int> watcherObjectIds, StatusUpdateNetMsg status)
    {
        var payload = NetCodec.EncodeStatusUpdate(status);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.StatusUpdate, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutDie(IReadOnlyList<int> watcherObjectIds, EntityDieMsg die)
    {
        var payload = NetCodec.EncodeEntityDie(die);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.EntityDie, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutDropItem(IReadOnlyList<int> watcherObjectIds, DropItemNetMsg drop)
    {
        var payload = NetCodec.EncodeDropItem(drop);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.DropItem, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutEntityDespawn(IReadOnlyList<int> watcherObjectIds, EntityDespawnMsg despawn)
    {
        var payload = NetCodec.EncodeEntityDespawn(despawn);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.EntityDespawn, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutInventoryUpdate(IReadOnlyList<int> watcherObjectIds, InventoryUpdateNetMsg update)
    {
        var payload = NetCodec.EncodeInventoryUpdate(update);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.InventoryUpdate, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutEtcStatusUpdate(IReadOnlyList<int> watcherObjectIds, EtcStatusUpdateNetMsg status)
    {
        var payload = NetCodec.EncodeEtcStatusUpdate(status);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.EtcStatusUpdate, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutCreatureSay(IReadOnlyList<int> watcherObjectIds, CreatureSayNetMsg say)
    {
        var payload = NetCodec.EncodeCreatureSay(say);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.CreatureSay, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutSystemMessage(IReadOnlyList<int> watcherObjectIds, SystemMessageNetMsg message)
    {
        var payload = NetCodec.EncodeSystemMessage(message);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.SystemMessage, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutMagicSkillUse(IReadOnlyList<int> watcherObjectIds, MagicSkillUseNetMsg cast)
    {
        var payload = NetCodec.EncodeMagicSkillUse(cast);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.MagicSkillUse, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutRevive(IReadOnlyList<int> watcherObjectIds, ReviveNetMsg revive)
    {
        var payload = NetCodec.EncodeRevive(revive);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.Revive, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutTeleportToLocation(IReadOnlyList<int> watcherObjectIds, TeleportToLocationNetMsg teleport)
    {
        var payload = NetCodec.EncodeTeleportToLocation(teleport);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.TeleportToLocation, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutPlayerStats(IReadOnlyList<int> watcherObjectIds, PlayerStatsMsg stats)
    {
        var payload = NetCodec.EncodePlayerStats(stats);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.PlayerStats, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutAbnormalStatusUpdate(IReadOnlyList<int> watcherObjectIds, AbnormalStatusUpdateNetMsg update)
    {
        var payload = NetCodec.EncodeAbnormalStatusUpdate(update);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.AbnormalStatusUpdate, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutNpcHtml(IReadOnlyList<int> watcherObjectIds, NpcHtmlNetMsg html)
    {
        var payload = NetCodec.EncodeNpcHtml(html);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.NpcHtml, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutBuyList(IReadOnlyList<int> watcherObjectIds, BuyListNetMsg list)
    {
        var payload = NetCodec.EncodeBuyList(list);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.BuyList, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutSellList(IReadOnlyList<int> watcherObjectIds, SellListNetMsg list)
    {
        var payload = NetCodec.EncodeSellList(list);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.SellList, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutMultiSellList(IReadOnlyList<int> watcherObjectIds, MultiSellListNetMsg list)
    {
        var payload = NetCodec.EncodeMultiSellList(list);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.MultiSellList, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutShopBalance(IReadOnlyList<int> watcherObjectIds, ShopBalanceNetMsg balance)
    {
        var payload = NetCodec.EncodeShopBalance(balance);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.ShopBalance, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutWarehouseDepositList(IReadOnlyList<int> watcherObjectIds, WarehouseDepositListNetMsg list)
    {
        var payload = NetCodec.EncodeWarehouseDepositList(list);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.WarehouseDepositList, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutWarehouseWithdrawList(IReadOnlyList<int> watcherObjectIds, WarehouseWithdrawListNetMsg list)
    {
        var payload = NetCodec.EncodeWarehouseWithdrawList(list);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.WarehouseWithdrawList, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutSkillList(IReadOnlyList<int> watcherObjectIds, SkillListNetMsg list)
    {
        var payload = NetCodec.EncodeSkillList(list);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.SkillList, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutChangeWaitType(IReadOnlyList<int> watcherObjectIds, ChangeWaitTypeNetMsg waitType)
    {
        var payload = NetCodec.EncodeChangeWaitType(waitType);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.ChangeWaitType, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutSocialAction(IReadOnlyList<int> watcherObjectIds, SocialActionNetMsg action)
    {
        var payload = NetCodec.EncodeSocialAction(action);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.SocialAction, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutTargetSelected(IReadOnlyList<int> watcherObjectIds, TargetSelectedNetMsg selected)
    {
        var payload = NetCodec.EncodeTargetSelected(selected);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.TargetSelected, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutTargetUnselected(IReadOnlyList<int> watcherObjectIds, TargetUnselectedNetMsg unselected)
    {
        var payload = NetCodec.EncodeTargetUnselected(unselected);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.TargetUnselected, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutAcquireSkillList(IReadOnlyList<int> watcherObjectIds, AcquireSkillListNetMsg list)
    {
        var payload = NetCodec.EncodeAcquireSkillList(list);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.AcquireSkillList, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutAcquireSkillInfo(IReadOnlyList<int> watcherObjectIds, AcquireSkillInfoNetMsg info)
    {
        var payload = NetCodec.EncodeAcquireSkillInfo(info);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.AcquireSkillInfo, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutEnchantResult(IReadOnlyList<int> watcherObjectIds, EnchantResultNetMsg result)
    {
        var payload = NetCodec.EncodeEnchantResult(result);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.EnchantResult, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutPledgeStatus(IReadOnlyList<int> watcherObjectIds, PledgeStatusNetMsg status)
    {
        var payload = NetCodec.EncodePledgeStatus(status);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.PledgeStatus, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutPledgeMemberList(IReadOnlyList<int> watcherObjectIds, PledgeMemberListNetMsg list)
    {
        var payload = NetCodec.EncodePledgeMemberList(list);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.PledgeMemberList, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutClanAskJoin(IReadOnlyList<int> watcherObjectIds, ClanAskJoinNetMsg ask)
    {
        var payload = NetCodec.EncodeClanAskJoin(ask);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.ClanAskJoin, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutClanJoinResult(IReadOnlyList<int> watcherObjectIds, ClanJoinResultNetMsg result)
    {
        var payload = NetCodec.EncodeClanJoinResult(result);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.ClanJoinResult, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutQuestList(IReadOnlyList<int> watcherObjectIds, QuestListNetMsg list)
    {
        var payload = NetCodec.EncodeQuestList(list);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.QuestList, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutPartyAskJoin(IReadOnlyList<int> watcherObjectIds, PartyAskJoinNetMsg ask)
    {
        var payload = NetCodec.EncodePartyAskJoin(ask);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.PartyAskJoin, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutPartyJoinResult(IReadOnlyList<int> watcherObjectIds, PartyJoinResultNetMsg result)
    {
        var payload = NetCodec.EncodePartyJoinResult(result);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.PartyJoinResult, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutPartyMemberList(IReadOnlyList<int> watcherObjectIds, PartyMemberListNetMsg list)
    {
        var payload = NetCodec.EncodePartyMemberList(list);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.PartyMemberList, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutTradeAsk(IReadOnlyList<int> watcherObjectIds, TradeAskNetMsg ask)
    {
        var payload = NetCodec.EncodeTradeAsk(ask);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.TradeAsk, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutTradeRequestResult(IReadOnlyList<int> watcherObjectIds, TradeRequestResultNetMsg result)
    {
        var payload = NetCodec.EncodeTradeRequestResult(result);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.TradeRequestResult, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutTradeStart(IReadOnlyList<int> watcherObjectIds, TradeStartNetMsg start)
    {
        var payload = NetCodec.EncodeTradeStart(start);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.TradeStart, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutTradeOfferUpdate(IReadOnlyList<int> watcherObjectIds, TradeOfferUpdateNetMsg update)
    {
        var payload = NetCodec.EncodeTradeOfferUpdate(update);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.TradeOfferUpdate, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutTradeConfirmState(IReadOnlyList<int> watcherObjectIds, TradeConfirmStateNetMsg state)
    {
        var payload = NetCodec.EncodeTradeConfirmState(state);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.TradeConfirmState, payload, CancellationToken.None);
    }

    void INetPresenceBridge.FanoutTradeFinished(IReadOnlyList<int> watcherObjectIds, TradeFinishedNetMsg finished)
    {
        var payload = NetCodec.EncodeTradeFinished(finished);
        foreach (int watcher in watcherObjectIds)
            _ = _presence.SendAsync(watcher, NetOpcode.TradeFinished, payload, CancellationToken.None);
    }

    public async Task HandleAsync(NetConnection conn, CancellationToken ct)
    {
        // The world layer still emits V2 frames alongside Net fanout. A MemoryStream here
        // would grow for the whole session, so use a sink that drops every frame.
        await using var discard = new DiscardingStream();
        var zoneSession = new ZoneSession(discard);
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
                                    _presence.Unregister(registeredObjectId);
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
                            await OnSelectTargetAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.TargetCancelRequest:
                            await OnTargetCancelAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.AttackRequest:
                            await OnAttackAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PickupRequest:
                            await OnPickupAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.DropItemRequest:
                            await OnDropItemAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.DestroyItemRequest:
                            await OnDestroyItemAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.UseItem:
                            await OnUseItemAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.UnequipItem:
                            await OnUnequipItemAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.Say:
                            await OnSayAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.UseSkill:
                            await OnUseSkillAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ReviveRequest:
                            await OnReviveRequestAsync(conn, zoneSession, self, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestAcquireSkillInfo:
                            await OnAcquireSkillRequestAsync(conn, zoneSession, envelope, informing: true, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestAcquireSkill:
                            await OnAcquireSkillRequestAsync(conn, zoneSession, envelope, informing: false, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.EnchantRequest:
                            await OnEnchantRequestAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.EnchantConfirm:
                            await OnEnchantConfirmAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.EnchantCancel:
                            await OnEnchantCancelAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanCreateRequest:
                            await OnClanCreateAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanInviteRequest:
                            await OnClanInviteAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanAnswerJoinRequest:
                            await OnClanAnswerAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanLeaveRequest:
                            await OnClanLeaveAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanKickRequest:
                            await OnClanKickAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanDissolveRequest:
                            await OnClanDissolveAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.ClanCancelDissolveRequest:
                            await OnClanCancelDissolveAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.NpcTalk:
                            await OnNpcTalkAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.BypassRequest:
                            await OnBypassRequestAsync(conn, zoneSession, self, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestBuyItem:
                            await OnRequestBuyItemAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestSellItem:
                            await OnRequestSellItemAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestMultiSellChoose:
                            await OnRequestMultiSellChooseAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.WarehouseDepositRequest:
                            await OnWarehouseDepositAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.WarehouseWithdrawRequest:
                            await OnWarehouseWithdrawAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestQuestList:
                            await OnRequestQuestListAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.RequestQuestAbort:
                            await OnRequestQuestAbortAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PartyInviteRequest:
                            await OnPartyInviteAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PartyAnswerJoinRequest:
                            await OnPartyAnswerAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PartyLeaveRequest:
                            await OnPartyLeaveAsync(conn, zoneSession, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PartyKickRequest:
                            await OnPartyKickAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.TradeRequest:
                            await OnTradeRequestAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.TradeAnswerRequest:
                            await OnTradeAnswerAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.TradeAddItemRequest:
                            await OnTradeAddItemAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.TradeDoneRequest:
                            await OnTradeDoneAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
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
                            if (zoneSession.Bound && zoneSession.InWorld)
                            {
                                var use = NetCodec.DecodeActionUse(envelope.Payload);
                                // Net renders sit/stand (ChangeWaitType), the walk/run toggle (ChangeMoveType),
                                // next-target (MyTargetSelected), panel Attack (AttackRequest) and the three social
                                // gestures (SocialAction); the rest of Mobius' action table is still V2-only.
                                if (use.ActionId is not (ActionProtocol.SitStand or ActionProtocol.WalkRun
                                    or ActionProtocol.Attack or ActionProtocol.NextTarget
                                    or ActionProtocol.SocialGreeting or ActionProtocol.SocialVictory or ActionProtocol.SocialAdvance))
                                {
                                    await RejectAsync(conn, ActionProtocol.Unsupported, $"action {use.ActionId} not on Net yet", ct).ConfigureAwait(false);
                                    break;
                                }
                                string? actionError = await _world.TryCharacterActionAsync(zoneSession,
                                    new CharacterActionRequestMsg { ActionId = use.ActionId, CtrlPressed = use.CtrlPressed, ShiftPressed = use.ShiftPressed },
                                    ct).ConfigureAwait(false);
                                if (actionError != null)
                                    await RejectAsync(conn, actionError, "Action failed.", ct).ConfigureAwait(false);
                            }

                            break;
                        case NetOpcode.RequestAutoSoulShot:
                            if (zoneSession.Bound && zoneSession.InWorld)
                            {
                                var auto = NetCodec.DecodeAutoSoulShot(envelope.Payload);
                                string? autoError = await _world.TryAutoSoulShotAsync(zoneSession, auto.ItemId, auto.Enabled, ct).ConfigureAwait(false);
                                if (autoError != null)
                                    await RejectAsync(conn, autoError, "Auto shot failed.", ct).ConfigureAwait(false);
                            }

                            break;
                        case NetOpcode.RequestShortCutReg:
                            if (zoneSession.Bound && zoneSession.InWorld)
                            {
                                var reg = NetCodec.DecodeShortCut(envelope.Payload);
                                var regReply = _world.RegisterShortcut(zoneSession, reg.Type, reg.Slot, reg.Id, reg.Level);
                                await SendShortcutReplyAsync(conn, regReply, envelope.RequestSequence, ct).ConfigureAwait(false);
                            }

                            break;
                        case NetOpcode.RequestShortCutDel:
                            if (zoneSession.Bound && zoneSession.InWorld)
                            {
                                var del = NetCodec.DecodeShortCutDel(envelope.Payload);
                                var delReply = _world.DeleteShortcut(zoneSession, del.Slot);
                                await SendShortcutReplyAsync(conn, delReply, envelope.RequestSequence, ct).ConfigureAwait(false);
                            }

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
                    await RejectAsync(conn, "handler_error", ex.Message, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await LeaveNetAsync(zoneSession, self, registeredObjectId).ConfigureAwait(false);
        }
    }

    async Task LeaveNetAsync(ZoneSession zoneSession, EntityId self, int registeredObjectId)
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
            _presence.Unregister(registeredObjectId);

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
        var auth = NetCodec.DecodeZoneAuth(envelope.Payload);
        var status = _tokens.Validate(auth.Token, _clock(), out var parsed);
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

    static async Task SendShortcutReplyAsync(NetConnection conn, ZoneWorld.ShortcutReply reply, uint sequence, CancellationToken ct)
    {
        if (reply.Register != null)
            await conn.SendAsync(NetOpcode.ShortCutRegister, NetCodec.EncodeShortCut(reply.Register), sequence, ct).ConfigureAwait(false);
        else if (reply.DeleteSlot is int slot)
            await conn.SendAsync(NetOpcode.ShortCutDelete, NetCodec.EncodeShortCutDel(new ShortCutDelNetMsg { Slot = slot }), sequence, ct).ConfigureAwait(false);
        if (reply.Error != null)
            ZoneLog.Info($"shortcut rejected: {reply.Error}");
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
        var packets = NetEnterWorldProjector.FromEntity(entity, self, _clock().ToUnixTimeMilliseconds());
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

            var remote = NetEnterWorldProjector.BuildPresenceSpawn(other, otherId);
            await conn.SendAsync(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(remote), ct: ct)
                .ConfigureAwait(false);
        }

        var selfRemote = NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(entity, self));
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
        var msg = new MoveToMsg
        {
            ObjectId = zoneSession.ObjectId,
            DestX = move.DestX,
            DestY = move.DestY,
            DestZ = move.DestZ,
            OriginX = zoneSession.X,
            OriginY = zoneSession.Y,
            OriginZ = zoneSession.Z
        };

        string? error = await _world.TryMoveAsync(zoneSession, msg, ct).ConfigureAwait(false);
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
                NetEnterWorldProjector.BuildPresenceSpawn(newlySeen, otherId));
            await conn.SendAsync(NetOpcode.EntitySpawn, spawn, ct: ct).ConfigureAwait(false);
            if (newlySeen.Kind == ZoneEntityKind.Player &&
                _world.TryGetLivePlayer(zoneSession, out var selfEntity))
            {
                var toPeer = NetCodec.EncodeEntitySpawn(
                    NetEnterWorldProjector.BuildRemoteSpawn(selfEntity, self));
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

    async Task OnAcquireSkillRequestAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        bool informing,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var (skillId, level, skillType) = NetCodec.DecodeAcquireSkillRequest(envelope.Payload);
        string? error = await _world.TryAcquireSkillAsync(zoneSession, skillId, level, skillType, informing, ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, informing ? "Skill info failed." : "Learn failed.", ct).ConfigureAwait(false);
    }

    async Task OnEnchantRequestAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var request = NetCodec.DecodeEnchantRequest(envelope.Payload);
        string? error = await _world
            .TryEnchantRequestAsync(zoneSession, new EnchantRequestMsg { ObjectId = request.ScrollObjectId }, ct)
            .ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Enchant scroll select failed.", ct).ConfigureAwait(false);
    }

    async Task OnEnchantConfirmAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var confirm = NetCodec.DecodeEnchantConfirm(envelope.Payload);
        string? error = await _world
            .TryEnchantConfirmAsync(zoneSession, new EnchantConfirmMsg { ObjectId = confirm.TargetObjectId }, ct)
            .ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Enchant failed.", ct).ConfigureAwait(false);
    }

    async Task OnEnchantCancelAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryEnchantCancelAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Enchant cancel failed.", ct).ConfigureAwait(false);
    }

    async Task OnClanCreateAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var create = NetCodec.DecodeClanCreateRequest(envelope.Payload);
        string? error = await _world
            .TryClanCreateAsync(zoneSession, new ClanCreateRequestMsg { Name = create.Name }, _clock(), ct)
            .ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Clan create failed.", ct).ConfigureAwait(false);
    }

    async Task OnClanInviteAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var invite = NetCodec.DecodeClanInviteRequest(envelope.Payload);
        string? error = await _world
            .TryClanInviteAsync(
                zoneSession,
                new ClanInviteRequestMsg { TargetObjectId = invite.TargetObjectId, PledgeType = 0 },
                _clock(),
                ct)
            .ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Clan invite failed.", ct).ConfigureAwait(false);
    }

    async Task OnClanAnswerAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var answer = NetCodec.DecodeClanAnswer(envelope.Payload);
        string? error = await _world
            .TryClanAnswerAsync(
                zoneSession,
                new ClanAnswerJoinRequestMsg
                {
                    RequestorObjectId = answer.RequestorObjectId,
                    Response = answer.Response
                },
                _clock(),
                ct)
            .ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Clan answer failed.", ct).ConfigureAwait(false);
    }

    async Task OnClanLeaveAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryClanLeaveAsync(zoneSession, _clock(), ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Clan leave failed.", ct).ConfigureAwait(false);
    }

    async Task OnClanKickAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var kick = NetCodec.DecodeClanKickRequest(envelope.Payload);
        string? error = await _world
            .TryClanKickAsync(zoneSession, new ClanKickRequestMsg { TargetObjectId = kick.TargetObjectId }, _clock(), ct)
            .ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Clan kick failed.", ct).ConfigureAwait(false);
    }

    async Task OnClanDissolveAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryClanDissolveAsync(zoneSession, _clock(), ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Clan dissolve failed.", ct).ConfigureAwait(false);
    }

    async Task OnClanCancelDissolveAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryClanCancelDissolveAsync(zoneSession, _clock(), ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Clan cancel dissolve failed.", ct).ConfigureAwait(false);
    }

    async Task OnSelectTargetAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var select = NetCodec.DecodeSelectTarget(envelope.Payload);
        string? error = await _world.TrySelectTargetAsync(
            zoneSession,
            new SelectTargetMsg { TargetObjectId = select.TargetObjectId },
            ct).ConfigureAwait(false);
        if (error != null)
        {
            await RejectAsync(conn, error, "Select target failed.", ct).ConfigureAwait(false);
            return;
        }

        // Nothing to answer here: the world already fanned MyTargetSelected's Net mirror (TargetSelected, id +
        // Mobius colour), the target's StatusUpdate and the error path through INetPresenceBridge.
    }

    async Task OnTargetCancelAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryCancelTargetAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Target cancel failed.", ct).ConfigureAwait(false);
        // The world already fanned TargetUnselected, so nothing to answer here.
    }

    async Task OnAttackAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var attack = NetCodec.DecodeAttackRequest(envelope.Payload);
        string? error = await _world.TryAttackAsync(
            zoneSession,
            new AttackRequestMsg { TargetObjectId = attack.TargetObjectId },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Attack failed.", ct).ConfigureAwait(false);
        // AttackState / MoveState / StatusUpdate fan out via NetBridge from ZoneWorld combat.
    }

    async Task OnPickupAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var pickup = NetCodec.DecodePickupRequest(envelope.Payload);
        string? error = await _world.TryPickupAsync(
            zoneSession,
            new PickupRequestMsg { ObjectId = pickup.GroundObjectId },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Pickup failed.", ct).ConfigureAwait(false);
        // InventoryUpdate + EntityDespawn fan out via NetBridge.
    }

    async Task OnDestroyItemAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var destroy = NetCodec.DecodeDestroyItemRequest(envelope.Payload);
        string? error = await _world.TryDestroyItemAsync(
            zoneSession,
            new DestroyItemRequestMsg { ObjectId = destroy.ObjectId, Count = destroy.Count },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Destroy item failed.", ct).ConfigureAwait(false);
        // InventoryUpdate fans out from ZoneWorldGround.
    }

    async Task OnDropItemAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var drop = NetCodec.DecodeDropItemRequest(envelope.Payload);
        string? error = await _world.TryDropItemAsync(
            zoneSession,
            new DropItemRequestMsg
            {
                ObjectId = drop.ObjectId,
                Count = drop.Count,
                X = drop.X,
                Y = drop.Y,
                Z = drop.Z
            },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Drop failed.", ct).ConfigureAwait(false);
    }

    async Task OnUseItemAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var use = NetCodec.DecodeUseItem(envelope.Payload);
        string? error = await _world.TryUseItemAsync(
            zoneSession,
            new UseItemMsg { ObjectId = use.ObjectId },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "UseItem failed.", ct).ConfigureAwait(false);
    }

    async Task OnUnequipItemAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var unequip = NetCodec.DecodeUnequipItem(envelope.Payload);
        string? error = await _world.TryUnequipItemAsync(
            zoneSession,
            new UnequipItemMsg { PaperdollSlot = unequip.PaperdollSlot },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Unequip failed.", ct).ConfigureAwait(false);
    }

    async Task OnSayAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var say = NetCodec.DecodeSay(envelope.Payload);
        string? error = await _world.TrySayAsync(
            zoneSession,
            new SayMsg { Text = say.Text, ChatType = say.ChatType },
            _clock(),
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Say failed.", ct).ConfigureAwait(false);
    }

    async Task OnUseSkillAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var use = NetCodec.DecodeUseSkill(envelope.Payload);
        string? error = await _world.TrySkillUseAsync(
            zoneSession,
            new SkillUseRequestMsg
            {
                SkillId = use.SkillId,
                TargetObjectId = use.TargetObjectId,
                CtrlPressed = use.CtrlPressed
            },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "UseSkill failed.", ct).ConfigureAwait(false);
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
        string? error = await _world.TryReviveAsync(
            zoneSession,
            new ReviveRequestMsg { Action = revive.Action },
            ct).ConfigureAwait(false);
        if (error != null)
        {
            await RejectAsync(conn, error, "Revive failed.", ct).ConfigureAwait(false);
            return;
        }

        // Revive / Teleport / StatusUpdate fan via NetBridge; AOI after village relocate needs spawn/despawn.
        var after = _world.SnapshotKnownIds(zoneSession);
        await FanKnownlistDiffAsync(conn, zoneSession, self, before, after, ct).ConfigureAwait(false);
    }

    async Task OnNpcTalkAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var talk = NetCodec.DecodeNpcTalk(envelope.Payload);
        string? error = await _world.TryNpcTalkAsync(zoneSession, talk.NpcObjectId, ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "NpcTalk failed.", ct).ConfigureAwait(false);
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

    async Task OnRequestBuyItemAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var buy = NetCodec.DecodeRequestBuyItem(envelope.Payload);
        var items = (buy.Items ?? []).Select(i => new ShopCountMsg { ItemId = i.ItemId, Count = i.Count }).ToArray();
        string? error = await _world.TryBuyAsync(
            zoneSession,
            new RequestBuyItemMsg { NpcObjectId = buy.NpcObjectId, ListId = buy.ListId, Items = items },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Buy failed.", ct).ConfigureAwait(false);
    }

    async Task OnRequestSellItemAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var sell = NetCodec.DecodeRequestSellItem(envelope.Payload);
        var items = (sell.Items ?? []).Select(i => new ShopObjectCountMsg { ObjectId = i.ObjectId, Count = i.Count }).ToArray();
        string? error = await _world.TrySellAsync(
            zoneSession,
            new RequestSellItemMsg { NpcObjectId = sell.NpcObjectId, Items = items },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Sell failed.", ct).ConfigureAwait(false);
    }

    async Task OnRequestMultiSellChooseAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var choose = NetCodec.DecodeRequestMultiSellChoose(envelope.Payload);
        string? error = await _world.TryMultiSellAsync(
            zoneSession,
            new RequestMultiSellChooseMsg
            {
                NpcObjectId = choose.NpcObjectId,
                ListId = choose.ListId,
                EntryId = choose.EntryId,
                Amount = choose.Amount
            },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "MultiSell failed.", ct).ConfigureAwait(false);
    }

    async Task OnWarehouseDepositAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeWarehouseDepositRequest(envelope.Payload);
        var lines = (req.Lines ?? []).Select(l => new WarehouseCountMsg { ObjectId = l.ObjectId, Count = l.Count }).ToArray();
        string? error = await _world.TryWarehouseDepositAsync(
            zoneSession,
            new WarehouseDepositRequestMsg { NpcObjectId = req.NpcObjectId, Lines = lines },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Warehouse deposit failed.", ct).ConfigureAwait(false);
    }

    async Task OnWarehouseWithdrawAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeWarehouseWithdrawRequest(envelope.Payload);
        var lines = (req.Lines ?? []).Select(l => new WarehouseCountMsg { ObjectId = l.ObjectId, Count = l.Count }).ToArray();
        string? error = await _world.TryWarehouseWithdrawAsync(
            zoneSession,
            new WarehouseWithdrawRequestMsg { NpcObjectId = req.NpcObjectId, Lines = lines },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Warehouse withdraw failed.", ct).ConfigureAwait(false);
    }

    async Task OnRequestQuestListAsync(NetConnection conn, ZoneSession zoneSession, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryQuestListAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Quest list failed.", ct).ConfigureAwait(false);
    }

    async Task OnRequestQuestAbortAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var abort = NetCodec.DecodeRequestQuestAbort(envelope.Payload);
        string? error = await _world.TryQuestAbortAsync(zoneSession, abort.QuestId, ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Quest abort failed.", ct).ConfigureAwait(false);
    }

    async Task OnPartyInviteAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var invite = NetCodec.DecodePartyInviteRequest(envelope.Payload);
        string? error = await _world.TryPartyInviteAsync(
            zoneSession,
            new PartyInviteRequestMsg
            {
                TargetObjectId = invite.TargetObjectId,
                ItemDistribution = invite.ItemDistribution
            },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Party invite failed.", ct).ConfigureAwait(false);
    }

    async Task OnPartyAnswerAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var answer = NetCodec.DecodePartyAnswerJoinRequest(envelope.Payload);
        string? error = await _world.TryPartyAnswerAsync(
            zoneSession,
            new PartyAnswerJoinRequestMsg
            {
                RequestorObjectId = answer.RequestorObjectId,
                Response = answer.Response
            },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Party answer failed.", ct).ConfigureAwait(false);
    }

    async Task OnPartyLeaveAsync(NetConnection conn, ZoneSession zoneSession, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryPartyLeaveAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Party leave failed.", ct).ConfigureAwait(false);
    }

    async Task OnPartyKickAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var kick = NetCodec.DecodePartyKickRequest(envelope.Payload);
        string? error = await _world.TryPartyKickAsync(
            zoneSession,
            new PartyKickRequestMsg { TargetObjectId = kick.TargetObjectId },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Party kick failed.", ct).ConfigureAwait(false);
    }

    async Task OnTradeRequestAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeTradeRequest(envelope.Payload);
        string? error = await _world.TryTradeRequestAsync(
            zoneSession,
            new TradeRequestMsg { TargetObjectId = req.TargetObjectId },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Trade request failed.", ct).ConfigureAwait(false);
    }

    async Task OnTradeAnswerAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var answer = NetCodec.DecodeTradeAnswerRequest(envelope.Payload);
        string? error = await _world.TryTradeAnswerAsync(
            zoneSession,
            new TradeAnswerRequestMsg
            {
                RequestorObjectId = answer.RequestorObjectId,
                Response = answer.Response
            },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Trade answer failed.", ct).ConfigureAwait(false);
    }

    async Task OnTradeAddItemAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var add = NetCodec.DecodeTradeAddItemRequest(envelope.Payload);
        string? error = await _world.TryTradeAddItemAsync(
            zoneSession,
            new TradeAddItemRequestMsg
            {
                InventoryObjectId = add.InventoryObjectId,
                Count = add.Count
            },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Trade add item failed.", ct).ConfigureAwait(false);
    }

    async Task OnTradeDoneAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var done = NetCodec.DecodeTradeDoneRequest(envelope.Payload);
        string? error = await _world.TryTradeDoneAsync(
            zoneSession,
            new TradeDoneRequestMsg { Response = done.Response },
            ct).ConfigureAwait(false);
        if (error != null)
            await RejectAsync(conn, error, "Trade done failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }

    static Task RejectAsync(NetConnection conn, string code, string message, CancellationToken ct) =>
        conn.SendAsync(NetOpcode.Reject, NetCodec.EncodeReject(new NetReject { Code = code, Message = message }), ct: ct);
}
