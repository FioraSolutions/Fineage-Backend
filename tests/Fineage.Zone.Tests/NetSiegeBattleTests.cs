using Fineage.Accounts;
using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Siege;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// The running siege after Mobius: towers and the NPC garrison at the start, the countdown and the draw at the end,
/// Seal of Ruler on the holy artifact, headquarters in the HQ zone with the castle / HQ restart, gates that break and
/// come back, siege relation bits, and the chamberlain functions (tax, treasury, door upgrade, crown).
/// </summary>
public sealed class NetSiegeBattleTests
{
    const int Gludio = 1;
    const int OuterGate = 19210001;
    const int Wall = 19210003;
    static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Start_spawns_towers_and_garrison_and_an_npc_castle_ends_in_a_draw()
    {
        var (bench, clock) = Open();
        var leader = await LeaderAsync(bench, "attack", "Attackers");
        await RegisterAsync(bench, leader, attacker: true);
        long start = bench.World.Sieges.Find(Gludio)!.SiegeUnixMs;
        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(start);

        Assert.Equal(SiegeStartKind.Started, bench.World.StartSiege(Gludio));
        var npcs = bench.World.SiegeNpcsForTests(Gludio);
        Assert.Equal(3, npcs.Count(n => n.SiegeNpc!.Role == SiegeNpcRole.ControlTower));
        Assert.Equal(2, npcs.Count(n => n.SiegeNpc!.Role == SiegeNpcRole.FlameTower));
        Assert.True(npcs.Count(n => n.SiegeNpc!.Role == SiegeNpcRole.Guard) > 100);
        Assert.Contains(Lines(bench), l => l.Id == SiegeProtocol.SiegeStarted);
        Assert.Contains(Lines(bench), l => l.Id == SiegeProtocol.HoursLeft);

        clock.Now = clock.Now.AddHours(2);
        bench.World.TickSiegesForTests();
        Assert.False(bench.World.Sieges.Find(Gludio)!.InProgress);
        Assert.Contains(Lines(bench), l => l.Id == SiegeProtocol.SiegeFinished);
        Assert.Contains(Lines(bench), l => l.Id == SiegeProtocol.Draw);
        Assert.DoesNotContain(bench.World.SiegeNpcsForTests(Gludio), n => n.SiegeNpc!.Role != SiegeNpcRole.Door);
    }

    [Fact]
    public async Task A_siege_without_attackers_is_cancelled()
    {
        var (bench, clock) = Open();
        await bench.EnterAsync("watcher", "Watcher", clearRecorder: false);
        long start = bench.World.Sieges.Find(Gludio)!.SiegeUnixMs;
        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(start);
        Assert.Equal(SiegeStartKind.Cancelled, bench.World.StartSiege(Gludio));
        Assert.Contains(Lines(bench), l => l.Id == SiegeProtocol.SiegeCancelled);
        Assert.DoesNotContain(bench.World.SiegeNpcsForTests(Gludio), n => n.SiegeNpc!.Role != SiegeNpcRole.Door);
    }

    [Fact]
    public async Task Leader_seals_the_artifact_and_takes_the_castle()
    {
        var (bench, clock) = Open();
        var leader = await LeaderAsync(bench, "sealer", "Sealers");
        Assert.True(leader.Player.KnownSkills.ContainsKey(SiegeProtocol.SealOfRulerSkillId));
        Assert.True(leader.Player.KnownSkills.ContainsKey(SiegeProtocol.BuildHeadquartersSkillId));
        await RegisterAsync(bench, leader, attacker: true);
        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(bench.World.Sieges.Find(Gludio)!.SiegeUnixMs);
        Assert.Equal(SiegeStartKind.Started, bench.World.StartSiege(Gludio));

        var (x, y, z) = InsideCastle(bench);
        leader.MoveTo(x, y, z);
        var artifact = bench.World.SpawnArtifactForTests(Gludio, x + 50, y, z - 10);
        leader.Player.CurMp = leader.Player.MaxMp;
        Assert.Null(await bench.World.TrySkillUseAsync(leader.Session, SiegeProtocol.SealOfRulerSkillId, artifact.ObjectId, Ct));
        bench.World.ResolvePendingHitForTest(leader.Player);

        Assert.Equal(Gludio, bench.World.Sieges.CastleOwnedBy(leader.ClanId));
        var captured = Lines(bench).Last(l => l.Id == SiegeProtocol.CastleCaptured);
        Assert.Equal(["Sealers", "Gludio"], captured.Params);
        // An NPC castle taken by the only attacker ends the siege on the spot (Mobius midVictory → endSiege).
        Assert.False(bench.World.Sieges.Find(Gludio)!.InProgress);
        Assert.Contains(Lines(bench), l => l.Id == SiegeProtocol.Victorious);
        Assert.Contains(Lines(bench), l => l.Id == SiegeProtocol.ReputationGained);
    }

    [Fact]
    public async Task Seal_of_ruler_needs_the_artifact_in_range()
    {
        var (bench, clock) = Open();
        var leader = await LeaderAsync(bench, "farer", "Farers");
        await RegisterAsync(bench, leader, attacker: true);
        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(bench.World.Sieges.Find(Gludio)!.SiegeUnixMs);
        bench.World.StartSiege(Gludio);
        var (x, y, z) = InsideCastle(bench);
        leader.MoveTo(x, y, z);
        var artifact = bench.World.SpawnArtifactForTests(Gludio, x + 400, y, z);
        leader.Player.CurMp = leader.Player.MaxMp;

        Assert.Null(await bench.World.TrySkillUseAsync(leader.Session, SiegeProtocol.SealOfRulerSkillId, artifact.ObjectId, Ct));
        Assert.Equal(748, Lines(bench).Last().Id);
        Assert.Equal(0, bench.World.Sieges.Find(Gludio)!.OwnerClanId);
    }

    [Fact]
    public async Task Headquarters_need_the_hq_zone_and_give_a_restart_point()
    {
        var (bench, clock) = Open();
        var leader = await LeaderAsync(bench, "hqbuild", "Builders", [new StoredItem(SiegeProtocol.HeadquartersItemId, 300, -1)]);
        await RegisterAsync(bench, leader, attacker: true);
        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(bench.World.Sieges.Find(Gludio)!.SiegeUnixMs);
        bench.World.StartSiege(Gludio);
        leader.Player.CurMp = leader.Player.MaxMp;

        var (x, y, z) = InsideCastle(bench);
        leader.MoveTo(x, y, z);
        Assert.Null(await bench.World.TrySkillUseAsync(leader.Session, SiegeProtocol.BuildHeadquartersSkillId, 0, Ct));
        Assert.Equal(SiegeProtocol.BaseNotHere, Lines(bench).Last().Id);

        var (hx, hy, hz) = InsideHq(bench);
        leader.MoveTo(hx, hy, hz);
        Assert.Null(await bench.World.TrySkillUseAsync(leader.Session, SiegeProtocol.BuildHeadquartersSkillId, 0, Ct));
        bench.World.ResolvePendingHitForTest(leader.Player);
        var flag = Assert.Single(bench.World.SiegeNpcsForTests(Gludio), n => n.SiegeNpc!.Role == SiegeNpcRole.Flag);
        Assert.Equal(leader.ClanId, flag.SiegeNpc!.FlagClanId);
        Assert.Equal(0, leader.Inventory.CountOf(SiegeProtocol.HeadquartersItemId));

        leader.MoveTo(hx + 300, hy, hz);
        Assert.Null(await bench.World.TryAdminKillPlayerAsync(leader.Session, Ct));
        var die = bench.Rec.Of(nameof(IWorldEventPublisher.FanoutDie)).Last();
        Assert.Equal(DeathProtocol.RestartSiegeHq, (int)die[3]!);
        Assert.Equal(DeathProtocol.NoCastle, await bench.World.TryReviveAsync(leader.Session, DeathProtocol.ReviveToCastle, Ct));
        Assert.Null(await bench.World.TryReviveAsync(leader.Session, DeathProtocol.ReviveToSiegeHq, Ct));
        Assert.Equal((flag.X, flag.Y), (leader.Player.X, leader.Player.Y));
    }

    [Fact]
    public async Task Gates_break_during_a_siege_walls_do_not_and_both_come_back()
    {
        var (bench, clock) = Open();
        var leader = await LeaderAsync(bench, "rammer", "Rams");
        await RegisterAsync(bench, leader, attacker: true);
        var gate = bench.World.CastleDoorForTests(OuterGate);
        var wall = bench.World.CastleDoorForTests(Wall);
        Assert.NotNull(gate);
        Assert.NotNull(wall);

        // Outside a siege a gate takes nothing.
        Assert.True(bench.World.SiegeHitForTests(leader.Player, gate!, 1000));
        Assert.Equal(gate!.MaxHp, gate.CurHp);

        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(bench.World.Sieges.Find(Gludio)!.SiegeUnixMs);
        bench.World.StartSiege(Gludio);
        Assert.True(bench.World.SiegeHitForTests(leader.Player, wall!, 1000));
        Assert.Equal(wall!.MaxHp, wall.CurHp);
        Assert.True(bench.World.SiegeHitForTests(leader.Player, gate, gate.MaxHp));
        Assert.True(gate.Dead);
        Assert.True(bench.World.GeoDoorOpenForTests(OuterGate));
        Assert.Contains(Lines(bench), l => l.Id == SiegeProtocol.GateBroken);
        var status = Assert.IsType<DoorStatusNetMsg>(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutDoorStatus)).Last()[1]);
        Assert.True(status.Open);

        Assert.Null(bench.World.EndSiege(Gludio));
        Assert.False(gate.Dead);
        Assert.Equal(gate.MaxHp, gate.CurHp);
        Assert.False(bench.World.GeoDoorOpenForTests(OuterGate));
    }

    [Fact]
    public async Task Attackers_carry_the_siege_relation_bits()
    {
        var (bench, clock) = Open();
        var a = await LeaderAsync(bench, "relaa", "RelA");
        var b = await LeaderAsync(bench, "relbb", "RelB");
        await RegisterAsync(bench, a, attacker: true);
        await RegisterAsync(bench, b, attacker: true);
        Assert.Equal(0, bench.World.RelationOfForTest(a.Player, b.Player) & SiegeProtocol.RelationInSiege);
        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(bench.World.Sieges.Find(Gludio)!.SiegeUnixMs);
        bench.World.StartSiege(Gludio);
        int relation = bench.World.RelationOfForTest(a.Player, b.Player);
        Assert.NotEqual(0, relation & SiegeProtocol.RelationInSiege);
        Assert.NotEqual(0, relation & SiegeProtocol.RelationAttacker);
        Assert.NotEqual(0, relation & SiegeProtocol.RelationAlly);
        Assert.Equal(0, relation & SiegeProtocol.RelationEnemy);
    }

    [Fact]
    public async Task The_lord_sets_tax_moves_treasury_upgrades_a_gate_and_takes_the_crown()
    {
        var (bench, clock) = Open();
        var lord = await LeaderAsync(bench, "lord", "Lords", [new StoredItem(PledgeProtocol.AdenaItemId, 5_000_000, -1)]);
        await OwnAsync(bench, clock, lord);
        Park(bench, lord);

        Assert.Null(await Cmd(bench, lord, CastleCommandKind.SetTax, a1: 10));
        Assert.Equal(10, bench.World.Sieges.Find(Gludio)!.TaxPercent);
        Assert.Equal(SiegeProtocol.Tax, await Cmd(bench, lord, CastleCommandKind.SetTax, a1: 16));

        Assert.Null(await Cmd(bench, lord, CastleCommandKind.Deposit, amount: 1_000_000));
        Assert.Equal(1_000_000, bench.World.Sieges.Find(Gludio)!.Treasury);
        Assert.Equal(4_000_000, lord.Inventory.CountOf(PledgeProtocol.AdenaItemId));
        Assert.Equal(SiegeProtocol.Adena, await Cmd(bench, lord, CastleCommandKind.Withdraw, amount: 2_000_000));
        Assert.Null(await Cmd(bench, lord, CastleCommandKind.Withdraw, amount: 400_000));
        Assert.Equal(600_000, bench.World.Sieges.Find(Gludio)!.Treasury);

        int baseHp = bench.World.CastleDoorForTests(OuterGate)!.MaxHp;
        Assert.Null(await Cmd(bench, lord, CastleCommandKind.UpgradeDoor, a1: OuterGate, a2: 2));
        Assert.Equal(baseHp * 2, bench.World.CastleDoorForTests(OuterGate)!.MaxHp);
        Assert.Equal(4_400_000 - 3_000_000, lord.Inventory.CountOf(PledgeProtocol.AdenaItemId));
        Assert.Equal(SiegeProtocol.UpgradeLevel, await Cmd(bench, lord, CastleCommandKind.UpgradeDoor, a1: OuterGate, a2: 2));

        Assert.Null(await Cmd(bench, lord, CastleCommandKind.TakeCrown));
        Assert.Equal(1, lord.Inventory.CountOf(SiegeProtocol.LordsCrownItemId));
        Assert.Equal(SiegeProtocol.HasItem, await Cmd(bench, lord, CastleCommandKind.TakeCrown));

        var info = Assert.IsType<CastleInfoNetMsg>(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutCastleInfo)).Last()[1]);
        Assert.Equal("Lords", info.OwnerClanName);
        Assert.Equal(600_000, info.Treasury);
        Assert.Contains(info.Doors, d => d.DoorId == OuterGate && d.UpgradeRatio == 2);

        var saved = bench.Store.ListCastles().Single(c => c.Id == Gludio);
        Assert.Equal(600_000, saved.Treasury);
        Assert.Equal(2, saved.DoorUpgrades![OuterGate]);
    }

    [Fact]
    public async Task Castle_functions_belong_to_the_owner()
    {
        var (bench, clock) = Open();
        var lord = await LeaderAsync(bench, "owner", "Owners");
        await OwnAsync(bench, clock, lord);
        var other = await LeaderAsync(bench, "other", "Others");
        Park(bench, other);
        Assert.Equal(SiegeProtocol.Rights, await bench.World.TryCastleCommandAsync(other.Session,
            new CastleCommandNetMsg { Kind = CastleCommandKind.SetTax, CastleId = Gludio, IntArg1 = 5 }, clock.Now, Ct));
        Assert.Null(await bench.World.TryCastleCommandAsync(other.Session,
            new CastleCommandNetMsg { Kind = CastleCommandKind.Info, CastleId = Gludio }, clock.Now, Ct));
        var info = Assert.IsType<CastleInfoNetMsg>(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutCastleInfo)).Last()[1]);
        Assert.Equal(0, info.Treasury);
        Assert.Empty(info.Doors);
    }

    [Fact]
    public void Castle_codecs_round_trip()
    {
        var door = NetCodec.DecodeDoorStatus(NetCodec.EncodeDoorStatus(new DoorStatusNetMsg
        {
            ObjectId = 5,
            DoorId = OuterGate,
            Open = true,
            CurHp = 10,
            MaxHp = 20,
            Attackable = true,
            Wall = false,
            CastleId = 1,
            HallId = 0,
            UpgradeRatio = 3
        }));
        Assert.Equal((5, OuterGate, true, 10, 20, true, false, 1, 0, 3),
            (door.ObjectId, door.DoorId, door.Open, door.CurHp, door.MaxHp, door.Attackable, door.Wall, door.CastleId, door.HallId, door.UpgradeRatio));

        var cmd = NetCodec.DecodeCastleCommand(NetCodec.EncodeCastleCommand(new CastleCommandNetMsg
        {
            Kind = CastleCommandKind.UpgradeDoor,
            CastleId = 2,
            IntArg1 = 7,
            IntArg2 = 5,
            Amount = 1L << 40
        }));
        Assert.Equal((CastleCommandKind.UpgradeDoor, 2, 7, 5, 1L << 40), (cmd.Kind, cmd.CastleId, cmd.IntArg1, cmd.IntArg2, cmd.Amount));

        var info = NetCodec.DecodeCastleInfo(NetCodec.EncodeCastleInfo(new CastleInfoNetMsg
        {
            CastleId = 3,
            Name = "Giran",
            OwnerClanId = 9,
            OwnerClanName = "C",
            TaxPercent = 12,
            MaxTaxPercent = 15,
            Treasury = 1L << 33,
            SiegeUnixMs = 77,
            InProgress = true,
            Doors = [new CastleDoorNet { DoorId = 1, UpgradeType = 3, UpgradeRatio = 5, CurHp = 4, MaxHp = 8, Open = true }]
        }));
        Assert.Equal((3, "Giran", 9, "C", 12, 15, 1L << 33, 77L, true),
            (info.CastleId, info.Name, info.OwnerClanId, info.OwnerClanName, info.TaxPercent, info.MaxTaxPercent, info.Treasury,
                info.SiegeUnixMs, info.InProgress));
        var row = Assert.Single(info.Doors);
        Assert.Equal((1, 3, 5, 4, 8, true), (row.DoorId, row.UpgradeType, row.UpgradeRatio, row.CurHp, row.MaxHp, row.Open));
    }

    [Fact]
    public void Entity_die_restart_flags_are_an_optional_tail()
    {
        var plain = new EntityDieMsg { EntityValue = 9, EntityGeneration = 1, KillerObjectId = 3 };
        byte[] legacy = NetCodec.EncodeEntityDie(plain);
        Assert.Equal(0, NetCodec.DecodeEntityDie(legacy).RestartFlags);
        byte[] tailed = NetCodec.EncodeEntityDie(new EntityDieMsg
        {
            EntityValue = 9,
            EntityGeneration = 1,
            KillerObjectId = 3,
            RestartFlags = EntityDieMsg.RestartCastle | EntityDieMsg.RestartSiegeHq
        });
        Assert.Equal(legacy.Length + 1, tailed.Length);
        Assert.Equal(3, NetCodec.DecodeEntityDie(tailed).RestartFlags);
    }

    [Fact]
    public void Castle_table_has_the_nine_interlude_castles()
    {
        var table = CastleData.Default;
        Assert.Equal(9, table.All.Count());
        Assert.Equal(["Gludio", "Dion", "Giran", "Oren", "Aden", "Innadril", "Goddard", "Rune", "Schuttgart"],
            table.All.Select(c => c.Name));
        Assert.All(table.All, c => Assert.Equal(3, c.ControlTowers.Count));
        Assert.Equal(Gludio, table.ArtifactCastle(35063));
        Assert.Equal(Gludio, table.DoorCastle(OuterGate));
        Assert.True(table.Find(Gludio)!.Doors.Single(d => d.Id == Wall).Wall);
    }

    // ---- helpers ----

    static (WorldBench, MutableClock) Open()
    {
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        return (new WorldBench(siege: SiegeSettings.Mobius, clock: () => clock.Now), clock);
    }

    static List<SystemMessageMsg> Lines(WorldBench bench) =>
        bench.Rec.Of("FanoutSystemMessage").Select(args => (SystemMessageMsg)args[1]!).ToList();

    static (int, int, int) InsideCastle(WorldBench bench)
    {
        var p = CastleData.Default.Find(Gludio)!.OwnerRestart[0];
        Assert.Equal(Gludio, bench.World.SiegeAreas.CastleAt(p.X, p.Y, p.Z));
        return (p.X, p.Y, p.Z);
    }

    static (int, int, int) InsideHq(WorldBench bench)
    {
        var zone = CastleData.Default.Find(Gludio)!.HqZones[0];
        int x = (int)zone.Nodes.Average(n => n.X), y = (int)zone.Nodes.Average(n => n.Y);
        int z = -2400;
        Assert.True(CastleData.Default.Find(Gludio)!.InHqZone(x, y, z));
        Assert.Equal(Gludio, bench.World.SiegeAreas.CastleAt(x, y, z));
        return (x, y, z);
    }

    static async Task<BenchPlayer> LeaderAsync(WorldBench bench, string account, string clanName, StoredItem[]? items = null)
    {
        BenchPlayer leader = await bench.EnterAsync(account, account + "L", items, level: 40, x: 0, y: 0, z: 0, clearRecorder: false);
        int characterId = leader.Ticket.CharacterId;
        var clan = bench.Store.InsertClan(
            new ClanRecord(0, clanName, 4, characterId, 0, 0, 0,
                [new ClanMemberRecord(characterId, account, account + "L", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState(account, characterId, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.True(bench.World.ReloadClansForTests());
        leader.ClanId = clan!.Id;
        return leader;
    }

    static async Task RegisterAsync(WorldBench bench, BenchPlayer leader, bool attacker) =>
        Assert.Null(await bench.World.TrySiegeRegisterAsync(leader.Session,
            new SiegeRegisterRequestMsg { CastleId = Gludio, Attacker = attacker ? 1 : 0, Joining = 1 }, Ct));

    /// <summary>Register, start and capture Gludio, so the clan owns it and the siege is over.</summary>
    static async Task OwnAsync(WorldBench bench, MutableClock clock, BenchPlayer leader)
    {
        await RegisterAsync(bench, leader, attacker: true);
        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(bench.World.Sieges.Find(Gludio)!.SiegeUnixMs);
        Assert.Equal(SiegeStartKind.Started, bench.World.StartSiege(Gludio));
        Assert.Null(bench.World.CaptureCastle(leader.ClanId));
        Assert.Equal(Gludio, bench.World.Sieges.CastleOwnedBy(leader.ClanId));
        if (bench.World.Sieges.Find(Gludio)!.InProgress)
            Assert.Null(bench.World.EndSiege(Gludio));
    }

    /// <summary>Stand next to the Gludio chamberlain when the world spawned one.</summary>
    static void Park(WorldBench bench, BenchPlayer player)
    {
        var chamberlain = bench.World.FindNpcTemplateForTest(35100);
        if (chamberlain != null)
            player.MoveTo(chamberlain.X + 20, chamberlain.Y, chamberlain.Z);
    }

    static Task<string?> Cmd(WorldBench bench, BenchPlayer p, int kind, int a1 = 0, int a2 = 0, long amount = 0) =>
        bench.World.TryCastleCommandAsync(p.Session,
            NetCodec.DecodeCastleCommand(NetCodec.EncodeCastleCommand(new CastleCommandNetMsg
            {
                Kind = kind,
                CastleId = Gludio,
                IntArg1 = a1,
                IntArg2 = a2,
                Amount = amount
            })), bench.Now(), Ct);
}
