using System.Collections.Concurrent;

namespace Fineage.LoadTest.Travel;

/// <summary>Per-bot gatekeeper travel counters. Rejections are explained only when the server named a reason code.</summary>
public sealed class TravelStats
{
    public const int UnexplainedSampleLimit = 20;

    public long Attempted;
    public long Succeeded;
    public long Talks;
    public long WalkHops;
    public long TalkRangeRetries;
    public readonly ConcurrentDictionary<string, long> RejectedByReason = new(StringComparer.Ordinal);
    public readonly ConcurrentQueue<string> Unexplained = new();
    public long UnexplainedCount;

    public void Rejected(string reason) => RejectedByReason.AddOrUpdate(reason, 1, (_, n) => n + 1);

    public void Fail(string detail)
    {
        Interlocked.Increment(ref UnexplainedCount);
        if (Unexplained.Count < UnexplainedSampleLimit)
            Unexplained.Enqueue(detail);
    }
}

/// <summary>Fleet total for the run report.</summary>
public sealed class TravelSummary
{
    public int Travellers { get; init; }
    public long Attempted { get; init; }
    public long Succeeded { get; init; }
    public long Rejected => RejectedByReason.Values.Sum();
    public Dictionary<string, long> RejectedByReason { get; init; } = new(StringComparer.Ordinal);
    public long Unexplained { get; init; }
    public List<string> UnexplainedSamples { get; init; } = [];
    public long Talks { get; init; }
    public long WalkHops { get; init; }
    public long TalkRangeRetries { get; init; }

    public static TravelSummary From(IEnumerable<TravelStats> bots)
    {
        var list = bots.ToList();
        var reasons = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var b in list)
        {
            foreach (var (k, v) in b.RejectedByReason)
                reasons[k] = reasons.GetValueOrDefault(k) + v;
        }

        return new TravelSummary
        {
            Travellers = list.Count,
            Attempted = list.Sum(b => b.Attempted),
            Succeeded = list.Sum(b => b.Succeeded),
            RejectedByReason = reasons,
            Unexplained = list.Sum(b => b.UnexplainedCount),
            UnexplainedSamples = list.SelectMany(b => b.Unexplained).Take(TravelStats.UnexplainedSampleLimit).ToList(),
            Talks = list.Sum(b => b.Talks),
            WalkHops = list.Sum(b => b.WalkHops),
            TalkRangeRetries = list.Sum(b => b.TalkRangeRetries)
        };
    }

    /// <summary>Runner fail reason, or null. Any rejection without a server reason code fails the run.</summary>
    public string? FailReason() =>
        Unexplained == 0
            ? null
            : $"Gatekeeper travel: {Unexplained} unexplained teleport failure(s): {string.Join(" | ", UnexplainedSamples.Take(3))}";
}
