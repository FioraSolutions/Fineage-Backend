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
            "aoe-train" => AoeTrain(scenario, index),
            "aoe-multiparty" => AoeMultiParty(scenario, index),
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

    static (int x, int y, int z) AoeTrain(LoadScenario s, int index) =>
        index == 0
            ? (s.SpawnX, s.SpawnY, s.SpawnZ)
            : (s.SpawnX + 20 + (index % 4) * 15, s.SpawnY + (index / 4) * 15, s.SpawnZ);

    static (int x, int y, int z) AoeMultiParty(LoadScenario s, int index)
    {
        var hp = s.HeavyPlay ?? new HeavyPlayConfig { Mode = "aoe-multiparty" };
        int partySize = Math.Max(2, hp.PartySize);
        int party = index / partySize;
        int slot = index % partySize;
        int[] corners = [-700, 700];
        int cx = party is 0 or 2 ? corners[0] : corners[1];
        int cy = party < 2 ? corners[0] : corners[1];
        if (slot == 0)
            return (s.SpawnX + cx, s.SpawnY + cy, s.SpawnZ);
        return (s.SpawnX + cx + slot * 12, s.SpawnY + cy, s.SpawnZ);
    }

    static (int x, int y, int z) Siege(LoadScenario s, int index)
    {
        double angle = index * 0.62;
        int r = 40 + (index % 12) * 8;
        return (s.SpawnX + (int)(Math.Cos(angle) * r), s.SpawnY + (int)(Math.Sin(angle) * r), s.SpawnZ);
    }
}