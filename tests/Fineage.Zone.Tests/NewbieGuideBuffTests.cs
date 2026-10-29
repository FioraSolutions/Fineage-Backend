using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class NewbieGuideBuffTests
{
    [Fact]
    public void Spawned_newbie_guides_offer_support_magic_and_the_training_helper_does_not()
    {
        var html = NpcHtmlCatalog.LoadDefault();
        foreach (int npcId in new[] { 30598, 30599, 30600, 30601, 30602, 31076, 31077 })
        {
            Assert.True(html.TryGet($"default/{npcId}.htm", out var page), $"missing {npcId}.htm");
            Assert.Contains("default/SupportMagic.htm", page, StringComparison.Ordinal);
            Assert.Contains("default/BlessingOfProtection.htm", page, StringComparison.Ordinal);
        }

        Assert.True(html.TryGet("default/30009.htm", out var helper));
        Assert.Contains("Cedric's Training Hall", helper, StringComparison.Ordinal);
        Assert.Contains("bypass Script", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("SupportMagic", helper, StringComparison.Ordinal);
        Assert.True(html.TryGet("default/SupportMagic.htm", out var magic));
        Assert.Contains("npc_%objectId%_SupportMagic", magic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Talking_island_newbie_guide_applies_the_fighter_set()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        var ticket = login.Play("guide", "secret1", "Rookie", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);
        Assert.True(store.TrySaveProgress(ticket.Value.Account, ticket.Value.CharacterId, 16, 0, 0, 0, 0, 0, 0));
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
        var talk = await ReadAsync<NpcHtmlMsg>(stream, MsgType.NpcHtml);
        Assert.Contains("SupportMagic.htm", talk.Html, StringComparison.Ordinal);
        Assert.Contains($"npc_{guide.ObjectId}_", talk.Html, StringComparison.Ordinal);

        mark = stream.Length;
        Assert.Null(await world.TryBypassAsync(session, guide.ObjectId, "Link default/SupportMagic.htm", CancellationToken.None));
        stream.Position = mark;
        var list = await ReadAsync<NpcHtmlMsg>(stream, MsgType.NpcHtml);
        Assert.Contains("Receive supplemental magic.", list.Html, StringComparison.Ordinal);
        Assert.Contains($"npc_{guide.ObjectId}_SupportMagic", list.Html, StringComparison.Ordinal);

        mark = stream.Length;
        Assert.Null(await world.TryBypassAsync(
            session,
            guide.ObjectId,
            $"bypass -h npc_{guide.ObjectId}_SupportMagic",
            CancellationToken.None));
        stream.Position = mark;
        var casts = new List<int>();
        while (true)
        {
            var frame = await FrameIO.ReadAsync(stream);
            Assert.NotNull(frame);
            if (frame!.Value.Type == (ushort)MsgType.MagicSkillUse)
                casts.Add(MessagePackSerializer.Deserialize<MagicSkillUseMsg>(frame.Value.Body).SkillId);
            else if (frame.Value.Type == (ushort)MsgType.AbnormalStatusUpdate)
            {
                var abnormal = MessagePackSerializer.Deserialize<AbnormalStatusUpdateMsg>(frame.Value.Body);
                Assert.Contains(abnormal.Effects, effect => effect.Id == 4322 && effect.Value == 3 && effect.DurationSeconds == 3600);
                Assert.Contains(abnormal.Effects, effect => effect.Id == 4327);
                Assert.DoesNotContain(abnormal.Effects, effect => effect.Id == 4331);
                break;
            }
        }

        Assert.Contains(4322, casts);
        Assert.Contains(4327, casts);
        var result = await ReadAsync<NewbieBuffResultMsg>(stream, MsgType.NewbieBuffResult);
        Assert.Equal("", result.Refusal);
        Assert.Equal(7, result.SkillIds.Length);
        Assert.Equal(guide.ObjectId, result.NpcObjectId);
    }

    static async Task<T> ReadAsync<T>(Stream stream, MsgType type)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)type, frame!.Value.Type);
        return MessagePackSerializer.Deserialize<T>(frame.Value.Body);
    }
}
