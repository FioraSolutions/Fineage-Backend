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
        /// <summary>Player only (Mobius CharInfo): equipped item ids per paperdoll slot; empty for NPCs / old peers.</summary>
        public int[] Paperdoll { get; set; } = System.Array.Empty<int>();
        /// <summary>Mobius isAlikeDead: the entity is a corpse when it enters view (late observer sees it lying, not standing).</summary>
        public bool Dead { get; set; }
        /// <summary>Mobius level: drives NPC target colour (InterludeStatus.TargetColor) and level in the nameplate.</summary>
        public int Level { get; set; }
        /// <summary>Max HP so a client can size the HP bar without waiting for the first StatusUpdate.</summary>
        public int MaxHp { get; set; }
        /// <summary>Physical attack speed (Mobius P.AtkSpd); drives client attack anim timing. Optional wire tail.</summary>
        public int PAtkSpd { get; set; }
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

    public sealed class ValidatePositionMsg
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
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

    public sealed class ActionUseNetMsg
    {
        public int ActionId { get; set; }
        public bool CtrlPressed { get; set; }
        public bool ShiftPressed { get; set; }
    }

    /// <summary>Payload of <c>AutoAttackStart</c> / <c>AutoAttackStop</c>.</summary>
    public sealed class AttackStanceNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
    }

    /// <summary>Payload of <c>RequestAutoSoulShot</c> and <c>AutoSoulShot</c>.</summary>
    public sealed class AutoSoulShotNetMsg
    {
        public int ItemId { get; set; }
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// One Mobius shortcut. <see cref="Slot"/> is the client world slot (slot + page * 12).
    /// <see cref="Type"/>: 1 item (id = item object id), 2 skill (id = skill id), 3 action, 4 macro, 5 recipe.
    /// Payload of <c>RequestShortCutReg</c> and <c>ShortCutRegister</c>; rows of <c>ShortCutInit</c>.
    /// </summary>
    public sealed class ShortCutNetMsg
    {
        public int Type { get; set; }
        public int Slot { get; set; }
        public int Id { get; set; }
        public int Level { get; set; }
    }

    /// <summary>Payload of <c>ShortCutInit</c>: every saved slot for the character.</summary>
    public sealed class ShortCutInitNetMsg
    {
        public ShortCutNetMsg[] Shortcuts { get; set; } = System.Array.Empty<ShortCutNetMsg>();
    }

    /// <summary>Payload of <c>RequestShortCutDel</c> and <c>ShortCutDelete</c> (world slot).</summary>
    public sealed class ShortCutDelNetMsg
    {
        public int Slot { get; set; }
    }

    public sealed class ChangeMoveTypeNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public bool Running { get; set; } = true;
        /// <summary>Effective L2 run speed now (swim speed when <see cref="InWater"/>).</summary>
        public float RunSpeed { get; set; }
        /// <summary>Effective L2 walk speed now (swim speed when <see cref="InWater"/>).</summary>
        public float WalkSpeed { get; set; }
        public bool InWater { get; set; }
    }

    /// <summary>
    /// Mobius <c>EtcStatusUpdate</c> (<c>EtcStatusUpdate.java:32-45</c>): the status-bar oddities. The client needs the
    /// weight penalty level to draw the overload icon and HP-bar tint, and the grade-penalty flag for its own icon.
    /// Sent to the owner only.
    /// </summary>
    public sealed class EtcStatusUpdateNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        /// <summary>1–7 increase-force charges. Always 0 until the force system exists.</summary>
        public int Charges { get; set; }
        /// <summary>0 none, 1 ≥50%, 2 ≥66.6%, 3 ≥80%, 4 ≥100% of the load limit (Mobius skill 4270 level).</summary>
        public int WeightPenalty { get; set; }
        /// <summary>Mobius writes 1 when chat is refused, banned, or in silence mode.</summary>
        public bool ChatBlocked { get; set; }
        /// <summary>Inside a Mobius DANGER_AREA zone.</summary>
        public bool DangerArea { get; set; }
        /// <summary>
        /// Mobius writes 1 when either expertise penalty is above 0 (the packet field is a flag, not the 1–4 level).
        /// </summary>
        public bool GradePenalty { get; set; }
        /// <summary>Charm of Courage: allows resurrection on the same spot during a siege.</summary>
        public bool CharmOfCourage { get; set; }
        /// <summary>1–15 death-penalty buff level.</summary>
        public int DeathPenalty { get; set; }
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
        /// <summary>Body token ("rhand", "lrhand", "chest", "none", ...). Lets the client resolve the paperdoll slot without guessing.</summary>
        public string BodySlot { get; set; } = "";
        /// <summary>Item kind token: "weapon", "armor", "etc", "jewel".</summary>
        public string Kind { get; set; } = "";
        /// <summary>Client icon key from the item template (icon.weapon_..._i00).</summary>
        public string Icon { get; set; } = "";
        /// <summary>Weapon token ("sword", "dagger", "blunt", ...), empty when not a weapon.</summary>
        public string WeaponType { get; set; } = "";
        /// <summary>Armor token ("light", "heavy", "magic"), empty when not armor.</summary>
        public string ArmorType { get; set; } = "";
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

    /// <summary>
    /// Mobius <c>AcquireSkillList</c> row: one trainer offer (skill id, next level, max level, SP price, requirement mask).
    /// </summary>
    public sealed class AcquireSkillEntryNet
    {
        public int SkillId { get; set; }
        public int NextLevel { get; set; }
        public int MaxLevel { get; set; }
        public int SpCost { get; set; }
        /// <summary>Mobius <c>requirements</c> bitmask (1 = needs the previous level, 2 = needs a book/item).</summary>
        public int Requirements { get; set; }
    }

    /// <summary>Zone → client: Mobius <c>AcquireSkillList</c> (the trainer's learnable rows).</summary>
    public sealed class AcquireSkillListNetMsg
    {
        /// <summary>Mobius <c>AcquireSkillType</c> ordinal: 0 = class, 1 = fishing, 2 = pledge.</summary>
        public int SkillType { get; set; }
        public AcquireSkillEntryNet[] Skills { get; set; } = System.Array.Empty<AcquireSkillEntryNet>();
    }

    /// <summary>Mobius <c>AcquireSkillInfo</c> requirement row (item or prerequisite).</summary>
    public sealed class AcquireSkillRequirementNet
    {
        public int Type { get; set; }
        public int ItemId { get; set; }
        public long Count { get; set; }
        public int Unk { get; set; }
    }

    /// <summary>Zone → client: Mobius <c>AcquireSkillInfo</c> (the confirmation page for one offer).</summary>
    public sealed class AcquireSkillInfoNetMsg
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
        public int SpCost { get; set; }
        /// <summary>Mobius <c>AcquireSkillType</c> ordinal.</summary>
        public int SkillType { get; set; }
        public AcquireSkillRequirementNet[] Requirements { get; set; } = System.Array.Empty<AcquireSkillRequirementNet>();
    }

    /// <summary>Client → zone: a click on the scroll in the bag (Mobius <c>RequestEnchantItem</c>).</summary>
    public sealed class EnchantRequestNetMsg
    {
        public int ScrollObjectId { get; set; }
    }

    /// <summary>Client → zone: the item the player placed in the enchant slot.</summary>
    public sealed class EnchantConfirmNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    /// <summary>
    /// Zone → client: the finished enchant roll, mirroring <c>EnchantResultMsg</c>.
    /// Result ordinals follow <c>EnchantProtocol</c>: 0 cancel, 1 success, 2 keep, 3 reset, 4 crystallize.
    /// </summary>
    public sealed class EnchantResultNetMsg
    {
        public int Result { get; set; }
        /// <summary>Enchant level after the roll. 0 when the item was crystallized or reset.</summary>
        public int EnchantLevel { get; set; }
        /// <summary>Target object id. 0 on cancel.</summary>
        public int ObjectId { get; set; }
        /// <summary>Crystal item id gained from a normal failure. 0 when none are gained.</summary>
        public int CrystalItemId { get; set; }
        public int CrystalCount { get; set; }
    }

    /// <summary>Client → zone: Mobius <c>RequestCreatePledge</c> (the clan name).</summary>
    public sealed class ClanCreateRequestNetMsg
    {
        public string Name { get; set; } = "";
    }

    /// <summary>Client → zone: Mobius <c>RequestJoinPledge</c> (the leader's object id).</summary>
    public sealed class ClanInviteRequestNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    /// <summary>Client → zone: Mobius <c>RequestAnswerJoinPledge</c>. Response 0 declines.</summary>
    public sealed class ClanAnswerNetMsg
    {
        public int RequestorObjectId { get; set; }
        public int Response { get; set; }
    }

    /// <summary>Client → zone: Mobius <c>RequestWithdrawalPledge</c>. Empty body.</summary>
    public sealed class ClanLeaveRequestNetMsg
    {
    }

    /// <summary>Client → zone: Mobius <c>RequestOustPledgeMember</c> (the member's object id).</summary>
    public sealed class ClanKickRequestNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    /// <summary>
    /// Zone → client: Mobius <c>PledgeShowMemberListAll</c> / <c>PledgeStatusChanged</c>. Clan id 0 clears the pledge.
    /// </summary>
    public sealed class PledgeStatusNetMsg
    {
        public int ClanId { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public int LeaderObjectId { get; set; }
        public int CrestId { get; set; }
        public int MemberCount { get; set; }
        public long DissolvingExpiryUnixMs { get; set; }
        public long CharPenaltyExpiryUnixMs { get; set; }
    }

    /// <summary>Mobius <c>PledgeShowMemberListAll</c> row.</summary>
    public sealed class PledgeMemberNet
    {
        public int ObjectId { get; set; }
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Level { get; set; }
        public bool Online { get; set; }
        public int PledgeType { get; set; }
    }

    /// <summary>Zone → client: Mobius <c>PledgeShowMemberListAll</c> (the roster after any membership change).</summary>
    public sealed class PledgeMemberListNetMsg
    {
        public int ClanId { get; set; }
        public int LeaderObjectId { get; set; }
        public PledgeMemberNet[] Members { get; set; } = System.Array.Empty<PledgeMemberNet>();
    }

    /// <summary>Zone → client: Mobius <c>AskJoinPledge</c> (an invite arrived).</summary>
    public sealed class ClanAskJoinNetMsg
    {
        public int RequestorObjectId { get; set; }
        public string RequestorName { get; set; } = "";
        public int ClanId { get; set; }
        public string ClanName { get; set; } = "";
        public int PledgeType { get; set; }
    }

    /// <summary>Zone → client: Mobius <c>JoinPledge</c>. Response 0 is a decline.</summary>
    public sealed class ClanJoinResultNetMsg
    {
        public int Response { get; set; }
    }

    /// <summary>
    /// Mobius <c>ChangeWaitType</c>: sit/stand. Interlude also carries the actor position, which the client uses to
    /// settle the pawn before it plays the sitting animation.
    /// </summary>
    public sealed class ChangeWaitTypeNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        /// <summary>0 = sitting, 1 = standing (Interlude <c>ChangeWaitType.WT_SITTING/WT_STANDING</c>).</summary>
        public int WaitType { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    /// <summary>Mobius <c>SocialAction</c> (gesture ids 2–14, 15 = level up).</summary>
    public sealed class SocialActionNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int ActionId { get; set; }
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

    /// <summary>
    /// Zone → client: the player's selection was dropped (Mobius <c>TargetUnselected</c>), so the target
    /// window and nameplate close. Carries the object id that was unselected, 0 when there was none.
    /// </summary>
    public sealed class TargetUnselectedNetMsg
    {
        public int TargetObjectId { get; set; }
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
        /// <summary>Same tokens as <see cref="ItemListEntryNet"/> — client must not re-guess kind/slot from tables.</summary>
        public string BodySlot { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Icon { get; set; } = "";
        public string WeaponType { get; set; } = "";
        public string ArmorType { get; set; } = "";
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

    /// <summary>Client → zone: Mobius <c>RequestDestroyItem</c>. No coordinates: the slot is just deleted.</summary>
    public sealed class DestroyItemRequestMsg
    {
        public int ObjectId { get; set; }
        public int Count { get; set; } = 1;
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
        /// <summary>Restart to village. Matches Backend <c>Fineage.Protocol.DeathProtocol.ReviveToVillage</c>.</summary>
        public const int ReviveToVillage = 0;

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

    public sealed class PrivateStoreSellRowNet
    {
        public int InventoryObjectId { get; set; }
        public int ItemId { get; set; }
        public long Count { get; set; } = 1;
        public long Price { get; set; }
        public int EnchantLevel { get; set; }
    }

    public sealed class SetPrivateStoreSellNetMsg
    {
        public string Message { get; set; } = "";
        public int Packaged { get; set; }
        public PrivateStoreSellRowNet[] Items { get; set; } = System.Array.Empty<PrivateStoreSellRowNet>();
    }

    public sealed class PrivateStoreListRequestNetMsg
    {
        public int StoreObjectId { get; set; }
    }

    public sealed class PrivateStoreBuyNetMsg
    {
        public int StoreObjectId { get; set; }
        public PrivateStoreSellRowNet[] Items { get; set; } = System.Array.Empty<PrivateStoreSellRowNet>();
    }

    /// <summary>Broadcast store type + title (Mobius PrivateStoreMsgSell / UserInfo store bit).</summary>
    public sealed class PrivateStoreInfoNetMsg
    {
        public int ObjectId { get; set; }
        public int StoreType { get; set; }
        public string Message { get; set; } = "";
    }

    public sealed class PrivateStoreListSellNetMsg
    {
        public int StoreObjectId { get; set; }
        public string StoreName { get; set; } = "";
        public string Message { get; set; } = "";
        public int Packaged { get; set; }
        public long BuyerAdena { get; set; }
        public PrivateStoreSellRowNet[] Items { get; set; } = System.Array.Empty<PrivateStoreSellRowNet>();
    }

    public sealed class PrivateStoreBuyRowNet
    {
        public int ItemId { get; set; }
        public long Count { get; set; } = 1;
        public long Price { get; set; }
    }

    public sealed class SetPrivateStoreBuyNetMsg
    {
        public string Message { get; set; } = "";
        public PrivateStoreBuyRowNet[] Items { get; set; } = System.Array.Empty<PrivateStoreBuyRowNet>();
    }

    public sealed class PrivateStoreSellToNetMsg
    {
        public int StoreObjectId { get; set; }
        public PrivateStoreSellRowNet[] Items { get; set; } = System.Array.Empty<PrivateStoreSellRowNet>();
    }

    public sealed class PrivateStoreListBuyNetMsg
    {
        public int StoreObjectId { get; set; }
        public string StoreName { get; set; } = "";
        public string Message { get; set; } = "";
        public long StoreAdena { get; set; }
        public PrivateStoreBuyRowNet[] Items { get; set; } = System.Array.Empty<PrivateStoreBuyRowNet>();
    }

    public sealed class RecipeBookOpenNetMsg
    {
        public int Dwarven { get; set; }
    }

    public sealed class RecipeMakeSelfNetMsg
    {
        public int RecipeId { get; set; }
    }

    public sealed class RecipeBookListNetMsg
    {
        public int Dwarven { get; set; }
        public int[] RecipeIds { get; set; } = System.Array.Empty<int>();
    }

    /// <summary>0 = success, 1 = fail (materials lost), 2 = cannot craft (no consume).</summary>
    public sealed class RecipeItemMakeInfoNetMsg
    {
        public int RecipeId { get; set; }
        public int Result { get; set; }
        public int CurrentMp { get; set; }
        public int MaxMp { get; set; }
    }

    public sealed class RecipeShopRowNet
    {
        public int RecipeId { get; set; }
        public long Price { get; set; }
    }

    public sealed class SetRecipeShopNetMsg
    {
        public string Message { get; set; } = "";
        public RecipeShopRowNet[] Items { get; set; } = System.Array.Empty<RecipeShopRowNet>();
    }

    public sealed class RecipeShopMakeInfoNetMsg
    {
        public int ManufacturerObjectId { get; set; }
        public int RecipeId { get; set; }
    }

    public sealed class RecipeShopMakeItemNetMsg
    {
        public int ManufacturerObjectId { get; set; }
        public int RecipeId { get; set; }
    }

    public sealed class RecipeShopSellListNetMsg
    {
        public int ManufacturerObjectId { get; set; }
        public int CurrentMp { get; set; }
        public int MaxMp { get; set; }
        public long BuyerAdena { get; set; }
        public RecipeShopRowNet[] Items { get; set; } = System.Array.Empty<RecipeShopRowNet>();
    }

    public sealed class RecipeShopItemInfoNetMsg
    {
        public int ManufacturerObjectId { get; set; }
        public int RecipeId { get; set; }
        public int CurrentMp { get; set; }
        public int MaxMp { get; set; }
        /// <summary>0 = ok / success, 1 = fail (fee+mats consumed).</summary>
        public int Result { get; set; }
    }

    public sealed class FriendInviteNetMsg
    {
        public string Name { get; set; } = "";
    }

    public sealed class FriendAnswerNetMsg
    {
        public int Response { get; set; }
    }

    public sealed class FriendDeleteNetMsg
    {
        public string Name { get; set; } = "";
    }

    public sealed class BlockRequestNetMsg
    {
        public int Type { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class FriendInviteAskNetMsg
    {
        public string RequestorName { get; set; } = "";
    }

    public sealed class FriendRowNet
    {
        public int ObjectId { get; set; }
        public string Name { get; set; } = "";
        public int Online { get; set; }
    }

    public sealed class FriendListNetMsg
    {
        public FriendRowNet[] Friends { get; set; } = System.Array.Empty<FriendRowNet>();
    }

    /// <summary>Added=1 / removed=0; Online reflects presence when added or on login notify.</summary>
    public sealed class FriendUpdateNetMsg
    {
        public int Added { get; set; }
        public int ObjectId { get; set; }
        public string Name { get; set; } = "";
        public int Online { get; set; }
    }

    public sealed class BlockListNetMsg
    {
        public int BlockAll { get; set; }
        public FriendRowNet[] Blocked { get; set; } = System.Array.Empty<FriendRowNet>();
    }

    /// <summary>One paperdoll slot (Mobius UserInfo / CharInfo order, 17 slots, right hand = 7). Zeros when empty.</summary>
    public sealed class PaperdollSlotNet
    {
        public int ObjectId { get; set; }
        public int ItemId { get; set; }
        public int EnchantLevel { get; set; }
    }

    /// <summary>
    /// Mobius UserInfo for the owner: full character sheet (vitals, base stats, combat stats from the live
    /// server stat pipeline, speeds and paperdoll). Sent on enter and whenever gear/buffs/level change the sheet.
    /// </summary>
    public sealed class UserInfoNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Race { get; set; }
        public int Sex { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public float RunSpeed { get; set; }
        public float WalkSpeed { get; set; }
        public bool Running { get; set; } = true;
        public int Level { get; set; } = 1;
        public long Exp { get; set; }
        public int Sp { get; set; }
        public int CurHp { get; set; }
        public int MaxHp { get; set; }
        public int CurMp { get; set; }
        public int MaxMp { get; set; }
        public int CurCp { get; set; }
        public int MaxCp { get; set; }
        public int Str { get; set; }
        public int Dex { get; set; }
        public int Con { get; set; }
        public int Int { get; set; }
        public int Wit { get; set; }
        public int Men { get; set; }
        public int PAtk { get; set; }
        public int MAtk { get; set; }
        public int PDef { get; set; }
        public int MDef { get; set; }
        public int PAtkSpd { get; set; }
        public int MAtkSpd { get; set; }
        public int Accuracy { get; set; }
        public int Evasion { get; set; }
        public int CritRate { get; set; }
        public int WeaponGradePenalty { get; set; }
        public int ArmorGradePenalty { get; set; }
        public int ClanId { get; set; }
        public PaperdollSlotNet[] Paperdoll { get; set; } = System.Array.Empty<PaperdollSlotNet>();
    }
}
