using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q291–294 dwarf early hunts (batch 0–10) from scripts/gen_dwarf_hunts_291_294.py.</summary>
public sealed class DwarfHuntQuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Dwarf(int level) => new(4, level, 53);
    static QuestActor Human(int level) => new(0, level, 0);

    [Fact]
    public void Q291_redbonnet_collects_forty_pelts()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 291, Maryse = 30553, Wolf = 20317, Pelt = 1482, Hairpin = 1505, Soe = 736;
        var me = Dwarf(4);

        Assert.True(m.AdvEvent(j, bag, Q, Maryse, "30553-03", me).Ok);
        Assert.Equal(1, j.Find(Q)!.Cond);

        for (int i = 0; i < 40; i++)
            m.OnKill(j, bag, Wolf, me);
        Assert.Equal(40, bag.CountOf(Pelt));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Maryse, me).Ok);
        Assert.Equal(0, bag.CountOf(Pelt));
        Assert.Equal(1, bag.CountOf(Hairpin));
        Assert.Equal(1, bag.CountOf(Soe));
        Assert.Null(j.Find(Q)); // repeatable
    }

    [Fact]
    public void Q292_brigands_sells_jewelry_and_contract()
    {
        var (m, bag, j) = Setup(0.0); // jewelry + memo always drop
        const int Q = 292, Spiron = 30532, Balanki = 30533;
        const int Neck = 1483, Memo = 1486, Contract = 1487;
        var me = Dwarf(5);

        Assert.Contains("Dwarven characters", m.Talk(j, bag, Q, Spiron, Human(10)).Html ?? "");
        Assert.True(m.AdvEvent(j, bag, Q, Spiron, "30532-03", me).Ok);
        Assert.Equal(1, j.Find(Q)!.Cond);

        // Goblin Brigand 20322 → necklace @40% (roll 0) + memo @10% (roll 0)
        for (int i = 0; i < 3; i++)
            m.OnKill(j, bag, 20322, me);
        Assert.True(bag.CountOf(Neck) >= 1);
        Assert.Equal(3, bag.CountOf(Memo));
        Assert.Equal(2, j.Find(Q)!.Cond);

        int before = bag.CountOf(57);
        Assert.True(m.Talk(j, bag, Q, Spiron, me).Ok); // convert memos + sell necks
        Assert.Equal(0, bag.CountOf(Memo));
        Assert.Equal(0, bag.CountOf(Neck));
        Assert.True(bag.CountOf(57) > before);

        // Fresh contract via Balanki path: give contract item directly
        bag.Add(Contract, 1);
        before = bag.CountOf(57);
        Assert.True(m.Talk(j, bag, Q, Balanki, me).Ok);
        Assert.Equal(0, bag.CountOf(Contract));
        Assert.Equal(before + 620, bag.CountOf(57));
    }

    [Fact]
    public void Q293_hidden_veins_ore_and_map_payout()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 293, Filaur = 30535, Chichirin = 30539;
        const int Ore = 1488, Frag = 1489, Map = 1490;
        var me = Dwarf(6);

        Assert.True(m.AdvEvent(j, bag, Q, Filaur, "30535-04", me).Ok);
        Assert.Equal(1, j.Find(Q)!.Cond);

        // roll 0 → ore 50% hits; frag 5% also hits (independent)
        for (int i = 0; i < 4; i++)
            m.OnKill(j, bag, 20446, me);
        Assert.True(bag.CountOf(Ore) >= 1);
        Assert.True(bag.CountOf(Frag) >= 4);

        Assert.True(m.AdvEvent(j, bag, Q, Chichirin, "30539-03", me).Ok);
        Assert.Equal(1, bag.CountOf(Map));
        Assert.Equal(0, bag.CountOf(Frag));

        int before = bag.CountOf(57);
        Assert.True(m.Talk(j, bag, Q, Filaur, me).Ok);
        Assert.Equal(0, bag.CountOf(Ore));
        Assert.Equal(0, bag.CountOf(Map));
        Assert.True(bag.CountOf(57) >= before + 500);
    }

    [Fact]
    public void Q294_covert_business_ring_of_raccoon()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 294, Keef = 30534, Fang = 1491, Ring = 1508;
        var me = Dwarf(10);

        Assert.True(m.AdvEvent(j, bag, Q, Keef, "30534-03", me).Ok);
        // 50 kills × 2 fangs = 100
        for (int i = 0; i < 50; i++)
            m.OnKill(j, bag, 20370, me);
        Assert.Equal(100, bag.CountOf(Fang));
        Assert.Equal(2, j.Find(Q)!.Cond);

        var done = m.Talk(j, bag, Q, Keef, me);
        Assert.True(done.Ok);
        Assert.Equal(0, bag.CountOf(Fang));
        Assert.Equal(1, bag.CountOf(Ring));
        Assert.Equal(600, done.Sp);
        Assert.Null(j.Find(Q));

        // Second run while holding ring → adena instead
        Assert.True(m.AdvEvent(j, bag, Q, Keef, "30534-03", me).Ok);
        for (int i = 0; i < 50; i++)
            m.OnKill(j, bag, 20480, me);
        int before = bag.CountOf(57);
        Assert.True(m.Talk(j, bag, Q, Keef, me).Ok);
        Assert.Equal(before + 2400, bag.CountOf(57));
        Assert.Equal(1, bag.CountOf(Ring));
    }
}
