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
}

public sealed class ConsistencyReport
{
    public List<ConsistencyIssue> Issues { get; init; } = [];
    public int PairwiseObserversCompared { get; init; }
    public double BroadcastLatencyP50Ms { get; init; }
    public double BroadcastLatencyP95Ms { get; init; }
    public double BroadcastLatencyP99Ms { get; init; }
    public bool Passed => Issues.Count == 0;
}