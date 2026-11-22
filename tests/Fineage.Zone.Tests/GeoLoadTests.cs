using Fineage.Zone.Geo;
using Xunit;

namespace Fineage.Zone.Tests;

[Collection("Geodata environment")]
public sealed class GeoLoadTests
{
    static byte[] FlatRegionL2j(short height = 96)
    {
        var buf = new byte[256 * 256 * 3];
        int p = 0;
        for (int i = 0; i < 256 * 256; i++)
        {
            buf[p++] = 0;
            BitConverter.TryWriteBytes(buf.AsSpan(p), height);
            p += 2;
        }

        return buf;
    }

    static void WithFixtureDir(Action<string> use)
    {
        string dir = Path.Combine(Path.GetTempPath(), "fineage-geo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "20_18.l2j"), FlatRegionL2j());
            use(dir);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    static void WithEnv(string? required, string? dir, string? requiredRegions, Action body)
    {
        string? priorRequired = Environment.GetEnvironmentVariable(GeoSettings.RequiredVariable);
        string? priorDir = Environment.GetEnvironmentVariable(GeoSettings.DirVariable);
        string? priorRegions = Environment.GetEnvironmentVariable(GeoSettings.RequiredRegionsVariable);
        try
        {
            Environment.SetEnvironmentVariable(GeoSettings.RequiredVariable, required);
            Environment.SetEnvironmentVariable(GeoSettings.DirVariable, dir);
            Environment.SetEnvironmentVariable(GeoSettings.RequiredRegionsVariable, requiredRegions);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(GeoSettings.RequiredVariable, priorRequired);
            Environment.SetEnvironmentVariable(GeoSettings.DirVariable, priorDir);
            Environment.SetEnvironmentVariable(GeoSettings.RequiredRegionsVariable, priorRegions);
        }
    }

    [Fact]
    public void Required_throws_when_geodata_dir_missing()
    {
        WithEnv("1", "/nonexistent/fineage-geodata-" + Guid.NewGuid().ToString("N"), "20_18", () =>
        {
            var ex = Assert.Throws<InvalidOperationException>(GeoEngine.LoadDefault);
            Assert.Contains(GeoSettings.RequiredVariable, ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Required_throws_when_manifest_region_files_missing()
    {
        string empty = Path.Combine(Path.GetTempPath(), "fineage-geo-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            WithEnv("1", empty, "20_18,16_24", () =>
            {
                var ex = Assert.Throws<InvalidOperationException>(GeoEngine.LoadDefault);
                Assert.Contains("missing required region file", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("16_24.l2j", ex.Message, StringComparison.Ordinal);
                Assert.Contains("20_18.l2j", ex.Message, StringComparison.Ordinal);
            });
        }
        finally
        {
            try { Directory.Delete(empty, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Fixture_region_loads_and_FindPath_matches_blocked_wall_fixture()
    {
        WithFixtureDir(fixtureDir =>
        {
            WithEnv("1", fixtureDir, "20_18", () =>
            {
                var loaded = GeoEngine.LoadDefault();
                Assert.Equal(1, loaded.RegionCount);
                Assert.True(loaded.HasGeo(40, 40));
                Assert.Equal(96, loaded.GetHeight(40, 40, -5000));
            });
        });

        var region = GeoEngine.Region.Flat1(96);
        region.SetComplexBlock(1, 0, (cx, cy) =>
        {
            byte n = GeoEngine.All;
            if (cx == 3) n = (byte)(n & ~GeoEngine.East);
            if (cx == 4) n = (byte)(n & ~GeoEngine.West);
            return (96, n);
        });
        var geo = new GeoEngine();
        geo.SetRegion(20, 18, region);
        var path = geo.FindPath(8, 40, 96, 400, 40, 96);
        Assert.NotNull(path);
        Assert.Equal((400, 40, 96), path![^1]);
    }

    [Fact]
    public void LoadDirectory_lists_concrete_file_names()
    {
        WithFixtureDir(fixtureDir =>
        {
            var (geo, names, errors) = GeoEngine.LoadDirectory(fixtureDir, strict: false);
            Assert.Empty(errors);
            Assert.Equal(["20_18.l2j"], names);
            Assert.Equal(1, geo.RegionCount);
        });
    }
}

[CollectionDefinition("Geodata environment", DisableParallelization = true)]
public sealed class GeodataEnvironmentCollection : ICollectionFixture<object> { }