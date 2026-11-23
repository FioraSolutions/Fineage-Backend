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

    public double RttAvgMs => RttSamples == 0 ? 0 : RttSumMs / RttSamples;

    public void RecordRtt(double ms)
    {
        RttSumMs += ms;
        RttSamples++;
        if (ms > RttMaxMs)
            RttMaxMs = ms;
    }
}