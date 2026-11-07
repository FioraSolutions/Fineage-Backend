using Fineage.Game;
using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Siege;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Siege registration over Net: the castle page a clan reads, attacker registration, the duplicate refusal, and
/// the capture that moves ownership. The world runs on <see cref="WorldBench"/>, so these assert the recorded
/// <see cref="Fineage.Zone.Net.INetPresenceBridge"/> fanout instead of frames.
/// </summary>
public sealed class NetSiegeTests
{
    [Fact]
    public async Task Leader_registers_as_attacker_and_lists_the_castle()
    {
        var bench = Open();
        BenchPlayer leader = await EnterClanLeaderAsync(bench);

        Assert.Null(await bench.World.TrySiegeListAsync(leader.Session, 1, CancellationToken.None));
        var listed = LastInfo(bench);
        Assert.Equal(1, listed.CastleId);
        Assert.Equal("Gludio", listed.Name);
        Assert.Equal(0, listed.OwnerClanId);
        Assert.Equal(0, listed.TaxPercent);
        Assert.False(listed.RegistrationClosed);
        Assert.Empty(listed.Clans);

        Assert.Null(await bench.World.TrySiegeRegisterAsync(leader.Session, new SiegeRegisterRequestMsg
        {
            CastleId = 1,
            Attacker = 1,
            Joining = 1
        }, CancellationToken.None));

        var line = LastSystemMessage(bench);
        Assert.Equal(SiegeProtocol.RegisteredAttacker, line.Id);
        Assert.Equal("Gludio", Assert.Single(line.Params));

        var info = LastInfo(bench);
        var row = Assert.Single(info.Clans);
        Assert.Equal(leader.ClanId, row.ClanId);
        Assert.Equal(SiegeProtocol.Attacker, row.Side);
    }

    [Fact]
    public async Task A_second_registration_is_refused_and_capture_moves_ownership()
    {
        var bench = Open();
        BenchPlayer leader = await EnterClanLeaderAsync(bench);

        var request = new SiegeRegisterRequestMsg { CastleId = 1, Attacker = 1, Joining = 1 };
        Assert.Null(await bench.World.TrySiegeRegisterAsync(leader.Session, request, CancellationToken.None));
        Assert.Equal(SiegeProtocol.Duplicate, await bench.World.TrySiegeRegisterAsync(leader.Session, request, CancellationToken.None));

        long start = bench.World.Sieges.Find(1)!.SiegeUnixMs;
        Assert.Equal(SiegeStartKind.Started, bench.World.StartSiege(1, DateTimeOffset.FromUnixTimeMilliseconds(start)));
        Assert.Null(bench.World.CaptureCastle(leader.ClanId, DateTimeOffset.FromUnixTimeMilliseconds(start + 60_000)));

        Assert.Equal(leader.ClanId, bench.Store.FindClan(leader.ClanId)!.CastleId);
        Assert.Equal(1, bench.World.Sieges.CastleOwnedBy(leader.ClanId));
    }

    [Fact]
    public void Info_codec_round_trips_the_castle_and_its_rows()
    {
        var message = new SiegeInfoNetMsg
        {
            CastleId = 1,
            Name = "Gludio",
            OwnerClanId = 7,
            TaxPercent = 15,
            SiegeUnixMs = 100,
            RegistrationDeadlineUnixMs = 200,
            RegistrationClosed = true,
            InProgress = false,
            EndsUnixMs = 300,
            Clans = [new SiegeClanNet { ClanId = 9, Side = SiegeProtocol.Attacker }]
        };

        var copy = NetCodec.DecodeSiegeInfo(NetCodec.EncodeSiegeInfo(message));
        Assert.Equal(1, copy.CastleId);
        Assert.Equal("Gludio", copy.Name);
        Assert.Equal(7, copy.OwnerClanId);
        Assert.Equal(15, copy.TaxPercent);
        Assert.Equal(100, copy.SiegeUnixMs);
        Assert.Equal(200, copy.RegistrationDeadlineUnixMs);
        Assert.True(copy.RegistrationClosed);
        Assert.False(copy.InProgress);
        Assert.Equal(300, copy.EndsUnixMs);
        var row = Assert.Single(copy.Clans);
        Assert.Equal(9, row.ClanId);
        Assert.Equal(SiegeProtocol.Attacker, row.Side);
    }

    static SiegeInfoNetMsg LastInfo(WorldBench bench) =>
        Assert.IsType<SiegeInfoNetMsg>(bench.Rec.Of("FanoutSiegeInfo").Last()[1]);

    static SystemMessageNetMsg LastSystemMessage(WorldBench bench) =>
        Assert.IsType<SystemMessageNetMsg>(bench.Rec.Of("FanoutSystemMessage").Last()[1]);

    /// <summary>
    /// A level-40 character already leading a clan. The clan is written straight to the store because the
    /// create path is the clan suite's subject, not this one; <c>InsertClan</c> assigns its own id.
    /// </summary>
    static async Task<BenchPlayer> EnterClanLeaderAsync(WorldBench bench)
    {
        BenchPlayer leader = await bench.EnterAsync("leader", "Leader", level: 40, x: 0, y: 0, z: 0);
        int characterId = leader.Ticket.CharacterId;
        var clan = bench.Store.InsertClan(
            new ClanRecord(
                0, "Siege", 4, characterId, 0, 0, 0,
                [new ClanMemberRecord(characterId, "leader", "Leader", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("leader", characterId, 0, 0, 0));
        Assert.NotNull(clan);
        // The world caches clan rows at boot, so reread them before the siege asks who this player leads.
        Assert.True(bench.World.ReloadClansForTests());
        leader.ClanId = clan!.Id;
        return leader;
    }

    static WorldBench Open() => new(siege: SiegeSettings.Mobius);
}
