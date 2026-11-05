using System.Net;
using System.Net.Sockets;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Login.Net;

namespace Fineage.Login;

public sealed record AcceptanceResult(HandoffMsg Fighter, HandoffTicket Mystic);

/// <summary>Register → Human create → zone handoff, over the Net (PacketEnvelope) frame and in-process.</summary>
public static class LoginAcceptance
{
    public static async Task<AcceptanceResult> RunAsync()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var fighter = await RunFighterOverNetAsync(tokens).ConfigureAwait(false);
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
        if (zoneAuth.Account != mystic.Account || zoneAuth.Password != mystic.Token)
            throw new InvalidOperationException("zone auth did not carry the handoff token");

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

    static async Task<HandoffMsg> RunFighterOverNetAsync(HandoffTokens tokens)
    {
        var service = new LoginService(JsonAccountStore.InMemory(), tokens);
        // Net has no Register opcode: the lobby authenticates existing accounts only, so the
        // selftest account is seeded directly in the store before the socket opens.
        if (!service.Register("selftest", "pass1234").Ok)
            throw new InvalidOperationException("selftest account could not be registered");
        var server = new NetLoginServer(service);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var serverTask = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false);
            await using var conn = new NetConnection(accepted, 256);
            Task read = conn.RunReadLoopAsync(cts.Token);
            try
            {
                await server.HandleAsync(conn, cts.Token).ConfigureAwait(false);
            }
            finally
            {
                try { await read.ConfigureAwait(false); } catch { /* shutdown */ }
            }
        });

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, cts.Token).ConfigureAwait(false);
            await using var conn = new NetConnection(client, 256);
            Task read = conn.RunReadLoopAsync(cts.Token);
            try
            {
                var link = new NetLink(conn);
                await link.SendAsync(NetOpcode.Hello, NetCodec.EncodeHello(new HelloMsg
                {
                    Major = 1,
                    Minor = 0,
                    ClientName = "selftest",
                    FeatureFlags = ProtocolCapabilities.FeatureSession | ProtocolCapabilities.FeatureLobby
                })).ConfigureAwait(false);
                await link.ExpectAsync(NetOpcode.HelloAck).ConfigureAwait(false);

                await link.SendAsync(NetOpcode.Auth, NetCodec.EncodeAuth(new AuthMsg
                {
                    Account = "selftest",
                    Password = "pass1234"
                })).ConfigureAwait(false);
                if (!NetCodec.DecodeAuthResult(await link.ExpectAsync(NetOpcode.AuthResult).ConfigureAwait(false)).Ok)
                    throw new InvalidOperationException("selftest auth was rejected");

                await link.SendAsync(NetOpcode.CreateCharacter, NetCodec.EncodeCreateCharacter(new CreateCharacterMsg
                {
                    Name = "HumanOne",
                    ClassId = HumanClasses.Fighter,
                    Race = HumanClasses.RaceHuman,
                    Sex = HumanClasses.SexMale
                })).ConfigureAwait(false);
                var created = NetCodec.DecodeCreateResult(await link.ExpectAsync(NetOpcode.CreateResult).ConfigureAwait(false));
                if (!created.Ok || created.Character == null)
                    throw new InvalidOperationException($"create failed: {created.Code} {created.Message}");
                if (created.Character.ClassId != HumanClasses.Fighter || created.Character.Name != "HumanOne")
                    throw new InvalidOperationException("created character was not the Human fighter");
                await link.ExpectAsync(NetOpcode.CharacterRoster).ConfigureAwait(false);

                await link.SendAsync(NetOpcode.SelectCharacter, NetCodec.EncodeSelectCharacter(new SelectCharacterMsg
                {
                    CharacterId = created.Character.CharacterId
                })).ConfigureAwait(false);
                return NetCodec.DecodeHandoff(await link.ExpectAsync(NetOpcode.Handoff).ConfigureAwait(false));
            }
            finally
            {
                try { await read.ConfigureAwait(false); } catch { /* shutdown */ }
            }
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            try { await serverTask.ConfigureAwait(false); } catch { /* shutdown */ }
        }
    }

    /// <summary>Minimal request/response client over <see cref="NetConnection"/> for the selftest.</summary>
    sealed class NetLink(NetConnection conn)
    {
        public Task SendAsync(ushort opcode, byte[] payload) =>
            conn.SendAsync(opcode, payload, ct: CancellationToken.None);

        public async Task<byte[]> ExpectAsync(ushort expected)
        {
            PacketEnvelope envelope = await conn.Inbox.ReadAsync().ConfigureAwait(false);
            if (envelope.Opcode != expected)
            {
                string detail = envelope.Opcode == NetOpcode.Reject
                    ? " " + NetCodec.DecodeReject(envelope.Payload).Code
                    : "";
                throw new InvalidOperationException($"expected opcode 0x{expected:X4}, got 0x{envelope.Opcode:X4}{detail}");
            }

            return envelope.Payload;
        }
    }
}
