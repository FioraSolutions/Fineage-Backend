using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Clan window over the real Net wire with several sessions: <see cref="NetOpcode.PledgeCommand"/> answers, and the
/// clan war colour (<see cref="NetOpcode.RelationChanged"/>) that rides after a player's spawn when two war enemies meet.
/// </summary>
public sealed class NetPledgeWireTests
{
    static async Task<int> ClanAsync(NetMoveVisibilityRig rig, NetMoveVisibilityRig.Player p, string name)
    {
        p.Entity.Level = 10;
        Assert.Null(await rig.World.ExecuteAsync(p.Entity.Session!, new ClanCreateCommand(new ClanCreateRequestMsg { Name = name }), rig.Ct));
        return p.Entity.ClanId;
    }

    [Fact]
    public async Task Pledge_info_request_is_answered_on_the_wire()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var rig = await NetMoveVisibilityRig.StartAsync(cts.Token);
        var a = await rig.JoinAsync(NetMoveVisibilityRig.X0, NetMoveVisibilityRig.Y0);
        int clanId = await ClanAsync(rig, a, "WireClan");
        await a.DrainAsync();

        await a.Net.SendAsync(NetOpcode.PledgeCommand, NetCodec.EncodePledgeCommand(new PledgeCommandNetMsg
        {
            Kind = PledgeCommandKind.RequestPledgeInfo,
            IntArg1 = clanId
        }), rig.Ct);
        var frames = await a.DrainAsync();
        var info = NetCodec.DecodePledgeInfo(Assert.Single(frames, f => f.Opcode == NetOpcode.PledgeInfo).Payload);
        Assert.Equal(("WireClan", clanId), (info.Name, info.ClanId));
    }

    [Fact]
    public async Task War_enemies_that_walk_into_view_get_the_war_relation_after_the_spawn()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var rig = await NetMoveVisibilityRig.StartAsync(cts.Token);
        int x0 = NetMoveVisibilityRig.X0, y0 = NetMoveVisibilityRig.Y0;
        var a = await rig.JoinAsync(x0, y0);
        var b = await rig.JoinAsync(x0 + 2600, y0);
        var c = await rig.JoinAsync(x0 + 300, y0); // neutral bystander next to A
        int ca = await ClanAsync(rig, a, "Reds");
        int cb = await ClanAsync(rig, b, "Blues");
        Assert.True(rig.World.SetMutualWarForTest(ca, cb));
        await a.DrainAsync();
        await b.DrainAsync();
        await c.DrainAsync();

        await b.MoveAsync(x0 + 200, y0);
        await b.NextMoveOfAsync(b);
        for (int i = 0; i < 200 && b.Entity.X > x0 + 1000; i++)
            await rig.AdvanceAsync(200, 200);
        await a.NextSpawnOfAsync(b);
        var aFrames = await a.DrainAsync();
        var cFrames = await c.DrainAsync();

        var relation = aFrames.Where(f => f.Opcode == NetOpcode.RelationChanged)
            .Select(f => NetCodec.DecodeRelationChanged(f.Payload)).First(r => r.ObjectId == b.ObjectId);
        Assert.NotEqual(0, relation.Relation & PledgeProtocol.RelationMutualWar);
        Assert.True(relation.AutoAttackable);

        // The bystander sees B arrive too, but no war colour.
        Assert.DoesNotContain(cFrames, f => f.Opcode == NetOpcode.RelationChanged && NetCodec.DecodeRelationChanged(f.Payload).ObjectId == b.ObjectId);
    }
}
