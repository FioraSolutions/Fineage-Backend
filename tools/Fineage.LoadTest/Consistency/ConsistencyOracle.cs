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
                    if (!TryPeerTruth(b, out int truthX, out int truthY, out int truthHp, out DateTimeOffset truthAt))
                        continue;
                    if (a.Tracker.Entities.TryGetValue((ulong)b.ObjectId, out var viewB))
                    {
                        if ((Math.Abs(viewB.X - truthX) > PositionTolerance || Math.Abs(viewB.Y - truthY) > PositionTolerance) &&
                            GroundIsNotOlderThanView(truthAt, viewB.PositionUpdatedAt))
                        {
                            issues.Add(new ConsistencyIssue
                            {
                                Kind = ConsistencyKind.StalePosition,
                                ObserverBotId = a.BotId,
                                OtherBotId = b.BotId,
                                SubjectObjectId = b.ObjectId,
                                Detail = $"peer view ({viewB.X},{viewB.Y}) vs ground ({truthX},{truthY})",
                                ObservedAt = viewB.PositionUpdatedAt != default ? viewB.PositionUpdatedAt : null
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

                    if (!TryPeerTruth(a, out truthX, out truthY, out truthHp, out truthAt))
                        continue;
                    if (b.Tracker.Entities.TryGetValue((ulong)a.ObjectId, out var viewA))
                    {
                        if ((Math.Abs(viewA.X - truthX) > PositionTolerance || Math.Abs(viewA.Y - truthY) > PositionTolerance) &&
                            GroundIsNotOlderThanView(truthAt, viewA.PositionUpdatedAt))
                        {
                            issues.Add(new ConsistencyIssue
                            {
                                Kind = ConsistencyKind.StalePosition,
                                ObserverBotId = b.BotId,
                                OtherBotId = a.BotId,
                                SubjectObjectId = a.ObjectId,
                                Detail = $"peer view ({viewA.X},{viewA.Y}) vs ground ({truthX},{truthY})",
                                ObservedAt = viewA.PositionUpdatedAt != default ? viewA.PositionUpdatedAt : null
                            });
                        }
                    }
                }
            }
        }

        // One pass over each bot's frame log. The combat check used to call Snapshot()
        // (a full ToArray) from inside the per-watcher scan, which allocated until the
        // report never got written after a 200-bot run.
        var combat = IndexCombat(bots);
        // Positions at the time of each event: economy bots teleport hub/warehouse/manor/arena, so the end-of-run
        // position says nothing about whether a watcher was in range when an early attack happened.
        var timelines = bots.ToDictionary(b => b.BotId, b => PositionTimeline.From(b.ObservationLog.Snapshot()));

        foreach (var bot in bots)
            CheckCombatFanout(bot, roster, watch, issues, combat, timelines);
        CheckVisibilityLeaks(bots, roster, watch, issues, combat, timelines);

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
            SkillTimeoutCount = bots.Sum(b => b.ObservationLog.Tracker.SkillTimeoutCount),
            BotKillCount = bots.Sum(b => b.ObservationLog.Tracker.KillsBySelf)
        };
    }

    /// <summary>Attack and skill sightings decoded once per bot. Timestamps stay in log order.</summary>
    sealed class CombatIndex
    {
        public List<(DateTimeOffset At, ulong Attacker)> Attacks { get; } = new();
        public Dictionary<ulong, List<DateTimeOffset>> AttacksByAttacker { get; } = new();
        public List<(DateTimeOffset At, int Actor, ushort Opcode)> Sightings { get; } = new();
    }

    static Dictionary<int, CombatIndex> IndexCombat(IReadOnlyList<FineageBotClient> bots)
    {
        var index = new Dictionary<int, CombatIndex>(bots.Count);
        foreach (var bot in bots)
        {
            var ix = new CombatIndex();
            foreach (var frame in bot.ObservationLog.Snapshot())
            {
                if (frame.Opcode == NetOpcode.AttackState)
                {
                    var attack = NetCodec.DecodeAttackState(frame.Payload);
                    if (attack.Damage == 0 && attack.Flags != 0)
                        continue;
                    ix.Attacks.Add((frame.At, attack.AttackerValue));
                    if (!ix.AttacksByAttacker.TryGetValue(attack.AttackerValue, out var times))
                    {
                        times = new List<DateTimeOffset>();
                        ix.AttacksByAttacker[attack.AttackerValue] = times;
                    }

                    times.Add(frame.At);
                    ix.Sightings.Add((frame.At, (int)attack.AttackerValue, frame.Opcode));
                }
                else if (frame.Opcode == NetOpcode.MagicSkillUse)
                {
                    ix.Sightings.Add((frame.At, NetCodec.DecodeMagicSkillUse(frame.Payload).CasterObjectId, frame.Opcode));
                }
            }

            index[bot.BotId] = ix;
        }

        return index;
    }

    static bool SawAttackNear(List<DateTimeOffset> times, DateTimeOffset at)
    {
        long window = TimeSpan.FromMilliseconds(2500).Ticks;
        long target = at.UtcTicks;
        int lo = 0;
        int hi = times.Count - 1;
        int lower = times.Count;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (times[mid].UtcTicks >= target - window)
            {
                lower = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return lower < times.Count && times[lower].UtcTicks <= target + window;
    }

    static void CheckCombatFanout(
        FineageBotClient observer,
        List<BotRosterEntry> roster,
        int watch,
        List<ConsistencyIssue> issues,
        Dictionary<int, CombatIndex> combat,
        Dictionary<int, PositionTimeline> timelines)
    {
        var window = TimeSpan.FromMilliseconds(2500);
        int combatIssues = issues.Count(i => i.Kind == ConsistencyKind.MissingCombatFanout);
        if (combatIssues >= 25)
            return;

        var attacks = combat[observer.BotId].Attacks;
        int eventCount = Math.Min(40, attacks.Count);
        for (int e = 0; e < eventCount; e++)
        {
            var (at, attackerVal) = attacks[e];
            int attackerOid = (int)attackerVal;
            var attackerEntry = roster.FirstOrDefault(r => r.ObjectId == attackerOid);
            if (attackerEntry == null)
                continue;

            foreach (var w in roster)
            {
                if (w.BotId == observer.BotId)
                    continue;
                // In range at the attack and still in range when the window closes (no teleport out meanwhile).
                var (ax, ay) = PositionAt(timelines[attackerEntry.BotId], at, attackerEntry);
                var (wx, wy) = PositionAt(timelines[w.BotId], at, w);
                var (wx2, wy2) = PositionAt(timelines[w.BotId], at + window, w);
                if (Distance(wx, wy, ax, ay) > watch || Distance(wx2, wy2, ax, ay) > watch)
                    continue;
                bool saw = combat[w.BotId].AttacksByAttacker.TryGetValue(attackerVal, out var times)
                    && SawAttackNear(times, at);
                if (!saw)
                {
                    issues.Add(new ConsistencyIssue
                    {
                        Kind = ConsistencyKind.MissingCombatFanout,
                        ObserverBotId = w.BotId,
                        SubjectObjectId = attackerOid,
                        Detail = $"no AttackState for attacker {attackerOid} within 2.5s",
                        ObservedAt = at
                    });
                    combatIssues++;
                    if (combatIssues >= 25)
                        return;
                }
            }
        }
    }

    static void CheckVisibilityLeaks(
        IReadOnlyList<FineageBotClient> bots,
        List<BotRosterEntry> roster,
        int watch,
        List<ConsistencyIssue> issues,
        Dictionary<int, CombatIndex> combat,
        Dictionary<int, PositionTimeline> timelines)
    {
        // Positions at the time of each sighting: a bot that teleported town to town saw combat
        // in the town it was in then, not where it ended the run.
        foreach (var bot in bots)
        {
            var entry = roster.FirstOrDefault(r => r.BotId == bot.BotId);
            if (entry == null)
                continue;
            foreach (var (at, actorOid, opcode) in combat[bot.BotId].Sightings)
            {
                var actor = roster.FirstOrDefault(r => r.ObjectId == actorOid);
                if (actor == null)
                    continue;
                var (ox, oy) = PositionAt(timelines[bot.BotId], at, entry);
                var (ax, ay) = PositionAt(timelines[actor.BotId], at, actor);
                int dist = Distance(ox, oy, ax, ay);
                if (dist > watch * 2)
                {
                    issues.Add(new ConsistencyIssue
                    {
                        Kind = ConsistencyKind.VisibilityLeak,
                        ObserverBotId = bot.BotId,
                        SubjectObjectId = actorOid,
                        Detail = $"{opcode} from distance {dist} (> {watch * 2})",
                        ObservedAt = at
                    });
                }
            }
        }
    }

    static (int X, int Y) PositionAt(PositionTimeline timeline, DateTimeOffset at, BotRosterEntry fallback) =>
        timeline.TryAt(at, out int x, out int y) ? (x, y) : (fallback.X, fallback.Y);

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
            // The self pawn the server last placed (spawn, move, teleport) is the truth; the layout
            // slot is only for a bot that never saw itself. West of x=0 (Talking Island, Gludin) is a real position.
            if (tracker.Entities.TryGetValue((ulong)bot.SelfObjectId, out var self))
            {
                x = self.X;
                y = self.Y;
            }

            list.Add(new BotRosterEntry(bot.BotId, bot.SelfObjectId, x, y, z, bot, tracker));
        }

        return list;
    }

    static bool TryPeerTruth(BotRosterEntry peer, out int x, out int y, out int hp, out DateTimeOffset updatedAt)
    {
        if (peer.Tracker.Entities.TryGetValue((ulong)peer.ObjectId, out var self))
        {
            x = self.X;
            y = self.Y;
            hp = self.CurHp;
            updatedAt = self.PositionUpdatedAt;
            return true;
        }

        x = peer.X;
        y = peer.Y;
        hp = 0;
        updatedAt = default;
        return true;
    }

    /// <summary>
    /// A peer view newer than the subject's own last MoveState means the *ground* is stale, not the observer.
    /// The soak's 198 Bot74 errors were exactly that: the subject's self state was 3m49s older than every peer view.
    /// Unknown timestamps keep the old comparison so a genuinely stale observer is still reported.
    /// </summary>
    static bool GroundIsNotOlderThanView(DateTimeOffset groundAt, DateTimeOffset viewAt) =>
        groundAt == default || viewAt == default || groundAt >= viewAt;

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