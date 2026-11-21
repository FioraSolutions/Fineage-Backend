namespace Fineage.LoadTest.Models;

public sealed class LoadScenario
{
    public string Name { get; set; } = "unnamed";
    public int BotCount { get; set; } = 50;
    public int RampUpSeconds { get; set; } = 30;
    public int DurationSeconds { get; set; } = 120;
    public string SpawnLayout { get; set; } = "town";
    public int SpawnX { get; set; } = 46045;
    public int SpawnY { get; set; } = 41251;
    public int SpawnZ { get; set; } = -3440;
    public string AccountPrefix { get; set; } = "loadbot";
    public string Password { get; set; } = "loadtest-pass-v1";
    public string LoginHost { get; set; } = "127.0.0.1";
    public int LoginPort { get; set; } = 9106;
    public int ZonePort { get; set; } = 9777;
    public int ClassId { get; set; } = 25;
    public int Race { get; set; } = 1;
    public Dictionary<string, double> ActionWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["move"] = 0.35,
        ["attack"] = 0.2,
        ["skill"] = 0.1,
        ["say"] = 0.1,
        ["pickup"] = 0.1,
        ["trade"] = 0.05,
        ["party"] = 0.05,
        ["teleport"] = 0.05
    };
}