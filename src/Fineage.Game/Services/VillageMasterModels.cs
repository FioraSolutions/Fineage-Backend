namespace Fineage.Game.Services;

/// <summary>Who stands in front of the village master (Mobius <c>player.getRace / getClassId / getLevel</c>).</summary>
public readonly record struct ClassActor(int Race, int ClassId, int ClassLevel, bool Mage, int Level);

/// <summary>One profession change (Mobius <c>ClassChangeRequested</c> branch).</summary>
public sealed record ClassChangeOption(
    string Event,
    int ClassId,
    int FromClass,
    IReadOnlyList<int> ProofItemIds,
    int MinLevel,
    string LowLevel,
    string LowLevelNoProof,
    string Success,
    string NoProof);

/// <summary>Mobius <c>isInCategory</c> branch of a talk or a change request.</summary>
public sealed record CategoryGuard(IReadOnlyList<string> Categories, string Page);

/// <summary>One <c>onTalk</c> rule: the first match wins (Mobius if/else tree).</summary>
public sealed record TalkRule(
    string Page,
    IReadOnlyList<int> Races,
    IReadOnlyList<int> Classes,
    IReadOnlyList<int> ClassLevels,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> AnyCategory)
{
    public bool IsElse => Races.Count == 0 && Classes.Count == 0 && ClassLevels.Count == 0 && Categories.Count == 0 && AnyCategory.Count == 0;
}

/// <summary>Mobius one village_master script (<c>ElfHumanFighterChange2</c>, ...) as data.</summary>
public sealed record ClassChangeScript(
    string Script,
    IReadOnlyList<int> Npcs,
    string PagePrefix,
    IReadOnlyList<CategoryGuard> Guards,
    IReadOnlyList<ClassChangeOption> Options,
    IReadOnlyList<TalkRule> Talk,
    IReadOnlyList<string> EventPages,
    int RewardItemId,
    int RewardCount);

/// <summary>
/// What the world does after a village-master bypass: show <see cref="Page"/> (a file under
/// <c>html/village_master/&lt;script&gt;/</c>); when <see cref="Change"/> is set, take the proof, set the class, and give the coupon first.
/// </summary>
public sealed record VillageMasterResult(string Script, string Page, ClassChangeOption? Change = null, int RewardItemId = 0, int RewardCount = 0)
{
    public string HtmlPath => $"village_master/{Script}/{Page}";
}

/// <summary>
/// Mobius village_master logic (FirstClassTransferTalk + every *Change1/*Change2 script) driven by
/// <c>data/village-masters.json</c>. Pure decisions; the zone applies them.
/// </summary>
