using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>
/// Mobius early newbie quests Q1, Q153, Q166 — requires data/quests.json entries from
/// scripts/gen_early_wave1_newbie.py (parent runs generators).
/// </summary>
public sealed class EarlyNewbieQuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J, QuestCatalog Catalog) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal(), data.Quests);
    }

    static QuestActor Human(int level) => new(0, level, 0);
    static QuestActor DarkElf(int level) => new(2, level, 31);

    [Fact]
    public void Q001_letters_of_love_human_path()
    {
        const int Q = 1;
        var (m, bag, j, catalog) = Setup();
        Assert.NotNull(catalog.Find(Q));
        const int Darin = 30048, Roxxy = 30006, Baulro = 30033;
        const int Letter = 687, Kerchief = 688, Receipt = 1079, Potion = 1080, Neck = 906;
        var me = Human(5);

        Assert.True(m.Talk(j, bag, Q, Darin, me).Ok);
        Assert.Null(j.Find(Q));

        Assert.True(m.AdvEvent(j, bag, Q, Darin, "30048-06", me).Ok);
        Assert.NotNull(j.Find(Q));
        Assert.Equal(1, j.Find(Q)!.Cond);
        Assert.Equal(1, bag.CountOf(Letter));

        Assert.True(m.Talk(j, bag, Q, Roxxy, me).Ok);
        Assert.Equal(0, bag.CountOf(Letter));
        Assert.Equal(1, bag.CountOf(Kerchief));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Darin, me).Ok);
        Assert.Equal(1, bag.CountOf(Receipt));
        Assert.Equal(3, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Baulro, me).Ok);
        Assert.Equal(0, bag.CountOf(Receipt));
        Assert.Equal(1, bag.CountOf(Potion));
        Assert.Equal(4, j.Find(Q)!.Cond);

        var done = m.Talk(j, bag, Q, Darin, me);
        Assert.True(done.Ok);
        Assert.Equal(5672, done.Exp);
        Assert.Equal(446, done.Sp);
        Assert.Equal(2466, bag.CountOf(57));
        Assert.Equal(1, bag.CountOf(Neck));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q153_deliver_goods_end_to_end()
    {
        const int Q = 153;
        var (m, bag, j, catalog) = Setup();
        Assert.NotNull(catalog.Find(Q));
        const int Arnold = 30041, Jackson = 30002, Silvia = 30003, Rant = 30054;
        const int List = 1012, Box = 1013, Cloth = 1014, Pot = 1015;
        const int RJ = 1016, RS = 1017, RR = 1018;
        const int Ring = 875, Ss = 1835;
        var me = Human(5);

        Assert.True(m.Talk(j, bag, Q, Arnold, me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Arnold, "30041-02", me).Ok);
        Assert.Equal(1, j.Find(Q)!.Cond);
        Assert.Equal(1, bag.CountOf(List));

        Assert.True(m.Talk(j, bag, Q, Jackson, me).Ok);
        Assert.Equal(0, bag.CountOf(Box));
        Assert.Equal(1, bag.CountOf(RJ));

        Assert.True(m.Talk(j, bag, Q, Silvia, me).Ok);
        Assert.Equal(0, bag.CountOf(Cloth));
        Assert.Equal(1, bag.CountOf(RS));
        Assert.Equal(3, bag.CountOf(Ss));

        Assert.True(m.Talk(j, bag, Q, Rant, me).Ok);
        Assert.Equal(0, bag.CountOf(Pot));
        Assert.Equal(1, bag.CountOf(RR));
        Assert.Equal(2, j.Find(Q)!.Cond);

        var done = m.Talk(j, bag, Q, Arnold, me);
        Assert.True(done.Ok);
        Assert.Equal(600, done.Exp);
        Assert.Equal(2, bag.CountOf(Ring));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q166_mass_of_darkness_collect_and_finish()
    {
        const int Q = 166;
        var (m, bag, j, catalog) = Setup();
        Assert.NotNull(catalog.Find(Q));
        const int Undrias = 30130, Iria = 30135, Dorankus = 30139, Trudy = 30143;
        const int Uletter = 1088, Dagger = 1089, Wine = 1090, Script = 1091;
        var me = DarkElf(5);

        Assert.True(m.Talk(j, bag, Q, Undrias, me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Undrias, "30130-03", me).Ok);
        Assert.Equal(1, j.Find(Q)!.Cond);
        Assert.Equal(1, bag.CountOf(Uletter));

        Assert.True(m.Talk(j, bag, Q, Iria, me).Ok);
        Assert.Equal(1, bag.CountOf(Dagger));
        Assert.Equal(1, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Dorankus, me).Ok);
        Assert.Equal(1, bag.CountOf(Wine));
        Assert.Equal(1, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Trudy, me).Ok);
        Assert.Equal(1, bag.CountOf(Script));
        Assert.Equal(2, j.Find(Q)!.Cond);

        var done = m.Talk(j, bag, Q, Undrias, me);
        Assert.True(done.Ok);
        Assert.Equal(5672, done.Exp);
        Assert.Equal(2966, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }
}
