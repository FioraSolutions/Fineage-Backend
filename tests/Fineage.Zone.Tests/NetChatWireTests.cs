using Fineage.Net.Contract;
using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Chat over real Net sessions: the whisper target tail, and local say heard identically by everyone near.</summary>
[Trait("Category", "Integration")]
public sealed class NetChatWireTests
{
    const int X = 49000, Y = 50000, Z = -3000;
    static CancellationToken Ct => CancellationToken.None;

    static Task SayAsync(NetMoveVisibilityRig.Player who, string text, int type, string target = "") =>
        who.Net.SendAsync(NetOpcode.Say, NetCodec.EncodeSay(new SayNetMsg { Text = text, ChatType = type, Target = target }), Ct);

    static List<CreatureSayNetMsg> Says(IEnumerable<PacketEnvelope> frames) =>
        frames.Where(f => f.Opcode == NetOpcode.CreatureSay).Select(f => NetCodec.DecodeCreatureSay(f.Payload)).ToList();

    [Fact]
    public async Task Whisper_by_name_over_the_wire_reaches_only_the_receiver_and_echoes_to_the_sender()
    {
        var rig = await NetMoveVisibilityRig.StartAsync(Ct);
        await using var _ = rig;
        var a = await rig.JoinAsync(X, Y, Z);
        var b = await rig.JoinAsync(X + 50_000, Y, Z);
        var c = await rig.JoinAsync(X + 20, Y, Z);
        foreach (var p in new[] { a, b, c })
            await p.DrainAsync();

        await SayAsync(a, "psst", ChatProtocol.Tell, b.Entity.Name);
        var mine = Says(await a.DrainAsync());
        var theirs = Says(await b.DrainAsync());
        Assert.Empty(Says(await c.DrainAsync()));

        var got = Assert.Single(theirs);
        Assert.Equal((a.ObjectId, a.Entity.Name, "psst", ChatProtocol.Tell), (got.ObjectId, got.Name, got.Text, got.ChatType));
        Assert.Equal("->" + b.Entity.Name, Assert.Single(mine).Name);
    }

    [Fact]
    public async Task Local_say_bytes_are_identical_for_the_speaker_and_every_listener_in_range()
    {
        var rig = await NetMoveVisibilityRig.StartAsync(Ct);
        await using var _ = rig;
        var a = await rig.JoinAsync(X, Y, Z);
        var near = await rig.JoinAsync(X + 1000, Y, Z);
        var outOfSay = await rig.JoinAsync(X + 1600, Y, Z); // visible (AOI 2000) but past the 1250 say radius
        foreach (var p in new[] { a, near, outOfSay })
            await p.DrainAsync();

        await SayAsync(a, "hello all", ChatProtocol.All);
        var fa = (await a.DrainAsync()).Where(f => f.Opcode == NetOpcode.CreatureSay).ToList();
        var fn = (await near.DrainAsync()).Where(f => f.Opcode == NetOpcode.CreatureSay).ToList();
        Assert.Single(fa);
        Assert.True(fa[0].Payload.AsSpan().SequenceEqual(Assert.Single(fn).Payload));
        Assert.Empty(Says(await outOfSay.DrainAsync()));
    }
}
