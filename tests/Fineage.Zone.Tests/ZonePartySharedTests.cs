using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZonePartySharedTests
{
    [Fact]
    public async Task Two_clients_invite_join_and_leave_updates_party_list()
    {
        await ZoneMultiplayerAcceptance.RunPartySharedAsync();
    }
}
