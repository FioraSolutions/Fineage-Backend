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
        return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? System.Text.Json.JsonSerializer.Deserialize<LoadScenario>(text,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("empty scenario")
            : Yaml.Deserialize<LoadScenario>(text)
                ?? throw new InvalidOperationException("empty scenario");
    }
}