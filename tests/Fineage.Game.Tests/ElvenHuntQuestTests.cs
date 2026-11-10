using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius early Elven Village hunt quests Q105 / Q159 / Q162 / Q163 on the shipped datapack.</summary>
public sealed class ElvenHuntQuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Elf(int level) => new(1, level, 18);
    static QuestActor Human(int level) => new(0, level, 0);
    static QuestActor DarkElf(int level) => new(2, level, 31);

    [Fact]
    public void Q105_skirmish_with_orcs_linear_path()
    {
        var (m, bag, j) = Setup();
        const int Q = 105, Kendell = 30218, Order1 = 1836, Order5 = 1840, T1 = 1844, T2 = 1845;
        Assert.True(m.Talk(j, bag, Q, Kendell, Human(20)).Ok);
        Assert.Null(j.Find(Q));
        Assert.False(m.AdvEvent(j, bag, Q, Kendell, "30218-04", Elf(9)).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Kendell, "30218-04", Elf(10)).Ok);
        Assert.Equal(1, bag.CountOf(Order1));
        Assert.Equal(1, j.Find(Q)!.Cond);

        m.OnKill(j, bag, 27059, Elf(10));
        Assert.Equal(1, bag.CountOf(T1));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Kendell, Elf(10)).Ok);
        Assert.Equal(0, bag.CountOf(Order1));
        Assert.Equal(0, bag.CountOf(T1));
        Assert.Equal(1, bag.CountOf(Order5));
        Assert.Equal(3, j.Find(Q)!.Cond);

        m.OnKill(j, bag, 27064, Elf(10));
        Assert.Equal(1, bag.CountOf(T2));
        Assert.Equal(4, j.Find(Q)!.Cond);

        var done = m.Talk(j, bag, Q, Kendell, Elf(10));
        Assert.True(done.Ok);
        Assert.Equal(41478, done.Exp);
        Assert.Equal(17599, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q159_protect_the_water_source_two_phases()
    {
        var (m, bag, j) = Setup(0.0); // always drop
        const int Q = 159, Asterios = 30154, Dust = 1035, C1 = 1071, C2 = 1072, Zombie = 27017;
        Assert.True(m.AdvEvent(j, bag, Q, Asterios, "30154-04", Elf(12)).Ok);
        Assert.Equal(1, bag.CountOf(C1));

        m.OnKill(j, bag, Zombie, Elf(12));
        Assert.Equal(1, bag.CountOf(Dust));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Asterios, Elf(12)).Ok);
        Assert.Equal(0, bag.CountOf(C1));
        Assert.Equal(0, bag.CountOf(Dust));
        Assert.Equal(1, bag.CountOf(C2));
        Assert.Equal(3, j.Find(Q)!.Cond);

        for (int i = 0; i < 5; i++)
            m.OnKill(j, bag, Zombie, Elf(12));
        Assert.Equal(5, bag.CountOf(Dust));
        Assert.Equal(4, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Asterios, Elf(12)).Ok);
        Assert.Equal(18250, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q162_curse_rejects_dark_elf_and_pays_on_counts()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 162, Unoren = 30147, Bone = 1158, Skull = 1159, Shield = 625;
        Assert.Contains("Dark", m.Talk(j, bag, Q, Unoren, DarkElf(20)).Html);
        Assert.True(m.AdvEvent(j, bag, Q, Unoren, "30147-04", Elf(12)).Ok);

        for (int i = 0; i < 3; i++)
            m.OnKill(j, bag, 20033, Elf(12));
        for (int i = 0; i < 10; i++)
            m.OnKill(j, bag, 20463, Elf(12));
        Assert.Equal(3, bag.CountOf(Skull));
        Assert.Equal(10, bag.CountOf(Bone));

        var done = m.Talk(j, bag, Q, Unoren, Elf(12));
        Assert.True(done.Ok);
        Assert.Equal(1, bag.CountOf(Shield));
        Assert.Equal(22652, done.Exp);
        Assert.Equal(24000, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q163_legacy_of_the_poet_collects_four_poems()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 163, Starden = 30220;
        int[] poems = [1038, 1039, 1040, 1041];
        Assert.True(m.AdvEvent(j, bag, Q, Starden, "30220-05", Elf(11)).Ok);

        // Forced roll 0: every poem chance fires on the same kill (independent drops).
        m.OnKill(j, bag, 20372, Elf(11));
        foreach (int p in poems)
            Assert.Equal(1, bag.CountOf(p));

        var done = m.Talk(j, bag, Q, Starden, Elf(11));
        Assert.True(done.Ok);
        Assert.Equal(21643, done.Exp);
        Assert.Equal(13890, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }
}
