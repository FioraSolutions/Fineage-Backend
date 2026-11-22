using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.LoadTest.Tests;

public sealed class HistogramMathTests
{
    [Fact]
    public void Quantile_interpolates_inside_bucket_not_ceiling()
    {
        var bounds = MetricsText.DurationBoundsMs;
        int i1000 = Array.IndexOf(bounds, 1000);
        int i1500 = Array.IndexOf(bounds, 1500);
        var cumulative = new long[bounds.Length + 1];
        cumulative[i1000] = 0;
        cumulative[i1500] = 100;
        cumulative[^1] = 100;

        double p99 = HistogramMath.Quantile(bounds, cumulative, 100, 0.99);
        Assert.InRange(p99, 1400, 1499);
        Assert.NotEqual(1500, p99);
    }
}