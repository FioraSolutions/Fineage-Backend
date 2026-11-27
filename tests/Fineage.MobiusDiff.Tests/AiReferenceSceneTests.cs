using Fineage.Game;
using Fineage.MobiusDiff;
using Fineage.Zone.Npcs;
using Xunit;

namespace Fineage.MobiusDiff.Tests;

/// <summary>
/// Behavioural AI reference set (#980). These tests are deliberately Slow: the full catalog drives the
/// production ZoneWorld for every scene and belongs in the nightly differential job, not the fast PR gate.
/// </summary>
[Trait("Category", "Slow")]
public sealed class AiReferenceSceneTests
{
    static readonly string Root = RepoPaths.FindRoot();
    static readonly string SceneDirectory = Path.Combine(Root, "tools", "Fineage.MobiusDiff", "scenes", "ai");
    static readonly string ManifestPath = Path.Combine(SceneDirectory, "manifest.json");

    static IReadOnlyList<(string Path, AiScene Scene)> LoadScenes() =>
        Directory.GetFiles(SceneDirectory, "*.json")
            .Where(static f => !string.Equals(Path.GetFileName(f), "manifest.json", StringComparison.Ordinal))
            .OrderBy(static f => f, StringComparer.Ordinal)
            .Select(path => (path, AiSceneJson.Load(path)))
            .ToList();

    static AiTraceFile LoadGolden(AiScene scene)
    {
        string path = Path.Combine(Root, scene.Oracle.Golden);
        Assert.True(File.Exists(path), $"Golden missing for {scene.Id}: {path}");
        return AiSceneJson.LoadTrace(path);
    }

    [Fact]
    public void Coverage_manifest_is_strict_and_matches_the_catalog()
    {
        var scenes = LoadScenes();
        Assert.InRange(scenes.Count, 30, 50);
        var manifest = AiCoverageManifest.Load(ManifestPath);
        manifest.Validate(scenes.Select(static s => s.Scene).ToList(), ManifestPath);

        // Removing any required category's last scene must fail closed, not silently downgrade coverage.
        var reduced = scenes.Select(static s => s.Scene)
            .Where(s => s.Category != "raid")
            .ToList();
        Assert.Throws<InvalidDataException>(() => manifest.Validate(reduced, ManifestPath));
    }

    [Fact]
    public void Every_scene_has_an_independent_non_runtime_oracle_provenance()
    {
        foreach (var (_, scene) in LoadScenes())
        {
            var golden = LoadGolden(scene);
            Assert.Equal(scene.Id, golden.Scenario);
            Assert.Equal("mobius-ai-reference-trace", golden.Oracle.Kind);
            Assert.False(golden.Oracle.FineageOutputUsed,
                $"{scene.Id}: expected values must not come from Fineage output");
            Assert.False(golden.Oracle.FullServerVerified,
                $"{scene.Id}: a rule-model trace must not claim full Mobius server verification");
            Assert.False(string.IsNullOrWhiteSpace(golden.Oracle.Blocker),
                $"{scene.Id}: the missing-Headless-Mobius blocker must be recorded");
            Assert.Contains(golden.Oracle.Sources, s => s.Contains("Mobius CT0", StringComparison.Ordinal));
            Assert.NotEmpty(golden.Steps);
            Assert.All(golden.Steps, step => Assert.NotEmpty(step.Values));
        }
    }

    [Fact]
    public async Task Full_catalog_runs_and_known_real_divergences_remain_red()
    {
        var game = GameData.LoadDefault();
        var npcs = NpcCatalog.LoadDefault();
        var knownRed = new HashSet<string>(StringComparer.Ordinal)
        {
            "activation-idle-leave-92s",          // Mobius 90 s region hysteresis vs current activation lifetime
            "activation-returning-stays-awake",   // returnHome intent after the target leaves
            "death-aggro-cleared",                // AggroList cleared on death
            "hate-clear-on-kill",                 // AggroList cleared on death
            "return-clears-hate"                  // returnHome clears hate / starts return
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, scene) in LoadScenes())
        {
            seen.Add(scene.Id);
            var expected = LoadGolden(scene);
            var actual = await AiSceneProbe.RunAsync(scene, game, npcs);
            var report = AiSceneRunner.Run(scene, Path.GetRelativePath(Root, path), expected, actual);
            Assert.NotEmpty(report.Rows);
            Assert.Equal(report.Passed + report.Failed, report.Rows.Count);
            Assert.All(report.Rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Parameter)));
            if (knownRed.Contains(scene.Id))
                Assert.False(report.Pass, $"{scene.Id} must stay red until the real AI divergence is fixed");
        }
        Assert.Equal(43, seen.Count);
    }

    [Fact]
    public async Task Negative_control_mutation_makes_the_comparator_fail()
    {
        var scenePath = Path.Combine(SceneDirectory, "aggro-basic-skeleton.json");
        var scene = AiSceneJson.Load(scenePath);
        var expected = LoadGolden(scene);
        var actual = await AiSceneProbe.RunAsync(scene, GameData.LoadDefault(), NpcCatalog.LoadDefault());
        var report = AiSceneRunner.Run(
            scene,
            Path.GetRelativePath(Root, scenePath),
            expected,
            actual,
            mutate => mutate.Steps[0].Values["npc.mob.target"] = "wrong-target");

        Assert.False(report.Pass);
        Assert.Contains(report.Rows, row => !row.Pass && row.Parameter == "npc.mob.target");
    }
}
