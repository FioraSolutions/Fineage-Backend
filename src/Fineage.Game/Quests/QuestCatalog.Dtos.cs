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
        public List<int>? Classes { get; set; }
        public List<int>? HasItems { get; set; }
        public List<int>? LacksItems { get; set; }
        public Dictionary<string, int>? AtLeast { get; set; }
        public List<int>? HasAny { get; set; }
        public bool? IsMage { get; set; }
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
    }

    sealed class PriceDto
    {
        public int ItemId { get; set; }
        public int Adena { get; set; }
    }
}
