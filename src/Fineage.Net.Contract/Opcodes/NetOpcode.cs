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
        public const ushort DeleteCharacter = 0x0209;
        public const ushort DeleteResult = 0x020A;
        public const ushort RestoreCharacter = 0x020B;

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
        // 0x0344..0x034F reserved (clan WIP)
        /// <summary>Client → zone: Mobius ValidatePosition heartbeat (L2 XYZ + heading).</summary>
        public const ushort ValidatePosition = 0x0350;
        /// <summary>Client → zone: Mobius RequestActionUse (0 = sit/stand, 1 = walk/run, 12–14 = social).</summary>
        public const ushort RequestActionUse = 0x0351;
        /// <summary>Zone → client: Mobius ChangeMoveType + effective run/walk speed (swim speed while in water).</summary>
        public const ushort ChangeMoveType = 0x0352;
        /// <summary>Zone → client: Mobius AutoAttackStart (entity enters attack stance).</summary>
        public const ushort AutoAttackStart = 0x0353;
        /// <summary>Zone → client: Mobius AutoAttackStop (stance ended: 15 s without combat, or mob lost its target).</summary>
        public const ushort AutoAttackStop = 0x0354;
        /// <summary>Client → zone: Mobius RequestAutoSoulShot (shot item id, enable/disable).</summary>
        public const ushort RequestAutoSoulShot = 0x0355;
        /// <summary>Zone → client: Mobius ExAutoSoulShot (shot item id, auto now on/off).</summary>
        public const ushort AutoSoulShot = 0x0356;
        /// <summary>Zone → client: Mobius UserInfo (owner sheet: vitals, base + combat stats, speeds, paperdoll).</summary>
        public const ushort UserInfo = 0x0357;
        /// <summary>Client → zone: Mobius RequestShortCutReg (type, world slot = slot + page*12, id, level).</summary>
        public const ushort RequestShortCutReg = 0x0358;
        /// <summary>Client → zone: Mobius RequestShortCutDel (world slot).</summary>
        public const ushort RequestShortCutDel = 0x0359;
        /// <summary>Zone → client: Mobius ShortCutInit (full hotbar on enter world, after ItemList / SkillList).</summary>
        public const ushort ShortCutInit = 0x035A;
        /// <summary>Zone → client: Mobius ShortCutRegister (one slot set; reply to RequestShortCutReg).</summary>
        public const ushort ShortCutRegister = 0x035B;
        /// <summary>Zone → client: slot cleared (reply to RequestShortCutDel, rejected reg, or item gone).</summary>
        public const ushort ShortCutDelete = 0x035C;
        /// <summary>Zone → client: Mobius EtcStatusUpdate (weight/grade penalty flags and levels).</summary>
        public const ushort EtcStatusUpdate = 0x035D;
        /// <summary>Zone → client: Mobius ChangeWaitType (sit/stand + the pawn position).</summary>
        public const ushort ChangeWaitType = 0x035E;
        /// <summary>Zone → client: Mobius SocialAction (gesture id; 15 = level up).</summary>
        public const ushort SocialAction = 0x035F;
        /// <summary>Zone → client: Mobius AcquireSkillList (the trainer's learnable rows).</summary>
        public const ushort AcquireSkillList = 0x0360;
        /// <summary>Zone → client: Mobius AcquireSkillInfo (confirmation page for one trainer offer).</summary>
        public const ushort AcquireSkillInfo = 0x0361;
        /// <summary>Client → zone: Mobius RequestAcquireSkillInfo (skill id, level, skill type).</summary>
        public const ushort RequestAcquireSkillInfo = 0x0362;
        /// <summary>Client → zone: Mobius RequestAcquireSkill (skill id, level, skill type).</summary>
        public const ushort RequestAcquireSkill = 0x0363;
        /// <summary>Client → zone: Mobius RequestEnchantItem (scroll object id) — opens the enchant window.</summary>
        public const ushort EnchantRequest = 0x0364;
        /// <summary>Client → zone: enchant confirm (target object id).</summary>
        public const ushort EnchantConfirm = 0x0365;
        /// <summary>Client → zone: enchant cancel. Empty body.</summary>
        public const ushort EnchantCancel = 0x0366;
        /// <summary>Zone → client: the finished enchant roll (result, level, object id, crystals).</summary>
        public const ushort EnchantResult = 0x0367;
        /// <summary>Client → zone: Mobius RequestCreatePledge (clan name).</summary>
        public const ushort ClanCreateRequest = 0x0368;
        /// <summary>Client → zone: Mobius RequestJoinPledge (leader object id).</summary>
        public const ushort ClanInviteRequest = 0x0369;
        /// <summary>Client → zone: Mobius RequestAnswerJoinPledge (requestor object id, response).</summary>
        public const ushort ClanAnswerJoinRequest = 0x036A;
        /// <summary>Client → zone: Mobius RequestWithdrawalPledge. Empty body.</summary>
        public const ushort ClanLeaveRequest = 0x036B;
        /// <summary>Client → zone: Mobius RequestOustPledgeMember (member object id).</summary>
        public const ushort ClanKickRequest = 0x036C;
        /// <summary>Client → zone: Mobius RequestDissolvePledge. Empty body.</summary>
        public const ushort ClanDissolveRequest = 0x036D;
        /// <summary>Client → zone: Mobius RequestCancelDissolvePledge. Empty body.</summary>
        public const ushort ClanCancelDissolveRequest = 0x036E;
        /// <summary>Zone → client: Mobius PledgeStatusChanged / PledgeShowInfoUpdate.</summary>
        public const ushort PledgeStatus = 0x036F;
        /// <summary>Zone → client: Mobius PledgeShowMemberListAll (the roster after a membership change).</summary>
        public const ushort PledgeMemberList = 0x0370;
        /// <summary>Zone → client: Mobius AskJoinPledge (an invite arrived).</summary>
        public const ushort ClanAskJoin = 0x0371;
        /// <summary>Zone → client: Mobius JoinPledge (the invite was accepted or declined).</summary>
        public const ushort ClanJoinResult = 0x0372;
        /// <summary>Client → zone: Mobius RequestTargetCanceld. Empty body.</summary>
        public const ushort TargetCancelRequest = 0x0373;
        /// <summary>
        /// Zone → client: the selection was dropped, so the target window and the nameplate close.
        /// Mobius sends this to the observing peers as TargetUnselected; over Net the owner needs it too.
        /// </summary>
        public const ushort TargetUnselected = 0x0374;
        /// <summary>Client → zone: Mobius RequestDestroyItem. Body is <c>DestroyItemRequestMsg</c>.</summary>
        public const ushort DestroyItemRequest = 0x0375;
        /// <summary>Client → zone: one boss row, or every row when the body's npc id is 0.</summary>
        public const ushort BossStatusRequest = 0x0376;
        /// <summary>Zone → client: the boss schedule rows (raid and grand), sorted by npc id.</summary>
        public const ushort BossStatus = 0x0377;
        /// <summary>Client → zone: admin boss schedule edit (dead / force spawn). Gated by FINEAGE_ZONE_ADMIN.</summary>
        public const ushort AdminBossSchedule = 0x0378;
        /// <summary>Client → zone: ask for one castle's siege state.</summary>
        public const ushort SiegeListRequest = 0x0379;
        /// <summary>Client → zone: register or withdraw from a siege.</summary>
        public const ushort SiegeRegisterRequest = 0x037A;
        /// <summary>Client → zone: the castle owner answers a waiting defender.</summary>
        public const ushort SiegeDefenderAnswerRequest = 0x037B;
        /// <summary>Client → zone: admin capture. Gated by FINEAGE_ZONE_ADMIN.</summary>
        public const ushort AdminCaptureCastle = 0x037C;
        /// <summary>Zone → client: the castle and its registrations, also the reply to a list request.</summary>
        public const ushort SiegeInfo = 0x037D;
        /// <summary>Client → zone: ask for the Olympiad sheet. Empty body.</summary>
        public const ushort OlympiadStatusRequest = 0x037E;
        /// <summary>Client → zone: join the classed (1) or non-classed (0) queue.</summary>
        public const ushort OlympiadRegisterRequest = 0x037F;
        /// <summary>Client → zone: leave the queue. Empty body.</summary>
        public const ushort OlympiadUnregisterRequest = 0x0380;
        /// <summary>Client → zone: leave the running match (a loss). Empty body.</summary>
        public const ushort OlympiadLeaveRequest = 0x0381;
        /// <summary>Client → zone: admin match report. Gated by FINEAGE_ZONE_ADMIN.</summary>
        public const ushort AdminReportOlympiadResult = 0x0382;
        /// <summary>Zone → client: period, standing, queue and match slot.</summary>
        public const ushort OlympiadStatus = 0x0383;
        /// <summary>Zone → client: the match both participants were placed in.</summary>
        public const ushort OlympiadMatch = 0x0384;
        /// <summary>Zone → client: one countdown step.</summary>
        public const ushort OlympiadCountdown = 0x0385;
        /// <summary>Zone → client: the finished match and its reward.</summary>
        public const ushort OlympiadResult = 0x0386;
        /// <summary>Client → zone: ask for the Seven Signs record. Empty body.</summary>
        public const ushort SevenSignsStatusRequest = 0x0387;
        /// <summary>Client → zone: join a cabal or hand in seal stones.</summary>
        public const ushort SevenSignsActionRequest = 0x0388;
        /// <summary>Zone → client: the Seven Signs record.</summary>
        public const ushort SevenSignsStatus = 0x0389;
        /// <summary>Zone → client: the sky tint the record implies.</summary>
        public const ushort SevenSignsSky = 0x038A;
        /// <summary>Client → zone: enter or leave a catacomb or necropolis.</summary>
        public const ushort DungeonEnterRequest = 0x038B;
        /// <summary>Zone → client: the catacomb/necropolis teleport result and the validation kick.</summary>
        public const ushort DungeonEntered = 0x038C;
        /// <summary>Client → zone: Mobius SetPrivateStoreListSell (open / refresh sell store).</summary>
        public const ushort SetPrivateStoreSell = 0x038D;
        /// <summary>Client → zone: Mobius RequestPrivateStoreQuitSell. Empty body.</summary>
        public const ushort PrivateStoreQuit = 0x038E;
        /// <summary>Client → zone: request another player's sell list.</summary>
        public const ushort PrivateStoreListRequest = 0x038F;
        /// <summary>Client → zone: Mobius RequestPrivateStoreBuy.</summary>
        public const ushort PrivateStoreBuy = 0x0390;
        /// <summary>Zone → client: store type + message for a pawn (knownlist / self).</summary>
        public const ushort PrivateStoreInfo = 0x0391;
        /// <summary>Zone → client: sell list window for the buyer.</summary>
        public const ushort PrivateStoreListSell = 0x0392;
        /// <summary>Client → zone: Mobius SetPrivateStoreListBuy.</summary>
        public const ushort SetPrivateStoreBuy = 0x0393;
        /// <summary>Client → zone: Mobius RequestPrivateStoreSell (sell into a buy store).</summary>
        public const ushort PrivateStoreSellTo = 0x0394;
        /// <summary>Zone → client: buy list window for a visitor.</summary>
        public const ushort PrivateStoreListBuy = 0x0395;
        /// <summary>Client → zone: Mobius RequestRecipeBookOpen.</summary>
        public const ushort RecipeBookOpen = 0x0396;
        /// <summary>Client → zone: Mobius RequestRecipeItemMakeSelf.</summary>
        public const ushort RecipeMakeSelf = 0x0397;
        /// <summary>Zone → client: known recipe ids for one book tab.</summary>
        public const ushort RecipeBookList = 0x0398;
        /// <summary>Zone → client: craft result (success / fail / materials).</summary>
        public const ushort RecipeItemMakeInfo = 0x0399;
        /// <summary>Client → zone: Mobius RequestRecipeShopListSet (open manufacture).</summary>
        public const ushort SetRecipeShop = 0x039A;
        /// <summary>Client → zone: Mobius RequestRecipeShopMakeInfo.</summary>
        public const ushort RecipeShopMakeInfo = 0x039B;
        /// <summary>Client → zone: Mobius RequestRecipeShopMakeItem.</summary>
        public const ushort RecipeShopMakeItem = 0x039C;
        /// <summary>Zone → client: manufacture recipe list for a visitor.</summary>
        public const ushort RecipeShopSellList = 0x039D;
        /// <summary>Zone → client: manufacturer MP + recipe after make-info / make-item.</summary>
        public const ushort RecipeShopItemInfo = 0x039E;
        /// <summary>Client → zone: Mobius RequestFriendInvite.</summary>
        public const ushort FriendInvite = 0x039F;
        /// <summary>Client → zone: Mobius RequestAnswerFriendInvite.</summary>
        public const ushort FriendAnswer = 0x03A0;
        /// <summary>Client → zone: Mobius RequestFriendList.</summary>
        public const ushort FriendListRequest = 0x03A1;
        /// <summary>Client → zone: Mobius RequestFriendDel.</summary>
        public const ushort FriendDelete = 0x03A2;
        /// <summary>Client → zone: Mobius RequestBlock.</summary>
        public const ushort BlockRequest = 0x03A3;
        /// <summary>Zone → client: friend invite prompt (Mobius FriendAddRequest).</summary>
        public const ushort FriendInviteAsk = 0x03A4;
        /// <summary>Zone → client: full friend list.</summary>
        public const ushort FriendList = 0x03A5;
        /// <summary>Zone → client: add/remove/online friend row (Mobius FriendPacket).</summary>
        public const ushort FriendUpdate = 0x03A6;
        /// <summary>Zone → client: block list (+ block-all flag).</summary>
        public const ushort BlockList = 0x03A7;
    }
}
