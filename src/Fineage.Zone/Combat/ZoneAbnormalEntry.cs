namespace Fineage.Zone.Combat;

/// <summary>Server-side abnormal with absolute expiry. Wire rows are derived at fan-out time.</summary>
public sealed class ZoneAbnormalEntry
{
    public int Id { get; set; }
    public int Value { get; set; }
    public long ExpiresAtUnixMs { get; set; }
}
