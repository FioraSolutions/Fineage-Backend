using System.Globalization;
using System.Xml.Linq;

namespace Fineage.Game.Mobius;

public sealed record MobiusSpawnNpcRow(
    int NpcId,
    int Count,
    int RespawnDelaySeconds,
    int RespawnRandomSeconds,
    string ZoneName);

public sealed record MobiusSpawnTerritoryRow(
    string ZoneName,
    int MinZ,
    int MaxZ,
    IReadOnlyList<(int X, int Y)> Nodes,
    IReadOnlyList<MobiusSpawnNpcRow> Npcs);

public static class MobiusSpawnXmlReader
{
    public static IReadOnlyList<MobiusSpawnTerritoryRow> ReadFile(string path)
    {
        var doc = XDocument.Load(path, LoadOptions.None);
        if (doc.Root == null)
            return [];

        var list = new List<MobiusSpawnTerritoryRow>();
        foreach (var spawn in doc.Root.Elements("spawn"))
        {
            string zone = (string?)spawn.Attribute("zone") ?? "unknown";
            var territory = spawn.Element("territory");
            int minZ = (int?)territory?.Attribute("minZ") ?? 0;
            int maxZ = (int?)territory?.Attribute("maxZ") ?? 0;
            var nodes = territory?.Elements("node")
                .Select(n => ((int?)n.Attribute("x") ?? 0, (int?)n.Attribute("y") ?? 0))
                .ToList() ?? [];

            var npcs = spawn.Elements("npc").Select(n => new MobiusSpawnNpcRow(
                (int?)n.Attribute("id") ?? 0,
                (int?)n.Attribute("count") ?? 1,
                ParseSeconds(n.Attribute("respawnDelay")?.Value),
                ParseSeconds(n.Attribute("respawnRandom")?.Value),
                zone)).Where(n => n.NpcId > 0).ToList();

            if (npcs.Count > 0)
                list.Add(new MobiusSpawnTerritoryRow(zone, minZ, maxZ, nodes, npcs));
        }

        return list;
    }

    static int ParseSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
            return whole;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return (int)Math.Round(seconds);
        return 0;
    }
}
