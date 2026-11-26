using System.Globalization;
using System.Text.RegularExpressions;

namespace Fineage.LoadTest.Reporting;

public sealed class LogIssue
{
    public required string Key { get; init; }
    public int Count { get; set; }
    public string? SampleLine { get; init; }
}

/// <summary>
/// Scans the zone/login log of a run for fault signals.
/// </summary>
/// <remarks>
/// Two rules decide whether a line belongs to the run and whether it is a fault, and both used to be absent:
/// <list type="number">
/// <item>
/// The scan covers the run's own window. A soak appends to a log file that already holds earlier runs, so a
/// whole-file scan reported a previous run's disconnect as this run's failure forever.
/// </item>
/// <item>
/// A fault is an error/critical entry. A <c>warn:</c> line that mentions an exception is a diagnostic: the
/// zone's disconnect path logs <c>warn: net disconnect … exception=IOException</c> whenever a peer drops its
/// socket, which the load runner makes happen on purpose at the end of a scenario. Counting that as a server
/// fault made every clean soak report fail.
/// </item>
/// </list>
/// Continuation lines (a stack trace under its entry) inherit the entry's timestamp and level, so a trace does
/// not count as one issue per frame.
/// </remarks>
public static class LogScanner
{
    /// <summary>Rules that describe a fault on their own, whatever the entry level.</summary>
    static readonly (string Key, Regex Pattern)[] FaultRules =
    [
        ("stage_failure", new Regex(@"stage.*fail|StageFailures", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("desync", new Regex(@"desync", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("deadlock", new Regex(@"deadlock", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("handler_error", new Regex(@"handler_error", RegexOptions.IgnoreCase | RegexOptions.Compiled))
    ];

    /// <summary>Rules that only count when the entry itself is an error.</summary>
    /// <remarks>
    /// <c>Exception</c> has no leading word boundary on purpose: the type name is <c>InvalidOperationException</c>,
    /// <c>IOException</c>, and so on, so <c>\bException\b</c> never matches a real .NET exception type.
    /// </remarks>
    static readonly (string Key, Regex Pattern)[] ErrorLevelRules =
    [
        ("exception", new Regex(@"Exception\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("timeout", new Regex(@"timeout", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("ledger", new Regex(@"ledger.*(reject|adjust|fail)", RegexOptions.IgnoreCase | RegexOptions.Compiled))
    ];

    /// <summary>Structured host log: <c>ts=2026-11-26T08:34:28.5203940+00:00 level=Error …</c>.</summary>
    static readonly Regex StructuredRx = new(
        @"^ts=(?<ts>\S+)\s+level=(?<level>\w+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Console log: <c>2026-11-26T08:34:28.5203940+00:00 warn: …</c>.</summary>
    static readonly Regex ConsoleRx = new(
        @"^(?<ts>\d{4}-\d\d-\d\dT\S+)\s+(?<level>trce|dbug|info|warn|fail|crit|error|fatal)\s*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static List<LogIssue> ScanFiles(IEnumerable<string> paths) => ScanFiles(paths, null, null);

    /// <param name="from">First instant the run owns; <c>null</c> scans the whole file.</param>
    /// <param name="to">Last instant the run owns; <c>null</c> scans to the end of the file.</param>
    public static List<LogIssue> ScanFiles(
        IEnumerable<string> paths,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        var map = new Dictionary<string, LogIssue>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (!File.Exists(path))
                continue;

            ScanFile(path, map, from, to);
        }

        return map.Values.OrderByDescending(i => i.Count).ToList();
    }

    static void ScanFile(
        string path,
        Dictionary<string, LogIssue> map,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        DateTimeOffset? entryTs = null;
        bool entryIsError = false;
        bool entryNoted = false;

        foreach (string line in File.ReadLines(path))
        {
            if (TryReadEntry(line, out DateTimeOffset ts, out string level))
            {
                entryTs = ts;
                entryIsError = IsErrorLevel(level);
                entryNoted = false;
            }
            else if (entryTs == null)
            {
                // Bare text before the first timestamped entry (a startup banner): not an issue, and it has no
                // instant to place it in the run window.
                continue;
            }

            if (entryTs is not { } at || !InWindow(at, from, to))
                continue;

            // One issue per entry: a stack trace's frames belong to the entry above them, not to themselves.
            if (entryNoted)
                continue;

            if (NoteFirst(map, FaultRules, line) || (entryIsError && NoteFirst(map, ErrorLevelRules, line)))
                entryNoted = true;
        }
    }

    static bool NoteFirst(
        Dictionary<string, LogIssue> map,
        (string Key, Regex Pattern)[] rules,
        string line)
    {
        foreach (var (key, rx) in rules)
        {
            if (!rx.IsMatch(line))
                continue;
            Note(map, key, line);
            return true;
        }

        return false;
    }

    static void Note(Dictionary<string, LogIssue> map, string key, string line)
    {
        if (!map.TryGetValue(key, out LogIssue? issue))
        {
            issue = new LogIssue { Key = key, SampleLine = line.Trim() };
            map[key] = issue;
        }

        issue.Count++;
    }

    static bool InWindow(DateTimeOffset at, DateTimeOffset? from, DateTimeOffset? to) =>
        (from == null || at >= from.Value) && (to == null || at <= to.Value);

    /// <summary>The entry's timestamp and level from its leading marker, or false for a continuation line.</summary>
    static bool TryReadEntry(string line, out DateTimeOffset ts, out string level)
    {
        ts = default;
        level = "";
        if (line.Length < 24)
            return false;

        Match m = StructuredRx.Match(line);
        if (!m.Success)
            m = ConsoleRx.Match(line);
        if (!m.Success)
            return false;
        if (!DateTimeOffset.TryParse(m.Groups["ts"].Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out ts))
            return false;
        level = m.Groups["level"].Value;
        return true;
    }

    static bool IsErrorLevel(string level) =>
        level.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
        level.Equals("err", StringComparison.OrdinalIgnoreCase) ||
        level.Equals("fail", StringComparison.OrdinalIgnoreCase) ||
        level.Equals("crit", StringComparison.OrdinalIgnoreCase) ||
        level.Equals("critical", StringComparison.OrdinalIgnoreCase) ||
        level.Equals("fatal", StringComparison.OrdinalIgnoreCase);
}
