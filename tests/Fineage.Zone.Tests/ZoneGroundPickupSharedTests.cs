using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneGroundPickupSharedTests
{
    [Fact]
    public async Task Pickup_despawns_ground_for_both_clients_and_updates_picker_bag()
    {
        await ZoneMultiplayerAcceptance.RunGroundPickupSharedAsync();
    }
}
