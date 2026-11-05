using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Enter-world UserInfo / PlayerSpawned carry the live speeds, i.e. the class template base already multiplied by the
/// <c>FuncMoveSpeed</c> DEX bonus (Mobius <c>CreatureStat.getRunSpeed()</c> → <c>calcStat(Stat.MOVE_SPEED, …)</c>,
/// <c>FuncMoveSpeed.java:34-45</c>). Human Fighter DEX 30 → ×1.1, Human Mystic DEX 21 → ×1.01
/// (<c>data/stat-bonuses.json</c> = <c>statBonus.xml</c>) — the wire value is not the raw template number.
/// </summary>
public sealed class ZoneClassSpeedTests
{
    [Theory]
    [InlineData(0, 126.5f, 88f)]      // 115 / 80 × 1.10
    [InlineData(10, 121.2f, 78.78f)]  // 120 / 78 × 1.01
    public async Task Enter_world_user_info_uses_the_class_move_speed_with_dex_bonus(int classId, float run, float walk)
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
