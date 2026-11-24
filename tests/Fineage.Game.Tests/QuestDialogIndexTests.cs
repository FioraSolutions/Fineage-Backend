using System.Text;
using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>
/// The per-key dialog index (<see cref="QuestDefinition.DialogsByKey"/>) is a view over the datapack's own step list.
/// A lookup must return exactly what a linear scan of <see cref="QuestDefinition.Dialogs"/> returns: the first step of
/// that (hook, npc, event) key, in datapack order, that passes the gates.
/// </summary>
public sealed class QuestDialogIndexTests
{
    [Fact]
    public void Index_keeps_datapack_order_within_a_key()
    {
        var (machine, _) = Machine(Steps("go", "a", "b", "c"));
        var quest = machine.QuestDef(7)!;
        var group = Assert.Single(quest.DialogsByKey);

        Assert.Equal(3, group.Value.Count);
        Assert.Equal(quest.Dialogs, group.Value);
    }

    [Fact]
    public void Index_separates_keys_by_hook_npc_and_event()
    {
        var (machine, _) = Machine(
            """
            {"id":7,"name":"Q","dialogs":[
              {"npcId":1,"hook":"talk","when":"absent","html":"<html><body>a</body></html>"},
              {"npcId":2,"hook":"talk","when":"absent","html":"<html><body>b</body></html>"},
              {"npcId":1,"hook":"event","event":"go","when":"absent","html":"<html><body>c</body></html>"},
              {"npcId":1,"hook":"event","event":"stop","when":"absent","html":"<html><body>d</body></html>"}
            ]}
            """);

        var quest = machine.QuestDef(7)!;
        Assert.Equal(4, quest.DialogsByKey.Count);
        Assert.True(quest.DialogsByKey.ContainsKey(new QuestDialogKey(QuestHook.Talk, 1, "")));
        Assert.True(quest.DialogsByKey.ContainsKey(new QuestDialogKey(QuestHook.Talk, 2, "")));
        Assert.True(quest.DialogsByKey.ContainsKey(new QuestDialogKey(QuestHook.Event, 1, "go")));
        Assert.True(quest.DialogsByKey.ContainsKey(new QuestDialogKey(QuestHook.Event, 1, "stop")));
    }

    [Fact]
    public void Key_normalizes_the_event_name_for_hooks_that_do_not_use_it()
    {
        // A talk step matches whatever event name the caller passes: the index key must say so too.
        Assert.Equal(new QuestDialogKey(QuestHook.Talk, 5, ""), QuestDialogKey.Of(QuestHook.Talk, 5, "whatever"));
        Assert.Equal(new QuestDialogKey(QuestHook.Event, 5, "go"), QuestDialogKey.Of(QuestHook.Event, 5, "go"));
    }

    [Fact]
    public void The_first_step_that_passes_the_gates_wins_even_when_an_earlier_one_fails()
    {
        // Step 1 is gated to level 40+, step 2 is open: a level 1 character must get step 2.
        var (machine, inventory) = Machine(
            """
            {"id":7,"name":"Q","dialogs":[
              {"npcId":1,"hook":"event","event":"go","when":"absent","minLevel":40,"html":"<html><body>high</body></html>"},
              {"npcId":1,"hook":"event","event":"go","when":"absent","html":"<html><body>low</body></html>"}
            ]}
            """);

        var outcome = machine.AdvEvent(new QuestJournal(), inventory, 7, 1, "go", new QuestActor(0, 1, 0));
        Assert.True(outcome.Ok);
        Assert.Contains("low", outcome.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_step_is_marked_when_any_step_of_its_key_reaches_the_character()
    {
        // Regression cover for CollectNpcMarks: it asks the key, not the individual step, so a key whose open step
        // exists still marks its gated siblings. Indexing must not change which NPCs get a marker.
        var (machine, _) = Machine(
            """
            {"id":7,"name":"Q","dialogs":[
              {"npcId":300,"hook":"event","event":"go","when":"absent","minLevel":40,"html":"<html><body>high</body></html>"},
              {"npcId":300,"hook":"event","event":"go","when":"absent","html":"<html><body>low</body></html>"}
            ]}
            """);

        var marks = machine.CollectNpcMarks(new QuestJournal(), new QuestActor(0, 1, 0));
        Assert.Contains(marks, m => m.NpcId == 300 && m.Mark == 1);
    }

    [Fact]
    public void A_key_nothing_reaches_gets_no_marker()
    {
        var (machine, _) = Machine(
            """
            {"id":7,"name":"Q","dialogs":[
              {"npcId":301,"hook":"event","event":"go","when":"absent","minLevel":40,"html":"<html><body>high</body></html>"}
            ]}
            """);

        var marks = machine.CollectNpcMarks(new QuestJournal(), new QuestActor(0, 1, 0));
        Assert.DoesNotContain(marks, m => m.NpcId == 301);
    }

    [Fact]
    public void Started_quest_with_a_completing_step_marks_three()
    {
        var (machine, _) = Machine(
            """
            {"id":7,"name":"Q","dialogs":[
              {"npcId":302,"hook":"event","event":"go","when":"started","html":"<html><body>a</body></html>","ops":[{"op":"complete"}]}
            ]}
            """);

        var journal = new QuestJournal();
        journal.Add(7, QuestPhase.Started);
        var marks = machine.CollectNpcMarks(journal, new QuestActor(0, 1, 0));
        Assert.Contains(marks, m => m.NpcId == 302 && m.Mark == 3);
    }

    [Fact]
    public void Started_quest_without_a_completing_step_marks_two()
    {
        var (machine, _) = Machine(
            """
            {"id":7,"name":"Q","dialogs":[
              {"npcId":303,"hook":"event","event":"go","when":"started","html":"<html><body>a</body></html>"}
            ]}
            """);

        var journal = new QuestJournal();
        journal.Add(7, QuestPhase.Started);
        var marks = machine.CollectNpcMarks(journal, new QuestActor(0, 1, 0));
        Assert.Contains(marks, m => m.NpcId == 303 && m.Mark == 2);
    }

    [Fact]
    public void Shipped_pack_index_groups_are_exact_ordered_slices_of_the_step_list()
    {
        // The index is a pure view: every key's group must be exactly the steps of Dialogs with that key, in the
        // datapack's own order. That is what keeps a lookup equal to the old linear scan.
        var data = GameData.LoadDefault();
        foreach (var quest in data.Quests.Quests)
        {
            int indexed = 0;
            foreach (var (key, group) in quest.DialogsByKey)
            {
                indexed += group.Count;
                var expected = quest.Dialogs
                    .Where(dialog => QuestDialogKey.Of(dialog.Hook, dialog.NpcId, dialog.Event) == key)
                    .ToList();
                Assert.Equal(expected, group);
            }

            Assert.Equal(quest.Dialogs.Count, indexed);
        }
    }

    static (QuestMachine Machine, PlayerInventory Inventory) Machine(string questJson)
    {
        var items = new ItemCatalog(
        [
            new ItemTemplate(57, "Adena", ItemKind.Etc, BodySlot.None, "icon.etc_adena_i00", Stackable: true),
            new ItemTemplate(90001, "Note", ItemKind.Etc, BodySlot.None, "icon.etc_letter_white_i00", Stackable: true)
        ]);
        var catalog = QuestCatalog.Parse("{\"maxActive\":25,\"quests\":[" + questJson + "]}", items, "QuestDialogIndexTests");
        return (new QuestMachine(catalog, items, () => 0), new PlayerInventory(items));
    }

    /// <summary>Several steps under one event name, to exercise a key group with more than one entry.</summary>
    static string Steps(string eventName, params string[] unused)
    {
        var dialogs = new StringBuilder();
        dialogs.Append("{\"id\":7,\"name\":\"Q\",\"dialogs\":[");
        for (int i = 0; i < unused.Length; i++)
        {
            if (i > 0)
                dialogs.Append(',');
            dialogs.Append("{\"npcId\":1,\"hook\":\"event\",\"event\":\"")
                .Append(eventName)
                .Append("\",\"when\":\"absent\",\"html\":\"<html><body>")
                .Append(i)
                .Append("</body></html>\"}");
        }

        dialogs.Append("]}");
        return dialogs.ToString();
    }
}
