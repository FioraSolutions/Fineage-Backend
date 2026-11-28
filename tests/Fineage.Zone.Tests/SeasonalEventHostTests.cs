using Fineage.Zone.Drops;
using Fineage.Zone.SeasonalEvents;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius LongTimeEvent host: Christmas / L2Day / HeavyMedal time windows (#934).</summary>
public sealed class SeasonalEventHostTests
{
    [Fact]
    public void Missing_event_data_logs_event_id()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fineage-seasonal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "scripts", "events"));
        try
        {
            var warnings = new List<string>();
            var host = SeasonalEventHost.LoadFromDataRoot(dir, warnings.Add);
            Assert.Equal(0, host.LoadedCount);
            Assert.Contains(warnings, w => w.Contains("eventId=Christmas", StringComparison.Ordinal));
            Assert.Contains(warnings, w => w.Contains("eventId=L2Day", StringComparison.Ordinal));
            Assert.Contains(warnings, w => w.Contains("eventId=HeavyMedal", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Loads_L2Day_and_HeavyMedal_from_config_xml()
    {
        string data = Fineage.Zone.Spawns.GameDataPaths.FindDataDirectory(AppContext.BaseDirectory)
            ?? Fineage.Zone.Spawns.GameDataPaths.FindDataDirectory(Directory.GetCurrentDirectory())
            ?? throw new InvalidOperationException("data/ not found");

        var warnings = new List<string>();
        var host = SeasonalEventHost.LoadFromDataRoot(data, warnings.Add);
        Assert.True(host.TryGet("L2Day", out var l2));
        Assert.True(host.TryGet("HeavyMedal", out var medals));
        Assert.False(host.TryGet("Christmas", out _));
        Assert.Contains(warnings, w => w.Contains("eventId=Christmas", StringComparison.Ordinal));
        Assert.NotEmpty(l2.DropGroups);
        Assert.NotEmpty(medals.Spawns);
        Assert.Contains("L2 Day", l2.OnEnterMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Active_window_exposes_drop_groups()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var def = new SeasonalEventDefinition
        {
            Id = "L2Day",
            DisplayName = "L2 Day",
            StartUnixMs = now - 60_000,
            EndUnixMs = now + 60_000,
            DropGroups =
            [
                new DropGroupDefinition
                {
                    Chance = 100,
                    Items = [new DropItemDefinition { ItemId = 3875, Min = 1, Max = 1, Chance = 100 }]
                }
            ]
        };
        var host = SeasonalEventHost.FromDefinitions([def]);
        Assert.True(host.IsActive("L2Day", now));
        var windows = host.ActiveDropWindows(now);
        Assert.Single(windows);
        Assert.Equal("L2Day", windows[0].Id);
        Assert.Equal(3875, windows[0].Groups[0].Items[0].ItemId);
        Assert.False(host.IsActive("L2Day", now + 120_000));
    }

    [Fact]
    public void Parse_period_full_and_same_year()
    {
        Assert.True(SeasonalEventConfigLoader.TryParsePeriod("23 03 2006-28 03 2016", out long start, out long end));
        Assert.True(end > start);
        Assert.True(SeasonalEventConfigLoader.TryParsePeriod("27 02-28 02", out long s2, out long e2));
        Assert.True(e2 > s2);
    }
}
