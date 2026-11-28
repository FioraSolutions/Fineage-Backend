using System.Text.RegularExpressions;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Source-backed guard for the shared target-damage path: gameplay code may not subtract HP directly.
/// Healing, max-vital setup, death/respawn and explicit special effects are allowed, but a direct damage write
/// must either live in <c>ZoneWorldTargetDamage.cs</c> or carry an explicit <c>hp-write: &lt;reason&gt;</c> marker.
/// The guard also catches the indirect form <c>target.CurHp = nextHp;</c>.
/// </summary>
public sealed class DirectHpWriteGuardTests
{
    static readonly Regex DirectSubtract = new(@"\.CurHp\s*-=", RegexOptions.Compiled);
    static readonly Regex SubtractAssignment = new(@"\.CurHp\s*=\s*[^;]*\.CurHp\s*-", RegexOptions.Compiled);
    static readonly Regex LocalAssignment = new(@"\.CurHp\s*=\s*[A-Za-z_][A-Za-z0-9_]*\s*;", RegexOptions.Compiled);

    [Fact]
    public void Gameplay_code_does_not_bypass_the_shared_damage_function()
    {
        string root = RepoRoot();
        string source = Path.Combine(root, "src", "Fineage.Zone");
        var files = Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(files);

        var violations = new List<string>();
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(root, file);
            // The shared function itself and the ZoneEntity property setter are the only structural exceptions.
            if (relative.EndsWith(Path.Combine("World", "ZoneWorldTargetDamage.cs"), StringComparison.Ordinal)
                || relative.EndsWith(Path.Combine("World", "ZoneEntity.cs"), StringComparison.Ordinal))
                continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                bool direct = DirectSubtract.IsMatch(line)
                    || SubtractAssignment.IsMatch(line)
                    || LocalAssignment.IsMatch(line);
                if (!direct)
                    continue;
                if (line.Contains("hp-write:", StringComparison.Ordinal)
                    || line.Contains("ForTest", StringComparison.Ordinal))
                    continue;
                violations.Add($"{relative}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "Direct gameplay CurHp damage bypasses the shared ApplyTargetDamage path. Route it through " +
            "ZoneWorld.ApplyTargetDamage or annotate the line with '// hp-write: <reason>'.\n" +
            string.Join("\n", violations));
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Fineage.Backend.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("could not locate the repository root");
    }
}
