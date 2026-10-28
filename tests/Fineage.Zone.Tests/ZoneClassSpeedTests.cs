using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneClassSpeedTests
{
    [Theory]
    [InlineData(0, 115f, 80f)]
    [InlineData(10, 120f, 78f)]
    public async Task Enter_world_user_info_uses_the_class_move_speed(int classId, float run, float walk)
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("alice", "secret1").Ok);
        var created = login.CreateHuman("alice", "Aria" + classId, classId, race: 0);
        Assert.True(created.Ok, created.Message);
        var ticket = login.Play("alice", "secret1", created.Value!.Name, classId);
        Assert.True(ticket.Ok, ticket.Message);

        var world = new ZoneWorld(accounts: store, spawnRandom: new Random(1));
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));

        stream.Position = 0;
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.UserInfo, frame!.Value.Type);
        var user = MessagePackSerializer.Deserialize<UserInfoMsg>(frame.Value.Body);
        Assert.Equal(classId, user.ClassId);
        Assert.Equal(run, user.RunSpeed);
        Assert.Equal(walk, user.WalkSpeed);

        var itemFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(itemFrame);
        Assert.Equal((ushort)MsgType.ItemList, itemFrame!.Value.Type);
        var skillFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(skillFrame);
        Assert.Equal((ushort)MsgType.SkillList, skillFrame!.Value.Type);
        var next = await FrameIO.ReadAsync(stream);
        Assert.NotNull(next);
        Assert.Equal((ushort)MsgType.PlayerSpawned, next!.Value.Type);
        var spawn = MessagePackSerializer.Deserialize<PlayerSpawnedMsg>(next.Value.Body);
        Assert.Equal(run, spawn.RunSpeed);
        Assert.Equal(walk, spawn.WalkSpeed);
    }
}
