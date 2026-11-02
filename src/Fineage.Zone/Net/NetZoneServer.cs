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

    public NetZoneServer(
        HandoffTokens tokens,
        ZoneWorld world,
        IAccountStore? accounts = null,
        Func<DateTimeOffset>? clock = null,
        NetPresenceHub? presence = null)
    {
        _tokens = tokens;
        _world = world;
        // Character durable state is loaded by ZoneWorld.TryEnterAsync via the shared store.
        _ = accounts;
        _presence = presence ?? new NetPresenceHub();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
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
            _ = _presence.SendAsync(watcher, NetOpcode.MoveState, payload, CancellationToken.None);
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
        await using var discard = new MemoryStream();
        var zoneSession = new ZoneSession(discard);
        EntityId self = default;
        int registeredObjectId = 0;

        try
        {
            await foreach (var envelope in conn.Inbox.ReadAllAsync(ct).ConfigureAwait(false))
            {
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
                        case NetOpcode.AttackRequest:
                            await OnAttackAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.PickupRequest:
                            await OnPickupAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                            break;
                        case NetOpcode.DropItemRequest:
                            await OnDropItemAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
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
        await conn.SendAsync(NetOpcode.ItemList, NetCodec.EncodeItemList(packets.Items), ct: ct)
            .ConfigureAwait(false);
        await conn.SendAsync(NetOpcode.SkillList, NetCodec.EncodeSkillList(packets.Skills), ct: ct)
            .ConfigureAwait(false);

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

        int color = 0;
        if (_world.TryGetLivePlayer(zoneSession, out var player) &&
            _world.TryGetEntityById(zoneSession.SelectedTargetId, out var target) &&
            target.Kind == ZoneEntityKind.Npc)
            color = InterludeStatus.TargetColor(player.Level, target.Level);

        await conn.SendAsync(
            NetOpcode.TargetSelected,
            NetCodec.EncodeTargetSelected(new TargetSelectedNetMsg
            {
                TargetObjectId = zoneSession.SelectedTargetId,
                ColorHint = color
            }),
            envelope.RequestSequence,
            ct).ConfigureAwait(false);
    }

    async Task OnAttackAsync(
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
