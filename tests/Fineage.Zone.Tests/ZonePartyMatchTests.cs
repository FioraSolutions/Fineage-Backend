using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius party matching rooms: create / join / dismiss / waiting / chat (#928).</summary>
public sealed class ZonePartyMatchTests
{
    static async Task<(ZoneWorld World, ZoneSession Session, ZoneEntity Player)> Enter(
        MemoryAccountStore store, int characterId, string account, string name, IWorldEventPublisher? bridge = null)
    {
        var world = new ZoneWorld(accounts: store, ledgerJournal: new MemoryLedgerJournal());
        if (bridge != null)
            world.NetBridge = bridge;
        var session = new ZoneSession();
        session.Bind(characterId, account, name, HumanClasses.Fighter);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        Assert.True(world.TryGetLivePlayer(session, out var player));
        return (world, session, player);
    }

    static (MemoryAccountStore Store, LoginService Login) NewStore()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("partymatch", "pass1234").Ok);
        return (store, login);
    }

    [Fact]
    public async Task Create_join_dismiss_and_waiting_list()
    {
        var (store, login) = NewStore();
        var a = login.CreateHuman("partymatch", "Leader", HumanClasses.Fighter).Value!;
        var b = login.CreateHuman("partymatch", "Joiner", HumanClasses.Fighter).Value!;
        var (world, s1, p1) = await Enter(store, a.Id, "partymatch", "Leader");
        var s2 = new ZoneSession();
        s2.Bind(b.Id, "partymatch", "Joiner", HumanClasses.Fighter);
        Assert.True(await world.TryEnterAsync(s2, CancellationToken.None));
        Assert.True(world.TryGetLivePlayer(s2, out var p2));

        Assert.Null(await world.TryPartyMatchListAsync(s1, new PartyMatchListMsg
        {
            RoomId = 0,
            MaxMembers = 7,
            MinLevel = 1,
            MaxLevel = 80,
            Loot = 0,
            Title = "TI grind"
        }, CancellationToken.None));
        Assert.True(world.PartyMatch.InRoom(p1.ObjectId));
        int roomId = world.PartyMatch.RoomIdOf(p1.ObjectId);
        Assert.True(roomId > 0);

        Assert.Null(await world.TryPartyMatchDetailAsync(s2, new PartyMatchDetailRequestMsg { RoomId = roomId },
            CancellationToken.None));
        Assert.Equal(roomId, world.PartyMatch.RoomIdOf(p2.ObjectId));
        Assert.Equal(2, world.PartyMatch.GetRoom(roomId)!.MemberCount);

        Assert.Null(await world.TryPartyRoomDismissAsync(s1, roomId, CancellationToken.None));
        Assert.False(world.PartyMatch.InRoom(p1.ObjectId));
        Assert.False(world.PartyMatch.InRoom(p2.ObjectId));

        Assert.Null(await world.TryPartyMatchConfigAsync(s2, new PartyMatchConfigMsg
        {
            Auto = 0,
            Location = 0,
            Level = 0
        }, CancellationToken.None));
        Assert.True(world.PartyMatch.IsWaiting(p2.ObjectId));
        Assert.Null(await world.TryPartyMatchWaitingExitAsync(s2, CancellationToken.None));
        Assert.False(world.PartyMatch.IsWaiting(p2.ObjectId));
    }

    [Fact]
    public async Task PartyMatchRoom_chat_logs_when_missing_room_and_reaches_members()
    {
        var (store, login) = NewStore();
        var a = login.CreateHuman("partymatch", "ChatLead", HumanClasses.Fighter).Value!;
        var b = login.CreateHuman("partymatch", "ChatMate", HumanClasses.Fighter).Value!;
        var (bridge, rec) = NetBridgeRecorder.Create();
        var (world, s1, p1) = await Enter(store, a.Id, "partymatch", "ChatLead", bridge);
        var s2 = new ZoneSession();
        s2.Bind(b.Id, "partymatch", "ChatMate", HumanClasses.Fighter);
        Assert.True(await world.TryEnterAsync(s2, CancellationToken.None));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.Null(await world.TrySayAsync(s1, "lonely", ChatProtocol.PartyMatchRoom, now, CancellationToken.None));
        Assert.Empty(rec.Of("FanoutCreatureSay"));

        Assert.Null(await world.TryPartyMatchListAsync(s1, new PartyMatchListMsg
        {
            RoomId = 0,
            MaxMembers = 5,
            MinLevel = 1,
            MaxLevel = 80,
            Title = "chat"
        }, CancellationToken.None));
        int roomId = world.PartyMatch.RoomIdOf(p1.ObjectId);
        Assert.Null(await world.TryPartyMatchDetailAsync(s2, new PartyMatchDetailRequestMsg { RoomId = roomId },
            CancellationToken.None));

        rec.Calls.Clear();
        Assert.Null(await world.TrySayAsync(s1, "hello room", ChatProtocol.PartyMatchRoom, now.AddSeconds(2),
            CancellationToken.None));
        Assert.NotEmpty(rec.Of("FanoutCreatureSay"));
    }

    [Fact]
    public async Task Oust_and_withdraw()
    {
        var (store, login) = NewStore();
        var a = login.CreateHuman("partymatch", "Owner", HumanClasses.Fighter).Value!;
        var b = login.CreateHuman("partymatch", "Guest", HumanClasses.Fighter).Value!;
        var (world, s1, p1) = await Enter(store, a.Id, "partymatch", "Owner");
        var s2 = new ZoneSession();
        s2.Bind(b.Id, "partymatch", "Guest", HumanClasses.Fighter);
        Assert.True(await world.TryEnterAsync(s2, CancellationToken.None));
        Assert.True(world.TryGetLivePlayer(s2, out var p2));

        Assert.Null(await world.TryPartyMatchListAsync(s1, new PartyMatchListMsg
        {
            RoomId = 0,
            MaxMembers = 5,
            MinLevel = 1,
            MaxLevel = 80,
            Title = "oust"
        }, CancellationToken.None));
        int roomId = world.PartyMatch.RoomIdOf(p1.ObjectId);
        Assert.Null(await world.TryPartyMatchDetailAsync(s2, new PartyMatchDetailRequestMsg { RoomId = roomId },
            CancellationToken.None));

        Assert.Null(await world.TryPartyRoomOustAsync(s1, p2.ObjectId, CancellationToken.None));
        Assert.False(world.PartyMatch.InRoom(p2.ObjectId));
        Assert.True(world.PartyMatch.IsWaiting(p2.ObjectId));

        Assert.Null(await world.TryPartyMatchDetailAsync(s2, new PartyMatchDetailRequestMsg { RoomId = roomId },
            CancellationToken.None));
        Assert.Null(await world.TryPartyRoomWithdrawAsync(s2, roomId, CancellationToken.None));
        Assert.False(world.PartyMatch.InRoom(p2.ObjectId));
    }
}
