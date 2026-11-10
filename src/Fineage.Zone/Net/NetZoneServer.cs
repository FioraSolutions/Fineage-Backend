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
public sealed partial class NetZoneServer
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
}
