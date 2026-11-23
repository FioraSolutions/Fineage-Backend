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
                    // Bots closing TCP on teardown log "exception=IOException: Broken pipe".
                    // That is the client disconnect, not a server fault. A thrown exception
                    // that is not a transport close still matches.
                    if (key == "exception" && IsClientTransportClose(line))
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

    static bool IsClientTransportClose(string line) =>
        line.Contains("Broken pipe", StringComparison.Ordinal) ||
        line.Contains("Connection reset", StringComparison.Ordinal);
}