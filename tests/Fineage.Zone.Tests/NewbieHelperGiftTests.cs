using Fineage.Game.Services;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class NewbieHelperGiftTests
{
    [Fact]
    public async Task Talking_island_guide_gives_soothers_shots_and_tokens_once()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        var ticket = login.Play("gift", "secret1", "Gifted", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);
        Assert.True(store.TrySaveProgress(ticket.Value.Account, ticket.Value.CharacterId, 5, 0, 0, 0, 0, 0, 0));

        var village = SpawnCatalog.LoadDefault().GetMap(SpawnCatalog.TalkingIslandVillageMapId);
        var guide = Assert.Single(village.Npcs, row => row.NpcId == 30598);
        Assert.True(store.TrySavePresence(ticket.Value.Account, ticket.Value.CharacterId, guide.X, guide.Y, guide.Z, null));

        var world = new ZoneWorld(
            spawnCatalog: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
            {
                [SpawnCatalog.TalkingIslandVillageMapId] = village
            }),
            activeMapId: SpawnCatalog.TalkingIslandVillageMapId,
            accounts: store);
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.Value.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));

        long mark = stream.Length;
        Assert.Null(await world.TryNpcTalkAsync(session, guide.ObjectId, CancellationToken.None));
        stream.Position = mark;

        var inventory = await ReadAsync<InventoryUpdateMsg>(stream, MsgType.InventoryUpdate);
        Assert.Contains(inventory.Changes, row => row.ItemId == NewbieHelperGift.SoulshotNovice && row.Count == NewbieHelperGift.SoulshotCount);
        Assert.Contains(inventory.Changes, row => row.ItemId == NewbieHelperGift.TravelToken && row.Count == NewbieHelperGift.TokenCount);
        Assert.Contains(inventory.Changes, row => row.ItemId == NewbieHelperGift.ScrollRecovery && row.Count == NewbieHelperGift.ScrollCount);

        var html = await ReadAsync<NpcHtmlMsg>(stream, MsgType.NpcHtml);
        Assert.Contains("SupportMagic.htm", html.Html, StringComparison.Ordinal);

        mark = stream.Length;
        Assert.Null(await world.TryNpcTalkAsync(session, guide.ObjectId, CancellationToken.None));
        stream.Position = mark;
        // Second talk: HTML only, no second kit.
        _ = await ReadAsync<NpcHtmlMsg>(stream, MsgType.NpcHtml);
        while (stream.Position < stream.Length)
        {
            var frame = await FrameIO.ReadAsync(stream);
            Assert.NotNull(frame);
            Assert.NotEqual((ushort)MsgType.InventoryUpdate, frame!.Value.Type);
        }
    }

    static async Task<T> ReadAsync<T>(MemoryStream stream, MsgType type)
    {
        while (true)
        {
            var frame = await FrameIO.ReadAsync(stream);
            Assert.NotNull(frame);
            if (frame!.Value.Type == (ushort)type)
                return MessagePackSerializer.Deserialize<T>(frame.Value.Body);
        }
    }
}
