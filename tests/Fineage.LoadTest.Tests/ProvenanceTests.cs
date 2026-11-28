using Fineage.LoadTest.Metrics;
using Fineage.LoadTest.Reporting;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.LoadTest.Tests;

public sealed class ProvenanceTests
{
    [Fact]
    public void BuildInfo_extracts_the_sdk_source_revision()
    {
        Assert.Equal("cf71c75b", BuildInfo.ParseCommit("1.0.0+cf71c75b"));
        Assert.Equal("cf71c75b", BuildInfo.ParseCommit("1.0.0+cf71c75b.20261126"));
        Assert.Equal("1.0.0", BuildInfo.ParseCommit("1.0.0"));
    }

    [Fact]
    public void Metrics_text_carries_build_info_that_the_parser_reads()
    {
        string text = MetricsText.Prometheus(new MetricsSnapshot(), 0, 0);
        Assert.Contains("fineage_build_info{", text);

        ZoneBuildInfo? build = PrometheusParser.ParseBuildInfo(text);
        Assert.NotNull(build);
        Assert.False(string.IsNullOrWhiteSpace(build.Commit));
        Assert.False(string.IsNullOrWhiteSpace(build.ModuleId));
    }

    [Fact]
    public void Provenance_accepts_a_tied_build_and_rejects_missing_mismatch_and_rebuild()
    {
        var tied = new RunEnvironmentInfo
        {
            GitCommit = "abc123",
            BuildConfiguration = "Debug",
            ZoneBuildCommit = "abc123",
            ZoneBuildConfiguration = "Debug",
            ZoneBuildModuleId = "module-a",
            ZoneBinaryModuleId = "module-a",
            ZoneBinaryPath = "Fineage.Zone.dll"
        };
        Assert.Empty(RunEnvironmentCollector.ProvenanceProblems(tied, "abc123", requireClean: true));

        var missing = new RunEnvironmentInfo { GitCommit = "abc123" };
        Assert.Contains(
            RunEnvironmentCollector.ProvenanceProblems(missing, "abc123"),
            p => p.Contains("fineage_build_info is missing", StringComparison.Ordinal));

        var wrongCommit = new RunEnvironmentInfo
        {
            GitCommit = "abc123",
            ZoneBuildCommit = "def456",
            ZoneBuildModuleId = "module-a",
            ZoneBinaryModuleId = "module-a",
            ZoneBinaryPath = "Fineage.Zone.dll"
        };
        Assert.Contains(
            RunEnvironmentCollector.ProvenanceProblems(wrongCommit, "abc123"),
            p => p.Contains("!= intended", StringComparison.Ordinal));

        var rebuilt = new RunEnvironmentInfo
        {
            GitCommit = "abc123",
            ZoneBuildCommit = "abc123",
            ZoneBuildModuleId = "module-a",
            ZoneBinaryModuleId = "module-b",
            ZoneBinaryPath = "Fineage.Zone.dll"
        };
        Assert.Contains(
            RunEnvironmentCollector.ProvenanceProblems(rebuilt, "abc123"),
            p => p.Contains("rebuilt under the running Zone", StringComparison.Ordinal));
    }

    [Fact]
    public void Clean_provenance_needs_a_clean_tree_and_an_explicit_zone_dll()
    {
        var dirty = new RunEnvironmentInfo
        {
            GitCommit = "abc123",
            ZoneBuildCommit = "abc123",
            ZoneBuildModuleId = "module-a",
            ZoneBinaryModuleId = "module-a",
            ZoneBinaryPath = "Fineage.Zone.dll",
            ZoneBuildConfiguration = "Debug",
            SourceDirty = true
        };
        Assert.Contains(
            RunEnvironmentCollector.ProvenanceProblems(dirty, "abc123", requireClean: true),
            p => p.Contains("compiled source is dirty", StringComparison.Ordinal));

        var docsOnly = new RunEnvironmentInfo
        {
            GitCommit = "abc123",
            ZoneBuildCommit = "abc123",
            ZoneBuildModuleId = "module-a",
            ZoneBinaryModuleId = "module-a",
            ZoneBinaryPath = "Fineage.Zone.dll",
            ZoneBuildConfiguration = "Debug",
            GitDirty = true,
            SourceDirty = false
        };
        Assert.Empty(RunEnvironmentCollector.ProvenanceProblems(docsOnly, "abc123", requireClean: true));

        var noDll = new RunEnvironmentInfo
        {
            GitCommit = "abc123",
            ZoneBuildCommit = "abc123",
            ZoneBuildConfiguration = "Debug",
            ZoneBuildModuleId = "module-a"
        };
        Assert.Contains(
            RunEnvironmentCollector.ProvenanceProblems(noDll, "abc123", requireClean: true),
            p => p.Contains("--zone-dll", StringComparison.Ordinal));
    }

    [Fact]
    public void Clean_provenance_needs_configuration_and_module_id_from_the_loaded_zone()
    {
        var noModule = new RunEnvironmentInfo
        {
            GitCommit = "abc123",
            ZoneBuildCommit = "abc123",
            ZoneBuildConfiguration = "Debug",
            ZoneBuildModuleId = "",
            ZoneBinaryModuleId = "module-a",
            ZoneBinaryPath = "Fineage.Zone.dll"
        };
        Assert.Contains(
            RunEnvironmentCollector.ProvenanceProblems(noModule, "abc123", requireClean: true),
            p => p.Contains("module id is missing", StringComparison.Ordinal));

        var noConfig = new RunEnvironmentInfo
        {
            GitCommit = "abc123",
            ZoneBuildCommit = "abc123",
            ZoneBuildConfiguration = "unknown",
            ZoneBuildModuleId = "module-a",
            ZoneBinaryModuleId = "module-a",
            ZoneBinaryPath = "Fineage.Zone.dll"
        };
        Assert.Contains(
            RunEnvironmentCollector.ProvenanceProblems(noConfig, "abc123", requireClean: true),
            p => p.Contains("configuration is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void ModuleIdOfFile_matches_the_loaded_assembly_mvid()
    {
        string? path = typeof(ProvenanceTests).Assembly.Location;
        string? onDisk = RunEnvironmentCollector.ModuleIdOfFile(path);
        string loaded = typeof(ProvenanceTests).Assembly.ManifestModule.ModuleVersionId.ToString();

        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.Equal(loaded, onDisk);
    }

    [Fact]
    public void Zone_build_info_parser_needs_a_commit_label()
    {
        Assert.Null(PrometheusParser.ParseBuildInfo("fineage_build_info{configuration=\"Debug\"} 1"));

        ZoneBuildInfo? parsed = PrometheusParser.ParseBuildInfo(
            "fineage_build_info{commit=\"abc123\",configuration=\"Debug\",module_id=\"m\",version=\"1.0.0+abc123\"} 1");
        Assert.NotNull(parsed);
        Assert.Equal("abc123", parsed.Commit);
        Assert.Equal("Debug", parsed.Configuration);
        Assert.Equal("m", parsed.ModuleId);
    }
}
