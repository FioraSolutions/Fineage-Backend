namespace Fineage.LoadTest.Consistency;

public enum ConsistencyKind
{
    GhostEntity,
    MissingSpawn,
    StalePosition,
    HpDesync,
    MissingCombatFanout,
    OutOfOrderLifecycle,
    DuplicateSpawn,
    VisibilityLeak,
    SpawnOrder
}

public sealed class ConsistencyIssue
{
    public ConsistencyKind Kind { get; init; }
    public int ObserverBotId { get; init; }
    public int? OtherBotId { get; init; }
    public int? SubjectObjectId { get; init; }
    public string Detail { get; init; } = "";
    /// <summary>
    /// When the triggering frame was observed, when the check had one. The opt-in trace uses it to centre the
    /// evidence window; checks that only compare end state leave it null and the trace anchors on the last
    /// frame that mentioned the subject instead.
    /// </summary>
    public DateTimeOffset? ObservedAt { get; init; }
}

public sealed class ConsistencyReport
{
    public List<ConsistencyIssue> Issues { get; init; } = [];
    public int PairwiseObserversCompared { get; init; }
    public double BroadcastLatencyP50Ms { get; init; }
    public double BroadcastLatencyP95Ms { get; init; }
    public double BroadcastLatencyP99Ms { get; init; }
    public double BroadcastLatencyMaxMs { get; init; }
    public double SteadyBroadcastLatencyP99Ms { get; init; }
    public double SteadyBroadcastLatencyMaxMs { get; init; }
    public double SteadyAttackFanoutP99EstimateMs { get; init; }
    public double SteadySkillFanoutP99EstimateMs { get; init; }
    public int AttackSampleCount { get; init; }
    public int SkillSampleCount { get; init; }
    public int AttackTimeoutCount { get; init; }
    public int SkillTimeoutCount { get; init; }
    /// <summary>Sum of EntityDie frames that named a bot as the killer.</summary>
    public int BotKillCount { get; init; }
    public bool Passed => Issues.Count == 0;
}