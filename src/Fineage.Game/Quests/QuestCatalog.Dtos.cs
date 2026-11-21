using System.Text.Json;

namespace Fineage.Game.Quests;

public sealed partial class QuestCatalog
{
    sealed class MetaDto
    {
        public int MaxActive { get; set; }
        public int TalkRange { get; set; }
    }

    sealed class QuestFileDto
    {
        public int MaxActive { get; set; }
        public int TalkRange { get; set; }
        public List<QuestDto>? Quests { get; set; }
    }

    sealed class QuestDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool Repeatable { get; set; }
        public bool? Abortable { get; set; }
        public List<int>? QuestItems { get; set; }
        public List<DialogDto>? Dialogs { get; set; }
        public Dictionary<string, List<OpDto>>? CondClientFx { get; set; }
        public List<JournalRewardDto>? JournalRewards { get; set; }
    }

    sealed class JournalRewardDto
    {
        public string? Kind { get; set; }
        public int ItemId { get; set; }
        public long Count { get; set; }
        public string? Label { get; set; }
    }

    sealed class DialogDto
    {
        public int NpcId { get; set; }
        public string? Hook { get; set; }
        public string? Event { get; set; }
        public string? When { get; set; }
        public int? Cond { get; set; }
        public string? WhenVar { get; set; }
        public string? WhenValue { get; set; }
        public string? Html { get; set; }
        /// <summary>Plain file name under the quest folder (Mobius HTML sibling).</summary>
        public string? HtmlFile { get; set; }
        public List<OpDto>? Ops { get; set; }
        public List<int>? Races { get; set; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public int HasItem { get; set; }
        public int MinCount { get; set; } = 1;
        public bool Party { get; set; }
        public bool Everyone { get; set; }
        public List<int>? Classes { get; set; }
        public List<int>? HasItems { get; set; }
        public List<int>? LacksItems { get; set; }
        public Dictionary<string, int>? AtLeast { get; set; }
        public Dictionary<string, int>? Below { get; set; }
        public List<int>? HasAny { get; set; }
        public bool? IsMage { get; set; }
        /// <summary>Mobius <c>player.isSubClassActive()</c>. Null = no gate.</summary>
        public bool? SubClass { get; set; }
        /// <summary>Mobius <c>getRandom(n) &lt; k</c> branch: the row matches with this probability (0..1). Null = always.</summary>
        public double? Chance { get; set; }
        /// <summary>Mobius <c>st.getInt(key) &gt;= n</c> per key (missing var reads 0).</summary>
        public Dictionary<string, int>? VarAtLeast { get; set; }
        /// <summary>Mobius <c>st.getInt(key) &lt; n</c> per key (missing var reads 0).</summary>
        public Dictionary<string, int>? VarBelow { get; set; }
        /// <summary>Mobius <c>getPaperdollItemId(PAPERDOLL_RHAND)</c> in this list. Null = no gate.</summary>
        public List<int>? Weapons { get; set; }
        public string? Owned { get; set; }
        public string? Pet { get; set; }
        public bool? ByPet { get; set; }
        public int MinEnchant { get; set; }
        public List<int>? SumItems { get; set; }
        public int SumMin { get; set; }
        public int SumMax { get; set; } = -1;
        public string? HoldsVarItem { get; set; }
        public HtmlByVarDto? HtmlByVar { get; set; }
    }

    sealed class OpDto
    {
        public string? Op { get; set; }
        public int? Cond { get; set; }
        public string? Key { get; set; }
        public string? Value { get; set; }
        public int ItemId { get; set; }
        public int? Count { get; set; }
        public long? Exp { get; set; }
        public int? Sp { get; set; }
        public string? Name { get; set; }
        public double? Chance { get; set; }
        public int? Limit { get; set; }
        public int? CondAtLimit { get; set; }
        public int? RewardItemId { get; set; }
        public int? Per { get; set; }
        public int? BonusMin { get; set; }
        public int? BonusAdena { get; set; }
        public int NpcId { get; set; }
        public List<PriceDto>? Prices { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int? Z { get; set; }
        public int? Action { get; set; }
        /// <summary><c>take_all</c>: item ids to remove entirely (Mobius <c>takeItems(player, id, -1)</c>).</summary>
        public List<int>? Items { get; set; }
        public string? Text { get; set; }
        public int? MinEnchant { get; set; }
        public int? Offset { get; set; }
        public int? ClassId { get; set; }
        public bool AtPlayer { get; set; }
        public int? DespawnSec { get; set; }
        public bool Owned { get; set; }
        public bool SoloGuard { get; set; }
        public string? Say { get; set; }
        public string? GoneSay { get; set; }
        public string? GuardSay { get; set; }
        public Dictionary<string, string>? GoneVars { get; set; }
        public Dictionary<string, string>? DeadVars { get; set; }
        public string? Kind { get; set; }
        public string? Html { get; set; }
        public int EventId { get; set; }
        public bool On { get; set; } = true;
    }

    sealed class HtmlByVarDto
    {
        public string? Var { get; set; }
        /// <summary>Var value → html file in the quest folder.</summary>
        public Dictionary<string, string>? Files { get; set; }
    }

    sealed class PriceDto
    {
        public int ItemId { get; set; }
        public int Adena { get; set; }
    }
}
