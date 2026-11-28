using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.RegularExpressions;
using Fineage.Protocol.Health;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.Zone.Tests;

[CollectionDefinition(nameof(MetricInventoryCollection), DisableParallelization = true)]
public sealed class MetricInventoryCollection;

/// <summary>
/// #992: <c>docs/operations/metrics.md</c> is the metric inventory. These tests keep it honest in both directions: every
/// instrument the server records moves the families the inventory names for it, every family /metrics serves is in the
/// inventory for the right process, and every metric an alert or the dashboard reads is in the inventory with a test.
/// They run alone because they record into the process-wide meter and swap the observable gauges' sources.
/// </summary>
[Collection(nameof(MetricInventoryCollection))]
public sealed partial class MetricInventoryTests
{
    /// <summary>One tag set every instrument accepts; the collector keeps the tag names it knows and ignores the rest.</summary>
    static readonly KeyValuePair<string, object?>[] Tags =
    [
        new("endpoint", "zone"),
        new("direction", "out"),
        new("kind", "snapshot"),
        new("stage", "inventory-probe"),
        new("phase", "submit_to_dequeue"),
        new("priority", "reliable"),
        new("lane", "inventory-probe"),
        new("reason", "inventory-probe"),
        new("result", "inventory-probe"),
        new("outcome", "inventory-probe"),
        new("code", "inventory-probe"),
        new("container", "inventory-probe"),
        new("severity", "error"),
        new("command", "inventory-probe"),
        new("source", "player"),
        new("opcode", "inventory-probe"),
        new("pending", 1),
    ];

    [Fact]
    public void Every_recorded_instrument_moves_the_families_the_inventory_names()
    {
        var inventory = Inventory.Load();
        var failures = new List<string>();
        foreach (var instrument in TelemetryInstruments())
        {
            var documented = inventory.Rows.Where(row => row.Instruments.Contains(instrument.Name)).Select(row => row.Family).ToHashSet();
            if (documented.Count == 0)
            {
                failures.Add($"{instrument.Name}: not in docs/operations/metrics.md");
                continue;
            }

            var moved = Moved(instrument);
            foreach (string family in documented.Where(family => !moved.Contains(family)))
                failures.Add($"{instrument.Name}: recorded once, but {family} did not change (moved: {string.Join(", ", moved.Order())})");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Theory]
    [InlineData(MetricsRole.Zone, "zone")]
    [InlineData(MetricsRole.Login, "login")]
    public void Every_exported_family_is_in_the_inventory_for_its_process(MetricsRole role, string process)
    {
        var inventory = Inventory.Load();
        var board = new HealthBoard();
        var savedBoard = FineageTelemetry.Health;
        FineageTelemetry.Health = board;
        FineageTelemetry.RegisterDiskPath("state", Path.GetTempPath());
        var nudges = new List<IDisposable>();
        try
        {
            using var metrics = new MetricsCollector(FineageTelemetry.Meter);
            foreach (var instrument in TelemetryInstruments())
                nudges.Add(Record(instrument));
            var exported = Families(MetricsText.Prometheus(metrics.Snapshot(), 1, 1, role)).Keys.ToHashSet();
            var expected = inventory.Rows.Where(row => row.Processes.Contains(process)).Select(row => row.Family).ToHashSet();
            Assert.Empty(exported.Except(expected).Order());
            Assert.Empty(expected.Except(exported).Order());
        }
        finally
        {
            // Reverse order: two nudges of one source (backlog, backlog_max) must restore the original, not each other.
            for (int i = nudges.Count - 1; i >= 0; i--)
                nudges[i].Dispose();
            FineageTelemetry.Health = savedBoard;
        }
    }

    [Fact]
    public void Inventory_rows_are_complete()
    {
        var inventory = Inventory.Load();
        var instruments = TelemetryInstruments().Select(instrument => instrument.Name).ToHashSet();
        foreach (var row in inventory.Rows)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Meaning), $"{row.Family}: meaning");
            Assert.False(string.IsNullOrWhiteSpace(row.Unit), $"{row.Family}: unit");
            Assert.NotEmpty(row.Processes);
            Assert.Contains(row.Type, new[] { "counter", "gauge", "histogram" });
            foreach (string name in row.Instruments)
                Assert.True(instruments.Contains(name), $"{row.Family}: {name} is not a FineageTelemetry instrument");
            if (row.Instruments.Count == 0)
                Assert.False(string.IsNullOrWhiteSpace(row.Source), $"{row.Family}: a scrape-time family names where it is read");
        }

        Assert.Equal(inventory.Rows.Count, inventory.Rows.Select(row => row.Family).Distinct().Count());
    }

    [Fact]
    public void Alerts_and_dashboard_read_only_inventoried_metrics_and_every_alert_metric_has_a_test()
    {
        var inventory = Inventory.Load();
        var families = inventory.Rows.ToDictionary(row => row.Family);
        string root = RepoRoot();
        var testSources = string.Join("\n", Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        var failures = new List<string>();
        string rulesDir = Path.Combine(root, "monitoring", "prometheus", "rules");
        string alerts = File.ReadAllText(Path.Combine(rulesDir, "fineage-alerts.yml"));
        string dashboards = string.Join("\n", Directory.EnumerateFiles(Path.Combine(root, "monitoring", "grafana", "dashboards"), "*.json")
            .Select(File.ReadAllText));
        string recording = File.ReadAllText(Path.Combine(rulesDir, "fineage-recording.yml"));
        foreach (var (text, isAlert) in new[] { (alerts, true), (recording, true), (dashboards, false) })
        {
            foreach (string reference in MetricReferences(text))
            {
                string family = BaseFamily(reference, families);
                if (!families.TryGetValue(family, out var row))
                {
                    failures.Add($"{reference}: read by {(isAlert ? "a rule" : "the dashboard")} but not in the inventory");
                    continue;
                }

                if (!isAlert)
                    continue;
                if (row.Tests.Count == 0)
                    failures.Add($"{family}: an alert reads it but the inventory names no test");
                foreach (string test in row.Tests.Where(test => !testSources.Contains(" " + test + "(", StringComparison.Ordinal)))
                    failures.Add($"{family}: test {test} does not exist");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Distinct()));
    }

    [Fact]
    public void Health_disk_and_process_families_follow_the_process_state()
    {
        var savedBoard = FineageTelemetry.Health;
        var board = new HealthBoard();
        board.Require("world");
        FineageTelemetry.Health = board;
        string dir = Directory.CreateTempSubdirectory("fineage-disk-").FullName;
        FineageTelemetry.RegisterDiskPath("inventory-probe", dir);
        try
        {
            using var metrics = new MetricsCollector(FineageTelemetry.Meter);
            var before = Families(MetricsText.Prometheus(metrics.Snapshot(), 0, 0, MetricsRole.Zone));
            Assert.Equal(["fineage_health_ready 0"], before["fineage_health_ready"]);
            Assert.Contains("fineage_health_check{check=\"world\"} 0", before["fineage_health_check"]);
            Assert.Equal(["fineage_health_live 1"], before["fineage_health_live"]);
            board.Complete("world");
            var allocate = new byte[4 << 20];
            GC.KeepAlive(allocate);
            GC.Collect(0);
            var after = Families(MetricsText.Prometheus(metrics.Snapshot(), 0, 0, MetricsRole.Zone));
            Assert.Equal(["fineage_health_ready 1"], after["fineage_health_ready"]);
            Assert.True(Value(after, "fineage_disk_free_bytes", "{path=\"inventory-probe\"}") > 0);
            Assert.True(Value(after, "fineage_disk_size_bytes", "{path=\"inventory-probe\"}") >= Value(after, "fineage_disk_free_bytes", "{path=\"inventory-probe\"}"));
            Assert.Equal(1, Value(after, "fineage_disk_probe_ok", "{path=\"inventory-probe\"}"));
            Assert.True(Value(after, "process_resident_memory_bytes", "") > 0);
            Assert.True(Value(after, "dotnet_gc_allocated_bytes_total", "") >= Value(before, "dotnet_gc_allocated_bytes_total", "") + (4 << 20));
            Assert.True(Value(after, "dotnet_gc_collections_total", "{generation=\"0\"}") > Value(before, "dotnet_gc_collections_total", "{generation=\"0\"}"));

            Directory.Delete(dir);
            var gone = Families(MetricsText.Prometheus(metrics.Snapshot(), 0, 0, MetricsRole.Zone));
            Assert.Equal(0, Value(gone, "fineage_disk_probe_ok", "{path=\"inventory-probe\"}"));
            Assert.DoesNotContain(gone["fineage_disk_free_bytes"], line => line.Contains("inventory-probe", StringComparison.Ordinal));
        }
        finally
        {
            FineageTelemetry.Health = savedBoard;
            FineageTelemetry.RegisterDiskPath("inventory-probe", Path.GetTempPath());
        }
    }

    [Fact]
    public void Login_scrape_does_not_serve_zone_simulation_families()
    {
        using var metrics = new MetricsCollector(FineageTelemetry.Meter);
        var login = Families(MetricsText.Prometheus(metrics.Snapshot(), 0, 0, MetricsRole.Login));
        Assert.DoesNotContain("fineage_tick_ms", login.Keys);
        Assert.DoesNotContain("fineage_fanout_queue_depth", login.Keys);
        Assert.DoesNotContain("fineage_ledger_flush_failures_total", login.Keys);
        Assert.Contains("process_resident_memory_bytes", login.Keys);
    }

    /// <summary>Families whose samples differ after one recording of <paramref name="instrument"/>.</summary>
    static HashSet<string> Moved(Instrument instrument)
    {
        using var metrics = new MetricsCollector(FineageTelemetry.Meter);
        var before = Families(MetricsText.Prometheus(metrics.Snapshot(), 0, 0, MetricsRole.Zone));
        using var nudge = Record(instrument);
        var after = Families(MetricsText.Prometheus(metrics.Snapshot(), 0, 0, MetricsRole.Zone));
        return after.Where(pair => !before.TryGetValue(pair.Key, out var lines) || !lines.SequenceEqual(pair.Value))
            .Select(pair => pair.Key).ToHashSet();
    }

    /// <summary>Records one measurement; an observable gauge gets a non-zero source until the result is disposed.</summary>
    static IDisposable Record(Instrument instrument)
    {
        switch (instrument)
        {
            case Counter<long> counter:
                counter.Add(3, Tags);
                return Nothing.Instance;
            case UpDownCounter<long> upDown:
                upDown.Add(1, Tags);
                return new Undo(() => upDown.Add(-1, Tags));
            case Histogram<double> histogram:
                histogram.Record(7.5, Tags);
                return Nothing.Instance;
            case Histogram<long> histogram:
                histogram.Record(7, Tags);
                return Nothing.Instance;
            case Histogram<int> histogram:
                histogram.Record(7, Tags);
                return Nothing.Instance;
            default:
                if (Nudges.TryGetValue(instrument.Name, out var nudge))
                    return nudge();
                throw new InvalidOperationException(
                    $"{instrument.Name} ({instrument.GetType().Name}): add a nudge so the inventory test can prove it moves");
        }
    }

    /// <summary>How each observable gauge is made to read non-zero, through the same source production sets.</summary>
    static readonly Dictionary<string, Func<IDisposable>> Nudges = new(StringComparer.Ordinal)
    {
        ["fineage.tick.max_ms"] = () =>
        {
            FineageTelemetry.NoteTickRunning(987_654);
            return new Undo(FineageTelemetry.ClearTickRunning);
        },
        ["fineage.command.queue.age_max_ms"] = () =>
        {
            FineageTelemetry.NoteCommandQueueAge(Unique());
            return Nothing.Instance;
        },
        ["fineage.agent.command.queue.oldest_age_ms"] = () =>
        {
            FineageTelemetry.NoteAgentCommandQueueOldestAge(Unique());
            return new Undo(() => FineageTelemetry.NoteAgentCommandQueueOldestAge(0));
        },
        ["fineage.step.watchdog.stall_max_ms"] = () =>
        {
            FineageTelemetry.NoteWatchdogStall(Unique());
            return Nothing.Instance;
        },
        ["fineage.step.watchdog.stalls"] = () =>
        {
            FineageTelemetry.NoteWatchdogEpisode();
            return Nothing.Instance;
        },
        ["fineage.persist.pending"] = () => Swap(() => FineageTelemetry.PersistPendingCount, v => FineageTelemetry.PersistPendingCount = v, () => 7),
        ["fineage.persist.oldest_age_ms"] = () => Swap(() => FineageTelemetry.PersistOldestAgeMs, v => FineageTelemetry.PersistOldestAgeMs = v, () => 7.5),
        ["fineage.economy.persist.pending"] = () =>
            Swap(() => FineageTelemetry.EconomyPersistPendingCount, v => FineageTelemetry.EconomyPersistPendingCount = v, () => 7),
        ["fineage.economy.persist.oldest_age_ms"] = () =>
            Swap(() => FineageTelemetry.EconomyPersistOldestAgeMs, v => FineageTelemetry.EconomyPersistOldestAgeMs = v, () => 7.5),
        ["fineage.npc.activation.backlog"] = () =>
            Swap(() => FineageTelemetry.ActivationBacklog, v => FineageTelemetry.ActivationBacklog = v, () => new NpcActivationBacklogReading(7, 7, 7, 7)),
        ["fineage.npc.activation.backlog_max"] = () =>
            Swap(() => FineageTelemetry.ActivationBacklog, v => FineageTelemetry.ActivationBacklog = v, () => new NpcActivationBacklogReading(7, 7, 7, 7)),
        ["fineage.zone.online"] = () => Swap(() => FineageTelemetry.OnlineCount, v => FineageTelemetry.OnlineCount = v, () => 7),
        ["fineage.command.ingress.queued"] = () =>
            Swap(() => FineageTelemetry.CommandIngressDepth, v => FineageTelemetry.CommandIngressDepth = v, () => 7),
    };

    static double _unique = 1_000_000;

    /// <summary>A value larger than any earlier one, so a since-start maximum moves.</summary>
    static double Unique() => _unique += 1_000;

    static IDisposable Swap<T>(Func<T?> get, Action<T?> set, T value) where T : class
    {
        var saved = get();
        set(value);
        return new Undo(() => set(saved));
    }

    static IEnumerable<Instrument> TelemetryInstruments() =>
        typeof(FineageTelemetry).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => typeof(Instrument).IsAssignableFrom(field.FieldType))
            .Select(field => (Instrument)field.GetValue(null)!)
            .OrderBy(instrument => instrument.Name, StringComparer.Ordinal);

    /// <summary>Family → sample lines, from <c># TYPE</c> declarations; histogram samples keep their suffixes.</summary>
    internal static Dictionary<string, List<string>> Families(string text)
    {
        var families = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string? current = null;
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("# TYPE ", StringComparison.Ordinal))
            {
                current = line.Split(' ')[2];
                families.TryAdd(current, []);
                continue;
            }

            if (line.StartsWith('#'))
                continue;
            Assert.True(current != null && line.StartsWith(current, StringComparison.Ordinal), $"sample outside its family: {line}");
            families[current!].Add(line);
        }

        return families;
    }

    static double Value(Dictionary<string, List<string>> families, string family, string labels)
    {
        string prefix = family + labels + " ";
        string line = Assert.Single(families[family], sample => sample.StartsWith(prefix, StringComparison.Ordinal));
        return double.Parse(line[prefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    static IEnumerable<string> MetricReferences(string text) =>
        MetricName().Matches(text).Select(match => match.Value).Distinct();

    static string BaseFamily(string reference, Dictionary<string, InventoryRow> families)
    {
        if (families.ContainsKey(reference))
            return reference;
        foreach (string suffix in new[] { "_bucket", "_sum", "_count" })
        {
            if (reference.EndsWith(suffix, StringComparison.Ordinal)
                && families.TryGetValue(reference[..^suffix.Length], out var row) && row.Type == "histogram")
                return row.Family;
        }

        return reference;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_:])(?:fineage|process|dotnet)_[a-z0-9_]+(?![A-Za-z0-9_:])")]
    private static partial Regex MetricName();

    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Fineage.Backend.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("repository root not found above " + AppContext.BaseDirectory);
    }

    internal sealed record InventoryRow(
        string Family,
        string Type,
        string Unit,
        IReadOnlySet<string> Processes,
        IReadOnlyList<string> Instruments,
        string Source,
        string Meaning,
        IReadOnlyList<string> Tests);

    internal sealed class Inventory
    {
        public required IReadOnlyList<InventoryRow> Rows { get; init; }

        /// <summary>Rows of the tables whose header starts <c>| Family |</c>; backticked names, comma-separated lists.</summary>
        public static Inventory Load()
        {
            string path = Path.Combine(RepoRoot(), "docs", "operations", "metrics.md");
            var rows = new List<InventoryRow>();
            bool inTable = false;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("| Family |", StringComparison.Ordinal))
                {
                    inTable = true;
                    continue;
                }

                if (!line.StartsWith('|'))
                {
                    inTable = false;
                    continue;
                }

                if (!inTable || line.StartsWith("| ---", StringComparison.Ordinal))
                    continue;
                string[] cells = line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
                Assert.True(cells.Length == 8, $"metrics.md row needs 8 cells: {line}");
                rows.Add(new InventoryRow(
                    Names(cells[0]).Single(),
                    cells[1],
                    cells[2],
                    cells[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(),
                    Names(cells[4]),
                    cells[5],
                    cells[6],
                    Names(cells[7])));
            }

            Assert.NotEmpty(rows);
            return new Inventory { Rows = rows };
        }

        static List<string> Names(string cell) =>
            Regex.Matches(cell, "`([^`]+)`").Select(match => match.Groups[1].Value).ToList();
    }

    sealed class Undo(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}
