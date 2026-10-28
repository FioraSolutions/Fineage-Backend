using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneCombatSharedTests
{
    [Fact]
    public async Task Two_clients_see_same_gremlin_hp_then_delete_on_death()
    {
        await ZoneMultiplayerAcceptance.RunCombatSharedAsync();
    }
}
