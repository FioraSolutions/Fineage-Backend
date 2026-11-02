namespace Fineage.Net.Contract
{
    public sealed class ZoneAuthMsg
    {
        public string Account { get; set; } = "";
        public string Token { get; set; } = "";
    }

    public sealed class ZoneAuthResultMsg
    {
        public bool Ok { get; set; }
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
    }

    public sealed class EnterWorldMsg
    {
    }

    public enum EntityKindNet : byte
    {
        Player = 1,
        Npc = 2,
        Item = 3,
    }

    public sealed class EntitySpawnMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public EntityKindNet Kind { get; set; }
        public bool IsSelf { get; set; }
        public string Name { get; set; } = "";
        public int TemplateId { get; set; }
        public int ClassId { get; set; }
        public int Race { get; set; }
        public int Sex { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
        public float RunSpeed { get; set; }
        public float WalkSpeed { get; set; }
        public bool Running { get; set; } = true;
        /// <summary>NPC only: true when the entity is combat-attackable (mobs).</summary>
        public bool Attackable { get; set; }
    }

    public sealed class PlayerStatsMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int Level { get; set; }
        public long Exp { get; set; }
        public int Sp { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Mp { get; set; }
        public int MaxMp { get; set; }
        public int Cp { get; set; }
        public int MaxCp { get; set; }
    }

    public sealed class MoveInputMsg
    {
        public int DestX { get; set; }
        public int DestY { get; set; }
        public int DestZ { get; set; }
    }

    public sealed class StopMoveMsg
    {
    }

    public sealed class MoveStateMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int OriginX { get; set; }
        public int OriginY { get; set; }
        public int OriginZ { get; set; }
        public int DestX { get; set; }
        public int DestY { get; set; }
        public int DestZ { get; set; }
        public bool Running { get; set; } = true;
    }

    public sealed class PositionCorrectionMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
    }

    /// <summary>Mobius/V2 ItemList row (self bag + paperdoll) for Net enter.</summary>
    public sealed class ItemListEntryNet
    {
        public int ObjectId { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int EnchantLevel { get; set; }
        /// <summary>Paperdoll slot index, or -1 when in the bag.</summary>
        public int EquippedSlot { get; set; } = -1;
    }

    public sealed class ItemListNetMsg
    {
        public ItemListEntryNet[] Items { get; set; } = System.Array.Empty<ItemListEntryNet>();
    }

    /// <summary>AutoSkills / known skill row (Mobius SkillList) for Net enter.</summary>
    public sealed class SkillListEntryNet
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
        public int ReuseRemainingMs { get; set; }
    }

    public sealed class SkillListNetMsg
    {
        public SkillListEntryNet[] Skills { get; set; } = System.Array.Empty<SkillListEntryNet>();
    }

    /// <summary>Forget / leave knownlist (Mobius DeleteObject) for Net peers.</summary>
    public sealed class EntityDespawnMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
    }

    public sealed class SelectTargetNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    public sealed class AttackRequestNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    public sealed class TargetSelectedNetMsg
    {
        public int TargetObjectId { get; set; }
        public int ColorHint { get; set; }
    }

    public sealed class AttackStateMsg
    {
        public ulong AttackerValue { get; set; }
        public uint AttackerGeneration { get; set; }
        public ulong TargetValue { get; set; }
        public uint TargetGeneration { get; set; }
        public int Damage { get; set; }
        public byte Flags { get; set; }
        public int AttackerX { get; set; }
        public int AttackerY { get; set; }
        public int AttackerZ { get; set; }
        public int TargetX { get; set; }
        public int TargetY { get; set; }
        public int TargetZ { get; set; }
    }

    public sealed class StatusUpdateNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int[] AttributeIds { get; set; } = System.Array.Empty<int>();
        public int[] Values { get; set; } = System.Array.Empty<int>();
    }

    public sealed class EntityDieMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int KillerObjectId { get; set; }
    }

    public sealed class DropItemNetMsg
    {
        public int DropperObjectId { get; set; }
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int ItemId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public bool Stackable { get; set; }
        public int Count { get; set; } = 1;
    }

    public sealed class PickupRequestNetMsg
    {
        public int GroundObjectId { get; set; }
    }

    /// <summary>0 add / 1 modify / 2 remove — same as V2 InventoryChange.</summary>
    public sealed class InventoryUpdateEntryNet
    {
        public int Change { get; set; }
        public int ObjectId { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int EnchantLevel { get; set; }
        public int EquippedSlot { get; set; } = -1;
    }

    public sealed class InventoryUpdateNetMsg
    {
        public InventoryUpdateEntryNet[] Changes { get; set; } = System.Array.Empty<InventoryUpdateEntryNet>();
    }

    public sealed class DropItemRequestNetMsg
    {
        public int ObjectId { get; set; }
        public int Count { get; set; } = 1;
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    public sealed class UseItemNetMsg
    {
        public int ObjectId { get; set; }
    }

    public sealed class UnequipItemNetMsg
    {
        public int PaperdollSlot { get; set; }
    }

    public sealed class SayNetMsg
    {
        public string Text { get; set; } = "";
        public int ChatType { get; set; }
    }

    public sealed class CreatureSayNetMsg
    {
        public int ObjectId { get; set; }
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
        public int ChatType { get; set; }
    }

    public sealed class SystemMessageNetMsg
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
        public string[] Params { get; set; } = System.Array.Empty<string>();
    }

    public sealed class UseSkillNetMsg
    {
        public int SkillId { get; set; }
        public int TargetObjectId { get; set; }
        public bool CtrlPressed { get; set; }
    }

    public sealed class MagicSkillUseNetMsg
    {
        public int CasterObjectId { get; set; }
        public int TargetObjectId { get; set; }
        public int SkillId { get; set; }
        public int SkillLevel { get; set; }
        public int HitTimeMs { get; set; }
        public int ReuseDelayMs { get; set; }
    }

    public sealed class ReviveRequestNetMsg
    {
        public int Action { get; set; }
    }

    public sealed class ReviveNetMsg
    {
        public int ObjectId { get; set; }
    }

    public sealed class TeleportToLocationNetMsg
    {
        public int ObjectId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
    }

    public sealed class AbnormalEffectNet
    {
        public int Id { get; set; }
        public int Value { get; set; }
        public int DurationSeconds { get; set; }
    }

    public sealed class AbnormalStatusUpdateNetMsg
    {
        public int ObjectId { get; set; }
        public AbnormalEffectNet[] Effects { get; set; } = System.Array.Empty<AbnormalEffectNet>();
    }

    public sealed class NpcTalkNetMsg
    {
        public int NpcObjectId { get; set; }
    }

    public sealed class BypassRequestNetMsg
    {
        public int NpcObjectId { get; set; }
        public string Command { get; set; } = "";
    }

    public sealed class NpcHtmlNetMsg
    {
        public int NpcObjectId { get; set; }
        public int NpcId { get; set; }
        public string Html { get; set; } = "";
        public int ItemId { get; set; }
    }

    public sealed class ShopCountNet
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
    }

    public sealed class ShopObjectCountNet
    {
        public int ObjectId { get; set; }
        public int Count { get; set; }
    }

    public sealed class RequestBuyItemNetMsg
    {
        public int NpcObjectId { get; set; }
        public int ListId { get; set; }
        public ShopCountNet[] Items { get; set; } = System.Array.Empty<ShopCountNet>();
    }

    public sealed class RequestSellItemNetMsg
    {
        public int NpcObjectId { get; set; }
        public ShopObjectCountNet[] Items { get; set; } = System.Array.Empty<ShopObjectCountNet>();
    }

    public sealed class RequestMultiSellChooseNetMsg
    {
        public int NpcObjectId { get; set; }
        public int ListId { get; set; }
        public int EntryId { get; set; }
        public int Amount { get; set; }
    }

    public sealed class BuyListItemNet
    {
        public int ItemId { get; set; }
        public int Price { get; set; }
        public int Count { get; set; } = -1;
    }

    public sealed class BuyListNetMsg
    {
        public int NpcObjectId { get; set; }
        public int ListId { get; set; }
        public int BaseTaxPercent { get; set; }
        public int CastleTaxPercent { get; set; }
        public BuyListItemNet[] Items { get; set; } = System.Array.Empty<BuyListItemNet>();
    }

    public sealed class SellListItemNet
    {
        public int ObjectId { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int Price { get; set; }
        public int EnchantLevel { get; set; }
    }

    public sealed class SellListNetMsg
    {
        public int NpcObjectId { get; set; }
        public SellListItemNet[] Items { get; set; } = System.Array.Empty<SellListItemNet>();
    }

    public sealed class MultiSellPartNet
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int EnchantLevel { get; set; }
    }

    public sealed class MultiSellEntryNet
    {
        public int EntryId { get; set; }
        public MultiSellPartNet[] Ingredients { get; set; } = System.Array.Empty<MultiSellPartNet>();
        public MultiSellPartNet[] Products { get; set; } = System.Array.Empty<MultiSellPartNet>();
    }

    public sealed class MultiSellListNetMsg
    {
        public int NpcObjectId { get; set; }
        public int ListId { get; set; }
        public bool MaintainEnchantment { get; set; }
        public bool InventoryOnly { get; set; }
        public int CastleTaxPercent { get; set; }
        public MultiSellEntryNet[] Entries { get; set; } = System.Array.Empty<MultiSellEntryNet>();
    }

    public sealed class ShopBalanceNetMsg
    {
        public int Adena { get; set; }
        public int Weight { get; set; }
        public int MaxWeight { get; set; }
        public int Slots { get; set; }
        public int MaxSlots { get; set; }
        public int BaseTaxPercent { get; set; }
        public int CastleTaxPercent { get; set; }
    }

    public sealed class WarehouseDepositListNetMsg
    {
        public int NpcObjectId { get; set; }
        public int FeePerLine { get; set; }
        public int SlotsUsed { get; set; }
        public int SlotsMax { get; set; }
        public int Adena { get; set; }
        public ItemListEntryNet[] Items { get; set; } = System.Array.Empty<ItemListEntryNet>();
    }

    public sealed class WarehouseWithdrawListNetMsg
    {
        public int NpcObjectId { get; set; }
        public int SlotsUsed { get; set; }
        public int SlotsMax { get; set; }
        public int InventorySlotsUsed { get; set; }
        public int InventorySlotsMax { get; set; }
        public int CurrentWeight { get; set; }
        public int MaxWeight { get; set; }
        public ItemListEntryNet[] Items { get; set; } = System.Array.Empty<ItemListEntryNet>();
    }

    public sealed class WarehouseDepositRequestNetMsg
    {
        public int NpcObjectId { get; set; }
        public ShopObjectCountNet[] Lines { get; set; } = System.Array.Empty<ShopObjectCountNet>();
    }

    public sealed class WarehouseWithdrawRequestNetMsg
    {
        public int NpcObjectId { get; set; }
        public ShopObjectCountNet[] Lines { get; set; } = System.Array.Empty<ShopObjectCountNet>();
    }

    public sealed class QuestJournalEntryNet
    {
        public int QuestId { get; set; }
        public string Name { get; set; } = "";
        public int State { get; set; }
        public int Cond { get; set; }
        public int ProgressFlags { get; set; }
        public int JournalWord { get; set; }
        public bool Repeatable { get; set; }
    }

    public sealed class QuestListNetMsg
    {
        public QuestJournalEntryNet[] Active { get; set; } = System.Array.Empty<QuestJournalEntryNet>();
        public byte[] CompletedMask { get; set; } = System.Array.Empty<byte>();
        public int[] CompletedQuestIds { get; set; } = System.Array.Empty<int>();
        public int MaxActive { get; set; }
    }

    public sealed class RequestQuestAbortNetMsg
    {
        public int QuestId { get; set; }
    }

    public sealed class PartyInviteRequestNetMsg
    {
        public int TargetObjectId { get; set; }
        public int ItemDistribution { get; set; }
    }

    public sealed class PartyAnswerJoinRequestNetMsg
    {
        public int RequestorObjectId { get; set; }
        public int Response { get; set; }
    }

    public sealed class PartyKickRequestNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    public sealed class PartyAskJoinNetMsg
    {
        public int RequestorObjectId { get; set; }
        public string RequestorName { get; set; } = "";
        public int ItemDistribution { get; set; }
    }

    public sealed class PartyJoinResultNetMsg
    {
        public int Response { get; set; }
    }

    public sealed class PartyMemberEntryNet
    {
        public int ObjectId { get; set; }
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Level { get; set; }
        public int CurHp { get; set; }
        public int MaxHp { get; set; }
        public int CurMp { get; set; }
        public int MaxMp { get; set; }
    }

    public sealed class PartyMemberListNetMsg
    {
        public int LeaderObjectId { get; set; }
        public int PartyId { get; set; }
        public PartyMemberEntryNet[] Members { get; set; } = System.Array.Empty<PartyMemberEntryNet>();
    }

    public sealed class TradeRequestNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    public sealed class TradeAnswerRequestNetMsg
    {
        public int RequestorObjectId { get; set; }
        public int Response { get; set; }
    }

    public sealed class TradeAddItemRequestNetMsg
    {
        public int InventoryObjectId { get; set; }
        public long Count { get; set; } = 1;
    }

    public sealed class TradeDoneRequestNetMsg
    {
        public int Response { get; set; }
    }

    public sealed class TradeAskNetMsg
    {
        public int RequestorObjectId { get; set; }
        public string RequestorName { get; set; } = "";
    }

    public sealed class TradeRequestResultNetMsg
    {
        public int Response { get; set; }
    }

    public sealed class TradeStartNetMsg
    {
        public int PartnerObjectId { get; set; }
        public string PartnerName { get; set; } = "";
    }

    public sealed class TradeOfferUpdateNetMsg
    {
        public int IsOwn { get; set; }
        public int InventoryObjectId { get; set; }
        public int ItemId { get; set; }
        public long Count { get; set; }
        public int EnchantLevel { get; set; }
    }

    public sealed class TradeConfirmStateNetMsg
    {
        public int SelfConfirmed { get; set; }
        public int PartnerConfirmed { get; set; }
    }

    public sealed class TradeFinishedNetMsg
    {
        public int Response { get; set; }
    }
}
