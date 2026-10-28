using System.Net;
using System.Net.Sockets;
using Fineage.Protocol;
using MessagePack;

namespace Fineage.Login;

public sealed record AcceptanceResult(HandoffMsg Fighter, HandoffTicket Mystic);

/// <summary>Register → Human create → zone handoff, over the V2 frame and in-process.</summary>
public static class LoginAcceptance
{
    public static async Task<AcceptanceResult> RunAsync()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var fighter = await RunFighterOverTcpAsync(tokens).ConfigureAwait(false);
        var mystic = RunMystic(tokens);
        if (!tokens.TryValidate(fighter.Token, DateTimeOffset.UtcNow, out var fighterClaims))
            throw new InvalidOperationException("fighter handoff token did not validate");
        if (fighterClaims.CharacterName != "HumanOne" || fighterClaims.ClassId != HumanClasses.Fighter)
            throw new InvalidOperationException("fighter claims mismatch");
        if (fighter.ZoneHost != ZoneHandoff.Host || fighter.ZonePort != ZoneHandoff.Port)
            throw new InvalidOperationException("fighter zone endpoint mismatch");
        if (!tokens.TryValidate(mystic.Token, DateTimeOffset.UtcNow, out var mysticClaims) ||
            mysticClaims.ClassId != HumanClasses.Mystic)
            throw new InvalidOperationException("mystic handoff token did not validate");

        var zoneAuth = mystic.ToZoneAuth();
        var roundTrip = MessagePackSerializer.Deserialize<AuthMsg>(MessagePackSerializer.Serialize(zoneAuth));
        if (roundTrip.Account != mystic.Account || roundTrip.Password != mystic.Token)
            throw new InvalidOperationException("zone AuthMsg did not round-trip the handoff token");

        return new AcceptanceResult(fighter, mystic);
    }

    public static void Write(TextWriter output, AcceptanceResult result)
    {
        WriteHandoff(output, result.Fighter.Account, result.Fighter.CharacterName, result.Fighter.ClassId,
            result.Fighter.Token, result.Fighter.ZoneHost, result.Fighter.ZonePort, result.Fighter.ExpiresUnixSeconds);
        WriteHandoff(output, result.Mystic.Account, result.Mystic.CharacterName, result.Mystic.ClassId,
            result.Mystic.Token, result.Mystic.ZoneHost, result.Mystic.ZonePort, result.Mystic.ExpiresUnixSeconds);
        output.WriteLine("selftest OK");
    }

    public static void WriteHandoff(
        TextWriter output,
        string account,
        string character,
        int classId,
        string token,
        string zoneHost,
        int zonePort,
        long expires)
    {
        output.WriteLine(
            $"HANDOFF account={account} character={character} classId={classId} token={token} zone={zoneHost}:{zonePort} expires={expires}");
        output.WriteLine($"ZONE_AUTH account={account} password={token}");
    }

    static HandoffTicket RunMystic(HandoffTokens tokens)
    {
        var service = new LoginService(JsonAccountStore.InMemory(), tokens);
        var result = service.Play("selftest2", "pass1234", "HumanTwo", HumanClasses.Mystic);
        if (!result.Ok || result.Value == null)
            throw new InvalidOperationException($"mystic play failed: {result.Code} {result.Message}");
        return result.Value;
    }

    static async Task<HandoffMsg> RunFighterOverTcpAsync(HandoffTokens tokens)
    {
        var service = new LoginService(JsonAccountStore.InMemory(), tokens);
        var server = new LoginServer(service);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            await using var acceptedStream = accepted.GetStream();
            await server.HandleAsync(acceptedStream, CancellationToken.None).ConfigureAwait(false);
        });

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
            await using var stream = client.GetStream();

            await SendAsync(stream, MsgType.Hello, new HelloMsg { ProtocolVersion = 1, ClientBuild = "selftest" })
                .ConfigureAwait(false);
            await ExpectAsync(stream, MsgType.HelloAck).ConfigureAwait(false);

            await SendAsync(stream, MsgType.Register, new RegisterMsg { Account = "selftest", Password = "pass1234" })
                .ConfigureAwait(false);
            await ExpectAsync(stream, MsgType.RegisterOk).ConfigureAwait(false);

            await SendAsync(stream, MsgType.CreateCharacter, new CreateCharacterMsg
            {
                Name = "HumanOne",
                ClassId = HumanClasses.Fighter,
                Race = HumanClasses.RaceHuman,
                Sex = HumanClasses.SexMale
            }).ConfigureAwait(false);
            var createdBody = await ExpectAsync(stream, MsgType.CharacterCreated).ConfigureAwait(false);
            var created = MessagePackSerializer.Deserialize<CharacterCreatedMsg>(createdBody);
            if (created.ClassId != HumanClasses.Fighter || created.Name != "HumanOne")
                throw new InvalidOperationException("created character was not the Human fighter");

            await SendAsync(stream, MsgType.SelectCharacter, new SelectCharacterMsg { CharacterId = created.CharacterId })
                .ConfigureAwait(false);
            var handoffBody = await ExpectAsync(stream, MsgType.Handoff).ConfigureAwait(false);
            return MessagePackSerializer.Deserialize<HandoffMsg>(handoffBody);
        }
        finally
        {
            listener.Stop();
            await serverTask.ConfigureAwait(false);
        }
    }

    static async Task SendAsync<T>(NetworkStream stream, MsgType type, T msg)
    {
        byte[] body = MessagePackSerializer.Serialize(msg);
        await FrameIO.SendAsync(stream, (ushort)type, body).ConfigureAwait(false);
    }

    static async Task<byte[]> ExpectAsync(NetworkStream stream, MsgType expected)
    {
        var frame = await FrameIO.ReadAsync(stream).ConfigureAwait(false);
        if (frame == null)
            throw new InvalidOperationException($"expected {expected}, got EOF");
        if (frame.Value.Type != (ushort)expected)
        {
            string detail = "";
            if (frame.Value.Type == (ushort)MsgType.Error)
            {
                var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
                detail = $" {err.Code} {err.Message}";
            }

            throw new InvalidOperationException($"expected {expected}, got {(MsgType)frame.Value.Type}{detail}");
        }

        return frame.Value.Body;
    }
}
