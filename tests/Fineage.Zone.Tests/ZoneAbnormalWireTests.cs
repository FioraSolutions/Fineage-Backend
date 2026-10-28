using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneAbnormalWireTests
{
    [Fact]
    public async Task SkillUse_shield_applies_mobius_buff_on_self()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret), create: new CharacterCreateOptions { StartingLevel = 7 });
        Assert.True(login.Register("wind", "pass1234").Ok);
        var created = login.CreateHuman("wind", "Walker", HumanClasses.Mystic);
        Assert.True(created.Ok);
        var ticket = login.Play("wind", "pass1234", created.Value!.Name, HumanClasses.Mystic);
        Assert.True(ticket.Ok && ticket.Value != null);

        var world = new ZoneWorld(accounts: store);
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));

        stream.SetLength(0);
        Assert.Null(await world.TrySkillUseAsync(session, new SkillUseRequestMsg { SkillId = 1040 }, CancellationToken.None));

        var abnormal = await ReadNextAbnormalAsync(stream);
        Assert.Equal(ticket.Value.CharacterId, abnormal.ObjectId);
        Assert.Single(abnormal.Effects);
        Assert.Equal(1040, abnormal.Effects[0].Id);
        Assert.Equal(1, abnormal.Effects[0].Value);
        Assert.Equal(1200, abnormal.Effects[0].DurationSeconds);
    }

    [Fact]
    public async Task SkillUse_curse_weakness_applies_debuff_on_gremlin()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret), create: new CharacterCreateOptions { StartingLevel = 14 });
        Assert.True(login.Register("shackle", "pass1234").Ok);
        var created = login.CreateHuman("shackle", "Binder", HumanClasses.Mystic);
        Assert.True(created.Ok);
        var near = TalkingIsland.Gremlins[0];
        Assert.True(store.TrySavePresence("shackle", created.Value!.Id, near.X, near.Y, near.Z, DateTimeOffset.UtcNow));
        var ticket = login.Play("shackle", "pass1234", created.Value!.Name, HumanClasses.Mystic);
        Assert.True(ticket.Ok && ticket.Value != null);

        var world = new ZoneWorld(accounts: store, spawnRandom: new Random(3));
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));

        int gremlinObjectId = 0;
        NpcInfoMsg gremlin = TalkingIsland.Gremlins[0];
        stream.Position = 0;
        while (true)
        {
            var frame = await FrameIO.ReadAsync(stream);
            if (frame == null)
                break;
            if (frame.Value.Type != (ushort)MsgType.NpcInfo)
                continue;
            var npc = MessagePackSerializer.Deserialize<NpcInfoMsg>(frame.Value.Body);
            if (npc.NpcId == TalkingIsland.GremlinNpcId)
            {
                gremlinObjectId = npc.ObjectId;
                gremlin = npc;
                break;
            }
        }

        Assert.True(gremlinObjectId > 0);
        Assert.Null(await world.TryMoveAsync(session, new MoveToMsg
        {
            DestX = gremlin.X,
            DestY = gremlin.Y,
            DestZ = gremlin.Z
        }, CancellationToken.None));
        Assert.Null(await world.TrySelectTargetAsync(session, new SelectTargetMsg { TargetObjectId = gremlinObjectId }, CancellationToken.None));

        stream.SetLength(0);
        Assert.Null(await world.TrySkillUseAsync(session, new SkillUseRequestMsg
        {
            SkillId = 1164,
            TargetObjectId = gremlinObjectId
        }, CancellationToken.None));

        var abnormal = await ReadNextAbnormalAsync(stream);
        Assert.Equal(gremlinObjectId, abnormal.ObjectId);
        Assert.Single(abnormal.Effects);
        Assert.Equal(1164, abnormal.Effects[0].Id);
        Assert.Equal(1, abnormal.Effects[0].Value);
        Assert.Equal(15, abnormal.Effects[0].DurationSeconds);
    }

    [Fact]
    public async Task Abnormal_expires_and_fanout_clears_effect()
    {
        long nowMs = 1_700_000_000_000L;
        var clock = new TestClock(nowMs);
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret), create: new CharacterCreateOptions { StartingLevel = 7 });
        Assert.True(login.Register("expire", "pass1234").Ok);
        var created = login.CreateHuman("expire", "Timer", HumanClasses.Mystic);
        Assert.True(created.Ok);
        var ticket = login.Play("expire", "pass1234", created.Value!.Name, HumanClasses.Mystic);
        Assert.True(ticket.Ok && ticket.Value != null);

        var world = new ZoneWorld(accounts: store, clock: clock.Now);
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));

        stream.SetLength(0);
        Assert.Null(await world.TryBuffRequestAsync(session, new BuffRequestMsg { EffectId = ZoneBuffs.SampleWindWalk }, CancellationToken.None));
        _ = await ReadNextAbnormalAsync(stream);

        clock.AdvanceSeconds(1201);
        stream.SetLength(0);
        await world.RunAbnormalTickAsync(CancellationToken.None);

        var cleared = await ReadNextAbnormalAsync(stream);
        Assert.Equal(ticket.Value.CharacterId, cleared.ObjectId);
        Assert.Empty(cleared.Effects);
    }

    static async Task<AbnormalStatusUpdateMsg> ReadNextAbnormalAsync(Stream stream)
    {
        stream.Position = 0;
        while (true)
        {
            var frame = await FrameIO.ReadAsync(stream);
            Assert.NotNull(frame);
            if (frame!.Value.Type == (ushort)MsgType.AbnormalStatusUpdate)
                return MessagePackSerializer.Deserialize<AbnormalStatusUpdateMsg>(frame.Value.Body);
        }
    }

    sealed class TestClock(long startMs)
    {
        long _ms = startMs;

        public DateTimeOffset Now() => DateTimeOffset.FromUnixTimeMilliseconds(_ms);

        public void AdvanceSeconds(int seconds) => _ms += seconds * 1000L;
    }
}
