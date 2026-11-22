namespace Fineage.Protocol.Telemetry;

/// <summary>Prometheus-style cumulative histogram quantiles with linear interpolation inside buckets.</summary>
public static class HistogramMath
{
    public static double Quantile(
        IReadOnlyList<double> upperBounds,
        IReadOnlyList<long> cumulative,
        long count,
        double q,
        double overflowUpperBound = 10_000)
    {
        if (count <= 0 || cumulative.Count == 0)
            return 0;
        if (q <= 0)
            return 0;
        if (q >= 1)
            return MaxEstimate(upperBounds, cumulative, count, overflowUpperBound);

        double rank = q * count;
        if (rank < 1)
            rank = 1;

        double prevLe = 0;
        long prevCum = 0;
        for (int i = 0; i < cumulative.Count; i++)
        {
            long cum = cumulative[i];
            if (cum < rank)
            {
                prevCum = cum;
                prevLe = i < upperBounds.Count ? upperBounds[i] : overflowUpperBound;
                continue;
            }

            double le = i < upperBounds.Count ? upperBounds[i] : overflowUpperBound;
            if (cum == prevCum)
                return le;

            double frac = (rank - prevCum) / (cum - prevCum);
            return prevLe + frac * (le - prevLe);
        }

        return overflowUpperBound;
    }

    public static double MaxEstimate(
        IReadOnlyList<double> upperBounds,
        IReadOnlyList<long> cumulative,
        long count,
        double overflowUpperBound = 10_000)
    {
        if (count <= 0)
            return 0;
        long prevCum = 0;
        for (int i = 0; i < cumulative.Count; i++)
        {
            long cum = cumulative[i];
            if (cum >= count)
            {
                double le = i < upperBounds.Count ? upperBounds[i] : overflowUpperBound;
                if (cum == prevCum)
                    return le;
                return le;
            }

            prevCum = cum;
        }

        return overflowUpperBound;
    }

    public static bool TryDelta(
        SortedDictionary<double, long> before,
        SortedDictionary<double, long> after,
        out SortedDictionary<double, long> delta)
    {
        delta = [];
        foreach (var (le, cum) in after)
        {
            long b = before.GetValueOrDefault(le);
            long d = cum - b;
            if (d < 0)
                return false;
            if (d > 0)
                delta[le] = d;
        }

        return true;
    }

    public static long DeltaCount(SortedDictionary<double, long> delta)
    {
        if (delta.Count == 0)
            return 0;
        return delta.Values.Max();
    }

    public static double DeltaSum(double afterSum, double beforeSum) => Math.Max(0, afterSum - beforeSum);
}