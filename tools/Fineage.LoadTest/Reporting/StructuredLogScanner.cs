using System.Text.RegularExpressions;

namespace Fineage.LoadTest.Reporting;

public sealed class LogLevelCount
{
    public required string Level { get; init; }
    public required string Template { get; init; }
    public int Count { get; set; }
}

public sealed class DistinctError
{
    public required string Template { get; init; }
    public required string FirstLine { get; init; }
}

public sealed class StructuredLogSummary
{
    public List<LogLevelCount> Counts { get; init; } = [];
    public List<DistinctError> FirstErrors { get; init; } = [];
    public string? MissingLogPaths { get; init; }
}

public static class StructuredLogScanner
{
    static readonly Regex LevelRx = new(@"\b(ERR|ERROR|WRN|WARN|WARNING)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static StructuredLogSummary Scan(IEnumerable<string> paths, int maxFirstErrors = 20, int lineTruncate = 240)
    {
        var counts = new Dictionary<string, LogLevelCount>(StringComparer.Ordinal);
        var firstErrors = new Dictionary<string, DistinctError>(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                missing.Add(path);
                continue;
            }

            foreach (string line in File.ReadLines(path))
            {
                var levelMatch = LevelRx.Match(line);
                if (!levelMatch.Success)
                    continue;
                string level = NormalizeLevel(levelMatch.Groups[1].Value);
                if (level is not ("Error" or "Warning"))
                    continue;

                string template = TemplateOf(line);
                string key = $"{level}|{template}";
                if (!counts.TryGetValue(key, out LogLevelCount? row))
                {
                    row = new LogLevelCount { Level = level, Template = template, Count = 0 };
                    counts[key] = row;
                }

                row.Count++;

                if (level == "Error" && !firstErrors.ContainsKey(template))
                {
                    string truncated = line.Length <= lineTruncate ? line.Trim() : line[..lineTruncate].Trim() + "…";
                    firstErrors[template] = new DistinctError { Template = template, FirstLine = truncated };
                }
            }
        }

        return new StructuredLogSummary
        {
            Counts = counts.Values.OrderByDescending(c => c.Count).Take(40).ToList(),
            FirstErrors = firstErrors.Values.Take(maxFirstErrors).ToList(),
            MissingLogPaths = missing.Count > 0 ? string.Join(", ", missing) : null
        };
    }

    static string NormalizeLevel(string raw) => raw.ToUpperInvariant() switch
    {
        "ERR" or "ERROR" => "Error",
        "WRN" or "WARN" or "WARNING" => "Warning",
        _ => raw
    };

    static string TemplateOf(string line)
    {
        string t = Regex.Replace(line, @"\b\d+\b", "N");
        t = Regex.Replace(t, @"0x[0-9a-fA-F]+", "0xHEX");
        t = Regex.Replace(t, @"[0-9]{5,}", "N");
        t = Regex.Replace(t, @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", "GUID");
        if (t.Length > 120)
            t = t[..120];
        return t.Trim();
    }
}