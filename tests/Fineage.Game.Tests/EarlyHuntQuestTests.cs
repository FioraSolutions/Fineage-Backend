using Fineage.Game;
using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>E2E against embedded JSON matching <c>scripts/gen_early_wave2_hunts.py</c> (Q165, Q271).</summary>
public sealed class EarlyHuntQuestTests
{
    static readonly ItemCatalog Items = new(
    [
        T(57, "Adena"),
        T(1060, "Lesser Healing Potion"),
        T(1160, "Dark Bezoar"),
        T(1473, "Kasha Wolf Fang"),
        T(1506, "Necklace of Courage"),
        T(1507, "Necklace of Valor"),
    ]);

    static ItemTemplate T(int id, string name) =>
        new(id, name, ItemKind.Etc, BodySlot.None, "icon.etc_piece_bone_white_i00", Stackable: true);

    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var catalog = QuestCatalog.Parse(EmbeddedQuestsJson, Items, "EarlyHuntQuestTests");
        return (new QuestMachine(catalog, Items, () => roll), new PlayerInventory(Items), new QuestJournal());
    }

    static QuestActor DarkElf(int level) => new(2, level, 31);
    static QuestActor Orc(int level) => new(3, level, 44);

    [Fact]
    public void Q165_shilens_hunt_collects_thirteen_bezoars()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 165, Nelsya = 30348, Bezoar = 1160, Potion = 1060;

        Assert.True(m.AdvEvent(j, bag, Q, Nelsya, "30348-03", DarkElf(3)).Ok);
        Assert.Equal(1, j.Find(Q)!.Cond);

        for (int i = 0; i < 13; i++)
            m.OnKill(j, bag, 20456, DarkElf(3));
        Assert.Equal(13, bag.CountOf(Bezoar));
        Assert.Equal(2, j.Find(Q)!.Cond);

        var done = m.Talk(j, bag, Q, Nelsya, DarkElf(3));
        Assert.True(done.Ok);
        Assert.Equal(5, bag.CountOf(Potion));
        Assert.Equal(1000, done.Exp);
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q271_proof_of_valor_fifty_fangs_and_necklace()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 271, Rukain = 30577, Fang = 1473, Necklace = 1506;

        Assert.True(m.AdvEvent(j, bag, Q, Rukain, "30577-04", Orc(4)).Ok); // class 44, race Orc (3)
        Assert.Equal(1, j.Find(Q)!.Cond);

        for (int i = 0; i < 50; i++)
            m.OnKill(j, bag, 20475, Orc(4));
        Assert.Equal(50, bag.CountOf(Fang));
        Assert.Equal(2, j.Find(Q)!.Cond);

        var done = m.Talk(j, bag, Q, Rukain, Orc(4));
        Assert.True(done.Ok);
        Assert.Equal(0, bag.CountOf(Fang));
        Assert.Equal(1, bag.CountOf(Necklace));
        Assert.Null(j.Find(Q));
    }

    const string EmbeddedQuestsJson = """
        {"maxActive":25,"talkRange":150,"quests":[
          {"id":165,"name":"Shilen's Hunt","repeatable":false,"abortable":true,"questItems":[1160],"dialogs":[
            {"npcId":30348,"hook":"event","event":"30348-03","when":"absent","races":[2],"minLevel":3,"html":"<html><body>start</body></html>","ops":[{"op":"create"},{"op":"start"},{"op":"set_cond","cond":1}]},
            {"npcId":30348,"hook":"talk","when":"started","cond":2,"atLeast":{"1160":13},"html":"<html><body>done</body></html>","ops":[
              {"op":"give_item","itemId":1060,"count":5},{"op":"give_exp_sp","exp":1000,"sp":0},{"op":"complete"}
            ]},
            {"npcId":20456,"hook":"kill","when":"started","cond":1,"html":"","ops":[
              {"op":"drop_item","itemId":1160,"count":1,"chance":0.3333333333333333,"limit":13,"condAtLimit":2}
            ]}
          ]},
          {"id":271,"name":"Proof of Valor","repeatable":true,"abortable":true,"questItems":[1473],"dialogs":[
            {"npcId":30577,"hook":"event","event":"30577-04","when":"absent","races":[3],"minLevel":4,"lacksItems":[1506,1507],"html":"<html><body>start</body></html>","ops":[{"op":"create"},{"op":"start"},{"op":"set_cond","cond":1}]},
            {"npcId":30577,"hook":"talk","when":"started","cond":2,"atLeast":{"1473":50},"html":"<html><body>done</body></html>","ops":[
              {"op":"give_item","itemId":1506,"count":1},{"op":"complete"}
            ]},
            {"npcId":20475,"hook":"kill","when":"started","cond":1,"html":"","ops":[
              {"op":"drop_item","itemId":1473,"count":1,"chance":1.0,"limit":50,"condAtLimit":2}
            ]}
          ]}
        ]}
        """;
}
