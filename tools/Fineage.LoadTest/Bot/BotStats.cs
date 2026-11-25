using System.Collections.Concurrent;
using Fineage.LoadTest.Travel;

namespace Fineage.LoadTest.Bot;

public sealed class BotStats
{
    public int BotId { get; init; }
    public string Account { get; init; } = "";
    public volatile bool InWorld;
    public volatile bool Disconnected;
    public string? LastError;
    public long ActionsAttempted;
    public long ActionsFailed;
    public long RelogsAttempted;
    public long RelogsSucceeded;
    public long RttSamples;
    public double RttSumMs;
    public double RttMaxMs;
    public long MissedBehaviorSlots;
    public DateTimeOffset? LastActionAt;
    /// <summary>Set for gatekeeper travellers only.</summary>
    public TravelStats? Travel;

    public long EconomyFlowsAttempted;
    public long EconomyFlowsSucceeded;
    public ConcurrentDictionary<string, int> EconomyRejectCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentBag<string> EconomyFlowFailures { get; } = [];

    public double RttAvgMs => RttSamples == 0 ? 0 : RttSumMs / RttSamples;

    int _economyFlowDepth;

    public bool InEconomyFlow => Volatile.Read(ref _economyFlowDepth) > 0;

    public void BeginEconomyFlow() => Interlocked.Increment(ref _economyFlowDepth);

    public void EndEconomyFlow()
    {
        if (Interlocked.Decrement(ref _economyFlowDepth) < 0)
            Interlocked.Exchange(ref _economyFlowDepth, 0);
    }

    public void RecordEconomyReject(string code)
    {
        if (!InEconomyFlow || !Consistency.EconomyConsistencyOracle.IsEconomyFlowReject(code))
            return;
        if (string.IsNullOrWhiteSpace(code))
            code = "unknown";
        EconomyRejectCounts.AddOrUpdate(code, 1, static (_, n) => n + 1);
    }

    public void RecordRtt(double ms)
    {
        RttSumMs += ms;
        RttSamples++;
        if (ms > RttMaxMs)
            RttMaxMs = ms;
    }
}