using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneCharacterActionWireTests
{
    [Fact]
    public async Task Sit_stand_broadcasts_change_wait_type()
    {
        var (world, session, stream) = await LiveSessionAsync();
        stream.SetLength(0);
        Assert.Null(await world.TryCharacterActionAsync(session, new CharacterActionRequestMsg { ActionId = ActionProtocol.SitStand }, CancellationToken.None));
        stream.Position = 0;

        var sit = await ReadNextAsync(stream, MsgType.ChangeWaitType);
        var wait = MessagePackSerializer.Deserialize<ChangeWaitTypeMsg>(sit.Body);
        Assert.Equal(session.ObjectId, wait.ObjectId);
        Assert.Equal(ActionProtocol.WaitSitting, wait.WaitType);

        stream.SetLength(0);
        Assert.Null(await world.TryCharacterActionAsync(session, new CharacterActionRequestMsg { ActionId = ActionProtocol.SitStand }, CancellationToken.None));
        stream.Position = 0;
        var stand = await ReadNextAsync(stream, MsgType.ChangeWaitType);
        wait = MessagePackSerializer.Deserialize<ChangeWaitTypeMsg>(stand.Body);
        Assert.Equal(ActionProtocol.WaitStanding, wait.WaitType);
    }

    [Fact]
    public async Task Walk_run_broadcasts_change_move_type()
    {
        var (world, session, stream) = await LiveSessionAsync();
        stream.SetLength(0);
        Assert.Null(await world.TryCharacterActionAsync(session, new CharacterActionRequestMsg { ActionId = ActionProtocol.WalkRun }, CancellationToken.None));
        stream.Position = 0;

        var frame = await ReadNextAsync(stream, MsgType.ChangeMoveType);
        var move = MessagePackSerializer.Deserialize<ChangeMoveTypeMsg>(frame.Body);
        Assert.Equal(session.ObjectId, move.ObjectId);
        Assert.False(move.Running);
    }

    [Fact]
    public async Task Social_greeting_fanout_to_peer()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("peer1", "pass1234").Ok);
        Assert.True(login.Register("peer2", "pass1234").Ok);
        var c1 = login.CreateHuman("peer1", "One", HumanClasses.Fighter);
        var c2 = login.CreateHuman("peer2", "Two", HumanClasses.Fighter);
        Assert.True(c1.Ok && c2.Ok);
        var t1 = login.Play("peer1", "pass1234", c1.Value!.Name, HumanClasses.Fighter);
        var t2 = login.Play("peer2", "pass1234", c2.Value!.Name, HumanClasses.Fighter);
        Assert.True(t1.Ok && t2.Ok);

        var world = new ZoneWorld(accounts: store, spawnRandom: new Random(1));
        await using var stream1 = new MemoryStream();
        await using var stream2 = new MemoryStream();
        var s1 = new ZoneSession(stream1);
        var s2 = new ZoneSession(stream2);
        s1.Bind(t1.Value!.CharacterId, t1.Value.Account, t1.Value.CharacterName, t1.Value.ClassId);
        s2.Bind(t2.Value!.CharacterId, t2.Value.Account, t2.Value.CharacterName, t2.Value.ClassId);
        Assert.True(await world.TryEnterAsync(s1, CancellationToken.None));
        Assert.True(await world.TryEnterAsync(s2, CancellationToken.None));

        stream1.SetLength(0);
        stream2.SetLength(0);
        Assert.Null(await world.TryCharacterActionAsync(s1, new CharacterActionRequestMsg { ActionId = ActionProtocol.SocialGreeting }, CancellationToken.None));
        stream2.Position = 0;

        var peer = await ReadNextAsync(stream2, MsgType.SocialAction);
        var social = MessagePackSerializer.Deserialize<SocialActionMsg>(peer.Body);
        Assert.Equal(s1.ObjectId, social.ObjectId);
        Assert.Equal(ActionProtocol.SocialGreeting, social.ActionId);
    }

    [Fact]
    public async Task Move_while_sitting_rejected()
    {
        var (world, session, stream) = await LiveSessionAsync();
        stream.SetLength(0);
        Assert.Null(await world.TryCharacterActionAsync(session, new CharacterActionRequestMsg { ActionId = ActionProtocol.SitStand }, CancellationToken.None));
        stream.Position = 0;
        _ = await ReadNextAsync(stream, MsgType.ChangeWaitType);

        var code = await world.TryMoveAsync(session, new MoveToMsg
        {
            DestX = session.X + 10,
            DestY = session.Y,
            DestZ = session.Z
        }, CancellationToken.None);
        Assert.Equal(ActionProtocol.Sitting, code);
    }

    [Fact]
    public async Task Select_target_self_sends_MyTargetSelected()
    {
        var (world, session, stream) = await LiveSessionAsync();
        stream.SetLength(0);

        Assert.Null(await world.TrySelectTargetAsync(
            session,
            new SelectTargetMsg { TargetObjectId = session.ObjectId },
            CancellationToken.None));

        stream.Position = 0;
        var frame = await ReadNextAsync(stream, MsgType.MyTargetSelected);
        var selected = MessagePackSerializer.Deserialize<MyTargetSelectedMsg>(frame.Body);
        Assert.Equal(session.ObjectId, selected.ObjectId);
        Assert.Equal(session.ObjectId, session.SelectedTargetId);
    }

    [Fact]
    public async Task Next_target_selects_different_gremlin()
    {
        var (world, session, stream) = await LiveSessionAsync();
        stream.Position = 0;

        int firstId = 0;
        int secondId = 0;
        while (true)
        {
            var frame = await FrameIO.ReadAsync(stream);
            if (frame == null)
                break;
            if (frame.Value.Type != (ushort)MsgType.NpcInfo)
                continue;
            var npc = MessagePackSerializer.Deserialize<NpcInfoMsg>(frame.Value.Body);
            if (npc.NpcId != TalkingIsland.GremlinNpcId)
                continue;
            if (firstId == 0)
                firstId = npc.ObjectId;
            else if (npc.ObjectId != firstId)
            {
                secondId = npc.ObjectId;
                break;
            }
        }

        Assert.True(firstId > 0);
        Assert.True(secondId > 0);

        stream.SetLength(0);
        Assert.Null(await world.TrySelectTargetAsync(session, new SelectTargetMsg { TargetObjectId = firstId }, CancellationToken.None));
        stream.Position = 0;
        _ = await ReadNextAsync(stream, MsgType.MyTargetSelected);

        stream.SetLength(0);
        Assert.Null(await world.TryCharacterActionAsync(session, new CharacterActionRequestMsg { ActionId = ActionProtocol.NextTarget }, CancellationToken.None));
        stream.Position = 0;
        var next = await ReadNextAsync(stream, MsgType.MyTargetSelected);
        var target = MessagePackSerializer.Deserialize<MyTargetSelectedMsg>(next.Body);
        Assert.NotEqual(firstId, target.ObjectId);
        Assert.True(target.ObjectId > 0);
    }

    static async Task<(ZoneWorld world, ZoneSession session, MemoryStream stream)> LiveSessionAsync()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret), create: new CharacterCreateOptions { StartingLevel = 5 });
        Assert.True(login.Register("act", "pass1234").Ok);
        var created = login.CreateHuman("act", "Actor", HumanClasses.Fighter);
        Assert.True(created.Ok);
        var ticket = login.Play("act", "pass1234", created.Value!.Name, HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);

        var world = new ZoneWorld(accounts: store, spawnRandom: new Random(2));
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
