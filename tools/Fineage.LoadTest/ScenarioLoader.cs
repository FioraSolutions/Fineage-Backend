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

        return scenario;
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

        return child;
    }
}