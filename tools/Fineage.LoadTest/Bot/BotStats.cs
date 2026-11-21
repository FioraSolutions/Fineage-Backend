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
    public long RttSamples;
    public double RttSumMs;
    public double RttMaxMs;
    public DateTimeOffset? LastActionAt;

    public double RttAvgMs => RttSamples == 0 ? 0 : RttSumMs / RttSamples;

    public void RecordRtt(double ms)
    {
        RttSumMs += ms;
        RttSamples++;
        if (ms > RttMaxMs)
            RttMaxMs = ms;
    }
}