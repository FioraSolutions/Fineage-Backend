using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Siege;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public class ZoneSiegeWireTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Leader_registers_as_attacker_and_lists_the_castle()
    {
        var store = JsonAccountStore.InMemory();
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var login = new LoginService(store, tokens);
        Assert.True(login.Register("leader", "secret1").Ok);
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter(
            "leader", "Leader", HumanClasses.Fighter, 0, 0, out var created, level: 40));
        var clan = store.InsertClan(new ClanRecord(
            0, "Siege", 4, created!.Id, 0, 0, 0,
            [new ClanMemberRecord(created.Id, "leader", "Leader", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("leader", created.Id, 0, 0, 0));
        Assert.NotNull(clan);

        var world = new ZoneWorld(accounts: store, clock: () => Now, siege: SiegeSettings.Mobius);
        await using var host = ZoneHost.Start(tokens, world, () => Now);
        await using var peer = await SiegePeer.ConnectAsync(host.Port);
        await peer.AuthAsync(tokens.Issue(created, DateTimeOffset.UtcNow));
        await peer.EnterInClanAsync();

        await peer.SendAsync(MsgType.SiegeListRequest, new SiegeListRequestMsg { CastleId = 1 });
        var listed = await peer.ExpectAsync<SiegeInfoMsg>(MsgType.SiegeInfo);
        Assert.Equal(1, listed.CastleId);
        Assert.Equal("Gludio", listed.Name);
        Assert.Equal(0, listed.OwnerClanId);
        Assert.Equal(0, listed.TaxPercent);
        Assert.False(listed.RegistrationClosed);
        Assert.Empty(listed.Clans);

        await peer.SendAsync(MsgType.SiegeRegisterRequest, new SiegeRegisterRequestMsg
        {
            CastleId = 1,
            Attacker = 1,
            Joining = 1
        });
        var line = await peer.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(SiegeProtocol.RegisteredAttacker, line.Id);
        Assert.Equal("Gludio", Assert.Single(line.Params));
        var info = await peer.ExpectAsync<SiegeInfoMsg>(MsgType.SiegeInfo);
        var row = Assert.Single(info.Clans);
        Assert.Equal(clan!.Id, row.ClanId);
        Assert.Equal(SiegeProtocol.Attacker, row.Side);

        await peer.SendAsync(MsgType.SiegeRegisterRequest, new SiegeRegisterRequestMsg
        {
            CastleId = 1,
            Attacker = 1,
            Joining = 1
        });
        Assert.Equal(SiegeProtocol.Duplicate, (await peer.ExpectAsync<ErrorMsg>(MsgType.Error)).Code);

        long start = world.Sieges.Find(1)!.SiegeUnixMs;
        Assert.Equal(SiegeStartKind.Started, world.StartSiege(1, DateTimeOffset.FromUnixTimeMilliseconds(start)));
        Assert.Null(world.CaptureCastle(clan.Id, DateTimeOffset.FromUnixTimeMilliseconds(start + 60_000)));
        Assert.Equal(clan.Id, store.FindClan(clan.Id)!.CastleId);
        Assert.Equal(1, world.Sieges.CastleOwnedBy(clan.Id));
    }

    sealed class SiegePeer : IAsyncDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;

        SiegePeer(TcpClient client, NetworkStream stream)
        {
            _client = client;
            _stream = stream;
        }

        public static async Task<SiegePeer> ConnectAsync(int port)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            return new SiegePeer(client, client.GetStream());
        }

        public async Task AuthAsync(HandoffTicket ticket)
        {
            await SendAsync(MsgType.Hello, new HelloMsg());
            await ExpectAsync<HelloAckMsg>(MsgType.HelloAck);
            await SendAsync(MsgType.Auth, ticket.ToZoneAuth());
            await ExpectAsync<AuthOkMsg>(MsgType.AuthOk);
        }

        public async Task EnterInClanAsync()
        {
            await SendAsync(MsgType.EnterWorld, new EnterWorldMsg());
            await ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
            await ExpectAsync<ItemListMsg>(MsgType.ItemList);
            await ExpectAsync<PlayerSpawnedMsg>(MsgType.PlayerSpawned);
            for (int i = 0; i < TalkingIsland.NpcCount; i++)
                await ExpectAsync<NpcInfoMsg>(MsgType.NpcInfo);
            await ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
            await ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
            await ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        }

        public async Task SendAsync<T>(MsgType type, T message)
        {
            byte[] body = MessagePackSerializer.Serialize(message);
            await FrameIO.SendAsync(_stream, (ushort)type, body);
        }

        public async Task<T> ExpectAsync<T>(MsgType expected)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var frame = await FrameIO.ReadAsync(_stream, cts.Token);
            Assert.NotNull(frame);
            if (frame!.Value.Type != (ushort)expected)
            {
                string detail = frame.Value.Type == (ushort)MsgType.Error
                    ? " " + MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body).Code
                    : "";
                Assert.Fail($"expected {expected}, got {(MsgType)frame.Value.Type}{detail}");
            }

            return MessagePackSerializer.Deserialize<T>(frame.Value.Body);
        }

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
