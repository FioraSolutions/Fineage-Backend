namespace Fineage.Zone.Tests;

/// <summary>A settable wall clock a test world reads; the engines it was built with take their time from the world.</summary>
public sealed class MutableGameClock(DateTimeOffset utcNow)
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
