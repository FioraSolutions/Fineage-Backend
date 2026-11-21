using Fineage.Accounts;
using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Inventory;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Clan-hall Scroll of Escape and death restart (Mobius CLANHALL teleports / RequestRestartPoint case 1).</summary>
public sealed class HallEscapeTests
{
    const int HallId = 28;
    const int ScrollClanHall = 1829;
    const int BlessedScrollClanHall = 5858;

    static NetBridgeRecorder Recorder(NetTestKit kit)
    {
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return rec;
    }

    static int Grant(NetTestKit kit, ZoneEntity player, int itemId, int count = 1)
    {
        lock (kit.World)
            return player.Inventory!.Add(itemId, count).ObjectId;
    }

    static void OwnHall(NetTestKit kit, ZoneSession session, int hallId)
    {
        var clan = kit.Store.InsertClan(
            new ClanRecord(
                0, "HallOwn", 4, session.ObjectId, 0, 0, 0,
                [new ClanMemberRecord(session.ObjectId, session.Account, session.Name, HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState(session.Account, session.ObjectId, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.True(kit.World.ReloadClansForTests());
        kit.World.SetClanHallOwnerForTests(hallId, clan!.Id);
    }

    [Fact]
    public async Task Clan_hall_escape_scroll_teleports_to_owner_spawn()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (session, player) = await kit.EnterAsync(classId: 18);
        OwnHall(kit, session, HallId);
        int scroll = Grant(kit, player, ScrollClanHall);
        int beforeX = player.X;

        Assert.Null(await kit.World.TryUseItemAsync(session, scroll, CancellationToken.None));
        var cast = Assert.IsType<MagicSkillUseMsg>(
            Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)))[1]);
        Assert.Equal(2040, cast.SkillId);
        Assert.Equal(20_000, cast.HitTimeMs);
        Assert.Equal(beforeX, player.X);

        await kit.World.CompleteCastForTestAsync(session);

        var expected = ClanHallSpawnCatalog.LoadDefault();
        Assert.True(expected.TryGet(HallId, out var spawn));
        Assert.Equal((spawn.Owner.X, spawn.Owner.Y, spawn.Owner.Z), (player.X, player.Y, player.Z));
        Assert.Equal(0, player.Inventory!.CountOf(ScrollClanHall));
        Assert.NotEmpty(rec.Of(nameof(IWorldEventPublisher.FanoutTeleportToLocation)));
    }

    [Fact]
    public async Task Blessed_clan_hall_escape_is_near_instant()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (session, player) = await kit.EnterAsync(classId: 18);
        OwnHall(kit, session, HallId);
        int scroll = Grant(kit, player, BlessedScrollClanHall);

        Assert.Null(await kit.World.TryUseItemAsync(session, scroll, CancellationToken.None));
        var cast = Assert.IsType<MagicSkillUseMsg>(
            Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)))[1]);
        Assert.Equal(2177, cast.SkillId);
        Assert.Equal(200, cast.HitTimeMs);
        await kit.World.CompleteCastForTestAsync(session);
        Assert.True(ClanHallSpawnCatalog.LoadDefault().TryGet(HallId, out var spawn));
        Assert.Equal(spawn.Owner.Z, player.Z);
    }

    [Fact]
    public async Task Clan_hall_escape_without_hideout_is_rejected()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 18);
        int scroll = Grant(kit, player, ScrollClanHall);
        Assert.Equal(ConsumableRules.EscapeNoClanHall,
            await kit.World.TryUseItemAsync(session, scroll, CancellationToken.None));
        Assert.Equal(1, player.Inventory!.CountOf(ScrollClanHall));
    }

    [Fact]
    public async Task Death_offers_clan_hall_restart_and_revives_there()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (session, player) = await kit.EnterAsync(classId: 18);
        OwnHall(kit, session, HallId);

        Assert.Null(await kit.World.TryAdminKillPlayerAsync(session, CancellationToken.None));
        var die = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutDie)));
        Assert.Equal(DeathProtocol.RestartClanHall, (int)die[3]!);

        Assert.Null(await kit.World.TryReviveAsync(session, DeathProtocol.ReviveToClanHall, CancellationToken.None));
        var spawn = ClanHallSpawnCatalog.LoadDefault();
        Assert.True(spawn.TryGet(HallId, out var row));
        Assert.Equal((row.Owner.X, row.Owner.Y, row.Owner.Z), (player.X, player.Y, player.Z));
        Assert.False(player.Dead);
    }

    [Fact]
    public async Task Revive_to_clan_hall_without_hideout_fails()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 18);
        Assert.Null(await kit.World.TryAdminKillPlayerAsync(session, CancellationToken.None));
        Assert.Equal(DeathProtocol.NoClanHall,
            await kit.World.TryReviveAsync(session, DeathProtocol.ReviveToClanHall, CancellationToken.None));
        Assert.True(player.Dead);
        _ = player;
    }
}