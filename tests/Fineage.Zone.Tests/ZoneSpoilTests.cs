using Fineage.Zone;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneSpoilTests
{
    [Fact]
    public async Task Admin_spoil_sweep_moves_corpse_roll_to_spoiler_bag()
    {
        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, "1");
        try
        {
            await ZoneMultiplayerAcceptance.RunSpoilAdminAsync().ConfigureAwait(false);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, null);
        }
    }
}
