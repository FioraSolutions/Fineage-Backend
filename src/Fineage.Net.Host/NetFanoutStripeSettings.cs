namespace Fineage.Net.Host;

/// <summary>
/// Outbound fan-out stripes (issue #867 PR2): one dedicated delivery thread per stripe, connections sharded by
/// <c>ConnectionId % StripeCount</c> so per-connection order holds. <c>FINEAGE_NET_FANOUT_STRIPES</c> sets the count
/// (1..64, default min(cores, 4)); <c>FINEAGE_NET_FANOUT_STRIPE_QUEUE</c> the per-stripe queue (64..1000000).
/// A value that is set but not a number in range throws at startup instead of being clamped.
/// </summary>
public static class NetFanoutStripeSettings
{
    public const int DefaultStripeCount = 4;
    public const int MaxStripeCount = 64;
    public const int DefaultQueueCapacityPerStripe = 4096;
    public const int MinQueueCapacityPerStripe = 64;
    public const int MaxQueueCapacityPerStripe = 1_000_000;
    const string StripesEnv = "FINEAGE_NET_FANOUT_STRIPES";
    const string QueueEnv = "FINEAGE_NET_FANOUT_STRIPE_QUEUE";

    public static int StripeCount =>
        Read(StripesEnv, 1, MaxStripeCount) ?? Math.Clamp(Environment.ProcessorCount, 1, DefaultStripeCount);

    public static int QueueCapacityPerStripe =>
        Read(QueueEnv, MinQueueCapacityPerStripe, MaxQueueCapacityPerStripe) ?? DefaultQueueCapacityPerStripe;

    public static void Validate(int stripeCount, int queueCapacity)
    {
        if (stripeCount < 1 || stripeCount > MaxStripeCount)
            throw new ArgumentOutOfRangeException(nameof(stripeCount), stripeCount, $"fan-out stripe count must be 1..{MaxStripeCount}");
        // Explicit capacities below the env minimum are allowed: tests use tiny queues to force overflow.
        if (queueCapacity < 1 || queueCapacity > MaxQueueCapacityPerStripe)
            throw new ArgumentOutOfRangeException(nameof(queueCapacity), queueCapacity,
                $"fan-out stripe queue must be 1..{MaxQueueCapacityPerStripe}");
    }

    static int? Read(string name, int min, int max)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (!int.TryParse(raw, out int n) || n < min || n > max)
            throw new InvalidOperationException($"{name}='{raw}' must be an integer in {min}..{max}");
        return n;
    }
}
