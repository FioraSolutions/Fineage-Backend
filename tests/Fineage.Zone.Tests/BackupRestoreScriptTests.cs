using System.Text.RegularExpressions;
using Fineage.Zone.Persistence;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// #996: world subsystem state is the <c>world_state</c> table, so the backup must prove the dump holds it, and the shell
/// scripts must not go back to requiring the pre-#996 sidecar files (a backup that only works while a legacy file exists
/// would refuse every post-cutover state directory).
/// </summary>
public sealed class BackupRestoreScriptTests
{
    [Fact]
    public void Backup_script_requires_world_state_in_the_dump_and_no_legacy_sidecar()
    {
        string script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "backup-state.sh"));
        Match block = Regex.Match(script, @"REQUIRED_SIDECARS=\((?<body>.*?)\)", RegexOptions.Singleline);
        Assert.True(block.Success, "backup-state.sh has no REQUIRED_SIDECARS array");
        Assert.Equal("", block.Groups["body"].Value.Trim());

        var tableLists = Regex.Matches(script, @"for required in (?<tables>[a-z_ ]+); do");
        Assert.NotEmpty(tableLists);
        Assert.All(tableLists, list => Assert.Contains("world_state", list.Groups["tables"].Value.Split(' ')));

        foreach (string legacy in WorldStateCatalog.All.Select(kind => kind.LegacyFileName))
        {
            Assert.DoesNotContain(legacy, script, StringComparison.Ordinal);
            Assert.DoesNotContain(legacy, File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "restore-drill.sh")), StringComparison.Ordinal);
        }

        // pg_dump rejects the project's Npgsql URL ("invalid connection option Host").
        Assert.Contains("libpq_url", script, StringComparison.Ordinal);
    }

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
