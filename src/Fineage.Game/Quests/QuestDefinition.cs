namespace Fineage.Game.Quests;

public enum QuestHook
{
    FirstTalk,
    Talk,
    Event,
    Kill,
    /// <summary>Mobius <c>onAttack</c>: a player's hit lands on the NPC (no page; gates such as <c>weapons</c> apply).</summary>
    Attack
}

public enum QuestWhen
{
    Absent,
    /// <summary>Any state (Mobius onAdvEvent pages that only branch on class / level / items).</summary>
    Any,
    Created,
    Started,
    Completed
}

public abstract record QuestOp;

public sealed record CreateOp : QuestOp;

public sealed record StartOp : QuestOp;

public sealed record SetCondOp(int Cond) : QuestOp;

public sealed record SetVarOp(string Key, string Value) : QuestOp;

/// <summary>Mobius <c>st.set(key, st.getInt(key) + n)</c>: integer counter (missing var reads 0).</summary>
public sealed record AddVarOp(string Key, int Delta) : QuestOp;

/// <summary>Mobius <c>takeItems(player, id, -1)</c> for each id: removes whatever is held; never fails.</summary>
public sealed record TakeAllOp(IReadOnlyList<int> ItemIds) : QuestOp;

public sealed record UnsetVarOp(string Key) : QuestOp;

public sealed record GiveItemOp(int ItemId, int Count) : QuestOp;

public sealed record GiveAdenaOp(int Count) : QuestOp;

public sealed record GiveExpSpOp(long Exp, int Sp) : QuestOp;

public sealed record TakeItemOp(int ItemId, int Count) : QuestOp;

public sealed record CompleteOp : QuestOp;

/// <summary>
/// Mobius <c>giveItemRandomly(player, npc, item, count, limit, chance, playSound)</c> on kill:
/// roll <c>chance</c>, never above <c>limit</c> (0 = no cap), ItemGet sound, Middle + <c>condAtLimit</c> when the cap is reached.
/// </summary>
public sealed record DropItemOp(int ItemId, int Count, double Chance, int Limit, int? CondAtLimit) : QuestOp;

/// <summary>
/// Hand in every <c>ItemId</c> for <c>Per</c> x <c>RewardItemId</c> each, plus <c>BonusAdena</c> when at least <c>BonusMin</c>
/// were handed in (Mobius Q267 style <c>rewardItems(count)</c> + <c>giveAdena</c> if count &gt;= 10).
/// </summary>
/// <summary>Adena per handed-in item (all registered kinds), plus a bonus when the total is at least <c>BonusMin</c> (Mobius Q316).</summary>
public sealed record SellItemsOp(IReadOnlyList<(int ItemId, int Adena)> Items, int BonusMin, int BonusAdena) : QuestOp;

public sealed record ExchangeAllOp(int ItemId, int RewardItemId, int Per, int BonusMin, int BonusAdena) : QuestOp;

public sealed record SoundOp(string Name) : QuestOp;

/// <summary>Mobius <c>TutorialShowHtml</c> / question mark / client event hooks.</summary>
public sealed record TutorialUiOp(TutorialUiWireKind Kind, string Html, int EventId, bool On) : QuestOp;

public enum TutorialUiWireKind : byte
{
    ShowHtml = 1,
    CloseHtml = 2,
    QuestionMark = 3,
    EnableClientEvent = 4,
}

/// <summary>
/// Mobius <c>addSpawn(npcId, npc, randomOffset, despawnDelay, isSummonSpawn)</c> from a quest event.
/// Zone materializes the NPC near the talking NPC and adds hate toward the player.
/// </summary>
/// <summary>Mobius <c>addSpawn</c>: near the dialog / killed NPC, or at a fixed point when X/Y/Z are set.</summary>
public sealed record SpawnNpcOp(int NpcId, int? X = null, int? Y = null, int? Z = null) : QuestOp;

/// <summary>A quest spawn at a fixed world point (Mobius <c>addSpawn(id, x, y, z, …)</c>).</summary>
public readonly record struct QuestSpawnPoint(int NpcId, int X, int Y, int Z);

/// <summary>Mobius <c>player.setNoble(true)</c> (Possessor of a Precious Soul 4).</summary>
public sealed record SetNobleOp : QuestOp;

/// <summary>Mobius <c>player.teleToLocation(x, y, z)</c> from a quest event.</summary>
public sealed record TeleportOp(int X, int Y, int Z) : QuestOp;

/// <summary>Mobius <c>broadcastPacket(new SocialAction(player, action))</c>.</summary>
public sealed record SocialOp(int Action) : QuestOp;

/// <summary>
/// Mobius <c>addSpawn</c> + a per-spawn quest timer (sagas): an NPC owned by the quest player, optionally at the
/// player's feet, that despawns after <see cref="DespawnSec"/> and writes vars back into the owner's quest row.
/// </summary>
/// <summary>Mobius saga <c>startQuestTimer</c> chat line after <paramref name="DelayMs"/> from spawn.</summary>
public sealed record QuestScheduledSay(int DelayMs, string Text);

public sealed record QuestSummon(
    int NpcId,
    int? X,
    int? Y,
    int? Z,
    bool AtPlayer,
    int DespawnSec,
    /// <summary>Kill / attack credit for this spawn goes to the owner's journal (<c>owned</c> steps).</summary>
    bool Owned,
    /// <summary>Mobius Archon of Halisha: a hit from anyone else, or from the owner in a party, makes it leave.</summary>
    bool SoloGuard,
    string? Say,
    string? GoneSay,
    string? GuardSay,
    IReadOnlyDictionary<string, string>? GoneVars,
    IReadOnlyDictionary<string, string>? DeadVars,
    IReadOnlyList<QuestScheduledSay>? ScheduledSays = null)
{
    /// <summary>Quest whose row the vars go into; filled when the step runs.</summary>
    public int QuestId { get; init; }
}

public sealed record SummonOp(QuestSummon Summon) : QuestOp;

/// <summary>Mobius <c>NpcSay</c> from the dialog / killed NPC; <c>PLAYERNAME</c> becomes the player's name.</summary>
public sealed record SayOp(string Text) : QuestOp;

/// <summary>Mobius <c>player.setPlayerClass</c> (+ base class when no subclass is active) — the saga 3rd class.</summary>
public sealed record ChangeClassOp(int ClassId) : QuestOp;

/// <summary>Mobius <c>deleteSpawn(npc)</c> on the dialog / attacked NPC, without its despawn vars.</summary>
public sealed record DespawnOp : QuestOp;

/// <summary>Mobius saga helper <c>cast(npc, player, skillId, level)</c> — visual <c>MagicSkillUse</c> only.</summary>
public sealed record CastSkillOp(int SkillId, int Level) : QuestOp;

/// <summary>Mobius Q419 quiz: take one random space-separated token out of <c>Pool</c> into <c>Target</c>.</summary>
public sealed record PickRandomOp(string Pool, string Target) : QuestOp;

/// <summary>Store the object id of the first held item of <c>Items</c> (enchant at least <c>MinEnchant</c>) in <c>Key</c> (Q421 flute).</summary>
public sealed record RememberItemOp(string Key, IReadOnlyList<int> Items, int MinEnchant) : QuestOp;

/// <summary>Swap the item whose object id is in <c>Key</c> for item id + <c>Offset</c>, keeping its enchant (Q421 flute → bugle).</summary>
public sealed record ExchangeVarItemOp(string Key, IReadOnlyList<int> Items, int Offset) : QuestOp;

/// <summary>Page chosen after the ops ran, by the value of a quest var (Q419 random quiz question).</summary>
public sealed record QuestHtmlByVar(string Var, IReadOnlyDictionary<string, string> Pages);

/// <summary>Side effects that zone carries out after the journal commit (summons, NPC speech, class change).</summary>
public sealed class QuestExtras
{
    public static QuestExtras None { get; } = new();

    public List<QuestSummon> Summons { get; } = [];
    public List<string> Says { get; } = [];
    public List<CastSkillOp> Casts { get; } = [];
    public int ClassChange { get; set; }
    public bool Despawn { get; set; }

    public bool Empty => Summons.Count == 0 && Says.Count == 0 && Casts.Count == 0 && ClassChange == 0 && !Despawn;

    public void MergeFrom(QuestExtras other)
    {
        Summons.AddRange(other.Summons);
        Says.AddRange(other.Says);
        Casts.AddRange(other.Casts);
        if (other.ClassChange != 0)
            ClassChange = other.ClassChange;
        Despawn |= other.Despawn;
    }
}

public sealed record QuestDialog(
    int NpcId,
    QuestHook Hook,
    string Event,
    QuestWhen When,
    int? Cond,
    string? WhenVar,
    string? WhenValue,
    string Html,
    IReadOnlyList<QuestOp> Ops,
    IReadOnlyList<int>? Races = null,
    int MinLevel = 0,
    int MaxLevel = 0,
    int HasItem = 0,
    int MinCount = 1,
    bool Party = false,
    IReadOnlyList<int>? Classes = null,
    IReadOnlyList<int>? HasItems = null,
    IReadOnlyList<int>? LacksItems = null,
    IReadOnlyDictionary<int, int>? AtLeast = null,
    /// <summary>Mobius <c>hasAtLeastOneQuestItem</c> — at least one listed item held.</summary>
    IReadOnlyList<int>? HasAny = null,
    /// <summary>Mobius <c>player.isMageClass()</c>. Null = no gate.</summary>
    bool? IsMage = null,
    /// <summary>Mobius <c>player.isSubClassActive()</c>. Null = no gate.</summary>
    bool? SubClass = null,
    double? Chance = null,
    IReadOnlyDictionary<string, int>? VarAtLeast = null,
    IReadOnlyDictionary<string, int>? VarBelow = null,
    IReadOnlyList<int>? Weapons = null,
    /// <summary>Mobius <c>getQuestItemsCount(id) &lt; n</c> per item (the upper bound of an <see cref="AtLeast"/> ladder).</summary>
    IReadOnlyDictionary<int, int>? Below = null,
    /// <summary>Mobius <c>executeForEachPlayer</c> kill step: every party member near the corpse runs it, not only the killer.</summary>
    bool Everyone = false,
    /// <summary>
    /// Kill / attack step on a quest-owned spawn, run against the owner's journal: <c>any</c> (whoever killed it)
    /// or <c>solo</c> (the owner killed it outside a party). Null = an ordinary step.
    /// </summary>
    string? Owned = null,
    /// <summary>Summoned pet vs the collar registered in var <c>summonOid</c>: none / out / registered / other.</summary>
    string? Pet = null,
    /// <summary>Attack step: the hit came from the player's pet (true) or the player (false).</summary>
    bool? ByPet = null,
    /// <summary>With <see cref="HasAny"/>: one of those items carries at least this enchant (pet collar level).</summary>
    int MinEnchant = 0,
    /// <summary>Sum of the held counts of these items must lie in [SumMin, SumMax] (SumMax -1 = no cap).</summary>
    IReadOnlyList<int>? SumItems = null,
    int SumMin = 0,
    int SumMax = -1,
    /// <summary>An item whose object id is stored in this var is still in the bag.</summary>
    string? HoldsVarItem = null,
    QuestHtmlByVar? HtmlByVar = null,
    bool? InRiftPeace = null,
    bool? InRift = null,
    int MaxStarted = 0,
    /// <summary>
    /// Catalog key for a file-backed page (<c>html/quests/&lt;folder&gt;/&lt;file&gt;</c>).
    /// Null when the HTML is inline in the quest JSON. The zone serves the player's locale.
    /// </summary>
    string? HtmlKey = null);

/// <summary>
/// Who is talking. Mobius quest scripts branch on <c>player.getRace()</c> / <c>getLevel()</c>
/// before showing the start page; dialogs carry the same gates as data (races, minLevel, maxLevel).
/// <c>ClassId</c> -1 = unknown (class gates do not match); Mobius path quests check <c>player.getClassId()</c>.
/// </summary>
public readonly record struct QuestActor(int Race, int Level, int ClassId = -1, bool SubClass = false)
{
    /// <summary>Control item object id of the summoned pet (0 = none).</summary>
    public int PetControlObjectId { get; init; }

    /// <summary>Attack hook: the landed hit came from the player's pet.</summary>
    public bool ByPet { get; init; }

    /// <summary>Mobius dimensional rift waiting room (type 0 room 0).</summary>
    public bool? InRiftPeace { get; init; }

    /// <summary>Mobius <c>checkIfInRiftZone</c> outer band (type 0 room 1).</summary>
    public bool? InRift { get; init; }

    /// <summary>Active started quest rows (Mobius <c>getAllActiveQuests().size()</c>).</summary>
    public int StartedQuestCount { get; init; }

    /// <summary>
    /// Uniform [0, 1) draws for this actor's random branches, drops and picks (the zone passes the acting character's
    /// region stream). Unset uses the machine's own source.
    /// </summary>
    public Func<double>? Roll { get; init; }
}

public sealed record QuestDefinition(
    int Id,
    string Name,
    bool Repeatable,
    bool Abortable,
    IReadOnlyList<int> QuestItems,
    IReadOnlyList<QuestDialog> Dialogs,
    IReadOnlyDictionary<int, IReadOnlyList<QuestOp>> CondClientFx = null!,
    IReadOnlyList<QuestJournalReward> JournalRewards = null!)
{
    public IReadOnlyDictionary<int, IReadOnlyList<QuestOp>> CondClientFx { get; init; } =
        CondClientFx ?? new Dictionary<int, IReadOnlyList<QuestOp>>();

    public IReadOnlyList<QuestJournalReward> JournalRewards { get; init; } =
        JournalRewards ?? Array.Empty<QuestJournalReward>();
}
