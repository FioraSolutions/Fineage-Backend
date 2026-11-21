using Fineage.LoadTest.Models;
using Fineage.LoadTest.Reporting;

namespace Fineage.LoadTest;

/// <summary>
/// Fineage uses the PacketEnvelope Net protocol; L2J Mobius expects the classic Interlude GameClient wire format.
/// The same headless bot cannot drive both without a full packet adapter (not shipped in this pass).
/// </summary>
public static class MobiusRunReport
{
    public static RunReport ScenarioNotSupported(LoadScenario scenario, int regionWorkers) => new()
    {
        ScenarioName = scenario.Name,
        Target = "mobius",
        RegionWorkers = regionWorkers,
        SpawnLayout = scenario.SpawnLayout,
        BotCount = scenario.BotCount,
        Passed = false,
        FailReasons =
        [
            "Mobius comparison bots require a classic Interlude GameClient adapter; Fineage LoadTest speaks PacketEnvelope Net only.",
            "Use scripts/fetch-mobius-interlude.sh to build Mobius for manual soak; see docs/perf/results-2026-11.md for resource-only notes."
        ],
        StartedAt = DateTimeOffset.UtcNow,
        FinishedAt = DateTimeOffset.UtcNow
    };
}