using System.Text;
using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Consistency;
using Fineage.LoadTest.Models;
using Fineage.LoadTest.Reporting;
using Fineage.Net.Contract;
using Xunit;

namespace Fineage.LoadTest.Tests;

public sealed class ConsistencyTraceTests
{
    [Fact]
    public void Trace_keeps_spawn_despawn_and_move_transitions_for_a_missing_pair()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-2);
        var observerLog = new BotObservationLog(1);
        var subjectLog = new BotObservationLog(2);

        observerLog.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1001, 1000, 1000, true)), t0);

        subjectLog.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(2002, 1000, 1000, true)), t0);
        subjectLog.Record(NetOpcode.MoveState, Move(2002, 1010, 1010), t0.AddSeconds(1));
        subjectLog.Record(NetOpcode.EntityDespawn, NetCodec.EncodeEntityDespawn(Despawn(2002)), t0.AddSeconds(2));
        subjectLog.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(2002, 1020, 1020, true)), t0.AddSeconds(3));
        subjectLog.Record(NetOpcode.MoveState, Move(2002, 1030, 1030), t0.AddSeconds(4));

        var observer = Bot(observerLog);
        var subject = Bot(subjectLog);
        var report = ConsistencyOracle.Analyze([observer, subject], new LoadScenario());
        Assert.Contains(report.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 1 && i.SubjectObjectId == 2002);

        var trace = ConsistencyTraceBuilder.Build([observer, subject], report, new ConsistencyTraceOptions
        {
            Enabled = true,
            MaxIssues = 4,
            MaxFramesPerIssue = 32,
            MaxBytes = 64 * 1024,
            WindowSeconds = 10
        });

        var issue = Assert.Single(trace.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 1 && i.SubjectObjectId == 2002);
        Assert.Contains(issue.Frames,
            f => f.Source == "subject" && f.Op == nameof(NetOpcode.EntitySpawn) && f.EntityId == 2002);
        Assert.Contains(issue.Frames,
            f => f.Source == "subject" && f.Op == nameof(NetOpcode.EntityDespawn) && f.EntityId == 2002);
        Assert.Contains(issue.Frames,
            f => f.Source == "subject" && f.Op == nameof(NetOpcode.MoveState) && f.EntityId == 2002);
        Assert.Contains(issue.Frames,
            f => f.Source == "observer" && f.Op == nameof(NetOpcode.EntitySpawn) && f.EntityId == 1001);
        Assert.False(issue.Truncated);
        Assert.False(trace.Truncated);
    }

    [Fact]
    public void Trace_is_bounded_by_issue_and_frame_count()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-2);
        var observerLog = new BotObservationLog(1);
        var subjectLog = new BotObservationLog(2);
        observerLog.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1001, 1000, 1000, true)), t0);
        subjectLog.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(2002, 1000, 1000, true)), t0);
        for (int i = 0; i < 500; i++)
        {
            subjectLog.Record(NetOpcode.MoveState, Move(2002, 1000 + i, 1000 + i), t0.AddMilliseconds(10 + i * 10));
        }

        var observer = Bot(observerLog);
        var subject = Bot(subjectLog);
        var report = new ConsistencyReport
        {
            Issues = Enumerable.Range(0, 100).Select(_ => new ConsistencyIssue
            {
                Kind = ConsistencyKind.MissingSpawn,
                ObserverBotId = 1,
                OtherBotId = 2,
                SubjectObjectId = 2002,
                Detail = "synthetic missing peer spawn"
            }).ToList()
        };

        var trace = ConsistencyTraceBuilder.Build([observer, subject], report, new ConsistencyTraceOptions
        {
            Enabled = true,
            MaxIssues = 3,
            MaxFramesPerIssue = 7,
            MaxBytes = 8192,
            WindowSeconds = 60
        });

        Assert.Equal(3, trace.TracedIssues);
        Assert.All(trace.Issues, i => Assert.True(i.Frames.Count <= 7));
        Assert.True(trace.Truncated);
        Assert.Equal(Encoding.UTF8.GetByteCount(ConsistencyTraceJson.ToJsonLines(trace)), trace.Bytes);
        Assert.True(trace.Bytes <= 8192, $"trace wrote {trace.Bytes} bytes");
    }

    [Fact]
    public void Trace_byte_limit_drops_issues_and_stays_under_cap()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-2);
        var observerLog = new BotObservationLog(1);
        var subjectLog = new BotObservationLog(2);
        observerLog.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1001, 1000, 1000, true)), t0);
        subjectLog.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(2002, 1000, 1000, true)), t0);
        for (int i = 0; i < 50; i++)
            subjectLog.Record(NetOpcode.MoveState, Move(2002, 1000 + i, 1000 + i), t0.AddMilliseconds(10 + i * 10));

        var observer = Bot(observerLog);
        var subject = Bot(subjectLog);
        var report = new ConsistencyReport
        {
            Issues = Enumerable.Range(0, 50).Select(_ => new ConsistencyIssue
            {
                Kind = ConsistencyKind.MissingSpawn,
                ObserverBotId = 1,
                OtherBotId = 2,
                SubjectObjectId = 2002,
                Detail = "synthetic missing peer spawn"
            }).ToList()
        };

        var trace = ConsistencyTraceBuilder.Build([observer, subject], report, new ConsistencyTraceOptions
        {
            Enabled = true,
            MaxIssues = 50,
            MaxFramesPerIssue = 200,
            MaxBytes = 4096,
            WindowSeconds = 60
        });

        Assert.True(trace.TracedIssues < 50);
        Assert.True(trace.Truncated);
        Assert.True(trace.Bytes <= 4096, $"trace wrote {trace.Bytes} bytes");
        Assert.Equal(Encoding.UTF8.GetByteCount(ConsistencyTraceJson.ToJsonLines(trace)), trace.Bytes);
    }

    [Fact]
    public void Short_reproduction_scenario_loads_with_the_trace_enabled()
    {
        var scenario = ScenarioLoader.Load(Path.Combine(HeavyPlayRolesTests.ScenarioDir(), "consistency-200-town-short.yaml"));

        Assert.Equal(200, scenario.BotCount);
        Assert.True(scenario.ConsistencyTrace.Enabled);
        Assert.Equal(100, scenario.DurationSeconds);
        Assert.True(scenario.ConsistencyTrace.MaxIssues > 0);
        Assert.True(scenario.ConsistencyTrace.MaxBytes > 0);
    }

    [Fact]
    public void Trace_spreads_the_issue_budget_across_kinds()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-2);
        var observerLog = new BotObservationLog(1);
        var subjectLog = new BotObservationLog(2);
        observerLog.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1001, 1000, 1000, true)), t0);
        subjectLog.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(2002, 1000, 1000, true)), t0);
        var observer = Bot(observerLog);
        var subject = Bot(subjectLog);

        var report = new ConsistencyReport
        {
            Issues = Enumerable.Range(0, 50)
                .Select(_ => new ConsistencyIssue
                {
                    Kind = ConsistencyKind.DuplicateSpawn,
                    ObserverBotId = 1,
                    OtherBotId = 2,
                    SubjectObjectId = 2002,
                    Detail = "synthetic duplicate spawn"
                })
                .Concat(Enumerable.Range(0, 50).Select(_ => new ConsistencyIssue
                {
                    Kind = ConsistencyKind.MissingSpawn,
                    ObserverBotId = 1,
                    OtherBotId = 2,
                    SubjectObjectId = 2002,
                    Detail = "synthetic missing peer spawn"
                }))
                .ToList()
        };

        var trace = ConsistencyTraceBuilder.Build([observer, subject], report, new ConsistencyTraceOptions
        {
            Enabled = true,
            MaxIssues = 2,
            MaxFramesPerIssue = 20,
            MaxBytes = 8192,
            WindowSeconds = 60
        });

        Assert.Equal(2, trace.TracedIssues);
        Assert.Equal(
            new[] { ConsistencyKind.MissingSpawn, ConsistencyKind.DuplicateSpawn },
            trace.Issues.Select(i => i.Kind).OrderBy(k => k));
        Assert.True(trace.Truncated);
    }

    [Fact]
    public void Report_writer_persists_the_trace_as_a_separate_jsonl_file()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-2);
        var issue = new ConsistencyTraceIssue
        {
            Kind = ConsistencyKind.MissingSpawn,
            ObserverBotId = 1,
            SubjectObjectId = 2002,
            Detail = "synthetic missing peer spawn",
            AnchorAt = t0,
            WindowStart = t0.AddSeconds(-8),
            WindowEnd = t0.AddSeconds(8),
            FramesAvailable = 1,
            Frames =
            [
                new ConsistencyTraceFrame
                {
                    At = t0,
                    Source = "subject",
                    Opcode = NetOpcode.EntitySpawn,
                    Op = nameof(NetOpcode.EntitySpawn),
                    EntityId = 2002,
                    Generation = 1,
                    X = 1000,
                    Y = 1000,
                    Z = 0
                }
            ]
        };
        int bytes = ConsistencyTraceJson.Utf8Bytes(ConsistencyTraceJson.SerializeIssue(issue)) + 1;
        var trace = new ConsistencyTrace
        {
            GeneratedAt = t0,
            TotalIssues = 1,
            TracedIssues = 1,
            FrameCount = 1,
            Bytes = bytes,
            WindowSeconds = 8,
            Issues = [issue]
        };
        var report = new RunReport
        {
            ScenarioName = "trace-writer",
            Target = "fineage",
            StartedAt = t0,
            FinishedAt = t0,
            Consistency = new ConsistencyReport(),
            ConsistencyTrace = trace
        };

        string dir = Directory.CreateTempSubdirectory("trace-writer").FullName;
        try
        {
            ReportWriter.Write(report, dir);

            string tracePath = Path.Combine(dir, "consistency-trace.jsonl");
            Assert.True(File.Exists(tracePath));
            Assert.Equal(ConsistencyTraceJson.ToJsonLines(trace), File.ReadAllText(tracePath));
            Assert.Equal(bytes, new FileInfo(tracePath).Length);
            Assert.DoesNotContain("consistencyTrace",
                File.ReadAllText(Path.Combine(dir, "report.json")), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Trace_keeps_the_full_duplicate_spawn_run_not_only_the_last_window()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-2);
        var observerLog = new BotObservationLog(9);
        for (int i = 0; i < 4; i++)
        {
            observerLog.Record(NetOpcode.EntitySpawn,
                NetCodec.EncodeEntitySpawn(Player(384, 1000 + i, 1000 + i, false)), t0.AddSeconds(i * 20));
            observerLog.Record(NetOpcode.MoveState, Move(384, 1010 + i, 1010 + i), t0.AddSeconds(i * 20 + 10));
        }

        var observer = Bot(observerLog);
        var report = new ConsistencyReport
        {
            Issues =
            [
                new ConsistencyIssue
                {
                    Kind = ConsistencyKind.DuplicateSpawn,
                    ObserverBotId = 9,
                    SubjectObjectId = 384,
                    Detail = "synthetic duplicate spawn"
                }
            ]
        };

        var trace = ConsistencyTraceBuilder.Build([observer], report, new ConsistencyTraceOptions
        {
            Enabled = true,
            MaxIssues = 1,
            MaxFramesPerIssue = 6,
            MaxBytes = 64 * 1024,
            WindowSeconds = 1
        });

        var issue = Assert.Single(trace.Issues);
        Assert.Equal(4, issue.Frames.Count(f => f.Op == nameof(NetOpcode.EntitySpawn) && f.EntityId == 384));
        Assert.True(issue.Frames.Count <= 6);
        Assert.True(issue.WindowEnd - issue.WindowStart >= TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Combat_trace_includes_the_observer_pawn_frames()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-2);
        var observerLog = new BotObservationLog(29);
        var subjectLog = new BotObservationLog(31);
        observerLog.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(394, 1000, 1000, true)), t0);
        observerLog.Record(NetOpcode.MoveState, Move(394, 1010, 1010), t0);
        subjectLog.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(396, 1100, 1100, true)), t0);
        subjectLog.Record(NetOpcode.AttackState, NetCodec.EncodeAttackState(new AttackStateMsg
        {
            AttackerValue = 396,
            AttackerGeneration = 1,
            TargetValue = 93144,
            TargetGeneration = 1,
            Damage = 79,
            AttackerX = 1100,
            AttackerY = 1100,
            TargetX = 1100,
            TargetY = 1100
        }), t0);

        var observer = Bot(observerLog);
        var subject = Bot(subjectLog);
        var report = new ConsistencyReport
        {
            Issues =
            [
                new ConsistencyIssue
                {
                    Kind = ConsistencyKind.MissingCombatFanout,
                    ObserverBotId = 29,
                    SubjectObjectId = 396,
                    Detail = "synthetic missing combat fanout",
                    ObservedAt = t0
                }
            ]
        };

        var trace = ConsistencyTraceBuilder.Build([observer, subject], report, new ConsistencyTraceOptions
        {
            Enabled = true,
            MaxIssues = 1,
            MaxFramesPerIssue = 16,
            MaxBytes = 64 * 1024,
            WindowSeconds = 8
        });

        var issue = Assert.Single(trace.Issues);
        Assert.Contains(issue.Frames, f => f.Source == "observer" && f.Op == nameof(NetOpcode.MoveState) && f.EntityId == 394);
        Assert.Contains(issue.Frames, f => f.Source == "subject" && f.Op == nameof(NetOpcode.AttackState) && f.EntityId == 396);
    }

    static FineageBotClient Bot(BotObservationLog log)
    {
        var bot = new FineageBotClient(
            new LoadScenario { GatekeeperTravelShare = 0 },
            log.BotId,
            new BotStats { BotId = log.BotId, InWorld = true, LastActionAt = DateTimeOffset.UtcNow },
            new BotPeerDirectory());
        foreach (var frame in log.Snapshot())
            bot.ObservationLog.Record(frame.Opcode, frame.Payload, frame.At);
        bot.SetSelfEntityForTest(log.Tracker.SelfEntityValue);
        return bot;
    }

    static EntitySpawnMsg Player(ulong id, int x, int y, bool isSelf) => new()
    {
        EntityValue = id,
        EntityGeneration = 1,
        Kind = EntityKindNet.Player,
        IsSelf = isSelf,
        X = x,
        Y = y,
        Z = 0,
        MaxHp = 100,
        CurHp = 100
    };

    static EntityDespawnMsg Despawn(ulong id) => new()
    {
        EntityValue = id,
        EntityGeneration = 1
    };

    static byte[] Move(ulong id, int x, int y) => NetCodec.EncodeMoveState(new MoveStateMsg
    {
        EntityValue = id,
        EntityGeneration = 1,
        OriginX = x,
        OriginY = y,
        DestX = x,
        DestY = y,
        DestZ = 0,
        Running = true
    });
}
