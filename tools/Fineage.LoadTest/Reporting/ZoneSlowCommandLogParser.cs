using System.Globalization;
using System.Text.RegularExpressions;

namespace Fineage.LoadTest.Reporting;

/// <summary>Parses zone <c>commands stage</c> slow warnings for per-command peaks (e.g. request:enter).</summary>
public static class ZoneSlowCommandLogParser
{
    static readonly Regex LineRx = new(
        @"commands stage tick=\d+ total=([0-9.]+)ms: (.+)",
        RegexOptions.Compiled);

    public static Dictionary<string, double> WorstCommandMs(string? zoneLogPath)
    {
        var worst = new Dictionary<string, double>(StringComparer.Ordinal);
        if (zoneLogPath == null || !File.Exists(zoneLogPath))
            return worst;

        foreach (string line in File.ReadLines(zoneLogPath))
        {
            if (!line.Contains("commands stage", StringComparison.Ordinal))
                continue;
            var m = LineRx.Match(line);
            if (!m.Success)
                continue;
            foreach (Match part in Regex.Matches(m.Groups[2].Value, @"([^=,]+)=([0-9.]+)ms"))
            {
                string cmd = part.Groups[1].Value.Trim();
                if (!double.TryParse(part.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms))
                    continue;
                worst[cmd] = Math.Max(worst.GetValueOrDefault(cmd), ms);
            }
        }

        return worst;
    }

    public static long OccurrenceCount(string? zoneLogPath, string command)
    {
        if (zoneLogPath == null || !File.Exists(zoneLogPath))
            return 0;
        long n = 0;
        foreach (string line in File.ReadLines(zoneLogPath))
        {
            if (line.Contains("commands stage", StringComparison.Ordinal) &&
                line.Contains($"{command}=", StringComparison.Ordinal))
                n++;
        }

        return n;
    }
}