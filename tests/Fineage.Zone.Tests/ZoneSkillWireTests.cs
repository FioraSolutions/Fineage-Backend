using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneSkillWireTests
{
    [Fact]
    public async Task EnterWorld_includes_mobius_auto_get_skills()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(tokens);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        Assert.True(login.Register("skill", "pass1234").Ok);
        var created = login.CreateHuman("skill", "Caster", HumanClasses.Fighter);
        Assert.True(created.Ok);
        var ticket = login.Play("skill", "pass1234", created.Value!.Name, HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource();
        var acceptTask = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
            await using var s = accepted.GetStream();
            await server.HandleAsync(s, cts.Token);
        });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)MsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, MsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)MsgType.Auth, MessagePackSerializer.Serialize(ticket.Value!.ToZoneAuth()));
        await ReadAsync(stream, MsgType.AuthOk);
        await FrameIO.SendAsync(stream, (ushort)MsgType.EnterWorld, MessagePackSerializer.Serialize(new EnterWorldMsg()));
        await ReadAsync(stream, MsgType.UserInfo);
        await ReadAsync(stream, MsgType.ItemList);
        var skillFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(skillFrame);
        Assert.Equal((ushort)MsgType.SkillList, skillFrame!.Value.Type);
        var list = MessagePackSerializer.Deserialize<SkillListMsg>(skillFrame.Value.Body);
        Assert.Contains(list.Skills, row => row.SkillId == 194);

        listener.Stop();
        await cts.CancelAsync();
        try
        {
            await acceptTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [Fact]
    public async Task SkillUse_power_strike_damages_gremlin_and_sends_magic_skill_use()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(
            store,
            tokens,
            create: new CharacterCreateOptions { StartingLevel = 5 });
        Assert.True(login.Register("duel", "pass1234").Ok);
        var created = login.CreateHuman("duel", "Slayer", HumanClasses.Fighter);
        Assert.True(created.Ok);
        var ticket = login.Play("duel", "pass1234", created.Value!.Name, HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);

        var now = DateTimeOffset.UtcNow;
        var world = new ZoneWorld(accounts: store, spawnRandom: new Random(3), clock: () => now);
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));

        stream.Position = 0;
        int gremlinObjectId = 0;
        NpcInfoMsg gremlinTemplate = TalkingIsland.Gremlins[0];
        while (true)
        {
            var frame = await FrameIO.ReadAsync(stream);
            if (frame == null)
                break;
            if (frame.Value.Type == (ushort)MsgType.NpcInfo)
            {
                var npc = MessagePackSerializer.Deserialize<NpcInfoMsg>(frame.Value.Body);
                if (npc.NpcId == TalkingIsland.GremlinNpcId)
                {
                    gremlinObjectId = npc.ObjectId;
                    gremlinTemplate = npc;
                    break;
                }
            }
        }

        Assert.True(gremlinObjectId > 0);
        Assert.Null(await world.TryMoveAsync(session, new MoveToMsg
        {
            DestX = gremlinTemplate.X,
            DestY = gremlinTemplate.Y,
            DestZ = gremlinTemplate.Z
        }, CancellationToken.None));
        Assert.Null(await world.TrySelectTargetAsync(session, new SelectTargetMsg { TargetObjectId = gremlinObjectId }, CancellationToken.None));

        stream.SetLength(0);
        Assert.Null(await world.TrySkillUseAsync(session, new SkillUseRequestMsg
        {
            SkillId = 3,
            TargetObjectId = gremlinObjectId
        }, CancellationToken.None));

        // Mobius onMagicHitTimer: no damage on key press; movement locked while casting.
        Assert.Equal("casting", await world.TryMoveAsync(session, new MoveToMsg
        {
            DestX = gremlinTemplate.X + 10,
            DestY = gremlinTemplate.Y,
            DestZ = gremlinTemplate.Z
        }, CancellationToken.None));
        await world.RunCombatTickAsync(CancellationToken.None);
        stream.Position = 0;
        while (await FrameIO.ReadAsync(stream) is { } early)
        {
            if (early.Type == (ushort)MsgType.StatusUpdate)
                Assert.NotEqual(gremlinObjectId, MessagePackSerializer.Deserialize<StatusUpdateMsg>(early.Body).ObjectId);
        }

        now = now.AddSeconds(5);
        await world.RunCombatTickAsync(CancellationToken.None);

        stream.Position = 0;
        MagicSkillUseMsg? cast = null;
        StatusUpdateMsg? targetHp = null;
        while (true)
        {
            var frame = await FrameIO.ReadAsync(stream);
            if (frame == null)
                break;
            if (frame.Value.Type == (ushort)MsgType.MagicSkillUse)
                cast = MessagePackSerializer.Deserialize<MagicSkillUseMsg>(frame.Value.Body);
            else if (frame.Value.Type == (ushort)MsgType.StatusUpdate)
            {
                var status = MessagePackSerializer.Deserialize<StatusUpdateMsg>(frame.Value.Body);
                if (cast != null && status.ObjectId == cast.TargetObjectId)
                    targetHp = status;
            }
        }

        Assert.NotNull(cast);
        Assert.Equal(3, cast!.SkillId);
        Assert.Equal(gremlinObjectId, cast.TargetObjectId);
        Assert.NotNull(targetHp);
        Assert.Equal(gremlinObjectId, targetHp!.ObjectId);
        Assert.True(targetHp.Values[0] > 0);
        Assert.True(targetHp.Values[0] < targetHp.Values[1]);

        // Mobius addDamageHate -> mob swings back at the attacker.
        stream.SetLength(0);
        now = now.AddSeconds(2);
        await world.RunCombatTickAsync(CancellationToken.None);
        stream.Position = 0;
        AttackMsg? retaliate = null;
        while (await FrameIO.ReadAsync(stream) is { } f)
        {
            if (f.Type == (ushort)MsgType.Attack)
                retaliate = MessagePackSerializer.Deserialize<AttackMsg>(f.Body);
        }
        Assert.NotNull(retaliate);
        Assert.Equal(gremlinObjectId, retaliate!.AttackerObjectId);
        Assert.Equal(session.ObjectId, retaliate.TargetObjectId);
    }

    static async Task ReadAsync(Stream stream, MsgType type)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)type, frame!.Value.Type);
    }
}
