using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.V2;
using MessagePack;
using Xunit;
using ProtocolMsgType = Fineage.Protocol.MsgType;

namespace Fineage.Login.Tests;

public sealed class LoginV2RosterTests
{
    [Fact]
    public async Task V2_list_enter_world_returns_play_token_and_handoff()
    {
        var tokens = HandoffTokens.FromEnvironment();
        var service = new LoginService(JsonAccountStore.InMemory(), tokens);
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

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello,
            MessagePackSerializer.Serialize(new Fineage.Protocol.HelloMsg()));
        await ExpectAsync(stream, ProtocolMsgType.HelloAck);

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Auth,
            MessagePackSerializer.Serialize(new Fineage.Protocol.AuthMsg { Account = "hero", Password = "pass1234" }));
        await ExpectAsync(stream, ProtocolMsgType.LoginOk);

        await FrameIO.SendAsync(stream, (ushort)Fineage.Protocol.V2.MsgType.CreateCharacter,
            MessagePackSerializer.Serialize(new Fineage.Protocol.V2.CreateCharacterMsg
            {
                Name = "Aria",
                Race = HumanClasses.RaceHuman,
                Sex = HumanClasses.SexMale,
                ClassId = HumanClasses.Fighter
            }));
        var createFrame = await ReadAsync(stream);
        Assert.Equal((ushort)Fineage.Protocol.V2.MsgType.CreateCharacterResult, createFrame.Type);
        var create = MessagePackSerializer.Deserialize<CreateCharacterResultMsg>(createFrame.Body);
        Assert.True(create.Ok);
        var pushedRoster = await ReadAsync(stream);
        Assert.Equal((ushort)Fineage.Protocol.V2.MsgType.CharacterRoster, pushedRoster.Type);

        await FrameIO.SendAsync(stream, (ushort)Fineage.Protocol.V2.MsgType.ListCharacters, []);
        var listFrame = await ReadAsync(stream);
        Assert.Equal((ushort)Fineage.Protocol.V2.MsgType.CharacterRoster, listFrame.Type);
        var roster = MessagePackSerializer.Deserialize<CharacterRosterMsg>(listFrame.Body);
        Assert.Single(roster.Characters);
        Assert.Equal("Aria", roster.Characters[0].Name);

        await FrameIO.SendAsync(stream, (ushort)Fineage.Protocol.V2.MsgType.EnterWorld,
            MessagePackSerializer.Serialize(new Fineage.Protocol.EnterWorldMsg { CharacterSlot = 0 }));
        var playFrame = await ReadAsync(stream);
        Assert.Equal((ushort)Fineage.Protocol.V2.MsgType.PlayToken, playFrame.Type);
        var handoffFrame = await ReadAsync(stream);
        Assert.Equal((ushort)ProtocolMsgType.Handoff, handoffFrame.Type);
        var handoff = MessagePackSerializer.Deserialize<HandoffMsg>(handoffFrame.Body);
        Assert.False(string.IsNullOrEmpty(handoff.Token));

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

    static async Task ExpectAsync(NetworkStream stream, ProtocolMsgType expected)
    {
        var frame = await ReadAsync(stream);
        Assert.Equal((ushort)expected, frame.Type);
    }

    static async Task<(ushort Type, byte[] Body)> ReadAsync(NetworkStream stream)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        return (frame!.Value.Type, frame.Value.Body);
    }
}
