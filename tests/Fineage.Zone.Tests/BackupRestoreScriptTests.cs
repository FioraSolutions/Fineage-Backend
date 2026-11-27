using System.Text.RegularExpressions;
using Fineage.Zone.Persistence;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// The backup drill keeps its own copy of the canonical sidecar list because it is a shell
/// script. That list must stay identical to <see cref="WorldStateSystems.All"/>; a new
/// sidecar that is not backed up is silent data loss on restore.
/// </summary>
public sealed class BackupRestoreScriptTests
{
    [Fact]
    public void Backup_script_archives_every_world_state_sidecar()
    {
        string script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "backup-state.sh"));
        Match block = Regex.Match(
            script,
            @"REQUIRED_SIDECARS=\((?<body>.*?)\)",
            RegexOptions.Singleline);
        Assert.True(block.Success, "backup-state.sh has no REQUIRED_SIDECARS array");

        var listed = Regex.Matches(block.Groups["body"].Value, @"(?m)^\s*(?<name>[a-z0-9-]+\.json)\s*$")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var expected = WorldStateSystems.All
            .Select(FileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, listed.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Only two sidecar files have historical names that differ from the system id.</summary>
    static string FileName(string system) => system switch
    {
        WorldStateSystems.MonsterRace => "monster-race-state.json",
        WorldStateSystems.Punishment => "punishments.json",
        WorldStateSystems.RaidBossPoints => "raid-boss-points-store.json",
        _ => system + ".json",
    };

    [Fact]
    public void Drill_workflow_is_scheduled_and_manual_only()
    {
        string workflow = File.ReadAllText(
            Path.Combine(RepoRoot(), ".github", "workflows", "backup-restore-drill.yml"));
        Assert.Contains("schedule:", workflow, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("restore-drill.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("upload-artifact", workflow, StringComparison.Ordinal);
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "scripts", "run-local-net.sh")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
