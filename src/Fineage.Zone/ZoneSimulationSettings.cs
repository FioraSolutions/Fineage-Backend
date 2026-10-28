using Fineage.Zone.Combat;

namespace Fineage.Zone;

public static class ZoneSimulationSettings
{
    public static int TickMilliseconds => ReadInt("FINEAGE_ZONE_TICK_MS", ZoneRegen.DefaultTickMs, min: 20, max: 60_000);

    static int ReadInt(string name, int fallback, int min, int max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value))
            return fallback;
        return Math.Clamp(value, min, max);
    }
}
