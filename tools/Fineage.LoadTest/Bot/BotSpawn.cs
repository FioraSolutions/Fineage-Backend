using Fineage.LoadTest.Models;

namespace Fineage.LoadTest.Bot;

public static class BotSpawn
{
    public static (int x, int y, int z) Position(LoadScenario scenario, int index)
    {
        return scenario.SpawnLayout.ToLowerInvariant() switch
        {
            "spread" => Spread(scenario, index),
            "siege" => Siege(scenario, index),
            "economy" => EconomyHub(scenario, index),
            _ => Town(scenario, index)
        };
    }

    static (int x, int y, int z) Town(LoadScenario s, int index) =>
        (s.SpawnX + (index % 7) * 12, s.SpawnY + (index / 7) * 12, s.SpawnZ);

    static (int x, int y, int z) EconomyHub(LoadScenario s, int index)
    {
        var hub = s.Economy ?? new EconomyHammerConfig { Enabled = true };
        return (hub.HubX + (index % 7) * 12, hub.HubY + (index / 7) * 12, hub.HubZ);
    }

    static (int x, int y, int z) Spread(LoadScenario s, int index)
    {
        int cols = (int)Math.Ceiling(Math.Sqrt(index + 1));
        int row = index / cols;
        int col = index % cols;
        return (s.SpawnX + col * 180, s.SpawnY + row * 180, s.SpawnZ);
    }

    static (int x, int y, int z) Siege(LoadScenario s, int index)
    {
        double angle = index * 0.62;
        int r = 40 + (index % 12) * 8;
        return (s.SpawnX + (int)(Math.Cos(angle) * r), s.SpawnY + (int)(Math.Sin(angle) * r), s.SpawnZ);
    }
}