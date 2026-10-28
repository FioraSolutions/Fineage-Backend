using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneSharedWorldTests
{
    [Fact]
    public async Task Two_sessions_see_each_other_share_gremlins_and_chat()
    {
        await ZoneMultiplayerAcceptance.RunSharedWorldAsync();
    }

    [Fact]
    public async Task Moving_out_of_aoi_removes_them_from_knownlist()
    {
        await ZoneMultiplayerAcceptance.RunKnownlistDropAsync();
    }
}
