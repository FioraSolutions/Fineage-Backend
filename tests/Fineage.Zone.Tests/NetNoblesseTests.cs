using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Game.Quests;
using Fineage.Game.Shops;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Block G1 noblesse after Mobius <c>Player.setNoble</c> and Q247 Possessor of a Precious Soul 4: the flag,
/// the noble skill tree, the UserInfo tail, the clan rank floor, persistence, and the quest that grants it.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NetNoblesseTests
{
    const int Caradine = 31740, LadyOfTheLake = 31745, Hannavalt = 30109;
    const int CaradinesLetter3 = 7679, NoblesseTiara = 7694;

    static string LastHtml(WorldBench bench) =>
        Assert.IsType<NpcHtmlMsg>(bench.Rec.Of("FanoutNpcHtml").Last()[1]).Html;

    [Fact]
    public async Task Noblesse_grants_skills_rank_and_wire_flag_and_survives_relog()
    {
        var bench = new WorldBench();
        var p = await bench.EnterAsync(classId: 2, level: 76);
        Assert.False(p.Player.Noble);
        Assert.DoesNotContain(1323, p.Player.KnownSkills.Keys);

        Assert.True(bench.World.TrySetNoble(p.ObjectId, true));
        Assert.True(p.Player.Noble);
        foreach (int id in new[] { 325, 326, 327, 1323, 1324, 1325, 1326, 1327 })
            Assert.Equal(1, p.Player.KnownSkills[id]);
        Assert.Equal(5, p.Player.PledgeClass);
        Assert.True(Assert.IsType<UserInfoMsg>(bench.Rec.Of("FanoutUserInfo").Last()[1]).Noble);
        Assert.NotEmpty(bench.Rec.Of("FanoutSkillList"));
        Assert.True(p.SavedRecord().Noble);

        await bench.World.LeaveAsync(p.Session, CancellationToken.None);
        var again = await bench.EnterAsync(p.Ticket.Account, p.Ticket.CharacterName);
        Assert.True(again.Player.Noble);
        Assert.Equal(1, again.Player.KnownSkills[1323]);
    }

    [Fact]
    public async Task Hero_skills_only_on_the_base_class()
    {
        var bench = new WorldBench();
        var p = await bench.EnterAsync(classId: 2, level: 76);
        var master = bench.World.SpawnFolkForTests(Hannavalt, p.Player.X + 30, p.Player.Y, p.Player.Z);
        p.Player.Hero = true;
        bench.World.TrySetNoble(p.ObjectId, true);
        Assert.Equal(1, p.Player.KnownSkills[395]);

        Assert.Null(await p.BypassAsync(master.ObjectId, "Subclass 4 5"));
        Assert.True(p.Player.SubClassActive);
        Assert.DoesNotContain(395, p.Player.KnownSkills.Keys);
        Assert.Equal(1, p.Player.KnownSkills[1323]); // noble skills follow every class
    }

    [Fact]
    public async Task Possessor_of_a_precious_soul_4_makes_a_subclass_character_noble()
    {
        var bench = new WorldBench();
        var p = await bench.EnterAsync(classId: 2, level: 76, items: [new StoredItem(CaradinesLetter3, 1, -1)]);
        var master = bench.World.SpawnFolkForTests(Hannavalt, p.Player.X + 30, p.Player.Y, p.Player.Z);
        var caradine = bench.World.SpawnFolkForTests(Caradine, p.Player.X + 20, p.Player.Y, p.Player.Z);

        // Base class: Caradine has nothing for it.
        Assert.Null(await p.QuestEvent(caradine.ObjectId, 247, ""));
        Assert.Null(p.Player.Quests.Find(247));
        Assert.NotNull(await p.QuestEvent(caradine.ObjectId, 247, "31740-03"));
        Assert.Null(p.Player.Quests.Find(247));

        Assert.Null(await p.BypassAsync(master.ObjectId, "Subclass 4 5"));
        p.Player.Level = 75;
        Assert.Null(await p.QuestEvent(caradine.ObjectId, 247, "31740-03"));
        Assert.Equal(1, p.Player.Quests.Find(247)!.Cond);
        Assert.Equal(0, p.Inventory.CountOf(CaradinesLetter3));

        Assert.Null(await p.QuestEvent(caradine.ObjectId, 247, "TELEPORT"));
        Assert.Equal(2, p.Player.Quests.Find(247)!.Cond);

        var lady = bench.World.SpawnFolkForTests(LadyOfTheLake, p.Player.X + 20, p.Player.Y, p.Player.Z);
        Assert.Null(await p.QuestEvent(lady.ObjectId, 247, ""));
        Assert.Contains("quest_event:247:31745-02", LastHtml(bench));
        Assert.Null(await p.QuestEvent(lady.ObjectId, 247, "31745-05"));

        Assert.True(p.Player.Noble);
        Assert.Equal(1, p.Inventory.CountOf(NoblesseTiara));
        Assert.Equal(QuestPhase.Completed, p.Player.Quests.Find(247)!.Phase);
        Assert.Contains(bench.Rec.Of("FanoutSocialAction"), c => (int)c[2]! == 3);
        Assert.True(p.SavedRecord().Noble);
    }

    [Fact]
    public async Task RequestNobless_at_village_master_after_Q247_gate_pays_and_grants()
    {
        var bench = new WorldBench();
        const int certificationSp = 93836;
        const int certificationAdena = certificationSp * 10;
        var p = await bench.EnterAsync(classId: 2, level: 76,
            items: [new StoredItem(CaradinesLetter3, 1, -1), new StoredItem(ShopTrade.AdenaItemId, certificationAdena, -1)]);
        var master = bench.World.SpawnFolkForTests(Hannavalt, p.Player.X + 30, p.Player.Y, p.Player.Z);
        var caradine = bench.World.SpawnFolkForTests(Caradine, p.Player.X + 20, p.Player.Y, p.Player.Z);

        Assert.Null(await p.BypassAsync(master.ObjectId, "Subclass 4 5"));
        p.Player.Level = 75;
        p.Player.Sp = certificationSp + 1;
        Assert.Null(await p.QuestEvent(caradine.ObjectId, 247, "31740-03"));
        p.Player.Quests.Find(247)!.Set("cond", NoblesseGrant.GateReadyCond.ToString());

        int spBefore = p.Player.Sp;
        int adenaBefore = p.Inventory.CountOf(ShopTrade.AdenaItemId);
        Assert.Null(await p.BypassAsync(master.ObjectId, "RequestNobless"));

        Assert.True(p.Player.Noble);
        Assert.Equal(1, p.Inventory.CountOf(NoblesseTiara));
        Assert.Equal(QuestPhase.Completed, p.Player.Quests.Find(247)!.Phase);
        Assert.Equal(spBefore - certificationSp, p.Player.Sp);
        Assert.Equal(adenaBefore - certificationAdena, p.Inventory.CountOf(ShopTrade.AdenaItemId));

        var bag = LedgerAccount.Inventory(p.Player.ObjectId);
        var questsAcct = LedgerAccount.Of(SystemAccount.Quests);
        var tx = bench.Ledger.Entries.Select(e => e.Transaction)
            .Last(t => t.Kind == LedgerTxKind.QuestReward && t.Reference == NoblesseGrant.GateQuestId);
        Assert.Equal(p.Player.ObjectId, tx.CausedBy);
        Assert.Contains(tx.Postings, post => post.Account == bag && post.ItemId == ShopTrade.AdenaItemId && post.Amount == -certificationAdena);
        Assert.Contains(tx.Postings, post => post.Account == questsAcct && post.ItemId == ShopTrade.AdenaItemId && post.Amount == certificationAdena);
        Assert.Contains(tx.Postings, post => post.Account == bag && post.ItemId == NoblesseTiara && post.Amount == 1);
    }

    [Fact]
    public async Task RequestNobless_refuses_before_Q247_gate()
    {
        var bench = new WorldBench();
        var p = await bench.EnterAsync(classId: 2, level: 76);
        var master = bench.World.SpawnFolkForTests(Hannavalt, p.Player.X + 30, p.Player.Y, p.Player.Z);
        Assert.Null(await p.BypassAsync(master.ObjectId, "RequestNobless"));
        Assert.Contains("gate quest", LastHtml(bench), StringComparison.OrdinalIgnoreCase);
        Assert.False(p.Player.Noble);
    }

    [Fact]
    public async Task Precious_soul_1_wants_an_active_subclass()
    {
        var bench = new WorldBench();
        var p = await bench.EnterAsync(classId: 2, level: 76);
        var talien = bench.World.SpawnFolkForTests(31739, p.Player.X + 20, p.Player.Y, p.Player.Z);
        Assert.Null(await p.QuestEvent(talien.ObjectId, 241, ""));
        Assert.DoesNotContain("quest_event:241:31739-03", LastHtml(bench));
        Assert.NotNull(await p.QuestEvent(talien.ObjectId, 241, "31739-03"));
        Assert.Null(p.Player.Quests.Find(241));
        var html = LastHtml(bench);
        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.Contains("Talien", html);
    }
}
