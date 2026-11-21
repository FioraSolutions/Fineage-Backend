using System.Globalization;
using System.Text.RegularExpressions;

namespace Fineage.LoadTest.Reporting;

public sealed class LogIssue
{
    public required string Key { get; init; }
    public int Count { get; set; }
    public string? SampleLine { get; set; }
}

public static class LogScanner
{
    static readonly (string Key, Regex Pattern)[] Rules =
    [
        ("exception", new Regex(@"\bException\b", RegexOptions.IgnoreCase)),
        ("stage_failure", new Regex(@"stage.*fail|StageFailures", RegexOptions.IgnoreCase)),
        ("ledger", new Regex(@"ledger.*(reject|adjust|fail)", RegexOptions.IgnoreCase)),
        ("desync", new Regex(@"desync", RegexOptions.IgnoreCase)),
        ("deadlock", new Regex(@"deadlock", RegexOptions.IgnoreCase)),
        ("timeout", new Regex(@"timeout", RegexOptions.IgnoreCase)),
        ("handler_error", new Regex(@"handler_error", RegexOptions.IgnoreCase))
    ];

    public static List<LogIssue> ScanFiles(IEnumerable<string> paths)
    {
        var map = new Dictionary<string, LogIssue>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (!File.Exists(path))
                continue;
            foreach (string line in File.ReadLines(path))
            {
                foreach (var (key, rx) in Rules)
                {
                    if (!rx.IsMatch(line))
                        continue;
                    if (!map.TryGetValue(key, out LogIssue? issue))
                    {
                        issue = new LogIssue { Key = key, SampleLine = line.Trim() };
                        map[key] = issue;
                    }

                    issue.Count++;
                    break;
                }
            }
        }

        return map.Values.OrderByDescending(i => i.Count).ToList();
    }

    /// <summary>Parses zone overrun warnings (slowest stage lines) for load-test stage breakdown.</summary>
    public static Dictionary<string, double> ParseTickOverrunSlowest(IEnumerable<string> paths)
    {
        var merged = new Dictionary<string, double>(StringComparer.Ordinal);
        var overrun = new Regex(
            @"simulation tick \d+ took ([0-9.]+) ms, over its \d+ ms step; slowest: ([^""]+)",
            RegexOptions.CultureInvariant);
        var stage = new Regex(@"([a-z0-9._-]+)=([0-9.]+)ms", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        foreach (string path in paths)
        {
            if (!File.Exists(path))
                continue;
            foreach (string line in File.ReadLines(path))
            {
                var m = overrun.Match(line);
                if (!m.Success)
                    continue;
                foreach (Match part in stage.Matches(m.Groups[2].Value))
                {
                    string name = part.Groups[1].Value;
                    double ms = double.Parse(part.Groups[2].Value, CultureInfo.InvariantCulture);
                    merged[name] = Math.Max(merged.GetValueOrDefault(name), ms);
                }
            }
        }

        return merged;
    }
}