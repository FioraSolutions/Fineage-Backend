using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Interest;
using Fineage.Zone.World;
using MessagePack;
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

public sealed class ZoneChatTests
{
    [Fact]
    public async Task Enter_ends_with_welcome_and_all_reaches_knownlist_inside_1250()
    {
        var (login, store, world) = Boot();
        await using var origin = await EnterAsync(login, store, world, "near", "Near", 0, 0);
        await using var edge = await EnterAsync(login, store, world, "edge", "Edge", ChatProtocol.LocalRange, 0);
        await using var past = await EnterAsync(login, store, world, "past", "Past", ChatProtocol.LocalRange + 1, 0);
        await using var far = await EnterAsync(login, store, world, "far", "Far", 5_000, 0);

        Assert.Equal(ChatProtocol.Welcome, origin.Welcome.Id);
        Assert.Equal("Near", Assert.Single(origin.Welcome.Params));
        Assert.Equal("Welcome, Near.", origin.Welcome.Text);
        Assert.Equal("Welcome, Edge.", edge.Welcome.Text);
        Assert.Equal((ushort)MsgType.SystemMessage, 1040);

        origin.Checkpoint();
        edge.Checkpoint();
        past.Checkpoint();
        far.Checkpoint();

        var now = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        Assert.Null(await world.TrySayAsync(origin.Session, new SayMsg { Text = "  hello  ", ChatType = ChatProtocol.All }, now, CancellationToken.None));

        var said = OnlySay(await origin.ReadNewAsync());
        Assert.Equal(origin.Session.ObjectId, said.ObjectId);
        Assert.Equal("Near", said.Name);
        Assert.Equal("hello", said.Text);
        Assert.Equal(ChatProtocol.All, said.ChatType);
        Assert.Equal("hello", OnlySay(await edge.ReadNewAsync()).Text);
        Assert.Empty(await past.ReadNewAsync());
        Assert.Empty(await far.ReadNewAsync());
    }

    [Fact]
    public async Task Shout_reaches_the_whole_zone_including_outside_interest()
    {
        var (login, store, world) = Boot(new RangeAoi(300));
        await using var origin = await EnterAsync(login, store, world, "origin", "Origin", 0, 0);
        await using var close = await EnterAsync(login, store, world, "close", "Close", 100, 0);
        await using var hidden = await EnterAsync(login, store, world, "hidden", "Hidden", 1_000, 0);
        origin.Checkpoint();
        close.Checkpoint();
        hidden.Checkpoint();

        var now = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        Assert.Null(await world.TrySayAsync(origin.Session, new SayMsg { Text = "local", ChatType = ChatProtocol.All }, now, CancellationToken.None));
        Assert.Equal("local", OnlySay(await origin.ReadNewAsync()).Text);
        Assert.Equal("local", OnlySay(await close.ReadNewAsync()).Text);
        Assert.Empty(await hidden.ReadNewAsync());

        origin.Checkpoint();
        close.Checkpoint();
        hidden.Checkpoint();
        Assert.Null(await world.TrySayAsync(origin.Session, new SayMsg { Text = "zone", ChatType = ChatProtocol.Shout }, now.AddSeconds(1), CancellationToken.None));
        var shouted = OnlySay(await origin.ReadNewAsync());
        Assert.Equal(ChatProtocol.Shout, shouted.ChatType);
        Assert.Equal("zone", shouted.Text);
        Assert.Equal("Origin", shouted.Name);
        Assert.Equal("zone", OnlySay(await close.ReadNewAsync()).Text);
        Assert.Equal("zone", OnlySay(await hidden.ReadNewAsync()).Text);
    }

    [Fact]
    public async Task Tell_party_and_clan_return_not_supported_without_a_broadcast()
    {
        var (login, store, world) = Boot();
        await using var speaker = await EnterAsync(login, store, world, "speaker", "Speaker", 0, 0);
        await using var other = await EnterAsync(login, store, world, "other", "Other", 100, 0);

        var now = new DateTimeOffset(2026, 10, 28, 1, 0, 0, TimeSpan.Zero);
        int[] types = [ChatProtocol.Tell, ChatProtocol.Party, ChatProtocol.Clan, ChatProtocol.Trade, ChatProtocol.Hero, 99];
        foreach (int type in types)
        {
            speaker.Checkpoint();
            other.Checkpoint();
            Assert.Null(await world.TrySayAsync(speaker.Session, new SayMsg { Text = "secret", ChatType = type }, now, CancellationToken.None));
            var notice = OnlySystem(await speaker.ReadNewAsync());
            Assert.Equal(ChatProtocol.ChatNotSupported, notice.Id);
            Assert.Equal("not supported yet", notice.Text);
            Assert.Equal(type.ToString(), Assert.Single(notice.Params));
            Assert.Empty(await other.ReadNewAsync());
            now = now.AddSeconds(1);
        }
    }

    [Fact]
    public async Task Flood_throttle_sends_a_system_message_and_then_allows_the_next_line()
    {
        var (login, store, world) = Boot();
        await using var speaker = await EnterAsync(login, store, world, "flood", "Flood", 0, 0);
        var now = new DateTimeOffset(2026, 10, 28, 2, 0, 0, TimeSpan.Zero);

        Assert.Null(await world.TrySayAsync(speaker.Session, new SayMsg { Text = "one", ChatType = ChatProtocol.All }, now, CancellationToken.None));
        Assert.Equal("one", OnlySay(await speaker.ReadNewAsync()).Text);

        Assert.Null(await world.TrySayAsync(speaker.Session, new SayMsg { Text = "two", ChatType = ChatProtocol.Shout }, now.AddMilliseconds(999), CancellationToken.None));
        var flooded = OnlySystem(await speaker.ReadNewAsync());
        Assert.Equal(ChatProtocol.ChatFlood, flooded.Id);
        Assert.Equal("Please do not spam the chat.", flooded.Text);
        Assert.Empty(flooded.Params);

        Assert.Null(await world.TrySayAsync(speaker.Session, new SayMsg { Text = "three", ChatType = ChatProtocol.All }, now.AddMilliseconds(1000), CancellationToken.None));
        Assert.Equal("three", OnlySay(await speaker.ReadNewAsync()).Text);
    }

    [Fact]
    public async Task Rejected_text_does_not_broadcast_or_start_the_flood_window()
    {
        var (login, store, world) = Boot();
        await using var speaker = await EnterAsync(login, store, world, "bad", "Bad", 0, 0);
        var now = new DateTimeOffset(2026, 10, 28, 3, 0, 0, TimeSpan.Zero);

        Assert.Equal("bad_request", await world.TrySayAsync(speaker.Session, new SayMsg { Text = "   ", ChatType = ChatProtocol.All }, now, CancellationToken.None));
        Assert.Equal("bad_request", await world.TrySayAsync(speaker.Session, new SayMsg { Text = new string('z', 101), ChatType = ChatProtocol.All }, now, CancellationToken.None));
        Assert.Equal("bad_request", await world.TrySayAsync(speaker.Session, new SayMsg { Text = "no\npe", ChatType = ChatProtocol.All }, now, CancellationToken.None));
        Assert.Empty(await speaker.ReadNewAsync());

        Assert.Null(await world.TrySayAsync(speaker.Session, new SayMsg { Text = "ok", ChatType = ChatProtocol.All }, now, CancellationToken.None));
        Assert.Equal("ok", OnlySay(await speaker.ReadNewAsync()).Text);
    }

    [Fact]
    public async Task Say_before_enter_returns_world()
    {
        var (_, _, world) = Boot();
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(4, "acct", "Ghost", HumanClasses.Fighter);
        Assert.Equal("world", await world.TrySayAsync(session, new SayMsg { Text = "hi", ChatType = ChatProtocol.All }, DateTimeOffset.UnixEpoch, CancellationToken.None));
    }

    [Fact]
    public async Task Zone_port_welcome_say_and_flood_use_the_wire_opcodes()
    {
        var time = new DateTimeOffset(2026, 10, 28, 4, 0, 0, TimeSpan.Zero);
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(tokens, new ZoneWorld(), () => time);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var ticket = login.Play("wire", "pass1234", "Wira", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource();
        var acceptTask = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
            await using var stream = accepted.GetStream();
            await server.HandleAsync(stream, cts.Token);
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
        await ZoneEnterWire.ReadSkillListAsync(stream);
        await ReadAsync(stream, MsgType.PlayerSpawned);
        for (int i = 0; i < TalkingIsland.NpcCount; i++)
            await ReadAsync(stream, MsgType.NpcInfo);

        var welcomeFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(welcomeFrame);
        Assert.Equal((ushort)MsgType.SystemMessage, welcomeFrame!.Value.Type);
        Assert.Equal(1040, welcomeFrame.Value.Type);
        var welcome = MessagePackSerializer.Deserialize<SystemMessageMsg>(welcomeFrame.Value.Body);
        Assert.Equal(ChatProtocol.Welcome, welcome.Id);
        Assert.Equal("Wira", Assert.Single(welcome.Params));
        Assert.Equal("Welcome, Wira.", welcome.Text);

        await FrameIO.SendAsync(stream, (ushort)MsgType.Say, MessagePackSerializer.Serialize(new SayMsg { Text = "hi", ChatType = ChatProtocol.All }));
        var saidFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(saidFrame);
        Assert.Equal((ushort)MsgType.CreatureSay, saidFrame!.Value.Type);
        Assert.Equal(1012, saidFrame.Value.Type);
        var said = MessagePackSerializer.Deserialize<CreatureSayMsg>(saidFrame.Value.Body);
        Assert.Equal(ticket.Value.CharacterId, said.ObjectId);
        Assert.Equal("Wira", said.Name);
        Assert.Equal("hi", said.Text);
        Assert.Equal(ChatProtocol.All, said.ChatType);

        await FrameIO.SendAsync(stream, (ushort)MsgType.Say, MessagePackSerializer.Serialize(new SayMsg { Text = "again", ChatType = ChatProtocol.All }));
        var floodFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(floodFrame);
        Assert.Equal((ushort)MsgType.SystemMessage, floodFrame!.Value.Type);
        var flood = MessagePackSerializer.Deserialize<SystemMessageMsg>(floodFrame.Value.Body);
        Assert.Equal(ChatProtocol.ChatFlood, flood.Id);
        Assert.Equal("Please do not spam the chat.", flood.Text);

        time = time.AddMilliseconds(ChatProtocol.FloodIntervalMs);
        await FrameIO.SendAsync(stream, (ushort)MsgType.Say, MessagePackSerializer.Serialize(new SayMsg { Text = "later", ChatType = ChatProtocol.Shout }));
        var laterFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(laterFrame);
        Assert.Equal((ushort)MsgType.CreatureSay, laterFrame!.Value.Type);
        var later = MessagePackSerializer.Deserialize<CreatureSayMsg>(laterFrame.Value.Body);
        Assert.Equal("later", later.Text);
        Assert.Equal(ChatProtocol.Shout, later.ChatType);

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

    static (LoginService Login, JsonAccountStore Store, ZoneWorld World) Boot(IAoi? aoi = null)
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        var world = aoi == null ? new ZoneWorld(accounts: store) : new ZoneWorld(aoi, accounts: store);
        return (login, store, world);
    }

    static async Task<Speaker> EnterAsync(LoginService login, JsonAccountStore store, ZoneWorld world, string account, string name, int x, int y)
    {
        var ticket = login.Play(account, "pass1234", name, HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null, ticket.Message);
        Assert.True(store.TrySavePresence(ticket.Value.Account, ticket.Value.CharacterId, x, y, 0, null));
        var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.Value.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        Assert.Equal(x, session.X);
        Assert.Equal(y, session.Y);
        var speaker = new Speaker(session, stream);
        speaker.Welcome = await speaker.WelcomeAsync();
        return speaker;
    }

    static CreatureSayMsg OnlySay(List<(ushort Type, byte[] Body)> frames)
    {
        var says = frames.Where(frame => frame.Type == (ushort)MsgType.CreatureSay).ToList();
        var one = Assert.Single(says);
        return MessagePackSerializer.Deserialize<CreatureSayMsg>(one.Body);
    }

    static SystemMessageMsg OnlySystem(List<(ushort Type, byte[] Body)> frames)
    {
        var notes = frames.Where(frame => frame.Type == (ushort)MsgType.SystemMessage).ToList();
        var one = Assert.Single(notes);
        return MessagePackSerializer.Deserialize<SystemMessageMsg>(one.Body);
    }

    static async Task ReadAsync(NetworkStream stream, MsgType type)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)type, frame!.Value.Type);
    }

    sealed class Speaker : IAsyncDisposable
    {
        readonly MemoryStream _stream;
        long _mark;

        public Speaker(ZoneSession session, MemoryStream stream)
        {
            Session = session;
            _stream = stream;
        }

        public ZoneSession Session { get; }

        public SystemMessageMsg Welcome { get; set; } = new();

        public void Checkpoint() => _mark = _stream.Position;

        public async Task<SystemMessageMsg> WelcomeAsync()
        {
            var frames = await ReadNewAsync();
            Assert.Equal((ushort)MsgType.SystemMessage, frames[^1].Type);
            var welcome = MessagePackSerializer.Deserialize<SystemMessageMsg>(frames[^1].Body);
            Assert.Equal(ChatProtocol.Welcome, welcome.Id);
            return welcome;
        }

        public async Task<List<(ushort Type, byte[] Body)>> ReadNewAsync()
        {
            _stream.Position = _mark;
            var frames = new List<(ushort Type, byte[] Body)>();
            while (true)
            {
                var frame = await FrameIO.ReadAsync(_stream);
                if (frame == null)
                    break;
                frames.Add(frame.Value);
            }

            _mark = _stream.Position;
            return frames;
        }

        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync();
        }
    }
}
