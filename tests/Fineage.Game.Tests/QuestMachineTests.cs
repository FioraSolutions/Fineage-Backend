using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class QuestMachineTests
{
    [Fact]
    public void Shipped_smoke_quest_is_one_time_and_caps_at_25()
    {
        var data = GameData.LoadDefault();
        Assert.Equal(25, data.Quests.MaxActive);
        Assert.Equal(150, data.Quests.TalkRange);
        var quest = data.Quests.Find(999);
        Assert.NotNull(quest);
        Assert.Equal("Roxxy's Message", quest!.Name);
        Assert.False(quest.Repeatable);
        Assert.True(quest.Abortable);
        Assert.Empty(quest.QuestItems);
    }

    [Fact]
    public void Experience_table_levels_from_the_cumulative_threshold()
    {
        var table = GameData.LoadDefault().Classes!.Experience;
        var stay = table.Add(0, 1, 0, 50, 5);
        Assert.Equal(50, stay.Exp);
        Assert.Equal(1, stay.Level);
        Assert.Equal(5, stay.Sp);

        var levelTwo = table.Add(0, 1, 0, 100, 0);
        Assert.Equal(2, levelTwo.Level);
        Assert.Equal(100, levelTwo.Exp);

        var capped = table.Add(0, 1, 0, long.MaxValue / 2, 0);
        Assert.Equal(table.MaxLevel, capped.Level);
        Assert.Equal(table.ExpToBeLevel(table.MaxLevel), capped.Exp);
    }

    [Fact]
    public void Start_sets_cond_and_the_26th_active_quest_is_refused()
    {
        var (machine, inventory) = Machine(Sample(maxActive: 25));
        var journal = new QuestJournal();
        for (int id = 1; id <= 25; id++)
        {
            var started = machine.AdvEvent(journal, inventory, id, 1, "go");
            Assert.True(started.Ok);
            Assert.Equal(QuestPhase.Started, journal.Find(id)!.Phase);
            Assert.Equal(1, journal.Find(id)!.Cond);
            Assert.Contains(QuestSounds.Accept, started.Sounds);
        }

        var blocked = machine.AdvEvent(journal, inventory, 26, 1, "go");
        Assert.Equal(QuestFailure.Limit, blocked.Failure);
        Assert.Equal(QuestNotice.TooMany, Assert.Single(blocked.Lines).Notice);
        Assert.Null(journal.Find(26));
        Assert.Equal(25, journal.StartedCount);
        Assert.Equal(25, machine.List(journal).Active.Length);
    }

    [Fact]
    public void Created_and_completed_do_not_count_toward_the_cap()
    {
        var (machine, inventory) = Machine("""
            {"maxActive":1,"quests":[
              {"id":1,"name":"Hold","dialogs":[{"npcId":1,"hook":"event","event":"go","when":"absent","html":"<html><body>a</body></html>","ops":[{"op":"create"},{"op":"start"}]}]},
              {"id":2,"name":"Done","dialogs":[{"npcId":1,"hook":"event","event":"go","when":"absent","html":"<html><body>b</body></html>","ops":[{"op":"create"},{"op":"start"},{"op":"complete"}]}]},
              {"id":3,"name":"Open","dialogs":[{"npcId":1,"hook":"event","event":"go","when":"absent","html":"<html><body>c</body></html>","ops":[{"op":"create"}]}]},
              {"id":4,"name":"Next","dialogs":[{"npcId":1,"hook":"event","event":"go","when":"absent","html":"<html><body>d</body></html>","ops":[{"op":"create"},{"op":"start"}]}]}
            ]}
            """);
        var journal = new QuestJournal();
        Assert.True(machine.AdvEvent(journal, inventory, 2, 1, "go").Ok);
        Assert.Equal(QuestPhase.Completed, journal.Find(2)!.Phase);
        Assert.True(machine.AdvEvent(journal, inventory, 3, 1, "go").Ok);
        Assert.Equal(QuestPhase.Created, journal.Find(3)!.Phase);
        Assert.True(machine.AdvEvent(journal, inventory, 4, 1, "go").Ok);
        Assert.Equal(1, journal.StartedCount);
    }

    [Fact]
    public void One_time_completion_blocks_a_restart_and_sets_the_mask_bit()
    {
        var (machine, inventory) = Machine(Finishable(id: 999, repeatable: false));
        var journal = new QuestJournal();
        Assert.True(machine.AdvEvent(journal, inventory, 999, 1, "go").Ok);
        var done = machine.AdvEvent(journal, inventory, 999, 1, "finish");
        Assert.True(done.Ok);
        Assert.Equal(QuestPhase.Completed, journal.Find(999)!.Phase);
        Assert.Equal(0, journal.Find(999)!.Cond);
        Assert.Empty(journal.Find(999)!.Vars);
        Assert.Contains(QuestSounds.Finish, done.Sounds);

        var again = machine.AdvEvent(journal, inventory, 999, 1, "again");
        Assert.Equal(QuestFailure.State, again.Failure);
        Assert.Equal(QuestNotice.AlreadyFinished, Assert.Single(again.Lines).Notice);

        var list = machine.List(journal);
        Assert.Empty(list.Active);
        Assert.Equal(999, Assert.Single(list.CompletedQuestIds));
        Assert.Equal(128, list.CompletedMask[999 / 8]);
    }

    [Fact]
    public void Repeatable_completion_deletes_the_row_so_it_can_be_taken_again()
    {
        var (machine, inventory) = Machine(Finishable(id: 4, repeatable: true));
        var journal = new QuestJournal();
        Assert.True(machine.AdvEvent(journal, inventory, 4, 1, "go").Ok);
        Assert.True(machine.AdvEvent(journal, inventory, 4, 1, "finish").Ok);
        Assert.Null(journal.Find(4));
        Assert.Empty(machine.List(journal).CompletedQuestIds);
        Assert.True(machine.AdvEvent(journal, inventory, 4, 1, "go").Ok);
        Assert.Equal(QuestPhase.Started, journal.Find(4)!.Phase);
    }

    [Fact]
    public void Abort_deletes_a_started_one_time_quest_and_refuses_a_locked_one()
    {
        var (machine, inventory) = Machine("""
            {"quests":[
              {"id":7,"name":"Free","abortable":true,"questItems":[90001],"dialogs":[
                {"npcId":1,"hook":"event","event":"go","when":"absent","html":"<html><body>a</body></html>","ops":[{"op":"create"},{"op":"start"},{"op":"give_item","itemId":90001,"count":2}]}
              ]},
              {"id":8,"name":"Locked","abortable":false,"dialogs":[
                {"npcId":1,"hook":"event","event":"go","when":"absent","html":"<html><body>b</body></html>","ops":[{"op":"create"},{"op":"start"}]}
              ]}
            ]}
            """);
        var journal = new QuestJournal();
        Assert.True(machine.AdvEvent(journal, inventory, 7, 1, "go").Ok);
        Assert.Equal(2, inventory.CountOf(90001));
        var aborted = machine.Abort(journal, inventory, 7);
        Assert.True(aborted.Ok);
        Assert.Contains(QuestSounds.GiveUp, aborted.Sounds);
        Assert.Equal(QuestNotice.Cancelled, Assert.Single(aborted.Lines).Notice);
        Assert.Null(journal.Find(7));
        Assert.Equal(0, inventory.CountOf(90001));
        Assert.Equal(BagTouchKind.Remove, Assert.Single(aborted.Items).Kind);

        Assert.True(machine.AdvEvent(journal, inventory, 7, 1, "go").Ok);

        Assert.True(machine.AdvEvent(journal, inventory, 8, 1, "go").Ok);
        var locked = machine.Abort(journal, inventory, 8);
        Assert.Equal(QuestFailure.Abort, locked.Failure);
        Assert.Equal(QuestNotice.NotAbortable, Assert.Single(locked.Lines).Notice);
        Assert.Equal(QuestPhase.Started, journal.Find(8)!.Phase);
        Assert.Equal(QuestFailure.State, machine.Abort(new QuestJournal(), inventory, 8).Failure);
        Assert.Equal(QuestFailure.Unknown, machine.Abort(journal, inventory, 99).Failure);
    }

    [Fact]
    public void Flags_replace_cond_in_the_journal_word_and_vars_round_trip()
    {
        var (machine, inventory) = Machine("""
            {"quests":[{"id":3,"name":"Flags","dialogs":[
              {"npcId":1,"hook":"event","event":"go","when":"absent","html":"<html><body>a</body></html>","ops":[
                {"op":"create"},{"op":"start"},
                {"op":"set","key":"step","value":"met"},
                {"op":"set","key":"__compltdStateFlags","value":"7"}
              ]}
            ]}]}
            """);
        var journal = new QuestJournal();
        Assert.True(machine.AdvEvent(journal, inventory, 3, 1, "go").Ok);
        var row = journal.Find(3)!;
        Assert.Equal("met", row.Get("step"));
        Assert.Equal(1, row.Cond);
        Assert.Equal(7, row.Flags);
        Assert.Equal(7, row.JournalWord);
        Assert.Equal(7, Assert.Single(machine.List(journal).Active).JournalWord);

        var loaded = QuestJournal.Load(journal.Save());
        Assert.Equal("met", loaded.Find(3)!.Get("step"));
        Assert.Equal(7, loaded.Find(3)!.JournalWord);
    }

    [Fact]
    public void Rewards_and_a_missing_item_do_not_partially_apply()
    {
        var (machine, inventory) = Machine("""
            {"quests":[{"id":5,"name":"Pay","dialogs":[
              {"npcId":1,"hook":"event","event":"pay","when":"started","html":"<html><body>ok</body></html>","ops":[
                {"op":"take_item","itemId":90001,"count":1},
                {"op":"give_adena","count":10},
                {"op":"give_item","itemId":736,"count":1},
                {"op":"give_exp_sp","exp":50,"sp":5}
              ]},
              {"npcId":1,"hook":"event","event":"go","when":"absent","html":"<html><body>a</body></html>","ops":[{"op":"create"},{"op":"start"}]}
            ]}]}
            """);
        var journal = new QuestJournal();
        Assert.True(machine.AdvEvent(journal, inventory, 5, 1, "go").Ok);
        var missing = machine.AdvEvent(journal, inventory, 5, 1, "pay");
        Assert.Equal(QuestFailure.Item, missing.Failure);
        Assert.Equal(0, inventory.CountOf(57));
        Assert.Equal(0, inventory.CountOf(736));
        Assert.Equal(0, missing.Exp);

        inventory.Add(90001, 1);
        var paid = machine.AdvEvent(journal, inventory, 5, 1, "pay");
        Assert.True(paid.Ok);
        Assert.Equal(0, inventory.CountOf(90001));
        Assert.Equal(10, inventory.CountOf(57));
        Assert.Equal(1, inventory.CountOf(736));
        Assert.Equal(50, paid.Exp);
        Assert.Equal(5, paid.Sp);
        Assert.Equal(QuestNotice.Adena, paid.Lines[0].Notice);
        Assert.Equal(QuestNotice.ItemEarned, paid.Lines[1].Notice);
        Assert.Equal(QuestNotice.ExpSp, paid.Lines[2].Notice);
        Assert.Contains(QuestSounds.ItemGet, paid.Sounds);
    }

    [Fact]
    public void Kill_hook_advances_cond_and_is_not_required_for_other_quests()
    {
        var (machine, inventory) = Machine("""
            {"quests":[{"id":11,"name":"Hunt","dialogs":[
              {"npcId":1,"hook":"event","event":"go","when":"absent","html":"<html><body>a</body></html>","ops":[{"op":"create"},{"op":"start"}]},
              {"npcId":20001,"hook":"kill","when":"started","cond":1,"ops":[{"op":"set_cond","cond":2}]}
            ]}]}
            """);
        var journal = new QuestJournal();
        Assert.True(machine.AdvEvent(journal, inventory, 11, 1, "go").Ok);
        var idle = machine.OnKill(journal, inventory, 20002);
        Assert.True(idle.Ok);
        Assert.False(idle.JournalChanged);
        Assert.Equal(1, journal.Find(11)!.Cond);

        var killed = ((IQuestKillHook)machine).OnKill(journal, inventory, 20001);
        Assert.True(killed.Ok);
        Assert.Equal(2, journal.Find(11)!.Cond);
        Assert.Contains(QuestSounds.Middle, killed.Sounds);
    }

    [Fact]
    public void Default_page_lists_a_talk_hook()
    {
        var (machine, inventory) = Machine("""
            {"quests":[{"id":12,"name":"Ask","dialogs":[
              {"npcId":4,"hook":"talk","when":"absent","html":"<html><body>Ask me.</body></html>","ops":[{"op":"create"}]}
            ]}]}
            """);
        var journal = new QuestJournal();
        var page = machine.FirstTalk(journal, inventory, 4, "Roxxy");
        Assert.Equal("default", page.Hook);
        Assert.Contains("quest_talk:12", page.Html, StringComparison.Ordinal);
        var talk = machine.Talk(journal, inventory, 12, 4);
        Assert.True(talk.Ok);
        Assert.Contains("Ask me.", talk.Html, StringComparison.Ordinal);
        Assert.Equal(QuestPhase.Created, journal.Find(12)!.Phase);
    }

    static (QuestMachine Machine, PlayerInventory Inventory) Machine(string json)
    {
        var items = new ItemCatalog(
        [
            new ItemTemplate(57, "Adena", ItemKind.Etc, BodySlot.None, "icon.etc_adena_i00", Stackable: true),
            new ItemTemplate(736, "Scroll of Escape", ItemKind.Etc, BodySlot.None, "icon.etc_scroll_of_return_i00", Stackable: true),
            new ItemTemplate(90001, "Roxxy's Note", ItemKind.Etc, BodySlot.None, "icon.etc_letter_white_i00", Stackable: true)
        ]);
        var catalog = QuestCatalog.Parse(json, items, "test");
        return (new QuestMachine(catalog, items), new PlayerInventory(items));
    }

    static string Sample(int maxActive) 
    {
        var quests = new System.Text.StringBuilder();
        quests.Append("{\"maxActive\":").Append(maxActive).Append(",\"quests\":[");
        for (int id = 1; id <= maxActive + 1; id++)
        {
            if (id > 1)
                quests.Append(',');
            quests.Append("{\"id\":").Append(id)
                .Append(",\"name\":\"Q").Append(id)
                .Append("\",\"dialogs\":[{\"npcId\":1,\"hook\":\"event\",\"event\":\"go\",\"when\":\"absent\",\"html\":\"<html><body>a</body></html>\",\"ops\":[{\"op\":\"create\"},{\"op\":\"start\"}]}]}");
        }

        quests.Append("]}");
        return quests.ToString();
    }

    static string Finishable(int id, bool repeatable) =>
        "{\"quests\":[{\"id\":" + id + ",\"name\":\"End\",\"repeatable\":" + (repeatable ? "true" : "false") + ",\"dialogs\":[" +
        "{\"npcId\":1,\"hook\":\"event\",\"event\":\"go\",\"when\":\"absent\",\"html\":\"<html><body>a</body></html>\",\"ops\":[{\"op\":\"create\"},{\"op\":\"start\"}]}," +
        "{\"npcId\":1,\"hook\":\"event\",\"event\":\"finish\",\"when\":\"started\",\"html\":\"<html><body>b</body></html>\",\"ops\":[{\"op\":\"set\",\"key\":\"note\",\"value\":\"x\"},{\"op\":\"complete\"}]}," +
        "{\"npcId\":1,\"hook\":\"event\",\"event\":\"again\",\"when\":\"completed\",\"html\":\"<html><body>c</body></html>\",\"ops\":[{\"op\":\"start\"}]}" +
        "]}]}";
}
