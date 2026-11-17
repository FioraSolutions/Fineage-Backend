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
    IReadOnlyDictionary<int, int>? Below = null);

/// <summary>
/// Who is talking. Mobius quest scripts branch on <c>player.getRace()</c> / <c>getLevel()</c>
/// before showing the start page; dialogs carry the same gates as data (races, minLevel, maxLevel).
/// <c>ClassId</c> -1 = unknown (class gates do not match); Mobius path quests check <c>player.getClassId()</c>.
/// </summary>
public readonly record struct QuestActor(int Race, int Level, int ClassId = -1, bool SubClass = false);

public sealed record QuestDefinition(
    int Id,
    string Name,
    bool Repeatable,
    bool Abortable,
    IReadOnlyList<int> QuestItems,
    IReadOnlyList<QuestDialog> Dialogs);
