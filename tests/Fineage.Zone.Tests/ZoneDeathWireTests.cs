using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneDeathWireTests
{
    [Fact]
    public async Task Admin_kill_fanouts_die_and_zero_hp()
    {
        var (world, session, stream) = await LiveSessionAsync();
        stream.SetLength(0);

        Assert.Null(await world.TryAdminKillPlayerAsync(session, CancellationToken.None));
        stream.Position = 0;

        var dieFrame = await ReadNextAsync(stream, MsgType.Die);
        var die = MessagePackSerializer.Deserialize<DieMsg>(dieFrame.Body);
        Assert.Equal(session.ObjectId, die.ObjectId);

        var statusFrame = await ReadNextAsync(stream, MsgType.StatusUpdate);
        var status = MessagePackSerializer.Deserialize<StatusUpdateMsg>(statusFrame.Body);
        Assert.Equal(session.ObjectId, status.ObjectId);
        Assert.Equal(0, status.Values[0]);
    }

    [Fact]
    public async Task Dead_player_cannot_move()
    {
        var (world, session, _) = await LiveSessionAsync();
        Assert.Null(await world.TryAdminKillPlayerAsync(session, CancellationToken.None));

        var code = await world.TryMoveAsync(session, new MoveToMsg
        {
            DestX = session.X + 10,
            DestY = session.Y,
            DestZ = session.Z
        }, CancellationToken.None);
        Assert.Equal(InventoryProtocol.Dead, code);
    }

    [Fact]
    public async Task Revive_to_village_restores_and_teleports()
    {
        var (world, session, stream) = await LiveSessionAsync();
        int corpseX = session.X;
        int corpseY = session.Y;
        Assert.Null(await world.TryAdminKillPlayerAsync(session, CancellationToken.None));

        stream.SetLength(0);
        Assert.Null(await world.TryReviveAsync(session, new ReviveRequestMsg { Action = DeathProtocol.ReviveToVillage }, CancellationToken.None));
        stream.Position = 0;

        var revive = await ReadNextAsync(stream, MsgType.Revive);
        Assert.Equal(session.ObjectId, MessagePackSerializer.Deserialize<ReviveMsg>(revive.Body).ObjectId);

        var move = await ReadNextAsync(stream, MsgType.MoveToAck);
        var ack = MessagePackSerializer.Deserialize<MoveToMsg>(move.Body);
        Assert.Equal(TalkingIslandRestart.X, ack.DestX);
        Assert.Equal(TalkingIslandRestart.Y, ack.DestY);
        Assert.Equal(TalkingIslandRestart.Z, ack.DestZ);
        Assert.Equal(corpseX, ack.OriginX);
        Assert.Equal(corpseY, ack.OriginY);

        var teleport = await ReadNextAsync(stream, MsgType.TeleportToLocation);
        var tp = MessagePackSerializer.Deserialize<TeleportToLocationMsg>(teleport.Body);
        Assert.Equal(TalkingIslandRestart.X, tp.X);
        Assert.Equal(TalkingIslandRestart.Y, tp.Y);

        var user = await ReadNextAsync(stream, MsgType.UserInfo);
        _ = MessagePackSerializer.Deserialize<UserInfoMsg>(user.Body);

        var vitals = await ReadNextAsync(stream, MsgType.StatusUpdate);
        var hp = MessagePackSerializer.Deserialize<StatusUpdateMsg>(vitals.Body);
        Assert.True(hp.Values[0] > 0);

        Assert.Equal(TalkingIslandRestart.X, session.X);
        Assert.Equal(TalkingIslandRestart.Y, session.Y);
    }

    [Fact]
    public async Task Peer_sees_die_when_player_dies()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("death1", "pass1234").Ok);
        Assert.True(login.Register("death2", "pass1234").Ok);
        var c1 = login.CreateHuman("death1", "One", HumanClasses.Fighter);
        var c2 = login.CreateHuman("death2", "Two", HumanClasses.Fighter);
        Assert.True(c1.Ok && c2.Ok);
        var t1 = login.Play("death1", "pass1234", c1.Value!.Name, HumanClasses.Fighter);
        var t2 = login.Play("death2", "pass1234", c2.Value!.Name, HumanClasses.Fighter);
        Assert.True(t1.Ok && t2.Ok);

        var world = new ZoneWorld(accounts: store, spawnRandom: new Random(4));
        await using var stream1 = new MemoryStream();
        await using var stream2 = new MemoryStream();
        var s1 = new ZoneSession(stream1);
        var s2 = new ZoneSession(stream2);
        s1.Bind(t1.Value!.CharacterId, t1.Value.Account, t1.Value.CharacterName, t1.Value.ClassId);
        s2.Bind(t2.Value!.CharacterId, t2.Value.Account, t2.Value.CharacterName, t2.Value.ClassId);
        Assert.True(await world.TryEnterAsync(s1, CancellationToken.None));
        Assert.True(await world.TryEnterAsync(s2, CancellationToken.None));

        stream2.SetLength(0);
        Assert.Null(await world.TryAdminKillPlayerAsync(s1, CancellationToken.None));
        stream2.Position = 0;

        var peerDie = await ReadNextAsync(stream2, MsgType.Die);
        var die = MessagePackSerializer.Deserialize<DieMsg>(peerDie.Body);
        Assert.Equal(s1.ObjectId, die.ObjectId);
    }

    static async Task<(ZoneWorld world, ZoneSession session, MemoryStream stream)> LiveSessionAsync()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("dead", "pass1234").Ok);
        var created = login.CreateHuman("dead", "Corpse", HumanClasses.Fighter);
        Assert.True(created.Ok);
        var ticket = login.Play("dead", "pass1234", created.Value!.Name, HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);

        var world = new ZoneWorld(accounts: store, spawnRandom: new Random(9));
        var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        return (world, session, stream);
    }

    static async Task<(ushort Type, byte[] Body)> ReadNextAsync(Stream stream, MsgType type)
    {
        while (true)
        {
            var frame = await FrameIO.ReadAsync(stream);
            if (frame == null)
                throw new InvalidOperationException($"Stream ended before {type}.");
            if (frame.Value.Type == (ushort)type)
                return frame.Value;
        }
    }
}
