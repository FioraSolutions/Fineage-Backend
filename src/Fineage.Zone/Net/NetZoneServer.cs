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
public sealed class NetZoneServer
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
    readonly NetWorldEventPublisher _events;

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
        _events = new NetWorldEventPublisher(_world, _ids, _presence, PlayerSpawnPAtkSpd);
        _world.NetBridge = _events;
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



    internal static UserInfoNetMsg ToNetUserInfo(Fineage.Protocol.UserInfoMsg info) =>
        NetWorldEventPublisher.ToNetUserInfo(info);

    internal static EtcStatusUpdateNetMsg ToNetEtcStatus(Fineage.Protocol.EtcStatusUpdateMsg status, int objectId) =>
        NetWorldEventPublisher.ToNetEtcStatus(status, objectId);

    internal static PledgeStatusNetMsg ToNetPledgeStatus(Fineage.Protocol.PledgeStatusChangedMsg status) =>
        NetWorldEventPublisher.ToNetPledgeStatus(status);

    internal static PledgeMemberListNetMsg ToNetPledgeMemberList(Fineage.Protocol.PledgeMemberListMsg list) =>
        NetWorldEventPublisher.ToNetPledgeMemberList(list);

    internal static QuestListNetMsg ToNetQuestList(Fineage.Protocol.QuestListMsg list) =>
        NetWorldEventPublisher.ToNetQuestList(list);

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

}
