using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneTradeSharedTests
{
    [Fact]
    public async Task Two_clients_request_trade_and_exchange_item()
    {
        await ZoneMultiplayerAcceptance.RunTradeSharedAsync();
    }
}
