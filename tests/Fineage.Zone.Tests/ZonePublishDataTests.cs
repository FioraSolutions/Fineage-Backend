using System.Text.RegularExpressions;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Publish guard for the data files the Zone reads at startup.
/// <para>
/// <c>Fineage.Zone.csproj</c> lists every <c>data/*.json</c> it copies to the output by hand. A catalog can
/// be added, its JSON committed, every test still green — because tests find <c>data/</c> by walking up the
/// source tree — and then <c>dotnet publish</c> ships without the file and the deployed Zone dies on
/// <see cref="FileNotFoundException"/>. That is exactly what broke <c>deploy-pi</c> (herbs.json, spoil.json,
/// skill-combat.json, skill-passives.json, skill-stat-effects.json, village-masters.json).
/// </para>
/// <para>
/// The test reads the file names straight out of the sources instead of keeping a second list here: every
/// <c>"name.json"</c> literal under <c>src/</c> that names a tracked data file must appear in the csproj.
/// </para>
/// </summary>
public sealed class ZonePublishDataTests
{
    // Structured data loaded through a path built at runtime; not a data/ literal and not published this way.
    static readonly HashSet<string> RuntimeStoreFiles = new(StringComparer.Ordinal)
    {
        "boss-schedule.json",
        "olympiad.json",
        "seven-signs.json",
    };

    [Fact]
    public void Every_data_file_the_zone_reads_is_published()
    {
        string root = RepoRoot();
        var published = CsprojDataFiles(Path.Combine(root, "src", "Fineage.Zone", "Fineage.Zone.csproj"));
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (string name in DataFileLiterals(Path.Combine(root, "src")))
        {
            if (RuntimeStoreFiles.Contains(name))
                continue;
            if (!File.Exists(Path.Combine(root, "data", name)))
                continue;
            if (!published.Contains(name))
                missing.Add(name);
        }

        Assert.True(
            missing.Count == 0,
            "Fineage.Zone.csproj does not publish: " + string.Join(", ", missing) +
            ". Add a <Content Include=\"..\\..\\data\\<file>\" CopyToOutputDirectory=\"PreserveNewest\" /> row.");
    }

    static HashSet<string> CsprojDataFiles(string csproj)
    {
        string text = File.ReadAllText(csproj);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, @"Include=""[^""]*data\\(?<file>[^""\\]+\.json)"""))
            names.Add(m.Groups["file"].Value);
        return names;
    }

    static IEnumerable<string> DataFileLiterals(string srcDirectory)
    {
        foreach (string file in Directory.EnumerateFiles(srcDirectory, "*.cs", SearchOption.AllDirectories))
        {
            // Skip build output that can sit under src/ after a local publish.
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(?<file>[a-z0-9][a-z0-9-]*\\.json)\""))
                yield return m.Groups["file"].Value;
        }
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
