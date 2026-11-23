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
            scenario = Merge(parent, scenario);
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
        var unknown = scenario.ActionWeights.Keys.Where(k => !KnownActions.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"scenario {scenario.Name}: unknown action(s) {string.Join(", ", unknown)}; known: {string.Join(", ", KnownActions)}. " +
                "Town-to-town teleports are gatekeeperTravelShare, not an action weight.");
        }

        _ = scenario.ResolvedGatekeeperTravelShare;
    }

    static LoadScenario Merge(LoadScenario parent, LoadScenario child)
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
        if (child.ActionWeights.Count == 0)
            child.ActionWeights = new Dictionary<string, double>(parent.ActionWeights, StringComparer.OrdinalIgnoreCase);
        else
        {
            foreach (var (k, v) in parent.ActionWeights)
            {
                if (!child.ActionWeights.ContainsKey(k))
                    child.ActionWeights[k] = v;
            }
        }

        if (child.Economy == null && parent.Economy != null)
            child.Economy = parent.Economy;
        if (child.HeavyPlay == null && parent.HeavyPlay != null)
            child.HeavyPlay = parent.HeavyPlay;

        return child;
    }
}