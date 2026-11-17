using Fineage.Accounts;
using Fineage.Game.Quests;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Block G1 subclasses after Mobius <c>VillageMaster</c> <c>Subclass N</c> and <c>Player.addSubClass</c> /
/// <c>setActiveClass</c> / <c>modifySubClass</c>: add at 75+, a level-40 slot per subclass, separate level / exp / SP /
/// skills, switching back and forth, the master filter, the three-slot cap and persistence of the class state.
/// </summary>
public sealed class NetSubclassTests
{
    const int Gladiator = 2, Warlord = 3, Paladin = 5, DarkAvenger = 6, TreasureHunter = 8, Hawkeye = 9;
    const int Hannavalt = 30109; // VillageMasterFighter
    const int Angus = 30474;     // VillageMasterDElf

    static string LastHtml(WorldBench bench) =>
        Assert.IsType<NpcHtmlMsg>(bench.Rec.Of("FanoutNpcHtml").Last()[1]).Html;

    static List<int> Notices(WorldBench bench) =>
        bench.Rec.Of("FanoutSystemMessage").Select(c => Assert.IsType<SystemMessageMsg>(c[1]).Id).ToList();

    static async Task<(WorldBench Bench, BenchPlayer P, ZoneEntity Master)> Open(int classId = Gladiator, int level = 76, int race = 0, int masterId = Hannavalt)
    {
        var bench = new WorldBench();
        var p = await bench.EnterAsync(classId: classId, race: race, level: level);
        var master = bench.World.SpawnFolkForTests(masterId, p.Player.X + 30, p.Player.Y, p.Player.Z);
        bench.Rec.Calls.Clear();
        return (bench, p, master);
    }

    [Fact]
    public async Task Fighter_master_lists_human_and_elf_fighter_classes_outside_the_base_line()
    {
        var (bench, p, master) = await Open();
        Assert.Null(await p.BypassAsync(master.ObjectId, "Subclass 0"));
        Assert.Contains("Subclass 1", LastHtml(bench));

        Assert.Null(await p.BypassAsync(master.ObjectId, "Subclass 1"));
        string list = LastHtml(bench);
        Assert.Contains($"Subclass 4 {Paladin}\"", list);
        Assert.Contains($"Subclass 4 {Warlord}\"", list);
        Assert.Contains($"Subclass 4 {Hawkeye}\"", list);
        Assert.DoesNotContain($"Subclass 4 {Gladiator}\"", list); // the base class
        Assert.DoesNotContain("Subclass 4 12\"", list);            // Sorcerer: mystic master's
        Assert.DoesNotContain("Subclass 4 33\"", list);            // Shillien Knight: Dark Elf master's
        Assert.DoesNotContain("Subclass 4 51\"", list);            // Overlord never
        Assert.Equal(bench.World.AvailableSubclasses(p.Player, master), bench.World.AvailableSubclasses(p.Player, master).Order());
    }

    [Fact]
    public async Task Adding_a_subclass_starts_it_at_40_and_switching_keeps_each_class_progress()
    {
        var (bench, p, master) = await Open();
        p.Player.Sp = 12345;
        p.Player.LearnedSkills[1] = 9; // a base-only learned row

        Assert.Null(await p.BypassAsync(master.ObjectId, $"Subclass 4 {Paladin}"));
        Assert.Contains("added a new subclass", LastHtml(bench));
        Assert.Contains(ZoneWorld.SubclassAddedMessageId, Notices(bench));
        Assert.Equal(Paladin, p.Player.ClassId);
        Assert.Equal(Paladin, p.Session.ClassId);
        Assert.Equal(1, p.Player.ClassIndex);
        Assert.Equal(Gladiator, p.Player.BaseClassId);
        Assert.Equal(ZoneWorld.SubclassStartLevel, p.Player.Level);
        Assert.Equal(0, p.Player.Sp);
        Assert.Empty(p.Player.LearnedSkills);
        Assert.Equal(bench.Game.Classes!.Experience.ExpToBeLevel(40), p.Player.Exp);
        Assert.Contains(bench.Rec.Of("FanoutMagicSkillUse"),
            c => Assert.IsType<MagicSkillUseMsg>(c[1]).SkillId == ZoneWorld.ClassChangeAnimationSkillId);
        Assert.NotEmpty(bench.Rec.Of("FanoutShortcutInit"));

        var saved = p.SavedRecord();
        Assert.Equal(Paladin, saved.ClassId);
        Assert.Equal(40, saved.Level);
        Assert.NotNull(saved.ClassState);
        Assert.Equal(Gladiator, saved.BaseClassId);
        Assert.Equal(1, saved.ClassState!.ClassIndex);
        var parked = Assert.Single(saved.ClassState.Slots);
        Assert.Equal((0, Gladiator, 76, 12345), (parked.Index, parked.ClassId, parked.Level, parked.Sp));
        Assert.Contains(parked.Skills, s => s.SkillId == 1 && s.Level == 9);

        // Paladin progress is its own.
        p.Player.Sp = 50;
        Assert.Null(await p.BypassAsync(master.ObjectId, "Subclass 5 1"));
        Assert.Contains("current subclass", LastHtml(bench));

        Assert.Null(await p.BypassAsync(master.ObjectId, "Subclass 5 0"));
        Assert.Contains(ZoneWorld.SubclassTransferMessageId, Notices(bench));
        Assert.Equal((Gladiator, 0, 76, 12345), (p.Player.ClassId, p.Player.ClassIndex, p.Player.Level, p.Player.Sp));
        Assert.Equal(9, p.Player.LearnedSkills[1]);
        var sub = Assert.Single(p.Player.ClassSlots);
        Assert.Equal((1, Paladin, 40, 50), (sub.Index, sub.ClassId, sub.Level, sub.Sp));
        Assert.Equal(Gladiator, p.SavedRecord().ClassId);
        Assert.Equal(76, p.SavedRecord().Level);
    }

    [Fact]
    public async Task Subclass_state_survives_relog()
    {
        var (bench, p, master) = await Open();
        Assert.Null(await p.BypassAsync(master.ObjectId, $"Subclass 4 {Paladin}"));
        await bench.World.LeaveAsync(p.Session, CancellationToken.None);

        var again = await bench.EnterAsync(p.Ticket.Account, p.Ticket.CharacterName);
        Assert.Equal(Paladin, again.Player.ClassId);
        Assert.Equal(1, again.Player.ClassIndex);
        Assert.True(again.Player.SubClassActive);
        Assert.Equal(Gladiator, again.Player.BaseClassId);
        Assert.Equal(76, Assert.Single(again.Player.ClassSlots).Level);
    }

    [Fact]
    public async Task Level_75_and_every_subclass_at_75_are_required_and_three_is_the_cap()
    {
        var (bench, low, master) = await Open(level: 74);
        Assert.Null(await low.BypassAsync(master.ObjectId, $"Subclass 4 {Paladin}"));
        Assert.Contains("aren't eligible", LastHtml(bench));
        Assert.Equal(Gladiator, low.Player.ClassId);

        var (bench2, p, master2) = await Open();
        Assert.Null(await p.BypassAsync(master2.ObjectId, $"Subclass 4 {Paladin}"));
        // The new subclass is level 40: a second one is refused until it reaches 75.
        Assert.Null(await p.BypassAsync(master2.ObjectId, $"Subclass 4 {Hawkeye}"));
        Assert.Contains("aren't eligible", LastHtml(bench2));
        Assert.Single(bench2.World.SubclassesOf(p.Player));

        p.Player.Level = 75;
        Assert.Null(await p.BypassAsync(master2.ObjectId, $"Subclass 4 {Hawkeye}"));
        p.Player.Level = 75;
        Assert.Null(await p.BypassAsync(master2.ObjectId, $"Subclass 4 {Warlord}"));
        p.Player.Level = 75;
        Assert.Equal(3, bench2.World.SubclassesOf(p.Player).Count);
        Assert.Null(await p.BypassAsync(master2.ObjectId, "Subclass 1"));
        Assert.Contains("aren't eligible", LastHtml(bench2));
        Assert.Null(await p.BypassAsync(master2.ObjectId, $"Subclass 4 {TreasureHunter}"));
        Assert.Equal(3, bench2.World.SubclassesOf(p.Player).Count);
    }

    [Fact]
    public async Task Exclusive_groups_and_chosen_subclasses_leave_the_list()
    {
        var (bench, p, master) = await Open(classId: Paladin, level: 76);
        var list = bench.World.AvailableSubclasses(p.Player, master);
        Assert.DoesNotContain(DarkAvenger, list); // DA / Paladin / TK / SK
        Assert.DoesNotContain(20, list);          // Temple Knight
        Assert.Contains(Hawkeye, list);

        Assert.Null(await p.BypassAsync(master.ObjectId, $"Subclass 4 {Hawkeye}"));
        list = bench.World.AvailableSubclasses(p.Player, master);
        Assert.DoesNotContain(Hawkeye, list);
    }

    [Fact]
    public async Task Elves_get_nothing_from_a_dark_elf_master()
    {
        var (bench, elf, master) = await Open(classId: 20, race: 1, masterId: Angus); // Temple Knight
        Assert.Empty(bench.World.AvailableSubclasses(elf.Player, master));
        Assert.Null(await elf.BypassAsync(master.ObjectId, "Subclass 1"));
        Assert.Contains("aren't eligible", LastHtml(bench));
    }

    [Fact]
    public async Task Modify_replaces_a_subclass_with_a_fresh_level_40_one()
    {
        var (bench, p, master) = await Open();
        Assert.Null(await p.BypassAsync(master.ObjectId, $"Subclass 4 {Paladin}"));
        p.Player.Level = 60;
        Assert.Null(await p.BypassAsync(master.ObjectId, "Subclass 3"));
        Assert.Contains(">Paladin<", LastHtml(bench));
        Assert.Null(await p.BypassAsync(master.ObjectId, "Subclass 6 1"));
        Assert.Contains($"Subclass 7 1 {Hawkeye}\"", LastHtml(bench));

        Assert.Null(await p.BypassAsync(master.ObjectId, $"Subclass 7 1 {Hawkeye}"));
        Assert.Contains("changed subclasses", LastHtml(bench));
        Assert.Equal((Hawkeye, 1, 40), (p.Player.ClassId, p.Player.ClassIndex, p.Player.Level));
        Assert.Equal(Gladiator, Assert.Single(p.Player.ClassSlots).ClassId);
    }

    [Fact]
    public async Task Subclass_quest_gate_is_off_by_default_and_noblesse_passes_it()
    {
        var (bench, p, master) = await Open();
        bench.World.SubclassRequireQuests = true;
        Assert.Null(await p.BypassAsync(master.ObjectId, $"Subclass 4 {Paladin}"));
        Assert.Contains("aren't eligible", LastHtml(bench)); // Fate's Whisper not done

        p.Player.Quests.Add(ZoneWorld.FatesWhisperQuestId, QuestPhase.Completed);
        Assert.Null(await p.BypassAsync(master.ObjectId, $"Subclass 4 {Paladin}"));
        Assert.Equal(Paladin, p.Player.ClassId);
    }
}
