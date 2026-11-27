using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Shops;
using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Zone.Geo;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// #991 shared operation idempotency. The protocol has no semantic operation id; its existing retry key
/// is <see cref="PacketEnvelope.RequestSequence"/>, enforced by <c>CommandDeduper</c> before a command is
/// submitted. Ten duplicate purchase frames with one sequence therefore reach the economic command once
/// and post one ledger move. A retry with a fresh sequence / new connection is not safely deduplicable
/// without inventing a client key; that blocker is documented in docs/trust-and-admin.md.
/// </summary>
[Trait("Category", "Integration")]
public sealed class OperationIdempotencyTests
{
    const int MerchantObjectId = 61000;
    const int MerchantNpcId = 30001;
    const int PurchasedItemId = 1835; // stackable starter-kit item present in the default catalog
    const int Sequence = 900_000;

    [Fact]
    public async Task Ten_duplicate_purchase_requests_with_one_sequence_yield_one_ledger_operation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;

        var store = MemoryAccountStore.InMemory();
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        var journal = new MemoryLedgerJournal();
        var world = new ZoneWorld(
            new RangeAoi(2_000),
            accounts: store,
            clock: () => clock.Now,
            spawnCatalog: Spawns.Merchant(MerchantObjectId, MerchantNpcId),
            activeMapId: "shop_flat",
            buyLists: BuyListCatalog.From(
            [
                new BuyListDefinition(1, [MerchantNpcId], [new BuyProduct(PurchasedItemId, 1)])
            ]),
            geo: GeoEngine.Empty,
            ledgerJournal: journal,
            allowAmbientBoats: false);

        await using var host = await NetZoneHost.StartAsync(world, store, clock: () => clock.Now);
        var login = new LoginService(
            store,
            host.Tokens,
            () => clock.Now,
            create: new CharacterCreateOptions { StartingAdena = 1_000 });
        Assert.True(login.Register("idem", "pass1234").Ok);
        LoginResult<CharacterRecord> created = login.CreateHuman("idem", "IdemHero", HumanClasses.Fighter);
        Assert.True(created.Ok, created.Code);
        LoginResult<HandoffTicket> play = login.Play("idem", "pass1234", "IdemHero", HumanClasses.Fighter);
        Assert.True(play.Ok && play.Value != null, play.Code);
        HandoffTicket ticket = play.Value!;
        Assert.True(store.TrySavePresence(
            ticket.Account, ticket.CharacterId, Spawns.HumanStartX, Spawns.HumanStartY, Spawns.HumanStartZ, null));

        await using NetZoneSession session = await host.ConnectAsync();
        await session.HelloAsync(ct);
        await session.AuthWithTicketAsync(ticket.Account, ticket.Token, ct);
        await session.EnterWorldAsync(ct);
        Assert.NotEqual(0UL, session.EnteredEntityValue);

        ZoneEntity player = world.RunStepForTest(() =>
        {
            Assert.True(world.TryGetEntityById((int)session.EnteredEntityValue, out ZoneEntity found));
            return found;
        });
        Assert.True(world.RunStepForTest(() => player.Inventory!.CountOf(57) >= 1));

        await session.SendAsync(
            NetOpcode.BypassRequest,
            NetCodec.EncodeBypassRequest(new BypassRequestNetMsg
            {
                NpcObjectId = MerchantObjectId,
                Command = "Buy 1"
            }),
            ct);
        await session.DrainAsync(ct, quietMs: 200);

        int adenaBefore = world.RunStepForTest(() => player.Inventory!.CountOf(57));
        int shopMovesBefore = journal.ReadAll().Count(
            entry => entry.Transaction.Kind == LedgerTxKind.ShopBuy);
        byte[] purchase = NetCodec.EncodeRequestBuyItem(new RequestBuyItemNetMsg
        {
            NpcObjectId = MerchantObjectId,
            ListId = 1,
            Items = [new ShopCountNet { ItemId = PurchasedItemId, Count = 1 }]
        });

        for (int i = 0; i < 10; i++)
            await session.SendWithSequenceAsync(NetOpcode.RequestBuyItem, Sequence, purchase, ct);

        await session.DrainAsync(ct, quietMs: 500);
        Assert.Equal(adenaBefore - 1, world.RunStepForTest(() => player.Inventory!.CountOf(57)));
        Assert.Equal(shopMovesBefore + 1, journal.ReadAll().Count(
            entry => entry.Transaction.Kind == LedgerTxKind.ShopBuy));
    }
}
