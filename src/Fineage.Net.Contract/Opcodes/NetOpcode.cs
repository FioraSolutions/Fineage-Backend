namespace Fineage.Net.Contract
{
    /// <summary>Namespaced opcodes. High byte ≈ domain; low byte ≈ message.</summary>
    public static class NetOpcode
    {
        // Session 0x01xx
        public const ushort Hello = 0x0101;
        public const ushort HelloAck = 0x0102;
        public const ushort Reject = 0x01FF;

        // Lobby 0x02xx (login port)
        public const ushort Auth = 0x0201;
        public const ushort AuthResult = 0x0202;
        public const ushort ListCharacters = 0x0203;
        public const ushort CharacterRoster = 0x0204;
        public const ushort CreateCharacter = 0x0205;
        public const ushort CreateResult = 0x0206;
        public const ushort SelectCharacter = 0x0207;
        public const ushort Handoff = 0x0208;

        // World 0x03xx (zone port)
        public const ushort ZoneAuth = 0x0301;
        public const ushort ZoneAuthResult = 0x0302;
        public const ushort EnterWorld = 0x0303;
        public const ushort EntitySpawn = 0x0304;
        public const ushort PlayerStats = 0x0305;
        public const ushort MoveInput = 0x0306;
        public const ushort StopMove = 0x0307;
        public const ushort MoveState = 0x0308;
        public const ushort PositionCorrection = 0x0309;
        public const ushort ItemList = 0x030A;
        public const ushort SkillList = 0x030B;
        public const ushort EntityDespawn = 0x030C;
        public const ushort SelectTarget = 0x030D;
        public const ushort AttackRequest = 0x030E;
        public const ushort TargetSelected = 0x030F;
        public const ushort AttackState = 0x0310;
        public const ushort StatusUpdate = 0x0311;
        public const ushort EntityDie = 0x0312;
        public const ushort DropItem = 0x0313;
        public const ushort PickupRequest = 0x0314;
        public const ushort InventoryUpdate = 0x0315;
        public const ushort DropItemRequest = 0x0316;
        public const ushort UseItem = 0x0317;
        public const ushort UnequipItem = 0x0318;
        public const ushort Say = 0x0319;
        public const ushort CreatureSay = 0x031A;
        public const ushort SystemMessage = 0x031B;
        public const ushort UseSkill = 0x031C;
        public const ushort MagicSkillUse = 0x031D;
        public const ushort ReviveRequest = 0x031E;
        public const ushort Revive = 0x031F;
        public const ushort TeleportToLocation = 0x0320;
        public const ushort AbnormalStatusUpdate = 0x0321;
        public const ushort NpcTalk = 0x0322;
        public const ushort BypassRequest = 0x0323;
        public const ushort NpcHtml = 0x0324;
        public const ushort BuyList = 0x0325;
        public const ushort SellList = 0x0326;
        public const ushort MultiSellList = 0x0327;
        public const ushort ShopBalance = 0x0328;
        public const ushort RequestBuyItem = 0x0329;
        public const ushort RequestSellItem = 0x032A;
        public const ushort RequestMultiSellChoose = 0x032B;
        public const ushort WarehouseDepositList = 0x032C;
        public const ushort WarehouseWithdrawList = 0x032D;
        public const ushort WarehouseDepositRequest = 0x032E;
        public const ushort WarehouseWithdrawRequest = 0x032F;
        public const ushort RequestQuestList = 0x0330;
        public const ushort RequestQuestAbort = 0x0331;
        public const ushort QuestList = 0x0332;
        public const ushort PartyInviteRequest = 0x0333;
        public const ushort PartyAnswerJoinRequest = 0x0334;
        public const ushort PartyLeaveRequest = 0x0335;
        public const ushort PartyKickRequest = 0x0336;
        public const ushort PartyAskJoin = 0x0337;
        public const ushort PartyJoinResult = 0x0338;
        public const ushort PartyMemberList = 0x0339;
        public const ushort TradeRequest = 0x033A;
        public const ushort TradeAnswerRequest = 0x033B;
        public const ushort TradeAddItemRequest = 0x033C;
        public const ushort TradeDoneRequest = 0x033D;
        public const ushort TradeAsk = 0x033E;
        public const ushort TradeRequestResult = 0x033F;
        public const ushort TradeStart = 0x0340;
        public const ushort TradeOfferUpdate = 0x0341;
        public const ushort TradeConfirmState = 0x0342;
        public const ushort TradeFinished = 0x0343;
    }
}
