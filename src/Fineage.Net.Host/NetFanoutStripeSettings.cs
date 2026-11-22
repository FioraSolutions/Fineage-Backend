namespace Fineage.Net.Host;

/// <summary>Outbound fan-out stripe count (<c>FINEAGE_NET_FANOUT_STRIPES</c>, issue #867 PR2).</summary>
public static class NetFanoutStripeSettings
{
    public const int DefaultStripeCount = 4;
    public const int MaxStripeCount = 64;
    public const int DefaultQueueCapacityPerStripe = 4096;

    public static int StripeCount
    {
        get
        {
            string? raw = Environment.GetEnvironmentVariable("FINEAGE_NET_FANOUT_STRIPES");
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out int n))
                return Math.Clamp(n, 1, MaxStripeCount);
            return Math.Clamp(Environment.ProcessorCount, 1, DefaultStripeCount);
        }
    }

    public static int QueueCapacityPerStripe
    {
        get
        {
            string? raw = Environment.GetEnvironmentVariable("FINEAGE_NET_FANOUT_STRIPE_QUEUE");
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out int n))
                return Math.Clamp(n, 64, 1_000_000);
            return DefaultQueueCapacityPerStripe;
        }
    }
}