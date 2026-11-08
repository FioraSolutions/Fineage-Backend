using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Interest;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class SayRulesTests
{
    [Fact]
    public void Trims_and_rejects_empty_long_and_control_text()
    {
        Assert.True(SayRules.TryNormalize("  hello  ", out string hello));
        Assert.Equal("hello", hello);
        Assert.True(SayRules.TryNormalize(new string('a', ChatProtocol.MaxTextLength), out string full));
        Assert.Equal(ChatProtocol.MaxTextLength, full.Length);

        Assert.False(SayRules.TryNormalize(null, out _));
        Assert.False(SayRules.TryNormalize("", out _));
        Assert.False(SayRules.TryNormalize("   ", out _));
        Assert.False(SayRules.TryNormalize(new string('a', ChatProtocol.MaxTextLength + 1), out _));
        Assert.False(SayRules.TryNormalize("  " + new string('a', ChatProtocol.MaxTextLength), out _));
        Assert.False(SayRules.TryNormalize("hi\n", out _));
        Assert.False(SayRules.TryNormalize("hi\u007f", out _));
    }

    [Fact]
    public void Local_range_includes_the_boundary()
    {
        Assert.True(SayRules.InLocalRange(0, 0, ChatProtocol.LocalRange, 0));
        Assert.False(SayRules.InLocalRange(0, 0, ChatProtocol.LocalRange + 1, 0));
        Assert.Equal(1250, ChatProtocol.LocalRange);
        Assert.Equal(100, ChatProtocol.MaxTextLength);
        Assert.Equal(1000, ChatProtocol.FloodIntervalMs);
    }

    [Fact]
    public void Flood_window_starts_after_the_first_accepted_say()
    {
        Assert.False(SayRules.IsFlood(0, 5_000));
        Assert.True(SayRules.IsFlood(5_000, 5_999));
        Assert.False(SayRules.IsFlood(5_000, 6_000));
    }
}

/// <summary>
/// Chat routing over the Net bridge: who hears a line, the flood window, and the refusal for channels the
/// server does not carry. These used to be asserted by reading V2 MessagePack frames off a MemoryStream;
/// they now assert the Net fanout the client actually receives.
/// </summary>
public sealed class NetChatTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);

    /// <summary>One entry per fanout call: the watchers it was addressed to and the line they got.</summary>
    static List<(IReadOnlyList<int> Watchers, CreatureSayNetMsg Line)> Says(NetBridgeRecorder rec) =>
        rec.Of(nameof(INetPresenceBridge.FanoutCreatureSay))
            .Select(args => ((IReadOnlyList<int>)args[0], (CreatureSayNetMsg)args[1]))
            .ToList();

    static bool Hears(IEnumerable<(IReadOnlyList<int> Watchers, CreatureSayNetMsg Line)> says, int objectId, string text) =>
        says.Any(s => s.Watchers.Contains(objectId) && s.Line.Text == text);

    static bool HearsAny(IEnumerable<(IReadOnlyList<int> Watchers, CreatureSayNetMsg Line)> says, int objectId) =>
        says.Any(s => s.Watchers.Contains(objectId));

    static List<SystemMessageNetMsg> SystemLines(NetBridgeRecorder rec) =>
        rec.Of(nameof(INetPresenceBridge.FanoutSystemMessage))
            .Select(args => (SystemMessageNetMsg)args[1])
            .ToList();

    [Fact]
    public async Task All_reaches_the_local_knownlist_inside_1250_and_nothing_past_it()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        var (origin, _) = await kit.EnterAsync(x: 0, y: 0);
        var (edge, _) = await kit.EnterAsync(x: ChatProtocol.LocalRange, y: 0);
        var (past, _) = await kit.EnterAsync(x: ChatProtocol.LocalRange + 1, y: 0);
        var (far, _) = await kit.EnterAsync(x: 5_000, y: 0);
        rec.Calls.Clear();

        Assert.Null(await kit.World.TrySayAsync(origin, "  hello  ", ChatProtocol.All, Now, CancellationToken.None));

        // The speaker always hears their own line, and so does everyone on the local boundary.
        Assert.True(Hears(Says(rec), origin.ObjectId, "hello"));
        Assert.True(Hears(Says(rec), edge.ObjectId, "hello"));
        Assert.False(HearsAny(Says(rec), past.ObjectId));
        Assert.False(HearsAny(Says(rec), far.ObjectId));

        // The line the client draws is the speaker's, with the trimmed text.
        var line = Says(rec).Select(s => s.Line).First();
        Assert.Equal(origin.ObjectId, line.ObjectId);
        Assert.Equal("hello", line.Text);
    }

    [Fact]
    public async Task Shout_reaches_the_whole_zone_including_outside_interest()
    {
        var kit = new NetTestKit(aoi: new RangeAoi(300));
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        var (origin, _) = await kit.EnterAsync(x: 0, y: 0);
        var (close, _) = await kit.EnterAsync(x: 100, y: 0);
        var (hidden, _) = await kit.EnterAsync(x: 1_000, y: 0);

        Assert.Null(await kit.World.TrySayAsync(origin, "local", ChatProtocol.All, Now, CancellationToken.None));
        Assert.True(Hears(Says(rec), close.ObjectId, "local"));
        Assert.False(HearsAny(Says(rec), hidden.ObjectId));

        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(origin, "zone", ChatProtocol.Shout, Now.AddSeconds(1), CancellationToken.None));

        Assert.True(Hears(Says(rec), close.ObjectId, "zone"));
        Assert.True(Hears(Says(rec), hidden.ObjectId, "zone"));
        Assert.All(Says(rec), s => Assert.Equal(ChatProtocol.Shout, s.Line.ChatType));
    }

    [Fact]
    public async Task Tell_party_and_clan_are_refused_without_a_broadcast()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        var (speaker, _) = await kit.EnterAsync(x: 0, y: 0);
        await kit.EnterAsync(x: 100, y: 0);
        rec.Calls.Clear();

        DateTimeOffset now = Now;
        int[] types = [ChatProtocol.Tell, ChatProtocol.Party, ChatProtocol.Clan, ChatProtocol.Trade, ChatProtocol.Hero, 99];
        foreach (int type in types)
        {
            Assert.Null(await kit.World.TrySayAsync(speaker, "secret", type, now, CancellationToken.None));

            SystemMessageNetMsg notice = Assert.Single(SystemLines(rec));
            Assert.Equal(ChatProtocol.ChatNotSupported, notice.Id);
            Assert.Equal("not supported yet", notice.Text);
            Assert.Equal(type.ToString(), Assert.Single(notice.Params));
            Assert.Empty(Says(rec));


            rec.Calls.Clear();
            now = now.AddSeconds(1);
        }
    }

    [Fact]
    public async Task Flood_throttle_answers_with_a_system_line_then_allows_the_next_one()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (speaker, _) = await kit.EnterAsync(x: 0, y: 0);
        rec.Calls.Clear();

        Assert.Null(await kit.World.TrySayAsync(speaker, "one", ChatProtocol.All, Now, CancellationToken.None));
        Assert.Equal("one", Assert.Single(Says(rec)).Line.Text);

        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(speaker, "two", ChatProtocol.Shout, Now.AddMilliseconds(999), CancellationToken.None));
        Assert.Equal("Please do not spam the chat.", Assert.Single(SystemLines(rec)).Text);
        Assert.Empty(Says(rec));

        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(speaker, "three", ChatProtocol.All, Now.AddMilliseconds(1000), CancellationToken.None));
        Assert.Equal("three", Assert.Single(Says(rec)).Line.Text);
    }

    [Fact]
    public async Task Rejected_text_does_not_broadcast_or_start_the_flood_window()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (speaker, _) = await kit.EnterAsync(x: 0, y: 0);
        rec.Calls.Clear();

        Assert.Equal("bad_request", await kit.World.TrySayAsync(speaker, "   ", ChatProtocol.All, Now, CancellationToken.None));
        Assert.Equal("bad_request", await kit.World.TrySayAsync(
            speaker, new string('z', ChatProtocol.MaxTextLength + 1), ChatProtocol.All, Now, CancellationToken.None));
        Assert.Equal("bad_request", await kit.World.TrySayAsync(speaker, "no\npe", ChatProtocol.All, Now, CancellationToken.None));
        Assert.Empty(Says(rec));

        // None of the refusals touched the flood window, so the next good line still goes out.
        Assert.Null(await kit.World.TrySayAsync(speaker, "ok", ChatProtocol.All, Now, CancellationToken.None));
        Assert.Equal("ok", Assert.Single(Says(rec)).Line.Text);
    }
}
