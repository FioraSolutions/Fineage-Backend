using Fineage.Accounts;
using Fineage.Login.Net;
using Xunit;

namespace Fineage.Login.Tests;

[Collection("Handoff environment")]
public sealed class ZoneHandoffEndpointTests
{
    [Fact]
    public void Zone_port_precedence_is_shared_by_tickets_and_login_server()
    {
        string? priorZone = Environment.GetEnvironmentVariable("FINEAGE_ZONE_PORT");
        string? priorNetZone = Environment.GetEnvironmentVariable("FINEAGE_NET_ZONE_PORT");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_ZONE_PORT", "12345");
            Environment.SetEnvironmentVariable("FINEAGE_NET_ZONE_PORT", "23456");
            var service = new LoginService(MemoryAccountStore.InMemory(), HandoffTokens.FromEnvironment(allowDefaultSecret: true));
            var server = new NetLoginServer(service);
            int advertised = Assert.IsType<int>(
                typeof(NetLoginServer).GetField("_zonePort", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!.GetValue(server));
            Assert.Equal(ZoneHandoff.AdvertisePort, advertised);
            Assert.Equal(12345, advertised);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_ZONE_PORT", priorZone);
            Environment.SetEnvironmentVariable("FINEAGE_NET_ZONE_PORT", priorNetZone);
        }
    }
}
