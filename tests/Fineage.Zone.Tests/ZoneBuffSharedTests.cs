using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneBuffSharedTests
{
    [Fact]
    public async Task Self_buff_fanout_reaches_caster_and_observer()
    {
        await ZoneMultiplayerAcceptance.RunBuffSharedAsync();
    }
}
