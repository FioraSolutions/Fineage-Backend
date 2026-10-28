using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class SevenSignsScheduleTests
{
    [Fact]
    public void Weekly_boundary_uses_the_server_zone_and_monday_18_00()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("FineagePlus3", TimeSpan.FromHours(3), "FineagePlus3", "FineagePlus3");
        var options = new SevenSignsOptions { TimeZone = zone, PeriodStartHour = 18, PeriodStartMinute = 0 };

        var mondayLocal1700 = new DateTimeOffset(2026, 10, 26, 14, 0, 0, TimeSpan.Zero);
        var next = SevenSignsSchedule.NextWeekly(mondayLocal1700, options);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.Zero), next);

        var exactly = new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal(exactly.AddDays(7), SevenSignsSchedule.NextWeekly(exactly, options));

        var sunday = new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.Zero), SevenSignsSchedule.NextWeekly(sunday, options));
    }

    [Fact]
    public void Minor_period_is_fifteen_minutes_and_competition_runs_until_the_next_monday()
    {
        var options = new SevenSignsOptions();
        var monday = new DateTimeOffset(2026, 10, 26, 18, 0, 0, TimeSpan.Zero);
        var recruitingEnds = SevenSignsSchedule.Next(SevenSignsPeriod.Recruiting, monday, options);
        Assert.Equal(monday.AddMinutes(15), recruitingEnds);

        var competitionEnds = SevenSignsSchedule.Next(SevenSignsPeriod.Competition, recruitingEnds, options);
        Assert.Equal(monday.AddDays(7), competitionEnds);
        Assert.Equal(TimeSpan.FromDays(7) - TimeSpan.FromMinutes(15), competitionEnds - recruitingEnds);
    }
}

public sealed class SevenSignsRulesTests
{
    [Theory]
    [InlineData(SevenSignsCabal.None, SevenSignsCabal.None, 0, 0, SevenSignsCabal.None)]
    [InlineData(SevenSignsCabal.None, SevenSignsCabal.Dawn, 35, 0, SevenSignsCabal.Dawn)]
    [InlineData(SevenSignsCabal.None, SevenSignsCabal.Dawn, 34, 0, SevenSignsCabal.None)]
    [InlineData(SevenSignsCabal.None, SevenSignsCabal.Dusk, 0, 35, SevenSignsCabal.Dusk)]
    [InlineData(SevenSignsCabal.Dawn, SevenSignsCabal.Dawn, 10, 0, SevenSignsCabal.Dawn)]
    [InlineData(SevenSignsCabal.Dawn, SevenSignsCabal.Dawn, 9, 0, SevenSignsCabal.None)]
    [InlineData(SevenSignsCabal.Dawn, SevenSignsCabal.None, 10, 40, SevenSignsCabal.Dawn)]
    [InlineData(SevenSignsCabal.Dawn, SevenSignsCabal.Dusk, 10, 35, SevenSignsCabal.Dusk)]
    [InlineData(SevenSignsCabal.Dawn, SevenSignsCabal.Dusk, 10, 34, SevenSignsCabal.Dawn)]
    [InlineData(SevenSignsCabal.Dawn, SevenSignsCabal.Dusk, 9, 34, SevenSignsCabal.None)]
    [InlineData(SevenSignsCabal.Dusk, SevenSignsCabal.Dawn, 35, 10, SevenSignsCabal.Dawn)]
    [InlineData(SevenSignsCabal.Dusk, SevenSignsCabal.Dawn, 34, 10, SevenSignsCabal.Dusk)]
    [InlineData(SevenSignsCabal.Dusk, SevenSignsCabal.Dusk, 0, 10, SevenSignsCabal.Dusk)]
    public void Seal_owner_uses_35_percent_to_claim_and_10_percent_to_keep(
        SevenSignsCabal previous,
        SevenSignsCabal winner,
        int dawnPercent,
        int duskPercent,
        SevenSignsCabal owner)
    {
        Assert.Equal(owner, SevenSignsRules.ResolveOwner(previous, winner, dawnPercent, duskPercent, 35, 10));
    }

    [Fact]
    public void Stone_points_are_3_5_and_10_and_the_score_is_a_500_share_plus_festival()
    {
        Assert.Equal(18, SevenSignsRules.ContributionPoints(1, 1, 1));
        Assert.Equal(150, SevenSignsRules.CabalScore(30, 100, 0, 500));
        Assert.Equal(350, SevenSignsRules.CabalScore(70, 100, 0, 500));
        Assert.Equal(SevenSignsCabal.Dusk, SevenSignsRules.Highest(350, 200));
        Assert.Equal(SevenSignsCabal.None, SevenSignsRules.Highest(10, 10));
        Assert.Equal(10, SevenSignsRules.MemberPercent(1, 10));
        Assert.Equal(9, SevenSignsRules.MemberPercent(1, 11));
    }
}

public sealed class SevenSignsEngineTests
{
    static readonly DateTimeOffset Start = new(2026, 10, 26, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Periods_walk_recruitment_competition_results_validation_and_reset()
    {
        var clock = new MutableGameClock(Start);
        var festival = new RecordingFestival();
        var engine = new SevenSignsEngine(clock, festival: festival);
        var notices = new List<SevenSignsNotice>();

        Assert.Equal(SevenSignsPeriod.Recruiting, engine.Period);
        Assert.False(engine.TryAdvance(Start.AddMinutes(14), notices));

        clock.UtcNow = Start.AddMinutes(15);
        Assert.True(engine.AdvanceDue(notices));
        Assert.Equal(SevenSignsPeriod.Competition, engine.Period);
        Assert.Equal(1, festival.Started);
        Assert.Contains(notices, notice => notice.Id == SevenSignsProtocol.PeriodCompetitionMessage);

        notices.Clear();
        clock.UtcNow = Start.AddDays(7);
        Assert.True(engine.AdvanceDue(notices));
        Assert.Equal(SevenSignsPeriod.Results, engine.Period);
        Assert.Equal(SevenSignsCabal.None, festival.EndedWith);
        Assert.Contains(notices, notice => notice.Id == SevenSignsProtocol.PeriodResultsMessage);
        Assert.DoesNotContain(notices, notice => notice.Id is SevenSignsProtocol.DawnWonMessage or SevenSignsProtocol.DuskWonMessage);

        notices.Clear();
        clock.UtcNow = Start.AddDays(7).AddMinutes(15);
        Assert.True(engine.AdvanceDue(notices));
        Assert.Equal(SevenSignsPeriod.SealValidation, engine.Period);
        Assert.Contains(notices, notice => notice.Id == SevenSignsProtocol.PeriodValidationMessage);
        Assert.Equal(SevenSignsProtocol.SkyNormal, engine.SkyState());

        notices.Clear();
        clock.UtcNow = Start.AddDays(14);
        Assert.True(engine.AdvanceDue(notices));
        Assert.Equal(SevenSignsPeriod.Recruiting, engine.Period);
        Assert.Equal(2, engine.Cycle);
        Assert.Equal(1, festival.Resets);
        Assert.Contains(notices, notice => notice.Id == SevenSignsProtocol.PeriodRecruitingMessage);
        Assert.Equal(0, engine.Score(SevenSignsCabal.Dawn));
        Assert.Equal(0, engine.Score(SevenSignsCabal.Dusk));
    }

    [Fact]
    public void Festival_reward_can_move_seal_ownership_without_rewriting_the_announced_winner()
    {
        var clock = new MutableGameClock(Start);
        var festival = new RecordingFestival
        {
            OnEnded = engine => engine.AddFestivalScore(SevenSignsCabal.Dusk, 600)
        };
        var engine = new SevenSignsEngine(clock, festival: festival);
        festival.Engine = engine;
        var notices = new List<SevenSignsNotice>();
        EnterCompetition(engine, clock, notices);

        Assert.Null(engine.TryJoin(1, classLevel: 1, false, SevenSignsCabal.Dawn, SevenSignsSeal.Avarice, null, notices));
        Assert.Null(engine.TryJoin(2, classLevel: 1, false, SevenSignsCabal.Dawn, SevenSignsSeal.Avarice, null, notices));
        Assert.Null(engine.TryContribute(1, blue: 0, green: 0, red: 10));

        notices.Clear();
        clock.UtcNow = engine.NextChange;
        Assert.True(engine.AdvanceDue(notices));
        Assert.Contains(notices, notice => notice.Id == SevenSignsProtocol.DawnWonMessage);
        Assert.Equal(SevenSignsCabal.Dawn, (SevenSignsCabal)engine.StatusFor(1).PreviousWinner);
        Assert.Equal(SevenSignsCabal.Dusk, (SevenSignsCabal)engine.StatusFor(1).WinningCabal);
        Assert.Equal(SevenSignsCabal.None, (SevenSignsCabal)engine.StatusFor(1).AvariceOwner);
    }

    [Fact]
    public void Contribution_and_festival_scores_feed_the_cabal_total()
    {
        var engine = CompetitionEngine();
        var notices = new List<SevenSignsNotice>();
        Assert.Null(engine.TryJoin(7, 1, false, SevenSignsCabal.Dusk, SevenSignsSeal.Strife, null, notices));
        Assert.Null(engine.TryContribute(7, 1, 1, 1));
        Assert.Equal(18, engine.PlayerContribution(7));
        Assert.Equal(500, engine.Score(SevenSignsCabal.Dusk));

        engine.AddFestivalScore(SevenSignsCabal.Dawn, 40);
        Assert.Equal(40, engine.StatusFor(7).DawnFestivalScore);
        Assert.Equal(0, engine.StatusFor(7).DuskFestivalScore);
        engine.AddFestivalScore(SevenSignsCabal.Dusk, 10);
        Assert.Equal(30, engine.StatusFor(7).DawnFestivalScore);
        Assert.Equal(10, engine.StatusFor(7).DuskFestivalScore);

        Assert.Equal(SevenSignsProtocol.ContributionRejected, engine.TryContribute(7, 0, 0, 100_000));
        Assert.Equal(18, engine.PlayerContribution(7));
    }

    [Fact]
    public void Dawn_join_charges_a_certificate_then_adena_and_dusk_blocks_castle_owners()
    {
        var engine = CompetitionEngine();
        var notices = new List<SevenSignsNotice>();
        int adena = 50_000;
        int certificates = 1;

        Assert.Equal(SevenSignsProtocol.ClassTooLow, engine.TryJoin(1, 0, false, SevenSignsCabal.Dusk, SevenSignsSeal.Gnosis, null, notices));
        Assert.Equal(SevenSignsProtocol.CastleBlocked, engine.TryJoin(2, 1, true, SevenSignsCabal.Dusk, SevenSignsSeal.Gnosis, null, notices));
        Assert.Equal(SevenSignsProtocol.PeriodClosed, new SevenSignsEngine(new MutableGameClock(Start)).TryJoin(3, 1, false, SevenSignsCabal.Dusk, SevenSignsSeal.Gnosis, null, notices));

        Assert.Null(engine.TryJoin(4, 2, true, SevenSignsCabal.Dawn, SevenSignsSeal.Avarice, Pay, notices));
        Assert.Equal(50_000, adena);
        Assert.Equal(1, certificates);

        Assert.Null(engine.TryJoin(5, 2, false, SevenSignsCabal.Dawn, SevenSignsSeal.Gnosis, Pay, notices));
        Assert.Equal(0, certificates);
        Assert.Equal(50_000, adena);

        Assert.Null(engine.TryJoin(6, 2, false, SevenSignsCabal.Dawn, SevenSignsSeal.Strife, Pay, notices));
        Assert.Equal(0, adena);

        Assert.Equal(SevenSignsProtocol.FeeRequired, engine.TryJoin(8, 2, false, SevenSignsCabal.Dawn, SevenSignsSeal.Strife, Pay, notices));
        Assert.Equal(SevenSignsProtocol.BadCabal, engine.TryJoin(4, 2, false, SevenSignsCabal.Dusk, SevenSignsSeal.Strife, null, notices));
        Assert.Null(engine.TryJoin(9, 1, false, SevenSignsCabal.Dawn, SevenSignsSeal.Avarice, Pay, notices));
        Assert.Equal(0, adena);

        bool Pay(SevenSignsJoinCost cost)
        {
            if (cost.CertificateCount > 0)
            {
                if (certificates < cost.CertificateCount)
                    return false;
                certificates -= cost.CertificateCount;
                return true;
            }

            if (adena < cost.Adena)
                return false;
            adena -= cost.Adena;
            return true;
        }
    }

    [Fact]
    public void Seal_choices_at_35_and_10_percent_set_owners_and_validation_effects()
    {
        var clock = new MutableGameClock(Start);
        var engine = new SevenSignsEngine(clock);
        var notices = new List<SevenSignsNotice>();
        EnterCompetition(engine, clock, notices);

        for (int i = 0; i < 7; i++)
            Assert.Null(engine.TryJoin(i + 1, 1, false, SevenSignsCabal.Dawn, SevenSignsSeal.Avarice, null, notices));
        for (int i = 0; i < 3; i++)
            Assert.Null(engine.TryJoin(100 + i, 1, false, SevenSignsCabal.Dawn, SevenSignsSeal.Gnosis, null, notices));
        Assert.Null(engine.TryContribute(1, 0, 0, 1));

        notices.Clear();
        clock.UtcNow = engine.NextChange;
        Assert.True(engine.AdvanceDue(notices));
        var status = engine.StatusFor(1);
        Assert.Equal(SevenSignsCabal.Dawn, (SevenSignsCabal)status.AvariceOwner);
        Assert.Equal(SevenSignsCabal.None, (SevenSignsCabal)status.GnosisOwner);
        Assert.Contains(notices, notice => notice.Id == SevenSignsProtocol.AvariceDawnMessage);

        clock.UtcNow = engine.NextChange;
        Assert.True(engine.AdvanceDue(notices));
        var effects = engine.Effects();
        Assert.True(effects.SealValidation);
        Assert.True(effects.MammonMerchant);
        Assert.True(effects.Lilith);
        Assert.False(effects.Anakim);
        Assert.False(effects.MammonBlacksmith);
        Assert.Equal(SevenSignsStrifeRole.None, effects.StrifeRole(SevenSignsCabal.Dawn));
        Assert.False(effects.DuskSummonBlocked);
        Assert.Equal(SevenSignsProtocol.SkyDawn, engine.SkyState());
        Assert.Equal(1.0, effects.StrifeGatePhysical);
    }

    [Fact]
    public void Strife_owned_by_dawn_blocks_dusk_summons_and_raises_gate_defense_during_validation()
    {
        var clock = new MutableGameClock(Start);
        var engine = new SevenSignsEngine(clock);
        var notices = new List<SevenSignsNotice>();
        EnterCompetition(engine, clock, notices);
        for (int i = 0; i < 10; i++)
            Assert.Null(engine.TryJoin(i + 1, 1, false, SevenSignsCabal.Dawn, SevenSignsSeal.Strife, null, notices));
        Assert.Null(engine.TryContribute(1, 1, 0, 0));
        clock.UtcNow = engine.NextChange;
        Assert.True(engine.AdvanceDue(notices));
        clock.UtcNow = engine.NextChange;
        Assert.True(engine.AdvanceDue(notices));

        var effects = engine.Effects();
        Assert.Equal(SevenSignsCabal.Dawn, effects.StrifeOwner);
        Assert.Equal(SevenSignsStrifeRole.Victor, effects.StrifeRole(SevenSignsCabal.Dawn));
        Assert.Equal(SevenSignsStrifeRole.Vanquished, effects.StrifeRole(SevenSignsCabal.Dusk));
        Assert.True(effects.BlocksSummon(SevenSignsCabal.Dusk));
        Assert.False(effects.BlocksSummon(SevenSignsCabal.Dawn));
        Assert.Equal(1.1, effects.StrifeGatePhysical);
        Assert.Equal(1.1, effects.StrifeGateMagic);
        Assert.False(effects.MammonMerchant);
    }

    [Fact]
    public void Snapshot_keeps_period_scores_and_player_rows()
    {
        string path = Path.Combine(Path.GetTempPath(), "fineage-ssq-" + Guid.NewGuid().ToString("n") + ".json");
        try
        {
            var clock = new MutableGameClock(Start);
            var store = new JsonSevenSignsStore(path);
            var engine = new SevenSignsEngine(clock, new SevenSignsOptions { LazyUpdate = true }, store);
            var notices = new List<SevenSignsNotice>();
            EnterCompetition(engine, clock, notices);
            Assert.Null(engine.TryJoin(42, 1, false, SevenSignsCabal.Dusk, SevenSignsSeal.Gnosis, null, notices));
            Assert.Null(engine.TryContribute(42, 2, 0, 0));
            var beforeSave = new SevenSignsEngine(clock, new SevenSignsOptions { LazyUpdate = true }, new JsonSevenSignsStore(path));
            Assert.Equal(SevenSignsPeriod.Competition, beforeSave.Period);
            Assert.Equal(SevenSignsCabal.None, (SevenSignsCabal)beforeSave.PlayerCabal(42));
            engine.Save();

            var loaded = new SevenSignsEngine(clock, new SevenSignsOptions { LazyUpdate = true }, new JsonSevenSignsStore(path));
            Assert.Equal(SevenSignsPeriod.Competition, loaded.Period);
            Assert.Equal(SevenSignsCabal.Dusk, (SevenSignsCabal)loaded.PlayerCabal(42));
            Assert.Equal(SevenSignsSeal.Gnosis, (SevenSignsSeal)loaded.PlayerSeal(42));
            Assert.Equal(6, loaded.PlayerContribution(42));
            Assert.Equal(6, loaded.StatusFor(42).PlayerAncientAdena);
            Assert.Equal(6, loaded.StatusFor(42).DuskStonePoints);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    static SevenSignsEngine CompetitionEngine()
    {
        var clock = new MutableGameClock(Start);
        var engine = new SevenSignsEngine(clock);
        EnterCompetition(engine, clock, []);
        return engine;
    }

    static void EnterCompetition(SevenSignsEngine engine, MutableGameClock clock, List<SevenSignsNotice> notices)
    {
        clock.UtcNow = Start.AddMinutes(15);
        Assert.True(engine.AdvanceDue(notices));
    }

    sealed class RecordingFestival : ISevenSignsFestivalHook
    {
        public int Started { get; private set; }
        public int Resets { get; private set; }
        public SevenSignsCabal? EndedWith { get; private set; }
        public SevenSignsEngine? Engine { get; set; }
        public Action<SevenSignsEngine>? OnEnded { get; init; }

        public void OnCompetitionStarted() => Started++;

        public void OnCompetitionEnded(SevenSignsCabal winnerBeforeReward)
        {
            EndedWith = winnerBeforeReward;
            if (Engine != null)
                OnEnded?.Invoke(Engine);
        }

        public void OnCycleReset() => Resets++;
    }
}

public sealed class SevenSignsWireTests
{
    [Fact]
    public async Task Status_join_and_contribution_round_trip_and_period_change_is_a_system_line()
    {
        var clock = new MutableGameClock(new DateTimeOffset(2026, 10, 26, 18, 0, 0, TimeSpan.Zero));
        var engine = new SevenSignsEngine(clock);
        var world = new ZoneWorld(sevenSigns: engine);
        await using var speaker = await EnterAsync(world, classId: 1);

        speaker.Checkpoint();
        Assert.Null(await world.TrySevenSignsStatusAsync(speaker.Session, CancellationToken.None));
        var frames = await speaker.ReadNewAsync();
        Assert.Equal((ushort)MsgType.SevenSignsStatus, frames[0].Type);
        Assert.Equal((ushort)MsgType.SevenSignsSky, frames[1].Type);
        var status = MessagePackSerializer.Deserialize<SevenSignsStatusMsg>(frames[0].Body);
        Assert.Equal(SevenSignsProtocol.PeriodRecruiting, status.Period);
        Assert.Equal(SevenSignsProtocol.SkyNormal, MessagePackSerializer.Deserialize<SevenSignsSkyMsg>(frames[1].Body).State);

        speaker.Checkpoint();
        var rejected = await world.TrySevenSignsActionAsync(speaker.Session, new SevenSignsActionRequestMsg
        {
            Action = SevenSignsProtocol.ActionJoin,
            Cabal = SevenSignsProtocol.CabalDusk,
            Seal = SevenSignsProtocol.SealAvarice
        }, CancellationToken.None);
        Assert.Equal(SevenSignsProtocol.PeriodClosed, rejected);

        clock.UtcNow = engine.NextChange;
        Assert.True(await world.AdvanceSevenSignsAsync(CancellationToken.None));
        var broadcast = await speaker.ReadNewAsync();
        Assert.Contains(broadcast, frame => frame.Type == (ushort)MsgType.SystemMessage);
        var line = MessagePackSerializer.Deserialize<SystemMessageMsg>(
            broadcast.Single(frame => frame.Type == (ushort)MsgType.SystemMessage).Body);
        Assert.Equal(SevenSignsProtocol.PeriodCompetitionMessage, line.Id);
        Assert.Equal((ushort)MsgType.SevenSignsSky, broadcast[^1].Type);

        var stones = speaker.Session;
        Assert.Null(await world.TryGrantItemAsync(stones, new GrantItemRequestMsg
        {
            ItemId = SevenSignsProtocol.RedSealStoneId,
            Count = 2
        }, CancellationToken.None));

        speaker.Checkpoint();
        Assert.Null(await world.TrySevenSignsActionAsync(stones, new SevenSignsActionRequestMsg
        {
            Action = SevenSignsProtocol.ActionJoin,
            Cabal = SevenSignsProtocol.CabalDusk,
            Seal = SevenSignsProtocol.SealStrife
        }, CancellationToken.None));
        Assert.Null(await world.TrySevenSignsActionAsync(stones, new SevenSignsActionRequestMsg
        {
            Action = SevenSignsProtocol.ActionContribute,
            RedStones = 2
        }, CancellationToken.None));

        var after = engine.StatusFor(stones.ObjectId);
        Assert.Equal(SevenSignsProtocol.CabalDusk, after.PlayerCabal);
        Assert.Equal(SevenSignsProtocol.SealStrife, after.PlayerSeal);
        Assert.Equal(20, after.PlayerContribution);
        Assert.Equal(20, after.PlayerAncientAdena);
        Assert.Equal(500, after.DuskScore);
    }

    static async Task<Speaker> EnterAsync(ZoneWorld world, int classId)
    {
        var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(4100 + classId, "ssq", "Priest", classId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        var speaker = new Speaker(session, stream);
        await speaker.WelcomeAsync();
        return speaker;
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

        public void Checkpoint() => _mark = _stream.Position;

        public async Task WelcomeAsync()
        {
            var frames = await ReadNewAsync();
            Assert.Equal((ushort)MsgType.SystemMessage, frames[^1].Type);
            var welcome = MessagePackSerializer.Deserialize<SystemMessageMsg>(frames[^1].Body);
            Assert.Equal(ChatProtocol.Welcome, welcome.Id);
        }

        public async Task<List<(ushort Type, byte[] Body)>> ReadNewAsync()
        {
            _stream.Position = _mark;
            var frames = new List<(ushort, byte[])>();
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

        public async ValueTask DisposeAsync() => await _stream.DisposeAsync();
    }
}
