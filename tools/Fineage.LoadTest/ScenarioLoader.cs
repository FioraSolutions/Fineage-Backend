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
            scenario = Merge(parent, scenario, DeclaresKey(path, text, "actionWeights"));
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
        "economy_store", "economy_manufacture", "economy_manor", "economy_pickup_drop"
    };

    /// <summary>True when the scenario file itself sets top-level <paramref name="key"/> (not a class default).</summary>
    static bool DeclaresKey(string path, string text, string key)
    {
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            return doc.RootElement.EnumerateObject().Any(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
        }

        var raw = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object?>>(text);
        return raw != null && raw.Keys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Child values win. <c>actionWeights</c> is a whole mix: a child that declares it replaces the parent's
    /// mix, a child that omits it inherits the parent's. Mixing the two let the base smoke mix
    /// (attack/skill/pickup/trade/party, summing to 1.03) shadow every economy action of the hammer.
    /// </summary>
    static LoadScenario Merge(LoadScenario parent, LoadScenario child, bool childDeclaresActionWeights)
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
        child.GatekeeperTravelShare ??= parent.GatekeeperTravelShare;
        child.DataDir ??= parent.DataDir;
        if (child.GatekeeperTripCooldownMs == 3000 && parent.GatekeeperTripCooldownMs != 3000)
            child.GatekeeperTripCooldownMs = parent.GatekeeperTripCooldownMs;
        if (!childDeclaresActionWeights)
            child.ActionWeights = new Dictionary<string, double>(parent.ActionWeights, StringComparer.OrdinalIgnoreCase);
        else
            child.ActionWeights = new Dictionary<string, double>(child.ActionWeights, StringComparer.OrdinalIgnoreCase);

        if (child.Economy == null && parent.Economy != null)
            child.Economy = parent.Economy;
        if (child.HeavyPlay == null && parent.HeavyPlay != null)
            child.HeavyPlay = parent.HeavyPlay;

        return child;
    }
}