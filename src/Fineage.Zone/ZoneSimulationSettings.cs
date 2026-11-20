using Fineage.Zone.Combat;

namespace Fineage.Zone;

public static class ZoneSimulationSettings
{
    public static int TickMilliseconds => ReadInt("FINEAGE_ZONE_TICK_MS", ZoneCombat.AutoAttackTickMs, min: 20, max: 60_000);

    /// <summary>How many fixed combat steps fit in one regen tick. Step ms must divide <see cref="ZoneRegen.DefaultTickMs"/>.</summary>
    public static int SlowPeriodTicks
    {
        get
        {
            int step = TickMilliseconds;
            if (ZoneRegen.DefaultTickMs % step != 0)
                throw new InvalidOperationException(
                    $"Zone tick step {step} ms does not divide the {ZoneRegen.DefaultTickMs} ms regen interval.");
            return ZoneRegen.DefaultTickMs / step;
        }
    }

    static int ReadInt(string name, int fallback, int min, int max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value))
            return fallback;
        return Math.Clamp(value, min, max);
    }
}
