using System.Text.Json;

namespace Fineage.Game.Quests;

/// <summary>
/// Third-class sagas (Mobius quests 70–100). Mobius runs one shared Java state machine (AbstractSagaQuest) over
/// per-saga arrays; Fineage keeps the arrays as data (<c>data/quests/_sagas.json</c>, see tools/import_sagas.py)
/// and expands every saga into ordinary quest steps here, so the generic <see cref="QuestMachine"/> runs them.
/// Pages sit in the saga folder (no quest.json) and keep Mobius file names (0-01.htm … 11-03.htm).
/// </summary>
public sealed partial class QuestCatalog
{
    public const string SagaFileName = "_sagas.json";

    /// <summary>Mobius ARCHON_HALISHA_NORM: Four Sepulchers bosses that also hand out the Halisha proof.</summary>
    static readonly int[] HalishaArchons = [18212, 18214, 18215, 18216, 18218];
    static readonly int[] GuardianAngels = [27214, 27215, 27216];
    static readonly int[] ArchonMinions = [21646, 21647, 21648, 21649, 21650, 21651];

    /// <summary>Mobius saga reward: exp, adena and the Book of Giants-style item 6622 on top of the class.</summary>
    public const long SagaRewardExp = 2_299_404;
    public const int SagaRewardAdena = 5_000_000;
    public const int SagaRewardItem = 6622;
    public const int SagaMinLevel = 76;

    sealed class SagaFileDto
    {
        public List<SagaDto>? Sagas { get; set; }
    }

    sealed class SagaDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Folder { get; set; }
        public List<int> Npc { get; set; } = [];
        public List<int> Items { get; set; } = [];
        public List<int> Mob { get; set; } = [];
        public int ClassId { get; set; }
        public int PrevClass { get; set; }
        public List<int> X { get; set; } = [];
        public List<int> Y { get; set; } = [];
        public List<int> Z { get; set; } = [];
        public List<string> Text { get; set; } = [];
    }

    static void LoadSagas(string questsDirectory, ItemCatalog items, HashSet<int> seen, List<QuestDefinition> quests)
    {
        string path = Path.Combine(questsDirectory, SagaFileName);
        if (!File.Exists(path))
            return;
        SagaFileDto file;
        try
        {
            file = JsonSerializer.Deserialize<SagaFileDto>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"{path} was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not read {path}.", ex);
        }

        foreach (var saga in file.Sagas ?? [])
        {
            string where = $"{path} saga {saga.Id}";
            if (saga.Npc.Count != 12 || saga.Items.Count != 12 || saga.Mob.Count != 3 || saga.X.Count != 3 ||
                saga.Y.Count != 3 || saga.Z.Count != 3 || saga.Text.Count != 18)
                throw new InvalidDataException($"{where} needs npc[12], items[12], mob[3], x/y/z[3] and text[18].");
            string folder = (saga.Folder ?? "").Trim();
            if (folder.Length == 0 || folder.Contains('/') || folder.Contains('\\') || folder.Contains(".."))
                throw new InvalidDataException($"{where} needs a plain folder name.");
            string dir = Path.Combine(questsDirectory, folder);
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException($"{where} folder '{folder}' not found.");
            quests.Add(BuildQuest(SagaQuest(saga), items, where, dir, seen));
        }
    }

    /// <summary>Our expansion of the Mobius saga flow into data steps (conds 1–20, see docs in AbstractSagaQuest).</summary>
    static QuestDto SagaQuest(SagaDto s)
    {
        var npc = s.Npc;
        var it = s.Items;
        var mob = s.Mob;
        var text = s.Text;
        var cls = new List<int> { s.PrevClass };
        var rows = new List<DialogDto>();

        DialogDto Row(int npcId, string hook, string when, int? cond, string? page, params OpDto[] ops) => new()
        {
            NpcId = npcId,
            Hook = hook,
            When = when,
            Cond = cond,
            HtmlFile = page,
            Ops = ops.ToList(),
            Classes = cls
        };
        DialogDto Talk(int npcId, int? cond, string page) => Row(npcId, "talk", cond == null ? "absent" : "started", cond, page);
        DialogDto Ev(int npcId, string evt, int? cond, string? page, params OpDto[] ops)
        {
            var row = Row(npcId, "event", cond == null ? "any" : "started", cond, page, ops);
            row.Event = evt;
            return row;
        }
        DialogDto Kill(int npcId, int cond, params OpDto[] ops) => Row(npcId, "kill", "started", cond, null, ops);
        static OpDto SetCond(int c) => new() { Op = "set_cond", Cond = c };
        static OpDto Give(int id, int n = 1) => new() { Op = "give_item", ItemId = id, Count = n };
        static OpDto Take(int id, int n = 1) => new() { Op = "take_item", ItemId = id, Count = n };
        static OpDto TakeAll(int id) => new() { Op = "take_all", Items = [id] };
        static OpDto Set(string key, string value) => new() { Op = "set", Key = key, Value = value };
        static OpDto Add(string key) => new() { Op = "add", Key = key, Value = "1" };
        static OpDto Say(string line) => new() { Op = "say", Text = line };
        OpDto[] Reward(bool takeTicket)
        {
            var ops = new List<OpDto>();
            if (takeTicket)
                ops.Add(TakeAll(it[10]));
            ops.Add(new OpDto { Op = "give_exp_sp", Exp = SagaRewardExp, Sp = 0 });
            ops.Add(new OpDto { Op = "give_adena", Count = SagaRewardAdena });
            ops.Add(Give(SagaRewardItem));
            ops.Add(new OpDto { Op = "change_class", ClassId = s.ClassId });
            ops.Add(new OpDto { Op = "social", Action = 3 });
            ops.Add(new OpDto { Op = "complete" });
            return ops.ToArray();
        }

        // Start (npc0). Mobius 0-1: level < 76 → 0-02, else 0-05; accept starts and hands the saga ticket items[10].
        var completed = Row(npc[0], "talk", "completed", null, null);
        completed.Classes = null;
        completed.Html = "<html><body>This quest has already been completed.</body></html>";
        rows.Add(completed);
        rows.Add(Talk(npc[0], null, "0-01.htm"));
        var levelOk = Ev(npc[0], "0-1", null, "0-05.htm");
        levelOk.MinLevel = SagaMinLevel;
        rows.Add(levelOk);
        rows.Add(Ev(npc[0], "0-1", null, "0-02.htm"));
        var accept = Ev(npc[0], "accept", null, "0-03.htm", new OpDto { Op = "create" }, new OpDto { Op = "start" }, Give(it[10]));
        accept.When = "absent";
        accept.MinLevel = SagaMinLevel;
        rows.Add(accept);

        // cond 1–2: npc2 then npc1.
        rows.Add(Talk(npc[0], 1, "0-04.htm"));
        rows.Add(Talk(npc[2], 1, "2-01.htm"));
        rows.Add(Ev(npc[2], "2-1", 1, "2-05.htm", SetCond(2)));
        rows.Add(Talk(npc[2], 2, "2-02.htm"));
        rows.Add(Talk(npc[1], 2, "1-01.htm"));
        rows.Add(Ev(npc[1], "1-3", 2, "1-05.htm", SetCond(3)));

        // cond 3: bring items[0] (Ice Crystal) and, for some sagas, items[11].
        var need = new List<int> { it[0] };
        if (it[11] != 0)
            need.Add(it[11]);
        var ready = Talk(npc[1], 3, "1-03.htm");
        ready.HasItems = need;
        rows.Add(ready);
        if (it[11] != 0)
        {
            var half = Talk(npc[1], 3, "1-02.htm");
            half.HasItem = it[0];
            rows.Add(half);
        }
        rows.Add(Talk(npc[1], 3, "1-02.htm"));
        var hand = new List<OpDto> { SetCond(4), Take(it[0]) };
        if (it[11] != 0)
            hand.Add(Take(it[11]));
        hand.Add(Give(it[1]));
        var handIn = Ev(npc[1], "1-4", 3, "1-06.htm", hand.ToArray());
        handIn.HasItems = need;
        rows.Add(handIn);

        // cond 4–5: npc2 then npc5.
        rows.Add(Talk(npc[1], 4, "1-04.htm"));
        rows.Add(Talk(npc[2], 4, "2-03.htm"));
        rows.Add(Ev(npc[2], "2-2", 4, "2-06.htm", SetCond(5), Take(it[1]), Give(it[4])));
        rows.Add(Talk(npc[2], 5, "2-04.htm"));
        rows.Add(Talk(npc[5], 5, "5-01.htm"));
        rows.Add(Ev(npc[5], "5-1", 5, "5-02.htm", SetCond(6), Take(it[4])));

        // cond 6: ten guardian angels (killer only).
        rows.Add(Talk(npc[5], 6, "5-03.htm"));
        rows.Add(Talk(npc[6], 6, "6-01.htm"));
        foreach (int angel in GuardianAngels)
        {
            var done = Kill(angel, 6, Give(it[5]), SetCond(7));
            done.VarAtLeast = new() { ["kills"] = 9 };
            rows.Add(done);
            rows.Add(Kill(angel, 6, Add("kills")));
        }

        // cond 7–8: npc6 then npc7, who raises mob[0] for a five-minute solo fight.
        rows.Add(Talk(npc[6], 7, "6-02.htm"));
        rows.Add(Ev(npc[6], "6-1", 7, "6-03.htm", SetCond(8), Set("spawned", "0"), Take(it[5])));
        rows.Add(Talk(npc[6], 8, "6-04.htm"));
        rows.Add(Talk(npc[7], 8, "7-01.htm"));
        var busy = Ev(npc[7], "7-1", 8, "7-03.htm");
        busy.VarAtLeast = new() { ["spawned"] = 1 };
        rows.Add(busy);
        rows.Add(Ev(npc[7], "7-1", 8, "7-02.htm", Set("spawned", "1"), new OpDto
        {
            Op = "summon",
            NpcId = mob[0],
            X = s.X[0],
            Y = s.Y[0],
            Z = s.Z[0],
            DespawnSec = 300,
            Owned = true,
            Say = text[0],
            GoneSay = text[1],
            GoneVars = new() { ["spawned"] = "0" },
            DeadVars = new() { ["spawned"] = "0" }
        }));
        var slain = Kill(mob[0], 8, Say(text[12]), Give(it[6]), SetCond(9));
        slain.Owned = "solo";
        rows.Add(slain);

        // cond 9–10.
        rows.Add(Talk(npc[7], 9, "7-05.htm"));
        rows.Add(Ev(npc[7], "7-2", 9, "7-06.htm", SetCond(10), Take(it[6])));
        rows.Add(Talk(npc[7], 10, "7-07.htm"));
        rows.Add(Talk(npc[3], 10, "3-01.htm"));
        rows.Add(Ev(npc[3], "3-5", 10, "3-07.htm"));
        rows.Add(Ev(npc[3], "3-6", 10, "3-02.htm", SetCond(11)));
        rows.Add(Ev(npc[3], "3-7", 10, "3-03.htm", SetCond(12)));

        // cond 11/12: bring items[2] (Divine Stone of Wisdom).
        foreach (int c in new[] { 11, 12 })
        {
            var stone = Talk(npc[3], c, "3-05.htm");
            stone.HasItem = it[2];
            rows.Add(stone);
            rows.Add(Talk(npc[3], c, "3-04.htm"));
            var give = Ev(npc[3], "3-8", c, "3-08.htm", SetCond(13), Take(it[2]), Give(it[7]));
            give.HasItem = it[2];
            rows.Add(give);
        }

        // cond 13–15: npc8, npc11, then the Halisha hunt around npc9.
        rows.Add(Talk(npc[3], 13, "3-06.htm"));
        rows.Add(Talk(npc[8], 13, "8-01.htm"));
        rows.Add(Ev(npc[8], "8-1", 13, "8-02.htm", SetCond(14), Take(it[7])));
        rows.Add(Talk(npc[8], 14, "8-03.htm"));
        rows.Add(Talk(npc[11], 14, "11-01.htm"));
        rows.Add(Ev(npc[11], "11-9", 14, "11-03.htm", SetCond(15)));
        rows.Add(Talk(npc[11], 15, "11-02.htm"));
        rows.Add(Talk(npc[9], 15, "9-01.htm"));

        OpDto[] Halisha() => [Give(it[8]), TakeAll(it[3]), SetCond(16)];
        foreach (int minion in ArchonMinions)
        {
            // Mobius giveHalishaMark: 700+ marks raise the Archon of Halisha at the player (once); else 1–4 marks.
            var raise = Kill(minion, 15, Take(it[3], 20), Set("spawned", "1"), new OpDto
            {
                Op = "summon",
                NpcId = mob[1],
                AtPlayer = true,
                DespawnSec = 600,
                Owned = true,
                SoloGuard = true,
                Say = text[13],
                GoneSay = text[6],
                GuardSay = text[5],
                GoneVars = new() { ["spawned"] = "0" },
                DeadVars = new() { ["spawned"] = "0" }
            });
            raise.Party = true;
            raise.AtLeast = new() { [it[3].ToString()] = 700 };
            raise.VarBelow = new() { ["spawned"] = 1 };
            rows.Add(raise);
            int[] counts = [1, 2, 3, 4];
            double[] odds = [0.25, 1.0 / 3, 0.5, 1.0];
            for (int i = 0; i < counts.Length; i++)
            {
                var mark = Kill(minion, 15, Give(it[3], counts[i]));
                mark.Party = true;
                mark.VarBelow = new() { ["spawned"] = 1 };
                mark.Chance = odds[i];
                rows.Add(mark);
            }
        }
        foreach (int archon in HalishaArchons)
            rows.Add(Kill(archon, 15, [Say(text[4]), .. Halisha()]));
        var archonSlain = Kill(mob[1], 15, [Say(text[4]), .. Halisha()]);
        archonSlain.Owned = "solo";
        rows.Add(archonSlain);

        // cond 16–17: npc9, then npc10 raises mob[2] and the helper npc[4] for a one-minute fight.
        rows.Add(Talk(npc[9], 16, "9-02.htm"));
        rows.Add(Ev(npc[9], "9-1", 16, "9-03.htm", SetCond(17), Set("Quest0", "0"), Set("Tab", "0"), Take(it[8])));
        rows.Add(Talk(npc[9], 17, "9-04.htm"));
        rows.Add(Talk(npc[10], 17, "10-01.htm"));
        var fight = Ev(npc[10], "10-1", 17, "10-02.htm", Set("Quest0", "1"), new OpDto
        {
            Op = "summon",
            NpcId = mob[2],
            X = s.X[1],
            Y = s.Y[1],
            Z = s.Z[1],
            DespawnSec = 59,
            Owned = true,
            Say = text[14],
            GoneSay = text[15],
            GoneVars = new() { ["Quest0"] = "2" }
        }, new OpDto
        {
            Op = "summon",
            NpcId = npc[4],
            X = s.X[2],
            Y = s.Y[2],
            Z = s.Z[2],
            DespawnSec = 60,
            Owned = true,
            Say = text[7],
            ScheduledSays =
            [
                new ScheduledSayDto { DelayMs = 500, Text = text[7] },
                new ScheduledSayDto { DelayMs = 2000, Text = text[8] },
                new ScheduledSayDto { DelayMs = 12000, Text = text[9] }
            ],
            GoneVars = new() { ["Quest0"] = "0" }
        });
        fight.VarBelow = new() { ["Quest0"] = 1 };
        rows.Add(fight);
        rows.Add(Ev(npc[10], "10-1", 17, "10-04.htm"));

        // Owner hits on mob[2]: Quest0 counts hits; past 15 the enemy flees and the helper's Tab page opens.
        var flee = Row(mob[2], "attack", "started", 17, null, Set("Quest0", "1"), Set("Tab", "1"), Say(text[17]),
            new OpDto { Op = "despawn" });
        flee.Owned = "any";
        flee.VarAtLeast = new() { ["Quest0"] = 15 };
        rows.Add(flee);
        var firstHit = Row(mob[2], "attack", "started", 17, null, Add("Quest0"), Say(text[16]));
        firstHit.Owned = "any";
        firstHit.VarBelow = new() { ["Quest0"] = 1 };
        rows.Add(firstHit);
        var hit = Row(mob[2], "attack", "started", 17, null, Add("Quest0"));
        hit.Owned = "any";
        rows.Add(hit);
        var beaten = Kill(mob[2], 17, SetCond(18), Set("Quest0", "0"), Say(text[13]));
        beaten.Owned = "any";
        rows.Add(beaten);

        // Helper npc[4] (Mobius onFirstTalk by Tab / Quest0).
        void Helper(int tab, int quest0, string page)
        {
            var row = Row(npc[4], "first_talk", "started", 17, page);
            row.VarAtLeast = new() { ["Tab"] = tab, ["Quest0"] = quest0 };
            row.VarBelow = new() { ["Tab"] = tab + 1, ["Quest0"] = quest0 + 1 };
            rows.Add(row);
        }
        Helper(0, 0, "4-01.htm");
        Helper(0, 1, "4-03.htm");
        Helper(1, 0, "4-04.htm");
        Helper(1, 1, "4-06.htm");
        rows.Add(Row(npc[4], "first_talk", "started", 18, "4-08.htm"));
        rows.Add(Ev(npc[4], "4-1", 17, "4-010.htm", new OpDto { Op = "cast_skill", ItemId = 4339, Count = 1 }));
        rows.Add(Ev(npc[4], "4-2", 17, "4-011.htm", new OpDto { Op = "cast_skill", ItemId = 4339, Count = 1 },
            Give(it[9]), SetCond(18)));
        rows.Add(Ev(npc[4], "4-3", 17, null, Give(it[9]), SetCond(18), Say(text[13]), Set("Quest0", "0"),
            new OpDto { Op = "despawn" }));

        // cond 18–20: back to npc10, then the master (npc0) changes the class.
        rows.Add(Talk(npc[10], 18, "10-05.htm"));
        rows.Add(Ev(npc[10], "10-2", 18, "10-06.htm", SetCond(19), Take(it[9])));
        rows.Add(Talk(npc[10], 19, "10-07.htm"));
        rows.Add(Talk(npc[0], 19, "0-06.htm"));
        var finishOps = new List<OpDto> { new() { Op = "cast_skill", ItemId = 4339, Count = 1 } };
        finishOps.AddRange(Reward(takeTicket: true));
        var finish = Ev(npc[0], "0-2", 19, "0-07.htm", finishOps.ToArray());
        finish.MinLevel = SagaMinLevel;
        rows.Add(finish);
        rows.Add(Ev(npc[0], "0-2", 19, "0-08.htm", TakeAll(it[10]), SetCond(20)));
        var late = Talk(npc[0], 20, "0-09.htm");
        late.MinLevel = SagaMinLevel;
        late.Ops = Reward(takeTicket: false).ToList();
        rows.Add(late);
        rows.Add(Talk(npc[0], 20, "0-010.htm"));

        var questItems = new List<int>();
        for (int i = 0; i < it.Count; i++)
        {
            if (i is 0 or 2 || it[i] == 0 || questItems.Contains(it[i]))
                continue;
            questItems.Add(it[i]);
        }

        return new QuestDto
        {
            Id = s.Id,
            Name = s.Name,
            Repeatable = false,
            Abortable = true,
            QuestItems = questItems,
            Dialogs = rows
        };
    }
}
