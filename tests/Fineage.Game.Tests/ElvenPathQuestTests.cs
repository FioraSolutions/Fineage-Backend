using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q406 / Q407 / Q409 elven first-job paths on the shipped datapack (drops forced with roll 0).</summary>
public sealed class ElvenPathQuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup()
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => 0.0), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Fighter(int level) => new(1, level, 18);
    static QuestActor Mystic(int level) => new(1, level, 25);

    [Theory]
    [InlineData(406, 30327, 18, 19, "30327-02a", "30327-02")]
    [InlineData(407, 30328, 18, 22, "30328-02a", "30328-02")]
    [InlineData(409, 30293, 25, 29, "30293-02a", "30293-02")]
    public void Accept_refuses_wrong_class_and_low_level(int q, int npc, int baseClass, int job, string jobPage, string otherPage)
    {
        var (m, bag, j) = Setup();
        Assert.Contains(PageText(q, jobPage), m.AdvEvent(j, bag, q, npc, "ACCEPT", new QuestActor(1, 20, job)).Html);
        Assert.Contains(PageText(q, otherPage), m.AdvEvent(j, bag, q, npc, "ACCEPT", new QuestActor(0, 20, 10)).Html);
        Assert.Contains(PageText(q, $"{npc}-03"), m.AdvEvent(j, bag, q, npc, "ACCEPT", new QuestActor(1, 17, baseClass)).Html);
        Assert.Null(j.Find(q));
    }

    [Fact]
    public void Elven_knight_path_runs_end_to_end()
    {
        var (m, bag, j) = Setup();
        var me = Fighter(18);
        Assert.True(m.AdvEvent(j, bag, 406, 30327, "ACCEPT", me).Ok);
        Assert.Null(j.Find(406)); // info page only
        Assert.True(m.AdvEvent(j, bag, 406, 30327, "30327-06", me).Ok);
        for (int i = 0; i < 25; i++)
            m.OnKill(j, bag, 20035, me);
        Assert.Equal(20, bag.CountOf(1205));
        Assert.Equal(2, j.Find(406)!.Cond);
        Assert.True(m.Talk(j, bag, 406, 30327, me).Ok);
        Assert.Equal(1, bag.CountOf(1202));
        Assert.True(m.AdvEvent(j, bag, 406, 30317, "30317-02", me).Ok);
        Assert.Equal(1, bag.CountOf(1276));
        for (int i = 0; i < 20; i++)
            m.OnKill(j, bag, 20782, me);
        Assert.Equal(5, j.Find(406)!.Cond);
        Assert.True(m.Talk(j, bag, 406, 30317, me).Ok);
        Assert.Equal(1, bag.CountOf(1203));
        Assert.Equal(0, bag.CountOf(1205));
        var done = m.Talk(j, bag, 406, 30327, me);
        Assert.Equal(591724, done.Exp);
        Assert.Equal(1, bag.CountOf(1204));
        Assert.Equal(QuestPhase.Completed, j.Find(406)!.Phase);
    }

    [Fact]
    public void Elven_scout_path_runs_end_to_end()
    {
        var (m, bag, j) = Setup();
        var me = Fighter(20);
        Assert.True(m.AdvEvent(j, bag, 407, 30328, "ACCEPT", me).Ok);
        Assert.Equal(1, bag.CountOf(1207));
        Assert.True(m.AdvEvent(j, bag, 407, 30337, "30337-03", me).Ok);
        Assert.Equal(2, j.Find(407)!.Cond);
        for (int i = 0; i < 4; i++)
            m.OnKill(j, bag, 20053, me);
        Assert.Equal(3, j.Find(407)!.Cond);
        Assert.Equal(1, bag.CountOf(1211));
        Assert.True(m.Talk(j, bag, 407, 30337, me).Ok);
        Assert.Equal(1, bag.CountOf(1214));
        Assert.True(m.Talk(j, bag, 407, 30426, me).Ok);
        Assert.Equal(5, j.Find(407)!.Cond);
        m.OnKill(j, bag, 27031, me);
        Assert.Equal(1, bag.CountOf(1293));
        Assert.True(m.Talk(j, bag, 407, 30426, me).Ok);
        Assert.Equal(1, bag.CountOf(1215));
        Assert.True(m.Talk(j, bag, 407, 30337, me).Ok);
        Assert.Equal(1, bag.CountOf(1216));
        var done = m.Talk(j, bag, 407, 30328, me);
        Assert.Equal(320534, done.Exp);
        Assert.Equal(1, bag.CountOf(1217));
        Assert.Equal(QuestPhase.Completed, j.Find(407)!.Phase);
    }

    [Fact]
    public void Elven_oracle_path_runs_end_to_end_with_kill_steps_simulated()
    {
        var (m, bag, j) = Setup();
        var me = Mystic(19);
        Assert.True(m.AdvEvent(j, bag, 409, 30293, "ACCEPT", me).Ok);
        Assert.Equal(1, bag.CountOf(1231));
        Assert.True(m.Talk(j, bag, 409, 30424, me).Ok); // Allana, memo 1 -> cond 2
        Assert.True(m.AdvEvent(j, bag, 409, 30424, "replay_1", me).Ok);
        m.OnKill(j, bag, 27032, me);                     // lizardman (spawned by replay_1 in Mobius)
        Assert.Equal(1, bag.CountOf(1234));
        Assert.True(m.Talk(j, bag, 409, 30424, me).Ok);
        Assert.Equal(1, bag.CountOf(1236));
        Assert.True(m.AdvEvent(j, bag, 409, 30428, "replay_2", me).Ok);
        m.OnKill(j, bag, 27035, me);                     // Tamil
        Assert.True(m.Talk(j, bag, 409, 30428, me).Ok);
        Assert.Equal(1, bag.CountOf(1232));
        Assert.True(m.Talk(j, bag, 409, 30424, me).Ok);
        Assert.Equal(1, bag.CountOf(1233));
        var done = m.Talk(j, bag, 409, 30293, me);
        Assert.Equal(456128, done.Exp);
        Assert.Equal(27090, done.Sp);
        Assert.Equal(1, bag.CountOf(1235));
        Assert.Equal(QuestPhase.Completed, j.Find(409)!.Phase);
    }

    static string PageText(int q, string page)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Fineage.Backend.slnx")))
            dir = dir.Parent;
        var scripts = Path.Combine(dir!.FullName, "ref/mobius-ct0-interlude/data/scripts/quests");
        var qdir = Directory.GetDirectories(scripts, $"Q00{q}_*")[0];
        var file = Directory.GetFiles(qdir, page + ".htm*")[0];
        return File.ReadAllText(file).Trim().Split("<a ")[0];
    }
}
