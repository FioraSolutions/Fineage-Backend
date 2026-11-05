using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Fineage.Game;
using Fineage.Login;
using Fineage.Login.Net;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Xunit;
using LobbyProtocol = Fineage.Protocol.LobbyProtocol;

namespace Fineage.Login.Tests;

/// <summary>
/// Character delete/restore on the Net lobby wire. The Net path shipped without these opcodes —
/// the client's `DeletePressed` was an explicit no-op — so the tests drive the real server over a
/// loopback socket and assert both the ack and the roster refresh the client redraws from.
/// </summary>
public class NetLobbyDeleteTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public async Task Delete_by_id_acks_and_sends_a_fresh_roster()
    {
        await using var harness = await LobbyHarness.StartAsync(startWithTwoCharacters: true);

        await harness.HelloAsync();
        await harness.AuthAsync("deleteme");
        CharacterRosterMsg roster = await harness.ListAsync();
        int victimId = roster.Characters[0].CharacterId;
        int survivorId = roster.Characters[1].CharacterId;

        await harness.SendAsync(
            NetOpcode.DeleteCharacter,
            NetCodec.EncodeDeleteCharacter(new DeleteCharacterMsg { CharacterId = victimId }));

        DeleteResultMsg result = await harness.ReadDeleteResultAsync();
        Assert.True(result.Ok, result.Message);
        Assert.Equal(victimId, result.CharacterId);

        // The client redraws from the roster, so the server must follow the ack with one.
        CharacterRosterMsg after = await harness.ReadRosterAsync();
        CharacterSummaryNet only = Assert.Single(after.Characters);
        Assert.Equal(survivorId, only.CharacterId);
    }

    [Fact]
    public async Task Delete_by_slot_resolves_the_same_character()
    {
        await using var harness = await LobbyHarness.StartAsync(startWithTwoCharacters: true);
        await harness.HelloAsync();
        await harness.AuthAsync("deleteslot");

        // Slot is the roster row index, which is how the char-select list addresses characters.
        await harness.SendAsync(
            NetOpcode.DeleteCharacter,
            NetCodec.EncodeDeleteCharacter(new DeleteCharacterMsg { Slot = 0 }));

        DeleteResultMsg result = await harness.ReadDeleteResultAsync();
        Assert.True(result.Ok, result.Message);
        Assert.Equal(0, result.Slot);

        CharacterRosterMsg after = await harness.ReadRosterAsync();
        Assert.Single(after.Characters);
    }

    [Fact]
    public async Task Deleting_an_unknown_character_fails_without_a_roster()
    {
        await using var harness = await LobbyHarness.StartAsync(startWithTwoCharacters: true);
        await harness.HelloAsync();
        await harness.AuthAsync("deletebad");

        await harness.SendAsync(
            NetOpcode.DeleteCharacter,
            NetCodec.EncodeDeleteCharacter(new DeleteCharacterMsg { CharacterId = 999_999 }));

        DeleteResultMsg result = await harness.ReadDeleteResultAsync();
        Assert.False(result.Ok);
        Assert.Equal(LobbyProtocol.CharacterNotFound, result.Code);
        Assert.Equal(999_999, result.CharacterId);
    }

    [Fact]
    public async Task Delete_without_auth_is_rejected()
    {
        await using var harness = await LobbyHarness.StartAsync(startWithTwoCharacters: true);
        await harness.HelloAsync();

        await harness.SendAsync(
            NetOpcode.DeleteCharacter,
            NetCodec.EncodeDeleteCharacter(new DeleteCharacterMsg { CharacterId = 1 }));

        RejectMsg reject = await harness.ReadRejectAsync();
        Assert.Equal("auth_required", reject.Code);
    }

    [Fact]
    public async Task Restore_clears_a_pending_delete_timer()
    {
        // With a grace period the delete is soft: the row stays, flagged PendingDelete.
        await using var harness = await LobbyHarness.StartAsync(
            startWithTwoCharacters: true,
            deleteAfterDays: 7);
        await harness.HelloAsync();
        await harness.AuthAsync("restoreme");
        CharacterRosterMsg roster = await harness.ListAsync();
        int targetId = roster.Characters[0].CharacterId;

        await harness.SendAsync(
            NetOpcode.DeleteCharacter,
            NetCodec.EncodeDeleteCharacter(new DeleteCharacterMsg { CharacterId = targetId }));
        DeleteResultMsg deleted = await harness.ReadDeleteResultAsync();
        Assert.True(deleted.Ok, deleted.Message);
        CharacterRosterMsg afterDelete = await harness.ReadRosterAsync();

        CharacterSummaryNet flagged = Assert.Single(
            afterDelete.Characters,
            c => c.CharacterId == targetId);
        Assert.True(flagged.PendingDelete, "soft delete must keep the row flagged");
        Assert.True(flagged.DeleteSecondsLeft > 0);

        await harness.SendAsync(
            NetOpcode.RestoreCharacter,
            NetCodec.EncodeRestoreCharacter(new RestoreCharacterMsg { CharacterId = targetId }));
        DeleteResultMsg restored = await harness.ReadDeleteResultAsync();
        Assert.True(restored.Ok, restored.Message);

        CharacterRosterMsg afterRestore = await harness.ReadRosterAsync();
        Assert.Equal(2, afterRestore.Characters.Length);
        CharacterSummaryNet cleared = Assert.Single(
            afterRestore.Characters,
            c => c.CharacterId == targetId);
        Assert.False(cleared.PendingDelete);
        Assert.Equal(0, cleared.DeleteSecondsLeft);
    }

    [Fact]
    public async Task Delete_result_roundtrips_the_codec()
    {
        var msg = new DeleteResultMsg
        {
            Ok = true,
            Code = "",
            Message = "",
            Slot = 2,
            CharacterId = 42
        };

        DeleteResultMsg decoded = NetCodec.DecodeDeleteResult(NetCodec.EncodeDeleteResult(msg));
        Assert.True(decoded.Ok);
        Assert.Equal(2, decoded.Slot);
        Assert.Equal(42, decoded.CharacterId);

        var failure = new DeleteResultMsg
        {
            Ok = false,
            Code = LobbyProtocol.DeleteFailed,
            Message = "Character could not be deleted.",
            Slot = -1,
            CharacterId = 7
        };

        DeleteResultMsg decodedFailure = NetCodec.DecodeDeleteResult(NetCodec.EncodeDeleteResult(failure));
        Assert.False(decodedFailure.Ok);
        Assert.Equal(LobbyProtocol.DeleteFailed, decodedFailure.Code);
        Assert.Equal("Character could not be deleted.", decodedFailure.Message);
        Assert.Equal(-1, decodedFailure.Slot);
    }

    sealed class LobbyHarness : IAsyncDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;
        readonly CancellationTokenSource _cts;
        readonly Task _serverTask;

        LobbyHarness(TcpClient client, NetworkStream stream, CancellationTokenSource cts, Task serverTask)
        {
            _client = client;
            _stream = stream;
            _cts = cts;
            _serverTask = serverTask;
        }

        public static async Task<LobbyHarness> StartAsync(
            bool startWithTwoCharacters,
            int deleteAfterDays = 0)
        {
            var store = JsonAccountStore.InMemory();
            var service = new LoginService(
                store,
                new HandoffTokens(HandoffTokens.DefaultSecret),
                deleteAfterDays: deleteAfterDays);
            var server = new NetLoginServer(service, clock: () => Now);

            if (startWithTwoCharacters)
            {
                // Account name matches the auth call in each test.
                foreach (string account in new[]
                         {
                             "deleteme", "deleteslot", "deletebad", "restoreme", "keepsboth"
                         })
                {
                    Assert.True(service.Register(account, "pass1234").Ok);
                    Assert.True(service.CreateHuman(account, $"{account}a", HumanClasses.Fighter).Ok);
                    Assert.True(service.CreateHuman(account, $"{account}b", HumanClasses.Mystic).Ok);
                }
            }

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var cts = new CancellationTokenSource();

            var serverTask = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
                await using var conn = new NetConnection(accepted, 256);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                Task read = conn.RunReadLoopAsync(linked.Token);
                try
                {
                    await server.HandleAsync(conn, linked.Token);
                }
                finally
                {
                    linked.Cancel();
                    try { await read; } catch { /* shutdown */ }
                }
            });

            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();
            return new LobbyHarness(client, stream, cts, serverTask);
        }

        public async Task HelloAsync()
        {
            await SendAsync(NetOpcode.Hello, NetCodec.EncodeHello(new HelloMsg
            {
                Major = 1,
                Minor = 0,
                ClientName = "tests",
                FeatureFlags = ProtocolCapabilities.FeatureSession | ProtocolCapabilities.FeatureLobby
            }));

            PacketEnvelope ack = await ReadAsync();
            Assert.Equal(NetOpcode.HelloAck, ack.Opcode);
        }

        public async Task AuthAsync(string account)
        {
            await SendAsync(NetOpcode.Auth, NetCodec.EncodeAuth(new AuthMsg
            {
                Account = account,
                Password = "pass1234"
            }));

            PacketEnvelope reply = await ReadAsync();
            Assert.Equal(NetOpcode.AuthResult, reply.Opcode);
            Assert.True(NetCodec.DecodeAuthResult(reply.Payload).Ok);
        }

        public async Task<CharacterRosterMsg> ListAsync()
        {
            await SendAsync(NetOpcode.ListCharacters, NetCodec.EncodeListCharacters(new ListCharactersMsg()));
            return await ReadRosterAsync();
        }

        public async Task<CharacterRosterMsg> ReadRosterAsync()
        {
            PacketEnvelope envelope = await ReadAsync();
            Assert.Equal(NetOpcode.CharacterRoster, envelope.Opcode);
            return NetCodec.DecodeCharacterRoster(envelope.Payload);
        }

        public async Task<DeleteResultMsg> ReadDeleteResultAsync()
        {
            PacketEnvelope envelope = await ReadAsync();
            Assert.Equal(NetOpcode.DeleteResult, envelope.Opcode);
            return NetCodec.DecodeDeleteResult(envelope.Payload);
        }

        public async Task<RejectMsg> ReadRejectAsync()
        {
            PacketEnvelope envelope = await ReadAsync();
            Assert.Equal(NetOpcode.Reject, envelope.Opcode);
            return NetCodec.DecodeReject(envelope.Payload);
        }

        // The server dedupes by RequestSequence, so every request needs a fresh one.
        uint _sequence;

        public Task SendAsync(ushort opcode, byte[] payload) =>
            SendAsync(opcode, payload, unchecked(++_sequence));

        async Task SendAsync(ushort opcode, byte[] payload, uint sequence)
        {
            byte[] frame = NetFraming.Encode(new PacketEnvelope
            {
                Major = 1,
                Minor = 0,
                Opcode = opcode,
                Flags = (ushort)PacketFlags.Reliable,
                RequestSequence = sequence,
                Payload = payload
            });
            await _stream.WriteAsync(frame, _cts.Token);
            await _stream.FlushAsync(_cts.Token);
        }

        async Task<PacketEnvelope> ReadAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            var header = new byte[4];
            await _stream.ReadExactlyAsync(header, timeout.Token);
            int total = (int)BinaryPrimitives.ReadUInt32LittleEndian(header);
            var body = new byte[total];
            await _stream.ReadExactlyAsync(body, timeout.Token);

            Assert.True(
                NetFraming.TryDecode(body, out PacketEnvelope envelope, out string? error),
                $"frame did not decode: {error}");
            return envelope;
        }

        public async ValueTask DisposeAsync()
        {
            try { _cts.Cancel(); } catch { /* ignore */ }
            try { await _serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* ignore */ }
            await _stream.DisposeAsync();
            _client.Dispose();
            _cts.Dispose();
        }
    }
}
