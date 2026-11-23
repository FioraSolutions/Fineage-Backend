using Fineage.Zone;
using Xunit;

namespace Fineage.Zone.Tests;

[Collection("Zone tick budget env")]
public sealed class ZoneTickBudgetSettingsTests
{
    [Theory]
    [InlineData("FINEAGE_ZONE_PRIVATES_SPAWN_BUDGET", "0")]
    [InlineData("FINEAGE_ZONE_PRIVATES_SPAWN_BUDGET", "abc")]
    [InlineData("FINEAGE_ZONE_PRIVATES_SPAWN_BUDGET", "10001")]
    [InlineData("FINEAGE_ZONE_REGION_WAKE_BUDGET", "0")]
    [InlineData("FINEAGE_ZONE_REGION_WAKE_BUDGET", "-5")]
    [InlineData("FINEAGE_ZONE_REGION_WAKE_BUDGET", "many")]
    public void Invalid_tick_budget_throws_naming_the_variable(string name, string value)
    {
        string? prior = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                name.Contains("PRIVATES")
                    ? ZoneSimulationSettings.PrivatesSpawnBudgetPerTick
                    : ZoneSimulationSettings.RegionWakeBudgetPerTick);
            Assert.Contains(name, error.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, prior);
        }
    }

    [Fact]
    public void Unset_tick_budgets_use_defaults()
    {
        Assert.Equal(16, ZoneSimulationSettings.PrivatesSpawnBudgetPerTick);
        Assert.Equal(128, ZoneSimulationSettings.RegionWakeBudgetPerTick);
    }
}
