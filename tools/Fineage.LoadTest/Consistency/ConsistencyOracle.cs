using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Models;
using Fineage.Net.Contract;

namespace Fineage.LoadTest.Consistency;

/// <summary>
/// Cross-checks per-client observation logs for AOI/presence parity (ghosts, missing spawns, combat fanout).
/// Server ground truth for player positions comes from the reconciled spawn/move state each bot recorded;
/// pairwise checks approximate Mobius visibility without a live debug tap into the sim.
/// </summary>
public static class ConsistencyOracle
{
    public const int AoiCellSize = 3000;
    /// <summary>3×3 grid watch footprint in world units (GridAoi.GetVisible).</summary>
    public const int WatchRadius = AoiCellSize * 2;
    /// <summary>World units; bots random-walk ±400 so views may lag one tick behind self-tracked move.</summary>
    public const int PositionTolerance = 900;

    public static ConsistencyReport Analyze(
        IReadOnlyList<FineageBotClient> bots,
        LoadScenario scenario,
        int aoiCellSize = AoiCellSize,
        DateTimeOffset? runStarted = null)
    {
        DateTimeOffset steadyAfter = runStarted ?? DateTimeOffset.MinValue;
        int watch = aoiCellSize * 2;
        var issues = new List<ConsistencyIssue>();
        var roster = BuildRoster(bots, scenario);

        foreach (var bot in bots)
        {
            var tracker = bot.ObservationLog.Tracker;
            foreach (var (key, ent) in tracker.Entities)
            {
                if (ent.SpawnsWithoutDespawn > 3)
                {
                    issues.Add(new ConsistencyIssue
                    {
                        Kind = ConsistencyKind.DuplicateSpawn,
                        ObserverBotId = bot.BotId,
                        SubjectObjectId = ent.ObjectId,
                        Detail = $"entity {key} spawned {ent.SpawnsWithoutDespawn} times without an intervening despawn"
                    });
                }

                if (ent.Despawned && ent.LastSpawnOrdinal > ent.LastDespawnOrdinal)
                {
                    issues.Add(new ConsistencyIssue
                    {
                        Kind = ConsistencyKind.GhostEntity,
                        ObserverBotId = bot.BotId,
                        SubjectObjectId = ent.ObjectId,
                        Detail = $"despawned then received spawn/move for entity {key}"
                    });
                }

                if (ent.LastDespawnOrdinal > 0 && ent.LastSpawnOrdinal > ent.LastDespawnOrdinal &&
                    ent.Despawned)
                {
                    issues.Add(new ConsistencyIssue
                    {
                        Kind = ConsistencyKind.OutOfOrderLifecycle,
                        ObserverBotId = bot.BotId,
                        SubjectObjectId = ent.ObjectId,
                        Detail = "alive fanout after despawn without respawn"
                    });
                }
            }
        }

        int pairs = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            for (int j = i + 1; j < roster.Count; j++)
            {
                var a = roster[i];
                var b = roster[j];
                int dist = Distance(a.X, a.Y, b.X, b.Y);
                bool shouldSee = dist <= watch;
                if (!shouldSee)
                    continue;
                pairs++;

                bool aSeesB = a.Tracker.Entities.ContainsKey((ulong)b.ObjectId);
                bool bSeesA = b.Tracker.Entities.ContainsKey((ulong)a.ObjectId);

                if (!aSeesB)
                {
                    issues.Add(new ConsistencyIssue
                    {
                        Kind = ConsistencyKind.MissingSpawn,
                        ObserverBotId = a.BotId,
                        OtherBotId = b.BotId,
                        SubjectObjectId = b.ObjectId,
                        Detail = $"distance {dist} ≤ {watch}: missing peer spawn"
                    });
                }

                if (!bSeesA)
                {
                    issues.Add(new ConsistencyIssue
                    {
                        Kind = ConsistencyKind.MissingSpawn,
                        ObserverBotId = b.BotId,
                        OtherBotId = a.BotId,
                        SubjectObjectId = a.ObjectId,
                        Detail = $"distance {dist} ≤ {watch}: missing peer spawn"
                    });
                }

                if (aSeesB && bSeesA)
                {
                    if (!TryPeerTruth(b, out int truthX, out int truthY, out int truthHp))
                        continue;
                    if (a.Tracker.Entities.TryGetValue((ulong)b.ObjectId, out var viewB))
                    {
                        if (Math.Abs(viewB.X - truthX) > PositionTolerance || Math.Abs(viewB.Y - truthY) > PositionTolerance)
                        {
                            issues.Add(new ConsistencyIssue
                            {
                                Kind = ConsistencyKind.StalePosition,
                                ObserverBotId = a.BotId,
                                OtherBotId = b.BotId,
                                Detail = $"peer view ({viewB.X},{viewB.Y}) vs ground ({truthX},{truthY})"
                            });
                        }

                        if (truthHp > 0 && viewB.CurHp > 0 && Math.Abs(viewB.CurHp - truthHp) > Math.Max(50, truthHp / 10))
                        {
                            issues.Add(new ConsistencyIssue
                            {
                                Kind = ConsistencyKind.HpDesync,
                                ObserverBotId = a.BotId,
                                OtherBotId = b.BotId,
                                SubjectObjectId = b.ObjectId,
                                Detail = $"HP view {viewB.CurHp} vs ground {truthHp}"
                            });
                        }
                    }

                    if (!TryPeerTruth(a, out truthX, out truthY, out truthHp))
                        continue;
                    if (b.Tracker.Entities.TryGetValue((ulong)a.ObjectId, out var viewA))
                    {
                        if (Math.Abs(viewA.X - truthX) > PositionTolerance || Math.Abs(viewA.Y - truthY) > PositionTolerance)
                        {
                            issues.Add(new ConsistencyIssue
                            {
                                Kind = ConsistencyKind.StalePosition,
                                ObserverBotId = b.BotId,
                                OtherBotId = a.BotId,
                                Detail = $"peer view ({viewA.X},{viewA.Y}) vs ground ({truthX},{truthY})"
                            });
                        }
                    }
                }
            }
        }

        foreach (var bot in bots)
            CheckCombatFanout(bot, roster, watch, issues);
        CheckVisibilityLeaks(bots, roster, watch, issues);

        var samples = bots.SelectMany(b => b.ObservationLog.Tracker.CommandToFanoutSamples).ToList();
        var attackSamples = bots.SelectMany(b => b.ObservationLog.Tracker.AttackFanoutSamples).ToList();
        var skillSamples = bots.SelectMany(b => b.ObservationLog.Tracker.SkillFanoutSamples).ToList();
        var latencies = samples.Select(s => s.Milliseconds).OrderBy(v => v).ToList();
        var steady = samples.Where(s => s.ObservedAt >= steadyAfter).Select(s => s.Milliseconds).OrderBy(v => v).ToList();
        var steadyAttack = attackSamples.Where(s => s.ObservedAt >= steadyAfter).Select(s => s.Milliseconds).OrderBy(v => v).ToList();
        var steadySkill = skillSamples.Where(s => s.ObservedAt >= steadyAfter).Select(s => s.Milliseconds).OrderBy(v => v).ToList();
        return new ConsistencyReport
        {
            Issues = issues,
            PairwiseObserversCompared = pairs,
            BroadcastLatencyP50Ms = Percentile(latencies, 0.5),
            BroadcastLatencyP95Ms = Percentile(latencies, 0.95),
            BroadcastLatencyP99Ms = Percentile(latencies, 0.99),
            BroadcastLatencyMaxMs = latencies.Count > 0 ? latencies[^1] : 0,
            SteadyBroadcastLatencyP99Ms = Percentile(steady, 0.99),
            SteadyBroadcastLatencyMaxMs = steady.Count > 0 ? steady[^1] : 0,
            SteadyAttackFanoutP99EstimateMs = Percentile(steadyAttack, 0.99),
            SteadySkillFanoutP99EstimateMs = Percentile(steadySkill, 0.99),
            AttackSampleCount = bots.Sum(b => b.ObservationLog.Tracker.AttackSampleCount),
            SkillSampleCount = bots.Sum(b => b.ObservationLog.Tracker.SkillSampleCount),
            AttackTimeoutCount = bots.Sum(b => b.ObservationLog.Tracker.AttackTimeoutCount),
            SkillTimeoutCount = bots.Sum(b => b.ObservationLog.Tracker.SkillTimeoutCount)
        };
    }

    static void CheckCombatFanout(FineageBotClient observer, List<BotRosterEntry> roster, int watch, List<ConsistencyIssue> issues)
    {
        int combatIssues = issues.Count(i => i.Kind == ConsistencyKind.MissingCombatFanout);
        if (combatIssues >= 25)
            return;

        var events = new List<(DateTimeOffset At, ulong Attacker)>();
        foreach (var frame in observer.ObservationLog.Snapshot())
        {
            if (frame.Opcode != NetOpcode.AttackState)
                continue;
            var a = NetCodec.DecodeAttackState(frame.Payload);
            if (a.Damage == 0 && a.Flags != 0)
                continue;
            events.Add((frame.At, a.AttackerValue));
            if (events.Count >= 40)
                break;
        }

        foreach (var (at, attackerVal) in events)
        {
            int attackerOid = (int)attackerVal;
            var attackerEntry = roster.FirstOrDefault(r => r.ObjectId == attackerOid);
            if (attackerEntry == null)
                continue;

            foreach (var w in roster.Where(r => r.BotId != observer.BotId))
            {
                if (Distance(w.X, w.Y, attackerEntry.X, attackerEntry.Y) > watch)
                    continue;
                bool saw = w.Client.ObservationLog.Snapshot().Any(f =>
                {
                    if (f.Opcode != NetOpcode.AttackState)
                        return false;
                    if (NetCodec.DecodeAttackState(f.Payload).AttackerValue != attackerVal)
                        return false;
                    return Math.Abs((f.At - at).TotalMilliseconds) <= 2500;
                });
                if (!saw)
                {
                    issues.Add(new ConsistencyIssue
                    {
                        Kind = ConsistencyKind.MissingCombatFanout,
                        ObserverBotId = w.BotId,
                        SubjectObjectId = attackerOid,
                        Detail = $"no AttackState for attacker {attackerOid} within 2.5s"
                    });
                    combatIssues++;
                    if (combatIssues >= 25)
                        return;
                }
            }
        }
    }

    static void CheckVisibilityLeaks(IReadOnlyList<FineageBotClient> bots, List<BotRosterEntry> roster, int watch, List<ConsistencyIssue> issues)
    {
        foreach (var bot in bots)
        {
            var entry = roster.FirstOrDefault(r => r.BotId == bot.BotId);
            if (entry == null)
                continue;
            foreach (var frame in bot.ObservationLog.Snapshot())
            {
                if (frame.Opcode != NetOpcode.AttackState && frame.Opcode != NetOpcode.MagicSkillUse)
                    continue;
                int actorOid = frame.Opcode == NetOpcode.AttackState
                    ? (int)NetCodec.DecodeAttackState(frame.Payload).AttackerValue
                    : NetCodec.DecodeMagicSkillUse(frame.Payload).CasterObjectId;
                var actor = roster.FirstOrDefault(r => r.ObjectId == actorOid);
                if (actor == null)
                    continue;
                int dist = Distance(entry.X, entry.Y, actor.X, actor.Y);
                if (dist > watch * 2)
                {
                    issues.Add(new ConsistencyIssue
                    {
                        Kind = ConsistencyKind.VisibilityLeak,
                        ObserverBotId = bot.BotId,
                        SubjectObjectId = actorOid,
                        Detail = $"{frame.Opcode} from distance {dist} (> {watch * 2})"
                    });
                }
            }
        }
    }

    static List<BotRosterEntry> BuildRoster(IReadOnlyList<FineageBotClient> bots, LoadScenario scenario)
    {
        var list = new List<BotRosterEntry>();
        foreach (var bot in bots)
        {
            if (!bot.Stats.InWorld || bot.Stats.Disconnected || bot.SelfObjectId == 0)
                continue;
            if (bot.Stats.LastActionAt is null || bot.Stats.LastActionAt < DateTimeOffset.UtcNow - TimeSpan.FromSeconds(45))
                continue;
            var (x, y, z) = BotSpawn.Position(scenario, bot.BotId);
            var tracker = bot.ObservationLog.Tracker;
            if (tracker.Entities.TryGetValue((ulong)bot.SelfObjectId, out var self))
            {
                x = self.X > 0 ? self.X : x;
                y = self.Y > 0 ? self.Y : y;
            }

            list.Add(new BotRosterEntry(bot.BotId, bot.SelfObjectId, x, y, z, bot, tracker));
        }

        return list;
    }

    static bool TryPeerTruth(BotRosterEntry peer, out int x, out int y, out int hp)
    {
        if (peer.Tracker.Entities.TryGetValue((ulong)peer.ObjectId, out var self))
        {
            x = self.X;
            y = self.Y;
            hp = self.CurHp;
            return true;
        }

        x = peer.X;
        y = peer.Y;
        hp = 0;
        return true;
    }

    public static bool IsHardFailure(ConsistencyKind kind) => kind is
        ConsistencyKind.GhostEntity or
        ConsistencyKind.MissingSpawn or
        ConsistencyKind.OutOfOrderLifecycle or
        ConsistencyKind.DuplicateSpawn or
        ConsistencyKind.VisibilityLeak or
        ConsistencyKind.MissingCombatFanout;

    static int Distance(int x0, int y0, int x1, int y1)
    {
        long dx = x1 - x0;
        long dy = y1 - y0;
        return (int)Math.Sqrt(dx * dx + dy * dy);
    }

    static double Percentile(IReadOnlyList<double> sortedSource, double q)
    {
        if (sortedSource.Count == 0)
            return 0;
        var sorted = sortedSource.OrderBy(v => v).ToList();
        int idx = (int)Math.Ceiling(q * sorted.Count) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
    }

    sealed record BotRosterEntry(
        int BotId,
        int ObjectId,
        int X,
        int Y,
        int Z,
        FineageBotClient Client,
        BotObservationLog.EntityTracker Tracker);
}