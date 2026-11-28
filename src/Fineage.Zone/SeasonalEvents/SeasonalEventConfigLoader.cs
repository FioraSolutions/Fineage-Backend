using System.Globalization;
using System.Xml.Linq;
using Fineage.Zone.Drops;

namespace Fineage.Zone.SeasonalEvents;

/// <summary>Mobius <c>LongTimeEvent.loadConfig</c> for <c>data/scripts/events/{id}/config.xml</c>.</summary>
public static class SeasonalEventConfigLoader
{
    public static SeasonalEventDefinition? TryLoad(string eventId, string configPath, Action<string> warn)
    {
        if (!File.Exists(configPath))
        {
            warn($"[seasonal-events] missing event data eventId={eventId} path={configPath}");
            return null;
        }

        try
        {
            var doc = XDocument.Load(configPath);
            var root = doc.Root;
            if (root == null || !string.Equals(root.Name.LocalName, "event", StringComparison.OrdinalIgnoreCase))
            {
                warn($"[seasonal-events] bad config root eventId={eventId} path={configPath}");
                return null;
            }

            string display = root.Attribute("name")?.Value ?? eventId;
            string? period = root.Attribute("active")?.Value;
            if (string.IsNullOrWhiteSpace(period) || !TryParsePeriod(period, out long startMs, out long endMs))
            {
                warn($"[seasonal-events] illegal event period eventId={eventId} active='{period}'");
                return null;
            }

            var drops = new List<DropGroupDefinition>();
            var droplist = root.Element("droplist");
            if (droplist != null)
            {
                foreach (var add in droplist.Elements("add"))
                {
                    if (!int.TryParse(add.Attribute("item")?.Value, out int itemId) || itemId <= 0)
                        continue;
                    int min = ParseInt(add.Attribute("min")?.Value, 1);
                    int max = ParseInt(add.Attribute("max")?.Value, min);
                    if (min > max)
                    {
                        warn($"[seasonal-events] eventId={eventId} item={itemId} min greater than max");
                        continue;
                    }

                    double chance = ParseChancePercent(add.Attribute("chance")?.Value);
                    if (chance < 0 || chance > 100)
                    {
                        warn($"[seasonal-events] eventId={eventId} item={itemId} incorrect drop chance");
                        continue;
                    }

                    // Mobius EventDropHolder: one independent roll per add-row.
                    drops.Add(new DropGroupDefinition
                    {
                        Chance = chance,
                        Items =
                        [
                            new DropItemDefinition
                            {
                                ItemId = itemId,
                                Min = min,
                                Max = max,
                                Chance = 100
                            }
                        ]
                    });
                }
            }

            var spawns = new List<SeasonalNpcSpawn>();
            var spawnlist = root.Element("spawnlist");
            if (spawnlist != null)
            {
                foreach (var add in spawnlist.Elements("add"))
                {
                    if (!int.TryParse(add.Attribute("npc")?.Value, out int npcId) || npcId <= 0)
                        continue;
                    spawns.Add(new SeasonalNpcSpawn
                    {
                        NpcId = npcId,
                        X = ParseInt(add.Attribute("x")?.Value, 0),
                        Y = ParseInt(add.Attribute("y")?.Value, 0),
                        Z = ParseInt(add.Attribute("z")?.Value, 0),
                        Heading = ParseInt(add.Attribute("heading")?.Value, 0)
                    });
                }
            }

            string onEnter = "";
            string onEnd = "";
            var messages = root.Element("messages");
            if (messages != null)
            {
                foreach (var add in messages.Elements("add"))
                {
                    string? type = add.Attribute("type")?.Value;
                    string? text = add.Attribute("text")?.Value ?? "";
                    if (string.Equals(type, "onEnter", StringComparison.OrdinalIgnoreCase))
                        onEnter = text;
                    else if (string.Equals(type, "onEnd", StringComparison.OrdinalIgnoreCase))
                        onEnd = text;
                }
            }

            return new SeasonalEventDefinition
            {
                Id = eventId,
                DisplayName = display,
                StartUnixMs = startMs,
                EndUnixMs = endMs,
                OnEnterMessage = onEnter,
                OnEndMessage = onEnd,
                Spawns = spawns,
                DropGroups = drops
            };
        }
        catch (Exception ex)
        {
            warn($"[seasonal-events] eventId={eventId} error reading {configPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Mobius: <c>dd MM yyyy-dd MM yyyy</c> or same-year <c>dd MM-dd MM</c>.</summary>
    public static bool TryParsePeriod(string period, out long startUnixMs, out long endUnixMs)
    {
        startUnixMs = 0;
        endUnixMs = 0;
        period = period.Trim();
        if (period.Length == 21)
        {
            // dd MM yyyy-dd MM yyyy
            var parts = period.Split('-', 2);
            if (parts.Length != 2)
                return false;
            if (!DateTime.TryParseExact(parts[0].Trim(), "dd MM yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start))
                return false;
            if (!DateTime.TryParseExact(parts[1].Trim(), "dd MM yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var end))
                return false;
            startUnixMs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
            // Inclusive end-of-day like Mobius DateRange typically covers the calendar day.
            endUnixMs = new DateTimeOffset(end.Date.AddDays(1).AddTicks(-1)).ToUnixTimeMilliseconds();
            return true;
        }

        if (period.Length == 11)
        {
            // dd MM-dd MM → current UTC year
            var parts = period.Split('-', 2);
            if (parts.Length != 2)
                return false;
            int year = DateTime.UtcNow.Year;
            if (!DateTime.TryParseExact($"{parts[0].Trim()} {year}", "dd MM yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start))
                return false;
            if (!DateTime.TryParseExact($"{parts[1].Trim()} {year}", "dd MM yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var end))
                return false;
            startUnixMs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
            endUnixMs = new DateTimeOffset(end.Date.AddDays(1).AddTicks(-1)).ToUnixTimeMilliseconds();
            return true;
        }

        return false;
    }

    static int ParseInt(string? text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    static double ParseChancePercent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        text = text.Trim();
        if (text.EndsWith('%'))
            text = text[..^1];
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
    }
}
