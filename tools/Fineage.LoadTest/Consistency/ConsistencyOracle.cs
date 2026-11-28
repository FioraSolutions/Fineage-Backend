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
    /// <summary>
    /// Mobius <c>broadcastUserInfo</c> sends the owner's UserInfo and the peer's CharInfo from the same simulation step.
    /// A repeated player EntitySpawn is a presence refresh only when the subject's own log carries a newer UserInfo
    /// inside this window; a lifecycle re-add from knownlist/enter has no owner UserInfo. The window absorbs two
    /// socket lanes.
    /// </summary>
    public const int PresenceRefreshWindowMs = 500;
    /// <summary>
    /// Bounded fan-out latency used to decide whether two samples describe the same instant. A position seen
    /// minutes apart is not evidence of divergence: the pawn may have crossed several AOI cells in between.
    /// This is a causal-comparison window, not a position tolerance; distances still use <see cref="PositionTolerance"/>.
    /// </summary>
    public const int ComparableWindowMs = 2500;

    public static ConsistencyReport Analyze(
        IReadOnlyList<FineageBotClient> bots,
        LoadScenario scenario,
        int aoiCellSize = AoiCellSize,
        DateTimeOffset? runStarted = null)
    {
        DateTimeOffset steadyAfter = runStarted ?? DateTimeOffset.MinValue;
        DateTimeOffset analysisNow = DateTimeOffset.UtcNow;
        int watch = aoiCellSize * 2;
        var issues = new List<ConsistencyIssue>();
        var roster = BuildRoster(bots, scenario);
        var selfUserInfo = BuildSelfUserInfoIndex(bots);
        // Every pairwise comparison below uses these self timelines, so both sides speak about the same instant.
        var timelines = bots.ToDictionary(b => b.BotId, b => PositionTimeline.From(b.ObservationLog.Snapshot()));
        // Combat events happen throughout the run, so liveness must come from the self frames at that instant,
        // not from the end-of-run pawn state.
        var liveness = bots.ToDictionary(
            b => b.BotId,
            b => LivenessTimeline.From(b.ObservationLog.Snapshot(), (ulong)b.SelfObjectId));

        foreach (var bot in bots)
        {
            var tracker = bot.ObservationLog.Tracker;
            var lifecycleRuns = CountLifecycleRuns(tracker.SpawnObservations, selfUserInfo);
            foreach (var (key, ent) in tracker.Entities)
            {
                if (ent.SpawnsWithoutDespawn > 0 &&
                    lifecycleRuns.TryGetValue(key, out var run) &&
                    run.LifecycleSpawns > 3)
                {
                    issues.Add(new ConsistencyIssue
                    {
                        Kind = ConsistencyKind.DuplicateSpawn,
                        ObserverBotId = bot.BotId,
                        SubjectObjectId = ent.ObjectId,
                        Detail = run.PresenceRefreshes > 0
                            ? $"entity {key} spawned {run.LifecycleSpawns} lifecycle times without an intervening despawn ({run.PresenceRefreshes} CharInfo refreshes)"
                            : $"entity {key} spawned {run.LifecycleSpawns} times without an intervening despawn"
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
                // Both endpoints must be sampled at one common instant. Comparing A's newest frame with B's
                // newest frame invents pairs when one bot has already moved away from the other's last update.
                if (!TryPairPositionsAtCommonTime(timelines[a.BotId], timelines[b.BotId], a, b,
                        out int ax, out int ay, out int bx, out int by, out DateTimeOffset pairAt))
                    continue;
                int dist = Distance(ax, ay, bx, by);
                // Presence is not a Euclidean radius: GridAoi.GetVisible returns the 3x3 cells around the observer's
                // own cell. A pair can be 3000..6000 apart across two cell edges (not visible), and two diagonal
                // neighbours can be >6000 apart but still visible. Both cases were false pair decisions at 6000.
                bool shouldSee = InGridWatch(ax, ay, bx, by, aoiCellSize);
                if (!shouldSee)
                    continue;

                bool aSeesB = a.Tracker.Entities.ContainsKey((ulong)b.ObjectId);
                bool bSeesA = b.Tracker.Entities.ContainsKey((ulong)a.ObjectId);
                bool aLive = IsLive(a);
                bool bLive = IsLive(b);
                if (!aLive && !bLive)
                    continue;
                pairs++; // one pair observation is meaningful when at least one side is live

                if (aLive && !aSeesB &&
                    HasComparableWatchEvidence(timelines[a.BotId], timelines[b.BotId],
                        liveness[a.BotId], liveness[b.BotId], a, b, pairAt, aoiCellSize))
                {
                    issues.Add(new ConsistencyIssue
                    {
                        Kind = ConsistencyKind.MissingSpawn,
                        ObserverBotId = a.BotId,
                        OtherBotId = b.BotId,
                        SubjectObjectId = b.ObjectId,
                        Detail = $"distance {dist} ≤ {watch}: missing peer spawn",
                        ObservedAt = pairAt != default ? pairAt : null
                    });
                }

                if (bLive && !bSeesA &&
                    HasComparableWatchEvidence(timelines[b.BotId], timelines[a.BotId],
                        liveness[b.BotId], liveness[a.BotId], b, a, pairAt, aoiCellSize))
                {
                    issues.Add(new ConsistencyIssue
                    {
                        Kind = ConsistencyKind.MissingSpawn,
                        ObserverBotId = b.BotId,
                        OtherBotId = a.BotId,
                        SubjectObjectId = a.ObjectId,
                        Detail = $"distance {dist} ≤ {watch}: missing peer spawn",
                        ObservedAt = pairAt != default ? pairAt : null
                    });
                }

                if (aLive && bLive && aSeesB && bSeesA)
                {
                    if (TryPeerTruth(b, out _, out _, out _, out _) &&
                        a.Tracker.Entities.TryGetValue((ulong)b.ObjectId, out var viewB))
                    {
                        if (!DiedAtOrAfter(b, viewB.PositionUpdatedAt) &&
                            TryStalePeerView(a, b, timelines[a.BotId], timelines[b.BotId], viewB, aoiCellSize,
                                out int truthX, out int truthY))
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

                        // Compare the observer's timestamped bar with the latest HP state the server was
                        // expected to fan out, not with the subject's end-of-run self HP. A level-up/kill
                        // reward is owner-only and does not create an expected peer state.
                        if (!DiedAtOrAfter(b, viewB.HpUpdatedAt) &&
                            TryExpectedPeerHp(b, out int expectedHp, out DateTimeOffset expectedAt) &&
                            expectedAt > viewB.HpUpdatedAt &&
                            analysisNow - expectedAt >= TimeSpan.FromMilliseconds(ComparableWindowMs) &&
                            expectedHp > 0 && viewB.CurHp > 0 &&
                            Math.Abs(viewB.CurHp - expectedHp) > Math.Max(50, expectedHp / 10))
                        {
                            issues.Add(new ConsistencyIssue
                            {
                                Kind = ConsistencyKind.HpDesync,
                                ObserverBotId = a.BotId,
                                OtherBotId = b.BotId,
                                SubjectObjectId = b.ObjectId,
                                Detail = $"HP view {viewB.CurHp} vs expected {expectedHp}",
                                ObservedAt = expectedAt
                            });
                        }
                    }

                    if (TryPeerTruth(a, out _, out _, out _, out _) &&
                        b.Tracker.Entities.TryGetValue((ulong)a.ObjectId, out var viewA))
                    {
                        if (!DiedAtOrAfter(a, viewA.PositionUpdatedAt) &&
                            TryStalePeerView(b, a, timelines[b.BotId], timelines[a.BotId], viewA, aoiCellSize,
                                out int truthX, out int truthY))
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

        foreach (var bot in bots)
            CheckCombatFanout(bot, roster, aoiCellSize, issues, combat, timelines, liveness);
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

    /// <summary>
    /// Owner-facing UserInfo timestamps keyed by entity. Every current FanoutCharInfo site is a Mobius
    /// <c>broadcastUserInfo</c>: the subject receives UserInfo and the peers receive EntitySpawn in the same step.
    /// The oracle uses that existing pairing instead of inventing a heuristic payload signature.
    /// </summary>
    static Dictionary<int, List<DateTimeOffset>> BuildSelfUserInfoIndex(IReadOnlyList<FineageBotClient> bots)
    {
        var index = new Dictionary<int, List<DateTimeOffset>>();
        foreach (var bot in bots)
        {
            foreach (var frame in bot.ObservationLog.Snapshot())
            {
                if (frame.Opcode != NetOpcode.UserInfo)
                    continue;
                var info = NetCodec.DecodeUserInfo(frame.Payload);
                int id = (int)info.EntityValue;
                if (id <= 0)
                    continue;
                if (!index.TryGetValue(id, out var times))
                    index[id] = times = [];
                times.Add(frame.At);
            }
        }

        return index;
    }

    /// <summary>
    /// Replays a bot's spawn observations into the final open run per entity. Only spawns that are not explained by
    /// a newer owner UserInfo count as lifecycle spawns; the first spawn of a run always does, so an initial spawn
    /// can never disappear from the gate.
    /// </summary>
    static Dictionary<ulong, LifecycleRun> CountLifecycleRuns(
        IReadOnlyList<BotObservationLog.EntityTracker.SpawnObservation> observations,
        Dictionary<int, List<DateTimeOffset>> selfUserInfo)
    {
        var runs = new Dictionary<ulong, LifecycleRun>();
        var generation = new Dictionary<ulong, uint>();
        foreach (var obs in observations)
        {
            if (obs.OpenedLife || !runs.TryGetValue(obs.EntityValue, out var run))
            {
                runs[obs.EntityValue] = new LifecycleRun(1, 0, obs.At);
                generation[obs.EntityValue] = obs.EntityGeneration;
                continue;
            }

            bool refresh = obs.EntityGeneration == generation[obs.EntityValue]
                && IsPresenceRefresh(obs, run.LastSpawnAt, selfUserInfo);
            runs[obs.EntityValue] = refresh
                ? run with { PresenceRefreshes = run.PresenceRefreshes + 1, LastSpawnAt = obs.At }
                : run with { LifecycleSpawns = run.LifecycleSpawns + 1, LastSpawnAt = obs.At };
            generation[obs.EntityValue] = obs.EntityGeneration;
        }

        return runs;
    }

    static bool IsPresenceRefresh(
        in BotObservationLog.EntityTracker.SpawnObservation obs,
        DateTimeOffset previousSpawnAt,
        Dictionary<int, List<DateTimeOffset>> selfUserInfo)
    {
        // PrepareCharInfo rebuilds a remote Player pawn (isSelf=false) and never an NPC. A self spawn stays a
        // lifecycle marker even when the owner also receives a char-info copy.
        if (obs.IsSelf || obs.Kind != EntityKindNet.Player)
            return false;
        return selfUserInfo.TryGetValue(obs.ObjectId, out var times)
            && HasTimestampBetween(times, previousSpawnAt, obs.At, TimeSpan.FromMilliseconds(PresenceRefreshWindowMs));
    }

    /// <summary>
    /// A refresh needs a new owner UserInfo since the previous spawn. Using the previous spawn as the lower bound
    /// keeps the enter UserInfo from legitimising a duplicate Enter/knownlist spawn that arrives in the same burst.
    /// </summary>
    static bool HasTimestampBetween(List<DateTimeOffset> sorted, DateTimeOffset after, DateTimeOffset at, TimeSpan window)
    {
        long low = Math.Max(after.UtcTicks + 1, at.UtcTicks - window.Ticks);
        long high = at.UtcTicks + window.Ticks;
        int lo = 0;
        int hi = sorted.Count - 1;
        int first = sorted.Count;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (sorted[mid].UtcTicks >= low)
            {
                first = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return first < sorted.Count && sorted[first].UtcTicks <= high;
    }

    sealed record LifecycleRun(int LifecycleSpawns, int PresenceRefreshes, DateTimeOffset LastSpawnAt);

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
        int cellSize,
        List<ConsistencyIssue> issues,
        Dictionary<int, CombatIndex> combat,
        Dictionary<int, PositionTimeline> timelines,
        Dictionary<int, LivenessTimeline> liveness)
    {
        var window = TimeSpan.FromMilliseconds(ComparableWindowMs);
        int combatIssues = issues.Count(i => i.Kind == ConsistencyKind.MissingCombatFanout);
        if (combatIssues >= 25)
            return;

        var observerEntry = roster.FirstOrDefault(r => r.BotId == observer.BotId);
        if (observerEntry == null)
            return;

        var attacks = combat[observer.BotId].Attacks;
        int eventCount = Math.Min(40, attacks.Count);
        for (int e = 0; e < eventCount; e++)
        {
            var (at, attackerVal) = attacks[e];
            // A sighting is evidence only while the bot that received it was alive. Its end-of-run pawn state
            // says nothing about an attack observed earlier (or about a watcher that died later).
            if (!IsLiveAt(liveness[observer.BotId], observerEntry, at))
                continue;

            int attackerOid = (int)attackerVal;
            var attackerEntry = roster.FirstOrDefault(r => r.ObjectId == attackerOid);
            if (attackerEntry == null)
                continue;

            foreach (var w in roster)
            {
                if (w.BotId == observer.BotId)
                    continue;
                if (!IsLiveAt(liveness[w.BotId], w, at))
                    continue;
                // Presence is the exact GridAoi 3x3 footprint, and a watcher dragged out of the footprint before
                // the bounded delivery closes is not a missing fan-out. Both sides move, so evaluate every self
                // sample in the interval, not only the two endpoints.
                if (!VisibleThroughout(timelines[attackerEntry.BotId], timelines[w.BotId],
                        attackerEntry, w, at, window, cellSize))
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

    /// <summary>
    /// Whether the attacker and watcher share the GridAoi 3x3 watch at the attack and at every self-position sample
    /// through the bounded delivery interval. The interval closes at <paramref name="at"/> + window even when the
    /// next sample arrives later, so a watcher dragged out by a teleport cannot hide behind a sparse timeline.
    /// </summary>
    static bool VisibleThroughout(
        PositionTimeline attackerTimeline,
        PositionTimeline watcherTimeline,
        BotRosterEntry attacker,
        BotRosterEntry watcher,
        DateTimeOffset at,
        TimeSpan window,
        int cellSize)
    {
        if (!VisibleAt(attackerTimeline, watcherTimeline, attacker, watcher, at, cellSize))
            return false;

        DateTimeOffset end = at + window;
        if (!VisibleAt(attackerTimeline, watcherTimeline, attacker, watcher, end, cellSize))
            return false;

        foreach (var point in attackerTimeline.Points)
        {
            if (point.At > at && point.At < end &&
                !VisibleAt(attackerTimeline, watcherTimeline, attacker, watcher, point.At, cellSize))
                return false;
        }

        foreach (var point in watcherTimeline.Points)
        {
            if (point.At > at && point.At < end &&
                !VisibleAt(attackerTimeline, watcherTimeline, attacker, watcher, point.At, cellSize))
                return false;
        }

        return true;
    }

    static bool VisibleAt(
        PositionTimeline attackerTimeline,
        PositionTimeline watcherTimeline,
        BotRosterEntry attacker,
        BotRosterEntry watcher,
        DateTimeOffset at,
        int cellSize)
    {
        var (ax, ay) = PositionAt(attackerTimeline, at, attacker);
        var (wx, wy) = PositionAt(watcherTimeline, at, watcher);
        return InGridWatch(wx, wy, ax, ay, cellSize);
    }

    /// <summary>
    /// Historical liveness for an event earlier in the run. A bot with no self liveness frame is only judged by
    /// end state when the log carried no liveness evidence at all; a log whose first self event is later means the
    /// pawn did not exist at the event time.
    /// </summary>
    static bool IsLiveAt(LivenessTimeline timeline, BotRosterEntry entry, DateTimeOffset at)
    {
        if (timeline.TryAt(at, out bool alive))
            return alive;
        return timeline.Count == 0 && IsLive(entry);
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

    static bool IsLive(BotRosterEntry entry) =>
        entry.Tracker.Entities.TryGetValue((ulong)entry.ObjectId, out var self) &&
        !self.Despawned && self.Alive && self.CurHp > 0;

    /// <summary>
    /// True when the subject died at or after the observer's view. The observer's historical view must not be
    /// compared with a post-death/revive ground; live disagreements without a death keep the old check.
    /// </summary>
    static bool DiedAtOrAfter(BotRosterEntry subject, DateTimeOffset viewAt) =>
        subject.Tracker.Entities.TryGetValue((ulong)subject.ObjectId, out var self) &&
        self.LastDeathAt is DateTimeOffset diedAt &&
        (viewAt == default || diedAt >= viewAt);

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
    /// The latest HP state the server was expected to send to peers, timestamped. Owner-only kill/level updates
    /// advance <see cref="BotObservationLog.TrackedEntity.CurHp"/> but not this state.
    /// </summary>
    static bool TryExpectedPeerHp(BotRosterEntry peer, out int hp, out DateTimeOffset at)
    {
        if (peer.Tracker.Entities.TryGetValue((ulong)peer.ObjectId, out var self) &&
            self.ExpectedPeerHpAt != default)
        {
            hp = self.ExpectedPeerHp;
            at = self.ExpectedPeerHpAt;
            return true;
        }

        hp = 0;
        at = default;
        return false;
    }

    /// <summary>
    /// Selects one instant both timelines can answer and returns both positions at that instant. The newest
    /// sample of either side is the reference so an old stationary observer still has a valid position; the
    /// missing-spawn check separately requires both sides live and fresh at that instant.
    /// </summary>
    static bool TryPairPositionsAtCommonTime(
        PositionTimeline aTimeline,
        PositionTimeline bTimeline,
        BotRosterEntry a,
        BotRosterEntry b,
        out int ax,
        out int ay,
        out int bx,
        out int by,
        out DateTimeOffset at)
    {
        ax = a.X;
        ay = a.Y;
        bx = b.X;
        by = b.Y;
        at = default;
        DateTimeOffset? aLast = aTimeline.LastAt;
        DateTimeOffset? bLast = bTimeline.LastAt;
        if (aLast is null || bLast is null)
            return true; // a bot whose self spawn was never observed keeps the roster position.

        DateTimeOffset latest = aLast.Value.UtcTicks >= bLast.Value.UtcTicks ? aLast.Value : bLast.Value;
        if (!aTimeline.TryAt(latest, out ax, out ay) || !bTimeline.TryAt(latest, out bx, out by))
            return false;
        at = latest;
        return true;
    }

    /// <summary>
    /// A missing spawn needs one live common instant where both self timelines are fresh and the pair is inside
    /// the same 3x3 watch. It must not be inferred from current membership plus a co-visible moment minutes ago:
    /// that is the long-soak case where a peer's self timeline ended while the observer kept moving.
    /// </summary>
    static bool HasComparableWatchEvidence(
        PositionTimeline observerTimeline,
        PositionTimeline subjectTimeline,
        LivenessTimeline observerLiveness,
        LivenessTimeline subjectLiveness,
        BotRosterEntry observer,
        BotRosterEntry subject,
        DateTimeOffset at,
        int cellSize)
    {
        if (at == default)
            return false;
        if (!IsLiveAt(observerLiveness, observer, at) || !IsLiveAt(subjectLiveness, subject, at))
            return false;
        long window = TimeSpan.FromMilliseconds(ComparableWindowMs).Ticks;
        if (!ObserverFreshAt(observerTimeline, at, window) || !FreshAt(subjectTimeline, at, window))
            return false;
        return PairVisibleAt(observerTimeline, subjectTimeline, at, observer, subject, cellSize);
    }

    /// <summary>
    /// An observer whose only self sample is its spawn has not moved: that origin remains its current position.
    /// Once the observer moved, an old last sample is an unobserved interval and cannot anchor a pair.
    /// </summary>
    static bool ObserverFreshAt(PositionTimeline timeline, DateTimeOffset at, long windowTicks) =>
        timeline.Count <= 1 ||
        (timeline.TryAt(at, out _, out _, out DateTimeOffset sampleAt) &&
         at.UtcTicks - sampleAt.UtcTicks <= windowTicks);

    /// <summary>
    /// The subject must have a self sample inside the window regardless of count. Its last origin is the pair
    /// ground, so a subject that stopped minutes ago must not be paired with the observer's current position.
    /// </summary>
    static bool FreshAt(PositionTimeline timeline, DateTimeOffset at, long windowTicks) =>
        timeline.TryAt(at, out _, out _, out DateTimeOffset sampleAt) &&
        at.UtcTicks - sampleAt.UtcTicks <= windowTicks;

    /// <summary>
    /// A peer's remembered position is stale only while the subject stayed visible from that view onward.
    /// Moving legs are asynchronous: a view captured at one leg is still the correct ground for that instant
    /// even when the subject's current origin is the next leg. If the pair left visibility in between, the old
    /// view belongs to a closed visibility interval and is not compared with a later origin.
    /// </summary>
    static bool TryStalePeerView(
        BotRosterEntry observer,
        BotRosterEntry subject,
        PositionTimeline observerTimeline,
        PositionTimeline subjectTimeline,
        BotObservationLog.TrackedEntity view,
        int cellSize,
        out int groundX,
        out int groundY)
    {
        groundX = subject.X;
        groundY = subject.Y;
        // A despawn closes the view; a later origin belongs to a new visibility interval, not a frozen view.
        if (view.Despawned)
            return false;

        DateTimeOffset viewAt = view.PositionUpdatedAt;
        if (viewAt == default || subjectTimeline.Count == 0)
        {
            // No timestamps: retain the end-state comparison so a genuinely frozen watcher is still reported.
            return PairVisibleAt(observerTimeline, subjectTimeline, viewAt, observer, subject, cellSize) &&
                   (Math.Abs(view.X - groundX) > PositionTolerance || Math.Abs(view.Y - groundY) > PositionTolerance);
        }

        long window = TimeSpan.FromMilliseconds(ComparableWindowMs).Ticks;
        DateTimeOffset subjectLast = subjectTimeline.LastAt!.Value;
        // The subject's own last position is older than the observer's view, so the *ground* is stale.
        // Comparing against it was the Bot74 artifact (self state 3m49s behind every peer view).
        if (subjectLast.UtcTicks < viewAt.UtcTicks - window)
            return false;

        if (!subjectTimeline.TryAt(viewAt, out groundX, out groundY, out _))
            return false;

        bool visibleAtView = PairVisibleAt(observerTimeline, subjectTimeline, viewAt, observer, subject, cellSize);
        if (visibleAtView &&
            (Math.Abs(view.X - groundX) > PositionTolerance || Math.Abs(view.Y - groundY) > PositionTolerance) &&
            !SubjectReachesViewNext(subjectTimeline, view, viewAt, window))
            return true;

        // A MoveState view is the origin of a movement turn the observer received. The next origin in the subject
        // timeline is the next turn, so comparing it as a continuous ground invents a freeze. This is checked after
        // the same-instant mismatch above, which still fails.
        if (view.IsMoveOrigin)
            return false;

        // A view can match the ground at its own timestamp yet be frozen while the subject keeps walking in
        // front of the observer. Only a later ground origin inside the same unbroken visibility interval is stale.
        foreach (var point in subjectTimeline.Points)
        {
            if (point.At.UtcTicks <= viewAt.UtcTicks)
                continue;
            if (Math.Abs(view.X - point.X) <= PositionTolerance && Math.Abs(view.Y - point.Y) <= PositionTolerance)
                continue;
            if (!PairVisibleThroughout(observerTimeline, subjectTimeline, viewAt, point.At,
                    observer, subject, cellSize))
                return false; // visibility ended: the old view is from the prior interval, not a frozen follower.
            groundX = point.X;
            groundY = point.Y;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the observer's view is the subject's <em>next</em> own position, received by the subject within the
    /// causal window after the observer got it. One server event (a TeleportToLocation, a MoveState) is written to
    /// the subject's socket and every watcher's socket independently, so receive order across bots is not causal:
    /// in bots-economy-hammer the hub teleport reached watchers 0.1-43 ms before it reached the teleporting bot,
    /// and the subject's self timeline at the watcher's instant was still the pre-teleport point (#1120). Only
    /// the next self sample is accepted, so a view frozen at an <em>earlier</em> position still fails, and the
    /// frozen-follower scan below still runs on the accepted view.
    /// </summary>
    static bool SubjectReachesViewNext(
        PositionTimeline subjectTimeline,
        BotObservationLog.TrackedEntity view,
        DateTimeOffset viewAt,
        long windowTicks)
    {
        foreach (var point in subjectTimeline.Points)
        {
            if (point.At.UtcTicks <= viewAt.UtcTicks)
                continue;
            return point.At.UtcTicks - viewAt.UtcTicks <= windowTicks &&
                   Math.Abs(view.X - point.X) <= PositionTolerance &&
                   Math.Abs(view.Y - point.Y) <= PositionTolerance;
        }

        return false;
    }

    /// <summary>Whether both timelines place the pair inside the same 3x3 GridAoi watch at one instant.</summary>
    static bool PairVisibleAt(
        PositionTimeline observerTimeline,
        PositionTimeline subjectTimeline,
        DateTimeOffset at,
        BotRosterEntry observer,
        BotRosterEntry subject,
        int cellSize)
    {
        int ox = observer.X;
        int oy = observer.Y;
        int sx = subject.X;
        int sy = subject.Y;
        if (at != default)
        {
            if (observerTimeline.TryAt(at, out int tx, out int ty))
            {
                ox = tx;
                oy = ty;
            }

            if (subjectTimeline.TryAt(at, out tx, out ty))
            {
                sx = tx;
                sy = ty;
            }
        }

        return InGridWatch(ox, oy, sx, sy, cellSize);
    }

    /// <summary>Every sample between two instants must still be visible for a view to count as continuously stale.</summary>
    static bool PairVisibleThroughout(
        PositionTimeline observerTimeline,
        PositionTimeline subjectTimeline,
        DateTimeOffset from,
        DateTimeOffset to,
        BotRosterEntry observer,
        BotRosterEntry subject,
        int cellSize)
    {
        if (!PairVisibleAt(observerTimeline, subjectTimeline, from, observer, subject, cellSize) ||
            !PairVisibleAt(observerTimeline, subjectTimeline, to, observer, subject, cellSize))
            return false;

        long low = from.UtcTicks;
        long high = to.UtcTicks;
        foreach (var point in observerTimeline.Points)
        {
            if (point.At.UtcTicks <= low || point.At.UtcTicks >= high)
                continue;
            if (!PairVisibleAt(observerTimeline, subjectTimeline, point.At, observer, subject, cellSize))
                return false;
        }

        foreach (var point in subjectTimeline.Points)
        {
            if (point.At.UtcTicks <= low || point.At.UtcTicks >= high)
                continue;
            if (!PairVisibleAt(observerTimeline, subjectTimeline, point.At, observer, subject, cellSize))
                return false;
        }

        return true;
    }

    public static bool IsHardFailure(ConsistencyKind kind) => kind is
        ConsistencyKind.GhostEntity or
        ConsistencyKind.MissingSpawn or
        ConsistencyKind.OutOfOrderLifecycle or
        ConsistencyKind.DuplicateSpawn or
        ConsistencyKind.VisibilityLeak or
        ConsistencyKind.MissingCombatFanout;

    /// <summary>
    /// Exact <see cref="Fineage.Zone.Interest.GridAoi.GetVisible"/> footprint: same cell or one cell on each axis.
    /// The oracle has no world reference, so it mirrors GridAoi's floor division instead of a distance radius.
    /// </summary>
    static bool InGridWatch(int ax, int ay, int bx, int by, int cellSize)
    {
        if (cellSize <= 0)
            return true; // FINEAGE_ZONE_AOI_CELL=0 means SingleCellAoi: the whole zone is visible.
        int acx = FloorDiv(ax, cellSize), acy = FloorDiv(ay, cellSize);
        int bcx = FloorDiv(bx, cellSize), bcy = FloorDiv(by, cellSize);
        return Math.Abs(acx - bcx) <= 1 && Math.Abs(acy - bcy) <= 1;
    }

    static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        if (value < 0 && value % divisor != 0)
            q--;
        return q;
    }

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