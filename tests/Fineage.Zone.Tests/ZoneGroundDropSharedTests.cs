using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneGroundDropSharedTests
{
    [Fact]
    public async Task Two_clients_see_same_ground_item_after_gremlin_kill()
    {
        await ZoneMultiplayerAcceptance.RunCombatSharedAsync();
    }
}
