using Fineage.LoadTest.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fineage.LoadTest;

public static class ScenarioLoader
{
    static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static LoadScenario Load(string path)
    {
        string text = File.ReadAllText(path);
        var scenario = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? System.Text.Json.JsonSerializer.Deserialize<LoadScenario>(text,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("empty scenario")
            : Yaml.Deserialize<LoadScenario>(text)
                ?? throw new InvalidOperationException("empty scenario");

        if (!string.IsNullOrWhiteSpace(scenario.Extends))
        {
            string basePath = Path.Combine(Path.GetDirectoryName(path) ?? ".", scenario.Extends);
            var parent = Load(basePath);
            var declared = DeclaredKeys(path, text);
            scenario = Merge(parent, scenario, declared);
        }

        Validate(scenario);
        return scenario;
    }

    /// <summary>Behaviors <c>FineageBotClient.RunBehaviorTickAsync</c> implements.</summary>
    public static readonly IReadOnlySet<string> KnownActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "move", "attack", "skill", "say", "sit", "social", "pickup", "trade", "party", "pvp", "relog"
    };

    public static void Validate(LoadScenario scenario)
    {
        bool structuredPlay = scenario.Economy is { Enabled: true } ||
                              scenario.HeavyPlay is { } hp && hp.IsHeavyPlay;
        if (!structuredPlay)
        {
            var unknown = scenario.ActionWeights.Keys.Where(k => !KnownActions.Contains(k)).ToList();
            if (unknown.Count > 0)
            {
                throw new InvalidOperationException(
                    $"scenario {scenario.Name}: unknown action(s) {string.Join(", ", unknown)}; known: {string.Join(", ", KnownActions)}. " +
                    "Town-to-town teleports are gatekeeperTravelShare, not an action weight.");
            }
        }

        foreach (var (name, weight) in scenario.ActionWeights)
        {
            if (weight < 0 || double.IsNaN(weight))
                throw new InvalidOperationException($"scenario {scenario.Name}: action '{name}' has weight {weight}; weights must be >= 0.");
            if (!KnownActions.Contains(name) && !BotEconomyActions.Contains(name))
                throw new InvalidOperationException(
                    $"scenario {scenario.Name}: unknown action '{name}'; known: {string.Join(", ", KnownActions.Concat(BotEconomyActions))}.");
        }

        if (scenario.ActionWeights.Count > 0 && scenario.ActionWeights.Values.Sum() <= 0)
            throw new InvalidOperationException($"scenario {scenario.Name}: actionWeights sum to 0.");

        _ = scenario.ResolvedGatekeeperTravelShare;
    }

    /// <summary>Economy flows <see cref="Bot.BotEconomyFlows.RunActionAsync"/> implements.</summary>
    public static readonly IReadOnlySet<string> BotEconomyActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "economy_hub", "economy_shop_buy", "economy_shop_sell", "economy_warehouse", "economy_trade",
        "economy_store", "economy_manufacture", "economy_manufacture_maker", "economy_manufacture_customer",
        "economy_manor", "economy_pickup_drop"
    };

    /// <summary>Top-level keys the scenario file itself sets (not class defaults), case-insensitive.</summary>
    static HashSet<string> DeclaredKeys(string path, string text)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            foreach (var property in doc.RootElement.EnumerateObject())
                keys.Add(property.Name);
            return keys;
        }

        var raw = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object?>>(text);
        if (raw != null)
        {
            foreach (string key in raw.Keys)
                keys.Add(key);
        }

        return keys;
    }

    /// <summary>
    /// Child values win. <c>actionWeights</c> is a whole mix: a child that declares it replaces the parent's
    /// mix, a child that omits it inherits the parent's. Mixing the two let the base smoke mix
    /// (attack/skill/pickup/trade/party, summing to 1.03) shadow every economy action of the hammer.
    /// <para>
    /// <c>loginPort</c>/<c>zonePort</c> need the declared-key check: their class defaults (9106/9777) are real
    /// values, so a value comparison cannot tell an omitted port from an explicit one and a child that omitted
    /// them silently re-pointed the run at the default endpoint instead of the prepared parent's.
    /// </para>
    /// </summary>
    static LoadScenario Merge(LoadScenario parent, LoadScenario child, HashSet<string> declaredKeys)
    {
        if (child.BotCount == 0)
            child.BotCount = parent.BotCount;
        if (child.RampUpSeconds == 0 && parent.RampUpSeconds != 0)
            child.RampUpSeconds = parent.RampUpSeconds;
        if (child.DurationSeconds == 0 && parent.DurationSeconds != 0)
            child.DurationSeconds = parent.DurationSeconds;
        if (string.IsNullOrWhiteSpace(child.SpawnLayout))
            child.SpawnLayout = parent.SpawnLayout;
        if (child.AoiCellSize == 3000 && parent.AoiCellSize != 3000)
            child.AoiCellSize = parent.AoiCellSize;
        if (child.GatekeeperTravelShare == null && parent.GatekeeperTravelShare != null)
            child.GatekeeperTravelShare = parent.GatekeeperTravelShare;
        if (!child.TeleportToSpawnOnEnter && parent.TeleportToSpawnOnEnter)
            child.TeleportToSpawnOnEnter = true;
        child.DataDir ??= parent.DataDir;
        if (child.GatekeeperTripCooldownMs == 3000 && parent.GatekeeperTripCooldownMs != 3000)
            child.GatekeeperTripCooldownMs = parent.GatekeeperTripCooldownMs;
        if (!declaredKeys.Contains("loginPort"))
            child.LoginPort = parent.LoginPort;
        if (!declaredKeys.Contains("zonePort"))
            child.ZonePort = parent.ZonePort;
        if (!declaredKeys.Contains("actionWeights"))
            child.ActionWeights = new Dictionary<string, double>(parent.ActionWeights, StringComparer.OrdinalIgnoreCase);
        else
            child.ActionWeights = new Dictionary<string, double>(child.ActionWeights, StringComparer.OrdinalIgnoreCase);

        if (child.Economy == null && parent.Economy != null)
            child.Economy = parent.Economy;
        if (child.HeavyPlay == null && parent.HeavyPlay != null)
            child.HeavyPlay = parent.HeavyPlay;

        if (!child.ConsistencyTrace.Enabled && parent.ConsistencyTrace.Enabled)
            child.ConsistencyTrace = parent.ConsistencyTrace;

        if (child.Economy == null && parent.Economy != null)
            child.Economy = parent.Economy;

        if (parent.Economy is { Enabled: true } && child.SpawnLayout is "town" or "")
            child.SpawnLayout = parent.SpawnLayout;

        return child;
    }
}