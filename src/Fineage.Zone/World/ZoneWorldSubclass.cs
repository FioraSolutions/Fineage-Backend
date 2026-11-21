using System.Text;
using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Protocol;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

/// <summary>
/// Block G1 subclasses after Mobius <c>VillageMaster</c> (<c>Subclass N</c>) and <c>Player.addSubClass</c> /
/// <c>setActiveClass</c> / <c>modifySubClass</c> (rules reimplemented, not copied).
/// <list type="bullet">
/// <item><description>Up to three subclasses. Adding one needs the active class at 75+ and every existing subclass at 75+.</description></item>
/// <item><description>A new subclass starts at level 40 with no SP; class skills come from the auto tree for its level.</description></item>
/// <item><description>Each class index keeps its own level, exp, SP, learned skills, hennas and shortcuts. Buffs drop on a switch.</description></item>
/// <item><description>Candidates: second-profession classes except Overlord and Warsmith, minus the base line, chosen subs and the
/// exclusive groups (DA/Paladin/TK/SK, TH/AW/PW, Hawkeye/SR/PR, Warlock/ES/PS, Sorcerer/SS/SH); Elves and Dark Elves exclude
/// each other; Human/Elf masters teach their fighter/mystic/priest type of Human/Elf classes, other masters their race.</description></item>
/// <item><description>Interlude does not need Mimir's Elixir. Fate's Whisper (Q234) is only checked when
/// <see cref="SubclassRequireQuests"/> is on (FINEAGE_SUBCLASS_REQUIRE_QUESTS); nobles always pass (Mobius checkQuests).</description></item>
/// </list>
/// </summary>
public sealed partial class ZoneWorld
{
    public const int MaxSubclasses = 3;
    public const int SubclassMinLevel = 75;
    public const int SubclassStartLevel = 40;
    internal const int SubclassAddedMessageId = 1269;
    internal const int SubclassTransferMessageId = 1270;
    internal const int SubclassCastingMessageId = 1295;
    internal const int SubclassWeightMessageId = 1894;
    internal const int SubclassInventoryMessageId = 2033;
    /// <summary>Mobius MagicSkillUse 5103: the class-change flash.</summary>
    internal const int ClassChangeAnimationSkillId = 5103;
    internal const int FatesWhisperQuestId = 234;

    const int RaceHuman = 0, RaceElf = 1, RaceDarkElf = 2, RaceOrc = 3, RaceDwarf = 4;
    static readonly int[] NeverSubclassed = [51, 57]; // Overlord, Warsmith
    static readonly int[][] ExclusiveSubclassGroups =
    [
        [6, 5, 20, 33],   // Dark Avenger, Paladin, Temple Knight, Shillien Knight
        [8, 23, 36],      // Treasure Hunter, Plains Walker, Abyss Walker
        [9, 24, 37],      // Hawkeye, Silver Ranger, Phantom Ranger
        [14, 28, 41],     // Warlock, Elemental Summoner, Phantom Summoner
        [12, 27, 40]      // Sorcerer, Spellsinger, Spellhowler
    ];
    /// <summary>Mobius <c>ClassType.PRIEST</c> roots: Cleric, Elven Oracle, Shillien Oracle and their children.</summary>
    static readonly int[] PriestLines = [15, 29, 42];

    /// <summary>Mobius <c>ALT_GAME_SUBCLASS_WITHOUT_QUESTS = false</c> → Fate's Whisper required. Default off for Interlude.</summary>
    public bool SubclassRequireQuests { get; set; } =
        Environment.GetEnvironmentVariable("FINEAGE_SUBCLASS_REQUIRE_QUESTS") is "1" or "true";

    void LoadClassState(ZoneEntity entity, CharacterRecord? stored)
    {
        var state = stored?.ClassState;
        entity.ClassSlots.Clear();
        if (state == null)
            return;
        entity.Noble = state.Noble;
        if (state.BaseClassId > 0)
            entity.BaseClassId = state.BaseClassId;
        entity.ClassIndex = Math.Clamp(state.ClassIndex, 0, MaxSubclasses);
        foreach (var slot in state.Slots.OrderBy(s => s.Index))
        {
            if (slot.Index is >= 0 and <= MaxSubclasses && slot.Index != entity.ClassIndex && PlayerClasses.TryGet(slot.ClassId, out _))
                entity.ClassSlots.Add(slot);
        }
    }

    static CharacterClassState ClassStateOf(ZoneEntity player) => new()
    {
        BaseClassId = player.BaseClassId,
        ClassIndex = player.ClassIndex,
        Noble = player.Noble,
        Slots = player.ClassSlots.Select(CloneSlot).ToList()
    };

    static StoredClassSlot CloneSlot(StoredClassSlot s) => new()
    {
        Index = s.Index,
        ClassId = s.ClassId,
        Level = s.Level,
        Exp = s.Exp,
        Sp = s.Sp,
        Skills = [.. s.Skills],
        HennaDye1 = s.HennaDye1,
        HennaDye2 = s.HennaDye2,
        HennaDye3 = s.HennaDye3,
        Shortcuts = [.. s.Shortcuts]
    };

    void SaveClassState(ZoneSession session, ZoneEntity player) =>
        _persist.SaveClassState(session.Account, session.ObjectId, ClassStateOf(player));

    /// <summary>Subclasses only (Mobius <c>getSubClasses</c>), ordered by index, including the active one.</summary>
    internal IReadOnlyList<(int Index, int ClassId, int Level)> SubclassesOf(ZoneEntity player)
    {
        var rows = new List<(int, int, int)>();
        foreach (var slot in player.ClassSlots)
        {
            if (slot.Index > 0)
                rows.Add((slot.Index, slot.ClassId, slot.Level));
        }
        if (player.SubClassActive)
            rows.Add((player.ClassIndex, player.ClassId, player.Level));
        rows.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return rows;
    }

    string? SubclassBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, int choice, string args)
    {
        if (!IsVillageMasterNpc(npc))
            return "bad_request";
        long now = NowUnixMs();
        if (player.CastingUntilMs > now)
        {
            Notice(session, SubclassCastingMessageId, "Sub classes may not be created or changed while a skill is in use.");
            return null;
        }
        // Mobius: an Olympiad registration is withdrawn first.
        _olympiad.Board.Unregister(ActorFor(player), now);
        if (player.Inventory != null && player.Inventory.Items.Count >= InventoryLimit(player) * 0.9)
        {
            Notice(session, SubclassInventoryMessageId, "A sub-class cannot be created or changed because you have exceeded your inventory limit.");
            return null;
        }
        if (player.WeightPenaltyLevel >= 2)
        {
            Notice(session, SubclassWeightMessageId, "A sub-class cannot be created or changed while you are over your weight limit.");
            return null;
        }

        var p = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int one = p.Length > 0 && int.TryParse(p[0], out int a) ? a : 0;
        int two = p.Length > 1 && int.TryParse(p[1], out int b) ? b : 0;
        var subs = SubclassesOf(player);
        string? page;
        switch (choice)
        {
            case 0:
                page = Page("SubClass.htm");
                break;
            case 1:
                {
                    var available = subs.Count >= MaxSubclasses ? [] : AvailableSubclasses(player, npc);
                    page = available.Count == 0
                        ? Page("SubClass_Fail.htm")
                        : Page("SubClass_Add.htm")?.Replace("%list%", ClassLinks(available, id =>
                            $"<a action=\"bypass npc_%objectId%_Subclass 4 {id}\" msg=\"1268;{ClassName(id)}\">{ClassName(id)}</a><br>"));
                    break;
                }
            case 2:
                if (subs.Count == 0)
                {
                    page = Page("SubClass_ChangeNo.htm");
                    break;
                }
                var list = new StringBuilder();
                list.Append($"<a action=\"bypass -h npc_%objectId%_Subclass 5 0\">{ClassName(player.BaseClassId)}</a><br>");
                foreach (var s in subs)
                    list.Append($"<a action=\"bypass -h npc_%objectId%_Subclass 5 {s.Index}\">{ClassName(s.ClassId)}</a><br>");
                page = Page("SubClass_Change.htm")?.Replace("%list%", list.ToString());
                break;
            case 3:
                if (subs.Count == 0)
                {
                    page = Page("SubClass_ModifyEmpty.htm");
                    break;
                }
                page = Page("SubClass_Modify.htm");
                for (int i = 1; i <= MaxSubclasses; i++)
                {
                    var row = subs.FirstOrDefault(s => s.Index == i);
                    page = page?.Replace($"%sub{i}%", row.Index == i ? ClassName(row.ClassId) : "<!-- -->");
                }
                break;
            case 4:
                if (!CanAddSubclass(player, subs) || !IsValidNewSubclass(player, npc, one))
                {
                    page = Page("SubClass_Fail.htm");
                    break;
                }
                if (!AddSubclass(session, player, one, subs.Count + 1))
                    return "bad_request";
                Notice(session, SubclassAddedMessageId, "The new sub class has been added.");
                page = Page("SubClass_AddOk.htm");
                break;
            case 5:
                if (player.ClassIndex == one)
                {
                    page = Page("SubClass_Current.htm");
                    break;
                }
                if (one != 0 && subs.All(s => s.Index != one))
                    return "bad_request";
                SetActiveClass(session, player, one);
                Notice(session, SubclassTransferMessageId, "The transfer of sub class has been completed.");
                return null; // Mobius: no page after a plain switch.
            case 6:
                {
                    if (one < 1 || one > MaxSubclasses)
                        return "bad_request";
                    var available = AvailableSubclasses(player, npc);
                    if (available.Count == 0)
                    {
                        Plain(session, "There are no sub classes available at this time.");
                        return null;
                    }
                    page = Page(one is >= 1 and <= 3 ? $"SubClass_ModifyChoice{one}.htm" : "SubClass_ModifyChoice.htm")
                        ?.Replace("%list%", ClassLinks(available, id =>
                            $"<a action=\"bypass npc_%objectId%_Subclass 7 {one} {id}\" msg=\"1445;\">{ClassName(id)}</a><br>"));
                    break;
                }
            case 7:
                if (one < 1 || subs.All(s => s.Index != one) || !IsValidNewSubclass(player, npc, two, replacingIndex: one))
                    return "bad_request";
                ModifySubclass(session, player, one, two);
                Notice(session, SubclassAddedMessageId, "The new sub class has been added.");
                page = Page("SubClass_ModifyOk.htm")?.Replace("%name%", ClassName(two));
                break;
            default:
                return "bad_request";
        }

        SendHtml(session, player, npc, page ?? QuestBypassHandler.Unavailable);
        return null;
    }

    string? Page(string file) => _html.TryGet("villagemaster/" + file, out var html) ? html : null;

    string ClassName(int classId) =>
        PlayerClasses.TryGet(classId, out var t) && t != null ? t.Name : $"Class {classId}";

    static string ClassLinks(IEnumerable<int> ids, Func<int, string> link) =>
        string.Concat(ids.Select(link));

    void Plain(ZoneSession session, string text) => Notice(session, 0, text);

    bool CanAddSubclass(ZoneEntity player, IReadOnlyList<(int Index, int ClassId, int Level)> subs)
    {
        if (subs.Count >= MaxSubclasses || player.Level < SubclassMinLevel)
            return false;
        if (subs.Any(s => s.Level < SubclassMinLevel))
            return false;
        if (!SubclassRequireQuests || player.Noble)
            return true;
        return player.Quests.Find(FatesWhisperQuestId)?.Phase == QuestPhase.Completed;
    }

    /// <summary>Mobius <c>getAvailableSubClasses</c> for this master, sorted by class id.</summary>
    internal IReadOnlyList<int> AvailableSubclasses(ZoneEntity player, ZoneEntity npc) =>
        FilterByMaster(SubclassCandidates(player, replacingIndex: 0), npc);

    /// <summary>
    /// Mobius master filter (ALT_GAME_SUBCLASS_EVERYWHERE off): Human/Elf masters (fighter / mystic / priest types) teach
    /// their class type of Human and Elf classes; Dark Elf, Orc and Dwarf masters teach their own race.
    /// </summary>
    List<int> FilterByMaster(List<int> candidates, ZoneEntity npc)
    {
        string type = npc.NpcType ?? "";
        int masterRace = type switch
        {
            "VillageMasterDElf" => RaceDarkElf,
            "VillageMasterOrc" => RaceOrc,
            "VillageMasterDwarf" => RaceDwarf,
            _ => RaceHuman
        };
        string teach = type switch
        {
            "VillageMasterPriest" => "priest",
            "VillageMasterMystic" => "mystic",
            _ => "fighter"
        };
        return candidates
            .Where(id =>
            {
                int race = PlayerClasses.Get(id).Race;
                return masterRace == RaceHuman
                    ? (race == RaceHuman || race == RaceElf) && ClassTypeOf(id) == teach
                    : race == masterRace;
            })
            .OrderBy(id => id)
            .ToList();
    }

    /// <summary>Mobius <c>getSubclasses</c> + chosen-sub removal (no master filter).</summary>
    List<int> SubclassCandidates(ZoneEntity player, int replacingIndex)
    {
        int baseSecond = SecondOccupationOf(player.BaseClassId);
        if (baseSecond < 0)
            return [];
        int race = player.Race;
        var taken = new HashSet<int>();
        foreach (var s in SubclassesOf(player))
        {
            if (s.Index != replacingIndex)
                taken.Add(SecondOccupationOf(s.ClassId) is var second and >= 0 ? second : s.ClassId);
        }

        var exclusive = ExclusiveSubclassGroups.FirstOrDefault(g => g.Contains(baseSecond)) ?? [];
        var baseLine = new HashSet<int>(PlayerClasses.ParentChain(player.BaseClassId));
        var activeLine = new HashSet<int>(PlayerClasses.ParentChain(player.ClassId));
        return PlayerClasses.All
            .Where(t => t.ClassLevel == 2 && !NeverSubclassed.Contains(t.ClassId))
            .Where(t => !(race == RaceElf && t.Race == RaceDarkElf) && !(race == RaceDarkElf && t.Race == RaceElf))
            .Select(t => t.ClassId)
            .Where(id => id != baseSecond && !exclusive.Contains(id) && !baseLine.Contains(id) && !activeLine.Contains(id) && !taken.Contains(id))
            .ToList();
    }

    /// <summary>Mobius <c>isValidNewSubClass</c>: offered by this master and not already one of the subclasses.</summary>
    bool IsValidNewSubclass(ZoneEntity player, ZoneEntity npc, int classId, int replacingIndex = 0) =>
        FilterByMaster(SubclassCandidates(player, replacingIndex), npc).Contains(classId);

    /// <summary>The second-profession class of <paramref name="classId"/>'s line (itself or its parent), -1 when below it.</summary>
    int SecondOccupationOf(int classId)
    {
        if (!PlayerClasses.TryGet(classId, out var t) || t == null)
            return -1;
        return t.ClassLevel switch
        {
            2 => classId,
            3 => t.ParentClassId,
            _ => -1
        };
    }

    /// <summary>Mobius <c>ClassType</c>: fighter roots, priests (Cleric / Oracle lines), the rest mystic.</summary>
    string ClassTypeOf(int classId)
    {
        var chain = PlayerClasses.ParentChain(classId);
        if (chain.Any(PriestLines.Contains))
            return "priest";
        int root = chain[0];
        return root is 10 or 25 or 38 or 49 ? "mystic" : "fighter";
    }

    /// <summary>Mobius <c>addSubClass</c> + <c>setActiveClass(new index)</c>.</summary>
    bool AddSubclass(ZoneSession session, ZoneEntity player, int classId, int index)
    {
        if (index is < 1 or > MaxSubclasses || SubclassesOf(player).Any(s => s.Index == index))
            return false;
        if (player.Identity?.BaseClassId is null or 0)
            player.BaseClassId = player.ClassId;
        player.ClassSlots.Add(new StoredClassSlot
        {
            Index = index,
            ClassId = classId,
            Level = SubclassStartLevel,
            Exp = PlayerClasses.Experience.ExpToBeLevel(SubclassStartLevel),
            Sp = 0
        });
        SetActiveClass(session, player, index);
        ZoneLog.Info($"subclass-add object={player.ObjectId} index={index} class={classId}");
        return true;
    }

    /// <summary>Mobius <c>modifySubClass</c>: the old subclass is wiped (skills, hennas, shortcuts) and a fresh one takes its index.</summary>
    void ModifySubclass(ZoneSession session, ZoneEntity player, int index, int classId)
    {
        if (player.ClassIndex == index)
            SetActiveClass(session, player, 0, announce: false);
        player.ClassSlots.RemoveAll(s => s.Index == index);
        player.ClassSlots.Add(new StoredClassSlot
        {
            Index = index,
            ClassId = classId,
            Level = SubclassStartLevel,
            Exp = PlayerClasses.Experience.ExpToBeLevel(SubclassStartLevel),
            Sp = 0
        });
        SetActiveClass(session, player, index);
        ZoneLog.Info($"subclass-modify object={player.ObjectId} index={index} class={classId}");
    }

    /// <summary>
    /// Mobius <c>setActiveClass</c>: park the playing class (level, exp, SP, skills, hennas, shortcuts) in its slot, load the
    /// target slot, drop buffs, rebuild the skill book and vitals, and broadcast. Persists everything the switch touched.
    /// </summary>
    internal void SetActiveClass(ZoneSession session, ZoneEntity player, int index, bool announce = true)
    {
        var target = player.ClassSlots.FirstOrDefault(s => s.Index == index);
        if (target == null || index == player.ClassIndex)
            return;
        if (!PlayerClasses.TryGet(target.ClassId, out var template) || template == null)
            return;

        player.ClassSlots.Remove(target);
        player.ClassSlots.Add(new StoredClassSlot
        {
            Index = player.ClassIndex,
            ClassId = player.ClassId,
            Level = player.Level,
            Exp = player.Exp,
            Sp = player.Sp,
            Skills = player.LearnedSkills.Select(kv => new StoredSkill(kv.Key, kv.Value)).ToList(),
            HennaDye1 = player.HennaDye1,
            HennaDye2 = player.HennaDye2,
            HennaDye3 = player.HennaDye3,
            Shortcuts = player.Shortcuts.Values.ToList()
        });
        player.ClassSlots.Sort((a, b) => a.Index.CompareTo(b.Index));

        player.ClassIndex = index;
        player.ClassId = target.ClassId;
        session.ChangeClass(target.ClassId);
        player.Level = Math.Max(1, target.Level);
        player.Exp = Math.Max(0, target.Exp);
        player.Sp = Math.Max(0, target.Sp);
        player.LearnedSkills.Clear();
        foreach (var s in target.Skills)
        {
            if (s.IsValid)
                player.LearnedSkills[s.SkillId] = s.Level;
        }
        player.HennaDye1 = target.HennaDye1;
        player.HennaDye2 = target.HennaDye2;
        player.HennaDye3 = target.HennaDye3;
        RefreshHenna(player);
        player.Shortcuts.Clear();
        foreach (var sc in target.Shortcuts)
        {
            if (sc != null && sc.IsValid)
                player.Shortcuts[sc.WorldSlot] = sc;
        }

        // Mobius stopAllEffectsExceptThoseThatLastThroughDeath: buffs of the old class do not carry over.
        bool hadAbnormals = player.Abnormals.Count > 0;
        player.Abnormals.Clear();
        player.BaseRunSpeed = template.RunSpeed;
        player.BaseWalkSpeed = template.WalkSpeed;
        player.Dex = template.Dex;
        RefreshKnownSkills(player);
        RefreshGearSkills(player);
        RefreshPlayerMaxima(player, fanout: false);
        player.CurHp = Math.Min(player.CurHp, player.MaxHp);
        player.CurMp = Math.Min(player.CurMp, player.MaxMp);
        RefreshGradePenalty(player);
        if (hadAbnormals)
            FanoutAbnormal(player);
        FanoutNetMoveType(player);
        FanoutMoveType(player);

        long now = NowUnixMs();
        NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, now));
        NetBridge?.FanoutShortcutInit([player.ObjectId], BuildShortcutInit(session));
        SelfUserInfo(player);
        var watchers = KnownNetPlayersOf(player);
        var peers = watchers.Where(id => id != player.ObjectId).ToList();
        if (peers.Count > 0)
            NetBridge?.FanoutCharInfo(peers, player.ObjectId);
        if (announce)
        {
            NetBridge?.FanoutMagicSkillUse(watchers, new MagicSkillUseMsg
            {
                CasterObjectId = player.ObjectId,
                TargetObjectId = player.ObjectId,
                SkillId = ClassChangeAnimationSkillId,
                SkillLevel = 1
            });
        }

        // Mobius setActiveClass exits Q422 Repent Your Sins.
        player.Quests.Remove(422);

        _persist.SaveClass(session.Account, session.ObjectId, player.ClassId);
        _persist.ReplaceLearnedSkills(session.Account, session.ObjectId,
            player.LearnedSkills.Select(kv => new StoredSkill(kv.Key, kv.Value)).ToList());
        _persist.SaveHenna(session.Account, session.ObjectId, player.HennaDye1, player.HennaDye2, player.HennaDye3);
        _persist.ReplaceShortcuts(session.Account, session.ObjectId, player.Shortcuts.Values.ToList());
        SaveQuestProgress(session, player); // level / exp / sp / vitals + quests
        SaveClassState(session, player);
        ZoneLog.Info($"subclass-switch object={player.ObjectId} index={index} class={player.ClassId} level={player.Level}");
    }
}
