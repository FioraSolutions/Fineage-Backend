using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using MessagePack;
using Xunit;
using ProtocolMsgType = Fineage.Protocol.MsgType;
using V2MsgType = Fineage.Protocol.V2.MsgType;

namespace Fineage.Login.Tests;

public sealed class LoginV2WireBoundaryTests
{
    [Fact]
    public async Task RequestServerList_on_login_port_returns_server_list()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);

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

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.RequestServerList, []);

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)V2MsgType.ServerList, frame!.Value.Type);
        var list = MessagePackSerializer.Deserialize<Fineage.Protocol.V2.ServerListMsg>(frame.Value.Body);
        Assert.NotNull(list);
        Assert.NotEmpty(list!.Servers);

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
    public async Task RequestServerList_on_login_pipeline_returns_server_list()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);
        await using var host = await PipelineLoginHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.RequestServerList, []);

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)V2MsgType.ServerList, frame!.Value.Type);
        var list = MessagePackSerializer.Deserialize<Fineage.Protocol.V2.ServerListMsg>(frame.Value.Body);
        Assert.NotNull(list);
        Assert.NotEmpty(list!.Servers);
        Assert.Equal(1, list.Servers[0].ServerId);
    }

    [Fact]
    public async Task SelectServer_on_login_pipeline_unknown_server_returns_server_not_found()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);
        await using var host = await PipelineLoginHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.SelectServer,
            MessagePackSerializer.Serialize(new Fineage.Protocol.V2.SelectServerMsg { ServerId = 99 }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("server_not_found", err.Code);
    }

    [Fact]
    public async Task SelectServer_on_login_port_unknown_server_returns_server_not_found()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);

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

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.SelectServer,
            MessagePackSerializer.Serialize(new Fineage.Protocol.V2.SelectServerMsg { ServerId = 99 }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("server_not_found", err.Code);

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
    public async Task SelectServer_on_login_port_without_auth_returns_auth_required()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);

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

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.SelectServer,
            MessagePackSerializer.Serialize(new Fineage.Protocol.V2.SelectServerMsg { ServerId = 1 }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("auth_required", err.Code);

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
    public async Task SelectServer_on_login_pipeline_without_auth_returns_auth_required()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);
        await using var host = await PipelineLoginHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.SelectServer,
            MessagePackSerializer.Serialize(new Fineage.Protocol.V2.SelectServerMsg { ServerId = 1 }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("auth_required", err.Code);
    }

    [Fact]
    public async Task SelectServer_on_login_port_after_auth_returns_character_required()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        service.Register("hero", "pass1234");
        var server = new LoginServer(service);

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

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Auth,
            MessagePackSerializer.Serialize(new AuthMsg { Account = "hero", Password = "pass1234" }));
        await ReadAsync(stream, ProtocolMsgType.LoginOk);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.SelectServer,
            MessagePackSerializer.Serialize(new Fineage.Protocol.V2.SelectServerMsg { ServerId = 1 }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("character_required", err.Code);

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
    public async Task SelectServer_on_login_pipeline_after_auth_returns_character_required()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        service.Register("hero", "pass1234");
        var server = new LoginServer(service);
        await using var host = await PipelineLoginHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Auth,
            MessagePackSerializer.Serialize(new AuthMsg { Account = "hero", Password = "pass1234" }));
        await ReadAsync(stream, ProtocolMsgType.LoginOk);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.SelectServer,
            MessagePackSerializer.Serialize(new Fineage.Protocol.V2.SelectServerMsg { ServerId = 1 }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("character_required", err.Code);
    }

    [Fact]
    public async Task EnterWorld_on_login_port_after_auth_empty_roster_returns_bad_slot()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        service.Register("hero", "pass1234");
        var server = new LoginServer(service);

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

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Auth,
            MessagePackSerializer.Serialize(new AuthMsg { Account = "hero", Password = "pass1234" }));
        await ReadAsync(stream, ProtocolMsgType.LoginOk);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.EnterWorld,
            MessagePackSerializer.Serialize(new EnterWorldMsg { CharacterSlot = 0 }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("bad_slot", err.Code);

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
    public async Task EnterWorld_on_login_pipeline_after_auth_empty_roster_returns_bad_slot()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        service.Register("hero", "pass1234");
        var server = new LoginServer(service);
        await using var host = await PipelineLoginHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Auth,
            MessagePackSerializer.Serialize(new AuthMsg { Account = "hero", Password = "pass1234" }));
        await ReadAsync(stream, ProtocolMsgType.LoginOk);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.EnterWorld,
            MessagePackSerializer.Serialize(new EnterWorldMsg { CharacterSlot = 0 }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("bad_slot", err.Code);
    }

    [Fact]
    public async Task EnterWorld_on_login_port_without_auth_returns_auth_required()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);

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

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.EnterWorld,
            MessagePackSerializer.Serialize(new EnterWorldMsg { CharacterSlot = 0 }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("auth_required", err.Code);

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
    public async Task EnterWorld_on_login_pipeline_without_auth_returns_auth_required()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);
        await using var host = await PipelineLoginHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.EnterWorld,
            MessagePackSerializer.Serialize(new EnterWorldMsg { CharacterSlot = 0 }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("auth_required", err.Code);
    }

    [Fact]
    public async Task CreateCharacter_on_login_port_without_auth_returns_auth_required()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);

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

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.CreateCharacter,
            MessagePackSerializer.Serialize(new Fineage.Protocol.V2.CreateCharacterMsg
            {
                Name = "Aria",
                Race = HumanClasses.RaceHuman,
                Sex = HumanClasses.SexMale,
                ClassId = HumanClasses.Fighter
            }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("auth_required", err.Code);

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
    public async Task CreateCharacter_on_login_pipeline_without_auth_returns_auth_required()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);
        await using var host = await PipelineLoginHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.CreateCharacter,
            MessagePackSerializer.Serialize(new Fineage.Protocol.V2.CreateCharacterMsg
            {
                Name = "Aria",
                Race = HumanClasses.RaceHuman,
                Sex = HumanClasses.SexMale,
                ClassId = HumanClasses.Fighter
            }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("auth_required", err.Code);
    }

    [Fact]
    public async Task ListCharacters_on_login_port_without_auth_returns_auth_required()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);

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

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.ListCharacters, []);

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("auth_required", err.Code);

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
    public async Task ListCharacters_on_login_pipeline_without_auth_returns_auth_required()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);
        await using var host = await PipelineLoginHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.ListCharacters, []);

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("auth_required", err.Code);
    }

    sealed class PipelineLoginHost : IAsyncDisposable
    {
        readonly CancellationTokenSource _cts;
        readonly Task _run;

        PipelineLoginHost(int port, CancellationTokenSource cts, Task run)
        {
            Port = port;
            _cts = cts;
            _run = run;
        }

        public int Port { get; }

        public static async Task<PipelineLoginHost> StartAsync(LoginServer server)
        {
            var cts = new CancellationTokenSource();
            var tcp = new TcpServer();
            var run = Task.Run(() => tcp.RunAsync("127.0.0.1", 0, 4, 8, "login-test", server.HandleAsync, cts.Token));

            for (int i = 0; i < 100; i++)
            {
                if (tcp.BoundPort != 0)
                    return new PipelineLoginHost(tcp.BoundPort, cts, run);

                await Task.Delay(20);
            }

            await cts.CancelAsync();
            throw new InvalidOperationException("TcpServer did not bind.");
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try
            {
                await _run.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException)
            {
            }

            _cts.Dispose();
        }
    }

    static async Task ReadAsync(NetworkStream stream, ProtocolMsgType expected)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)expected, frame!.Value.Type);
    }
}
