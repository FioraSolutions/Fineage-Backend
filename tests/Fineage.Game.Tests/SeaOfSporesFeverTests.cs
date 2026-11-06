using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q102 Sea of Spores Fever on the shipped datapack (dryad drops forced with roll 0).</summary>
public sealed class SeaOfSporesFeverTests
{
    const int Q = 102;
    const int Alberius = 30284;
    const int Cobendell = 30156;
    const int Berros = 30217;
    const int Veltress = 30219;
    const int Rayen = 30221;
    const int Gartrandell = 30285;
    const int Letter = 964;
    const int Amulet = 965;
    const int Tear = 966;
    const int List = 746;
    const int Med1 = 1130;
    const int Sword = 743;
    const int Staff = 744;

    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup()
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => 0.0), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Fighter(int level) => new(1, level, 18);
    static QuestActor Mystic(int level) => new(1, level, 25);

    [Fact]
    public void Accept_requires_elf_level_12()
    {
        var (m, bag, j) = Setup();
        Assert.True(m.Talk(j, bag, Q, Alberius, new QuestActor(0, 20, 0)).Ok);
        Assert.Null(j.Find(Q));
        m.AdvEvent(j, bag, Q, Alberius, "30284-02", Fighter(11));
        Assert.Null(j.Find(Q));
        Assert.True(m.AdvEvent(j, bag, Q, Alberius, "30284-02", Fighter(12)).Ok);
        Assert.NotNull(j.Find(Q));
    }

    [Fact]
    public void Fighter_path_runs_end_to_end()
    {
        var (m, bag, j) = Setup();
        var me = Fighter(12);
        Assert.True(m.AdvEvent(j, bag, Q, Alberius, "30284-02", me).Ok);
        Assert.Equal(1, bag.CountOf(Letter));
        Assert.Equal(1, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Cobendell, me).Ok);
        Assert.Equal(1, bag.CountOf(Amulet));
        Assert.Equal(2, j.Find(Q)!.Cond);

        for (int i = 0; i < 10; i++)
            m.OnKill(j, bag, 20013, me);
        Assert.Equal(10, bag.CountOf(Tear));
        Assert.Equal(3, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Cobendell, me).Ok);
        Assert.Equal(0, bag.CountOf(Tear));
        Assert.Equal(1, bag.CountOf(Med1));
        Assert.Equal(4, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Alberius, me).Ok);
        Assert.Equal(1, bag.CountOf(List));
        Assert.Equal(5, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Berros, me).Ok);
        Assert.True(m.Talk(j, bag, Q, Veltress, me).Ok);
        Assert.True(m.Talk(j, bag, Q, Rayen, me).Ok);
        Assert.True(m.Talk(j, bag, Q, Gartrandell, me).Ok);
        Assert.Equal(6, j.Find(Q)!.Cond);

        var done = m.Talk(j, bag, Q, Alberius, me);
        Assert.True(done.Ok);
        Assert.Equal(30202, done.Exp);
        Assert.Equal(1, bag.CountOf(Sword));
        Assert.Equal(0, bag.CountOf(Staff));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Mystic_reward_is_staff_and_spiritshots()
    {
        var (m, bag, j) = Setup();
        var me = Mystic(12);
        Assert.True(m.AdvEvent(j, bag, Q, Alberius, "30284-02", me).Ok);
        Assert.True(m.Talk(j, bag, Q, Cobendell, me).Ok);
        for (int i = 0; i < 10; i++)
            m.OnKill(j, bag, 20019, me);
        Assert.True(m.Talk(j, bag, Q, Cobendell, me).Ok);
        Assert.True(m.Talk(j, bag, Q, Alberius, me).Ok);
        Assert.True(m.Talk(j, bag, Q, Berros, me).Ok);
        Assert.True(m.Talk(j, bag, Q, Veltress, me).Ok);
        Assert.True(m.Talk(j, bag, Q, Rayen, me).Ok);
        Assert.True(m.Talk(j, bag, Q, Gartrandell, me).Ok);
        Assert.True(m.Talk(j, bag, Q, Alberius, me).Ok);
        Assert.Equal(1, bag.CountOf(Staff));
        Assert.Equal(0, bag.CountOf(Sword));
    }
}
