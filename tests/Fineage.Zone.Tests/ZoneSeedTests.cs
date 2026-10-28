using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneSeedTests
{
    [Fact]
    public async Task Login_handoff_enters_talking_island_and_moves_onto_a_gremlin()
    {
        await ZoneAcceptance.RunAsync();
    }
}
