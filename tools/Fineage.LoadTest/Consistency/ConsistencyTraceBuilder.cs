using Fineage.LoadTest.Bot;
using Fineage.Net.Contract;

namespace Fineage.LoadTest.Consistency;

/// <summary>
/// Builds the opt-in consistency trace from the existing per-bot <see cref="BotObservationLog"/> after the oracle
/// has decided what failed. Only frames that mention the issue's observer/subject inside a bounded time window are
/// decoded into id/coordinate/generation summaries; raw payloads and account credentials never enter the trace.
/// </summary>
public static class ConsistencyTraceBuilder
{
    public static ConsistencyTrace Build(
        IReadOnlyList<FineageBotClient> bots,
        ConsistencyReport report,
        ConsistencyTraceOptions options)
    {
        options.Validate();
        var byBotId = bots.ToDictionary(b => b.BotId);
        var byObjectId = bots
            .Where(b => b.SelfObjectId > 0)
            .GroupBy(b => b.SelfObjectId)
            .ToDictionary(g => g.Key, g => g.First());

        var issues = new List<ConsistencyTraceIssue>();
        int bytes = 0;
        bool truncated = false;

        foreach (var issue in FairIssueOrder(report.Issues))
        {
            if (issues.Count >= options.MaxIssues)
            {
                truncated = true;
                break;
            }

            byBotId.TryGetValue(issue.ObserverBotId, out var observer);
            FineageBotClient? subject = null;
            if (issue.OtherBotId is int otherBotId)
                byBotId.TryGetValue(otherBotId, out subject);
            else if (issue.SubjectObjectId is int subjectObjectId)
                byObjectId.TryGetValue(subjectObjectId, out subject);

            var evidence = Gather(issue, observer, subject, options);
            var materialized = FitToBytes(evidence, options, bytes);
            if (materialized == null)
            {
                truncated = true;
                break;
            }

            bytes += ConsistencyTraceJson.Utf8Bytes(ConsistencyTraceJson.SerializeIssue(materialized)) + 1;
            issues.Add(materialized);
        }

        return new ConsistencyTrace
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            TotalIssues = report.Issues.Count,
            TracedIssues = issues.Count,
            FrameCount = issues.Sum(i => i.Frames.Count),
            Bytes = bytes,
            Truncated = truncated || issues.Any(i => i.Truncated),
            WindowSeconds = options.WindowSeconds,
            Issues = issues
        };
    }

    /// <summary>
    /// Round-robins the report's kinds so a large DuplicateSpawn block does not spend the whole issue budget before
    /// the first MissingSpawn/StalePosition/MissingCombatFanout issue gets a trace.
    /// </summary>
    static IEnumerable<ConsistencyIssue> FairIssueOrder(IReadOnlyList<ConsistencyIssue> issues)
    {
        var queues = issues
            .GroupBy(i => i.Kind)
            .Select(g => new Queue<ConsistencyIssue>(g))
            .ToList();
        while (queues.Count > 0)
        {
            for (int i = 0; i < queues.Count; i++)
            {
                if (queues[i].Count == 0)
                {
                    queues.RemoveAt(i--);
                    continue;
                }

                yield return queues[i].Dequeue();
            }
        }
    }

    sealed record Evidence(
        ConsistencyIssue Issue,
        DateTimeOffset AnchorAt,
        DateTimeOffset WindowStart,
        DateTimeOffset WindowEnd,
        List<FrameCandidate> Candidates);

    readonly record struct FrameCandidate(ConsistencyTraceFrame Frame, bool IsMove);

    static Evidence Gather(
        ConsistencyIssue issue,
        FineageBotClient? observer,
        FineageBotClient? subject,
        ConsistencyTraceOptions options)
    {
        int subjectId = issue.SubjectObjectId ?? subject?.SelfObjectId ?? 0;
        int observerSelfId = observer?.SelfObjectId ?? 0;
        var observerLog = observer?.ObservationLog;
        var subjectLog = subject?.ObservationLog;
        bool subjectIsObserver = subject != null && observer != null && subject.BotId == observer.BotId;

        // The subject's frames carry the ground truth for a failing pair. The observer's own pawn frames are
        // included for the pair checks so the trace shows it was alive and where it was, not only what it saw.
        bool includeObserverSelf = issue.Kind is ConsistencyKind.MissingSpawn
            or ConsistencyKind.StalePosition
            or ConsistencyKind.GhostEntity
            or ConsistencyKind.OutOfOrderLifecycle
            or ConsistencyKind.DuplicateSpawn;

        var observerIds = new HashSet<int>();
        if (subjectId > 0)
            observerIds.Add(subjectId);
        if (includeObserverSelf && observerSelfId > 0)
            observerIds.Add(observerSelfId);

        DateTimeOffset? subjectLast = LastMatchingAt(observerLog, subjectId)
            ?? LastMatchingAt(subjectLog, subjectId);
        DateTimeOffset anchor = issue.ObservedAt
            ?? subjectLast
            ?? LastFrameAt(observerLog)
            ?? LastFrameAt(subjectLog)
            ?? DateTimeOffset.UtcNow;
        var half = TimeSpan.FromSeconds(options.WindowSeconds);
        DateTimeOffset start = anchor - half;
        DateTimeOffset end = anchor + half;

        var candidates = new List<FrameCandidate>();
        if (observerLog != null && observerIds.Count > 0)
            Collect(observerLog, "observer", observerIds, start, end, candidates);
        if (!subjectIsObserver && subjectLog != null && subjectId > 0)
            Collect(subjectLog, "subject", new HashSet<int> { subjectId }, start, end, candidates);

        return new Evidence(issue, anchor, start, end, candidates);
    }

    static ConsistencyTraceIssue? FitToBytes(Evidence evidence, ConsistencyTraceOptions options, int usedBytes)
    {
        int cap = options.MaxFramesPerIssue;
        while (true)
        {
            var issue = Materialize(evidence, cap);
            int bytes = ConsistencyTraceJson.Utf8Bytes(ConsistencyTraceJson.SerializeIssue(issue)) + 1;
            if (usedBytes + bytes <= options.MaxBytes)
                return issue;
            if (cap == 0)
                return null;
            cap = cap == 1 ? 0 : cap / 2;
        }
    }

    static ConsistencyTraceIssue Materialize(Evidence evidence, int frameCap)
    {
        var selected = Select(evidence.Candidates, frameCap);
        var issue = evidence.Issue;
        return new ConsistencyTraceIssue
        {
            Kind = issue.Kind,
            ObserverBotId = issue.ObserverBotId,
            OtherBotId = issue.OtherBotId,
            SubjectObjectId = issue.SubjectObjectId,
            Detail = issue.Detail,
            AnchorAt = evidence.AnchorAt,
            WindowStart = evidence.WindowStart,
            WindowEnd = evidence.WindowEnd,
            FramesAvailable = evidence.Candidates.Count,
            Truncated = selected.Count < evidence.Candidates.Count,
            Frames = selected
        };
    }

    /// <summary>
    /// Keeps every lifecycle/combat frame before spending the cap on movement frames, then restores time order so
    /// a spawn → move → despawn sequence still reads as a sequence in the file.
    /// </summary>
    static List<ConsistencyTraceFrame> Select(List<FrameCandidate> candidates, int frameCap)
    {
        if (frameCap <= 0)
            return [];
        if (candidates.Count <= frameCap)
            return candidates.Select(c => c.Frame).OrderBy(f => f.At).ToList();

        var transitions = candidates.Where(c => !c.IsMove).ToList();
        var moves = candidates.Where(c => c.IsMove).ToList();
        var chosen = transitions.Count <= frameCap
            ? transitions.ToList()
            : transitions.TakeLast(frameCap).ToList();
        int remaining = frameCap - chosen.Count;
        if (remaining > 0)
            chosen.AddRange(moves.TakeLast(Math.Min(remaining, moves.Count)));
        chosen.Sort((a, b) => a.Frame.At.CompareTo(b.Frame.At));
        return chosen.Select(c => c.Frame).ToList();
    }

    static void Collect(
        BotObservationLog log,
        string source,
        IReadOnlySet<int> matchingIds,
        DateTimeOffset start,
        DateTimeOffset end,
        List<FrameCandidate> destination)
    {
        foreach (var frame in log.Snapshot())
        {
            if (frame.At < start || frame.At > end)
                continue;
            if (!TrySummarize(frame, source, out var summary, out int entityId, out int? targetId, out bool isMove))
                continue;
            if (!matchingIds.Contains(entityId) && (targetId is not int t || !matchingIds.Contains(t)))
                continue;
            destination.Add(new FrameCandidate(summary, isMove));
        }
    }

    static DateTimeOffset? LastMatchingAt(BotObservationLog? log, int entityId)
    {
        if (log == null || entityId <= 0)
            return null;
        DateTimeOffset? last = null;
        foreach (var frame in log.Snapshot())
        {
            if (!TrySummarize(frame, "", out _, out int frameEntityId, out int? targetId, out _))
                continue;
            if (frameEntityId != entityId && targetId != entityId)
                continue;
            if (last == null || frame.At > last.Value)
                last = frame.At;
        }

        return last;
    }

    static DateTimeOffset? LastFrameAt(BotObservationLog? log)
    {
        var frames = log?.Snapshot();
        return frames is { Count: > 0 } ? frames[^1].At : null;
    }

    static bool TrySummarize(
        ObservedFrame frame,
        string source,
        out ConsistencyTraceFrame summary,
        out int entityId,
        out int? targetId,
        out bool isMove)
    {
        summary = null!;
        entityId = 0;
        targetId = null;
        isMove = false;
        switch (frame.Opcode)
        {
            case NetOpcode.EntitySpawn:
                {
                    var spawn = NetCodec.DecodeEntitySpawn(frame.Payload);
                    entityId = (int)spawn.EntityValue;
                    summary = new ConsistencyTraceFrame
                    {
                        At = frame.At,
                        Source = source,
                        Opcode = frame.Opcode,
                        Op = nameof(NetOpcode.EntitySpawn),
                        EntityId = entityId,
                        Generation = spawn.EntityGeneration,
                        EntityKind = spawn.Kind.ToString(),
                        IsSelf = spawn.IsSelf,
                        X = spawn.X,
                        Y = spawn.Y,
                        Z = spawn.Z,
                        Hp = spawn.CurHp,
                        MaxHp = spawn.MaxHp,
                        PayloadBytes = frame.Payload.Length
                    };
                    return true;
                }
            case NetOpcode.EntityDespawn:
                {
                    var despawn = NetCodec.DecodeEntityDespawn(frame.Payload);
                    entityId = (int)despawn.EntityValue;
                    summary = new ConsistencyTraceFrame
                    {
                        At = frame.At,
                        Source = source,
                        Opcode = frame.Opcode,
                        Op = nameof(NetOpcode.EntityDespawn),
                        EntityId = entityId,
                        Generation = despawn.EntityGeneration,
                        PayloadBytes = frame.Payload.Length
                    };
                    return true;
                }
            case NetOpcode.MoveState:
                {
                    var move = NetCodec.DecodeMoveState(frame.Payload);
                    entityId = (int)move.EntityValue;
                    isMove = true;
                    summary = new ConsistencyTraceFrame
                    {
                        At = frame.At,
                        Source = source,
                        Opcode = frame.Opcode,
                        Op = nameof(NetOpcode.MoveState),
                        EntityId = entityId,
                        Generation = move.EntityGeneration,
                        FromX = move.OriginX,
                        FromY = move.OriginY,
                        X = move.DestX,
                        Y = move.DestY,
                        Z = move.DestZ,
                        PayloadBytes = frame.Payload.Length
                    };
                    return true;
                }
            case NetOpcode.PositionCorrection:
                {
                    var correction = NetCodec.DecodePositionCorrection(frame.Payload);
                    entityId = (int)correction.EntityValue;
                    summary = new ConsistencyTraceFrame
                    {
                        At = frame.At,
                        Source = source,
                        Opcode = frame.Opcode,
                        Op = nameof(NetOpcode.PositionCorrection),
                        EntityId = entityId,
                        Generation = correction.EntityGeneration,
                        X = correction.X,
                        Y = correction.Y,
                        Z = correction.Z,
                        PayloadBytes = frame.Payload.Length
                    };
                    return true;
                }
            case NetOpcode.TeleportToLocation:
                {
                    var teleport = NetCodec.DecodeTeleportToLocation(frame.Payload);
                    entityId = teleport.ObjectId;
                    summary = new ConsistencyTraceFrame
                    {
                        At = frame.At,
                        Source = source,
                        Opcode = frame.Opcode,
                        Op = nameof(NetOpcode.TeleportToLocation),
                        EntityId = entityId,
                        X = teleport.X,
                        Y = teleport.Y,
                        Z = teleport.Z,
                        PayloadBytes = frame.Payload.Length
                    };
                    return true;
                }
            case NetOpcode.StatusUpdate:
                {
                    var status = NetCodec.DecodeStatusUpdate(frame.Payload);
                    entityId = (int)status.EntityValue;
                    int? hp = null;
                    int? maxHp = null;
                    for (int i = 0; i < status.AttributeIds.Length && i < status.Values.Length; i++)
                    {
                        if (status.AttributeIds[i] == Fineage.Protocol.InterludeStatus.CurHp)
                            hp = status.Values[i];
                        else if (status.AttributeIds[i] == Fineage.Protocol.InterludeStatus.MaxHp)
                            maxHp = status.Values[i];
                    }

                    summary = new ConsistencyTraceFrame
                    {
                        At = frame.At,
                        Source = source,
                        Opcode = frame.Opcode,
                        Op = nameof(NetOpcode.StatusUpdate),
                        EntityId = entityId,
                        Generation = status.EntityGeneration,
                        Hp = hp,
                        MaxHp = maxHp,
                        PayloadBytes = frame.Payload.Length
                    };
                    return true;
                }
            case NetOpcode.EntityDie:
                {
                    var die = NetCodec.DecodeEntityDie(frame.Payload);
                    entityId = (int)die.EntityValue;
                    targetId = die.KillerObjectId;
                    summary = new ConsistencyTraceFrame
                    {
                        At = frame.At,
                        Source = source,
                        Opcode = frame.Opcode,
                        Op = nameof(NetOpcode.EntityDie),
                        EntityId = entityId,
                        Generation = die.EntityGeneration,
                        TargetId = targetId,
                        PayloadBytes = frame.Payload.Length
                    };
                    return true;
                }
            case NetOpcode.AttackState:
                {
                    var attack = NetCodec.DecodeAttackState(frame.Payload);
                    entityId = (int)attack.AttackerValue;
                    targetId = (int)attack.TargetValue;
                    summary = new ConsistencyTraceFrame
                    {
                        At = frame.At,
                        Source = source,
                        Opcode = frame.Opcode,
                        Op = nameof(NetOpcode.AttackState),
                        EntityId = entityId,
                        Generation = attack.AttackerGeneration,
                        TargetId = targetId,
                        Damage = attack.Damage,
                        X = attack.AttackerX,
                        Y = attack.AttackerY,
                        Z = attack.AttackerZ,
                        PayloadBytes = frame.Payload.Length
                    };
                    return true;
                }
            case NetOpcode.MagicSkillUse:
                {
                    var skill = NetCodec.DecodeMagicSkillUse(frame.Payload);
                    entityId = skill.CasterObjectId;
                    targetId = skill.TargetObjectId;
                    summary = new ConsistencyTraceFrame
                    {
                        At = frame.At,
                        Source = source,
                        Opcode = frame.Opcode,
                        Op = nameof(NetOpcode.MagicSkillUse),
                        EntityId = entityId,
                        TargetId = targetId,
                        SkillId = skill.SkillId,
                        PayloadBytes = frame.Payload.Length
                    };
                    return true;
                }
            default:
                return false;
        }
    }
}
