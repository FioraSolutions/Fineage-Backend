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
        /// <summary>Zone → client: Mobius quest radar arrow target (ShowRadar / RadarControl).</summary>
        public const ushort QuestDirection = 0x0344;
        /// <summary>Zone → client: per-NPC quest markers above nameplates (player-specific).</summary>
        public const ushort QuestNpcMarks = 0x0345;
        /// <summary>Zone → client: newbie tutorial UI (TutorialShowHtml, question mark, client events).</summary>
        public const ushort TutorialUi = 0x0346;
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

        /// <summary>Client → zone: party leader hands leadership to a member (Interlude <c>RequestChangePartyLeader</c>). Body: int32 target objectId.</summary>
        public const ushort PartyChangeLeaderRequest = 0x03A8;

        /// <summary>Zone → party mates: one member's bars (Interlude <c>PartySmallWindowUpdate</c>). Coalesced, only on change.</summary>
        public const ushort PartyMemberUpdate = 0x03A9;

        /// <summary>Zone → party mates: one member's effect list (Interlude <c>PartySpelled</c>). Same body as <see cref="AbnormalStatusUpdate"/>.</summary>
        public const ushort PartySpelled = 0x03AA;

        /// <summary>Zone → party mates: map positions of the other members (Interlude <c>PartyMemberPosition</c>), periodic, only changed rows.</summary>
        public const ushort PartyMemberPosition = 0x03AB;

        /// <summary>Zone → caster + watchers: Interlude <c>MagicSkillLaunched</c> at hitTime (skill fires on its targets).</summary>
        public const ushort MagicSkillLaunched = 0x03AC;

        /// <summary>Zone → caster + watchers: Interlude <c>MagicSkillCanceled</c> (cast aborted / interrupted).</summary>
        public const ushort MagicSkillCanceled = 0x03AD;

        /// <summary>Zone → player(s): Interlude <c>PlaySound</c> (quest jingles, server-driven music).</summary>
        public const ushort PlaySound = 0x03AE;

        /// <summary>Client → zone: one clan / alliance window action (<see cref="PledgeCommandKind"/>).</summary>
        public const ushort PledgeCommand = 0x03AF;
        /// <summary>Zone → client: crest blob (Mobius PledgeCrest / ExPledgeCrestLarge / AllyCrest).</summary>
        public const ushort CrestData = 0x03B0;
        /// <summary>Zone → client: clan war lists (Mobius PledgeReceiveWarList).</summary>
        public const ushort PledgeWarList = 0x03B1;
        /// <summary>Zone → viewer: relation bits for one player (Mobius RelationChanged).</summary>
        public const ushort RelationChanged = 0x03B2;
        /// <summary>Zone → client: clan name / crest by id (Mobius PledgeInfo).</summary>
        public const ushort PledgeInfo = 0x03B3;
        /// <summary>Zone → client: alliance info (Mobius AllianceInfo).</summary>
        public const ushort AllyInfo = 0x03B4;
        /// <summary>Zone → client: alliance invitation (Mobius AskJoinAlly).</summary>
        public const ushort AllyAskJoin = 0x03B5;
        /// <summary>Zone → client: clan hall auction list.</summary>
        public const ushort ClanHallList = 0x03B6;
        /// <summary>Zone → client: castle gate / wall state (<c>DoorStatusNetMsg</c>), after its EntitySpawn.</summary>
        public const ushort DoorStatus = 0x03B7;
        /// <summary>Client → zone: castle lord action (<c>CastleCommandKind</c>).</summary>
        public const ushort CastleCommand = 0x03B8;
        /// <summary>Zone → client: castle page (<c>CastleInfoNetMsg</c>).</summary>
        public const ushort CastleInfo = 0x03B9;
        /// <summary>Client → zone: manor manager / castle lord manor action (<c>ManorCommandKind</c>).</summary>
        public const ushort ManorCommand = 0x03BA;
        /// <summary>Zone → client: manor window (<c>ManorInfoNetMsg</c>): seeds, crop purchases, sell list, settings.</summary>
        public const ushort ManorInfo = 0x03BB;
        /// <summary>Zone → client: fishing start / bite / HP tick / end (<c>FishingEventNetMsg</c>).</summary>
        public const ushort FishingEvent = 0x03BC;
        /// <summary>Client → zone: one summon / pet command (<c>SummonCommandNetMsg</c>, Interlude RequestActionUse pet actions).</summary>
        public const ushort SummonCommand = 0x03BD;
        /// <summary>Zone → owner: summon window (<c>SummonInfoNetMsg</c>, Mobius PetInfo / PetStatusUpdate).</summary>
        public const ushort SummonInfo = 0x03BE;
        /// <summary>Zone → owner: the summon is gone (<c>SummonGoneNetMsg</c>, Mobius PetDelete).</summary>
        public const ushort SummonGone = 0x03BF;
        /// <summary>Zone → owner: pet inventory (<c>PetInventoryNetMsg</c>, Mobius PetItemList).</summary>
        public const ushort PetInventory = 0x03C0;
        /// <summary>Client → zone: duel request / answer / surrender (<c>DuelCommandNetMsg</c>, Mobius RequestDuelStart / RequestDuelAnswerStart).</summary>
        public const ushort DuelCommand = 0x03C1;
        /// <summary>Zone → client: duel ask / ready / start / end (<c>DuelEventNetMsg</c>, Mobius ExDuelAskStart / ExDuelReady / ExDuelStart / ExDuelEnd).</summary>
        public const ushort DuelEvent = 0x03C2;
        /// <summary>Zone → client: an opponent's bars during a duel (<c>DuelUserInfoNetMsg</c>, Mobius ExDuelUpdateUserInfo).</summary>
        public const ushort DuelUserInfo = 0x03C3;
        /// <summary>Zone → watchers: mount / dismount visual (<c>RideNetMsg</c>, Mobius Ride 0x86).</summary>
        public const ushort Ride = 0x03C4;
        /// <summary>Zone → owner: pet / mount action gating (<c>PlayerActionStateNetMsg</c>).</summary>
        public const ushort PlayerActionState = 0x03C5;
        /// <summary>Zone → sender: account characters to ship packages to (Mobius PackageToList 0xC2).</summary>
        public const ushort PackageToList = 0x03C6;
        /// <summary>Zone → sender: sendable bag items for one recipient (Mobius PackageSendableList 0xC3).</summary>
        public const ushort PackageSendableList = 0x03C7;
        /// <summary>Client → zone: ship lines to a character on the account (Mobius RequestPackageSend 0x9F).</summary>
        public const ushort PackageSendRequest = 0x03C8;
        /// <summary>Client → zone: list sendable items for one recipient (Mobius RequestPackageSendableItemList 0x9E).</summary>
        public const ushort PackageSendableListRequest = 0x03C9;
        /// <summary>Zone → derby watchers: Interlude <c>MonRaceInfo</c> (0xDD).</summary>
        public const ushort MonRaceInfo = 0x03CA;
        /// <summary>Client → zone: answer wedding engagement.</summary>
        public const ushort WeddingEngageAnswer = 0x03CB;
        /// <summary>Zone → client: wedding engagement prompt.</summary>
        public const ushort WeddingEngageAsk = 0x03CC;

        /// <summary>Client → zone: Mobius <c>RequestConfirmTargetItem</c> (target object id).</summary>
        public const ushort AugmentConfirmTarget = 0x03CD;
        /// <summary>Client → zone: Mobius <c>RequestConfirmRefinerItem</c>.</summary>
        public const ushort AugmentConfirmRefiner = 0x03CE;
        /// <summary>Client → zone: Mobius <c>RequestConfirmGemStone</c>.</summary>
        public const ushort AugmentConfirmGem = 0x03CF;
        /// <summary>Client → zone: Mobius <c>RequestRefine</c>.</summary>
        public const ushort AugmentRefine = 0x03D0;
        /// <summary>Client → zone: Mobius <c>RequestRefineCancel</c> (remove augmentation).</summary>
        public const ushort AugmentRefineCancel = 0x03D1;
        /// <summary>Client → zone: close variation make window without refine.</summary>
        public const ushort AugmentCancelWindow = 0x03D2;
        /// <summary>Zone → client: Mobius <c>ExShowVariationMakeWindow</c>.</summary>
        public const ushort AugmentShowMakeWindow = 0x03D3;
        /// <summary>Zone → client: Mobius <c>ExShowVariationCancelWindow</c>.</summary>
        public const ushort AugmentShowCancelWindow = 0x03D4;
        /// <summary>Zone → client: Mobius <c>ExPutItemResultForVariationMake</c>.</summary>
        public const ushort AugmentPutItemResult = 0x03D5;
        /// <summary>Zone → client: Mobius <c>ExPutIntensiveResultForVariationMake</c>.</summary>
        public const ushort AugmentPutRefinerResult = 0x03D6;
        /// <summary>Zone → client: Mobius <c>ExPutCommissionResultForVariationMake</c>.</summary>
        public const ushort AugmentPutGemResult = 0x03D7;
        /// <summary>Zone → client: Mobius <c>ExVariationResult</c>.</summary>
        public const ushort AugmentVariationResult = 0x03D8;
        /// <summary>Zone → client: Mobius <c>ExPutItemResultForVariationCancel</c>.</summary>
        public const ushort AugmentPutCancelItemResult = 0x03D9;
        /// <summary>Zone → client: Mobius <c>ExVariationCancelResult</c>.</summary>
        public const ushort AugmentCancelResult = 0x03DA;
        /// <summary>Zone → client: Mobius ExCursedWeaponList (wire 0xFE 0x45).</summary>
        public const ushort CursedWeaponList = 0x03DB;
        /// <summary>Zone → client: Mobius ExCursedWeaponLocation (wire 0xFE 0x46).</summary>
        public const ushort CursedWeaponLocation = 0x03DC;
        /// <summary>Client → zone: Mobius <c>RequestExEnchantSkillInfo</c> (wire 0xD0 0x06). ExClientPackets.java:88.</summary>
        public const ushort RequestEnchantSkillInfo = 0x03DD;
        /// <summary>Client → zone: Mobius <c>RequestExEnchantSkill</c> (wire 0xD0 0x07). ExClientPackets.java:89.</summary>
        public const ushort RequestEnchantSkill = 0x03DE;
        /// <summary>Zone → client: Mobius <c>ExEnchantSkillList</c> (wire 0xFE 0x17). ServerPackets.java:241.</summary>
        public const ushort EnchantSkillList = 0x03DF;
        /// <summary>Zone → client: Mobius <c>ExEnchantSkillInfo</c> (wire 0xFE 0x18). ServerPackets.java:242.</summary>
        public const ushort EnchantSkillInfo = 0x03E0;
        /// <summary>
        /// Zone → client: Mobius <c>ClientSetTime</c> (wire <c>0xEC</c>).
        /// <c>ClientSetTime.java</c> — <c>writeD(gameMinutes)</c>, <c>writeD(6)</c>.
        /// </summary>
        public const ushort ClientSetTime = 0x03E1;
        /// <summary>Client → zone: retail <c>RequestRestart</c> (wire 0x46). Empty body. Returns to the character lobby.</summary>
        public const ushort RequestRestart = 0x03E2;
        /// <summary>Zone → client: retail <c>RestartResponse</c> (wire 0x5f). Followed by <see cref="CharacterRoster"/> when ok.</summary>
        public const ushort RestartResponse = 0x03E3;
        /// <summary>
        /// Client → zone: Mobius <c>RequestBypassUserCmd</c> (retail wire <c>0xaa</c>).
        /// Body is one int, the Interlude user-command id.
        /// 0x03E2 and 0x03E3 are the lobby restart pair.
        /// </summary>
        public const ushort BypassUserCmd = 0x03E4;
        /// <summary>
        /// Client → zone: Mobius <c>RequestCrystallizeItem</c> (retail wire <c>0x72</c>).
        /// Body is object id and count. 0x03E4 is the user-command opcode.
        /// </summary>
        public const ushort RequestCrystallizeItem = 0x03E5;
        /// <summary>
        /// Client → zone: Mobius <c>RequestRecipeBookDestroy</c> (retail wire <c>0xad</c>).
        /// Body is one int32 recipe list id. Reply is <see cref="RecipeBookList"/> for that tab.
        /// </summary>
        public const ushort RecipeBookDestroy = 0x03E6;
        /// <summary>
        /// Client → zone: Mobius <c>RequestChangePetName</c> (retail wire <c>0x89</c>).
        /// Body is one string (the new name). Owner-only; pet must be out and unnamed.
        /// </summary>
        public const ushort RequestChangePetName = 0x03E7;
        /// <summary>Zone → client: Mobius <c>ShopPreviewList</c> (retail <c>0xEF</c>). Wear try-on catalog.</summary>
        public const ushort ShopPreviewList = 0x03E8;
        /// <summary>Zone → client: Mobius <c>ShopPreviewInfo</c> (retail <c>0xF0</c>). Try-on paperdoll overlay.</summary>
        public const ushort ShopPreviewInfo = 0x03E9;
        /// <summary>Client → zone: Mobius <c>RequestPreviewItem</c> (retail <c>0xC6</c>). Pay wear fee and preview.</summary>
        public const ushort RequestPreviewItem = 0x03EA;
        /// <summary>Zone → client: Mobius <c>Dice</c> (retail <c>0xD4</c>).</summary>
        public const ushort Dice = 0x03EB;
        /// <summary>Zone → client: Mobius <c>ShowMiniMap</c> (retail <c>0x9D</c>).</summary>
        public const ushort ShowMiniMap = 0x03EC;
        /// <summary>Zone → client: Mobius <c>ShowCalculator</c> (retail <c>0xDC</c>).</summary>
        public const ushort ShowCalculator = 0x03ED;
        /// <summary>Zone → client: Mobius <c>ShowXMasSeal</c> (retail <c>0xF2</c>).</summary>
        public const ushort ShowXMasSeal = 0x03EE;
        /// <summary>Client → zone: Mobius <c>RequestMakeMacro</c> (retail <c>0xC1</c>).</summary>
        public const ushort RequestMakeMacro = 0x03EF;
        /// <summary>Client → zone: Mobius <c>RequestDeleteMacro</c> (retail <c>0xC2</c>).</summary>
        public const ushort RequestDeleteMacro = 0x03F0;
        /// <summary>Zone → client: Mobius <c>SendMacroList</c> (retail <c>0xE7</c>).</summary>
        public const ushort MacroList = 0x03F1;
        /// <summary>Client → zone: Mobius <c>RequestPartyMatchConfig</c> (retail <c>0x6F</c>).</summary>
        public const ushort RequestPartyMatchConfig = 0x03F2;
        /// <summary>Client → zone: Mobius <c>RequestPartyMatchList</c> (retail <c>0x70</c>).</summary>
        public const ushort RequestPartyMatchList = 0x03F3;
        /// <summary>Client → zone: Mobius <c>RequestPartyMatchDetail</c> (retail <c>0x71</c>).</summary>
        public const ushort RequestPartyMatchDetail = 0x03F4;
        /// <summary>Zone → client: Mobius <c>PartyMatchDetail</c> (retail <c>0x97</c>).</summary>
        public const ushort PartyMatchDetail = 0x03F5;
        /// <summary>Zone → client: Mobius <c>ListPartyWating</c> / <c>PARTY_MATCH_LIST</c> (retail <c>0x96</c>).</summary>
        public const ushort PartyMatchRoomList = 0x03F6;
        /// <summary>Client → zone: Mobius <c>RequestShowBoard</c> (retail <c>0x57</c>).</summary>
        public const ushort RequestShowBoard = 0x03F7;
        /// <summary>Client → zone: Mobius <c>RequestBBSwrite</c> (retail <c>0x22</c>).</summary>
        public const ushort RequestBBSwrite = 0x03F8;
        /// <summary>Zone → client: Mobius <c>ShowBoard</c> (retail <c>0x6E</c>).</summary>
        public const ushort ShowBoard = 0x03F9;
        /// <summary>Client → zone: Mobius <c>RequestPetition</c> (retail <c>0x7F</c>).</summary>
        public const ushort RequestPetition = 0x03FA;
        /// <summary>Client → zone: Mobius <c>RequestPetitionCancel</c> (retail <c>0x80</c>).</summary>
        public const ushort RequestPetitionCancel = 0x03FB;
        /// <summary>Client → zone: <c>RequestOustFromPartyRoom</c> (wire 0xD0 0x01).</summary>
        public const ushort RequestPartyRoomOust = unchecked((ushort)(NetOpcodeMobius.ExClientBase | 0x01));
        /// <summary>Client → zone: <c>RequestDismissPartyRoom</c> (wire 0xD0 0x02).</summary>
        public const ushort RequestPartyRoomDismiss = unchecked((ushort)(NetOpcodeMobius.ExClientBase | 0x02));
        /// <summary>Client → zone: <c>RequestWithdrawPartyRoom</c> (wire 0xD0 0x03).</summary>
        public const ushort RequestPartyRoomWithdraw = unchecked((ushort)(NetOpcodeMobius.ExClientBase | 0x03));
        /// <summary>Client → zone: <c>RequestAskJoinPartyRoom</c> (wire 0xD0 0x14).</summary>
        public const ushort RequestPartyRoomAskJoin = unchecked((ushort)(NetOpcodeMobius.ExClientBase | 0x14));
        /// <summary>Client → zone: <c>AnswerJoinPartyRoom</c> (wire 0xD0 0x15).</summary>
        public const ushort RequestPartyRoomAnswerJoin = unchecked((ushort)(NetOpcodeMobius.ExClientBase | 0x15));
        /// <summary>Client → zone: <c>RequestListPartyMatchingWaitingRoom</c> (wire 0xD0 0x16).</summary>
        public const ushort RequestPartyMatchWaitingList = unchecked((ushort)(NetOpcodeMobius.ExClientBase | 0x16));
        /// <summary>Client → zone: <c>RequestExitPartyMatchingWaitingRoom</c> (wire 0xD0 0x17).</summary>
        public const ushort RequestPartyMatchWaitingExit = unchecked((ushort)(NetOpcodeMobius.ExClientBase | 0x17));
        /// <summary>Zone → client: <c>ExPartyRoomMember</c> (wire 0xFE 0x0E).</summary>
        public const ushort PartyRoomMemberList = unchecked((ushort)(NetOpcodeMobius.ExServerBase | 0x0E));
        /// <summary>Zone → client: <c>ExClosePartyRoom</c> (wire 0xFE 0x0F).</summary>
        public const ushort PartyRoomClose = unchecked((ushort)(NetOpcodeMobius.ExServerBase | 0x0F));
        /// <summary>Zone → client: <c>ExManagePartyRoomMember</c> (wire 0xFE 0x10).</summary>
        public const ushort PartyRoomMemberUpdate = unchecked((ushort)(NetOpcodeMobius.ExServerBase | 0x10));
        /// <summary>Zone → client: <c>ExAskJoinPartyRoom</c> (wire 0xFE 0x34).</summary>
        public const ushort PartyRoomAskJoin = unchecked((ushort)(NetOpcodeMobius.ExServerBase | 0x34));
        /// <summary>Zone → client: <c>ExListPartyMatchingWaitingRoom</c> (wire 0xFE 0x35).</summary>
        public const ushort PartyMatchWaitingRoom = unchecked((ushort)(NetOpcodeMobius.ExServerBase | 0x35));
        /// <summary>Client → zone: Mobius <c>RequestWriteHeroWords</c> (wire 0xD0 0x0C). ExClientPackets.java:43.</summary>
        public const ushort RequestWriteHeroWords = unchecked((ushort)(NetOpcodeMobius.ExClientBase | 0x0C));
        /// <summary>Client → zone: <c>RequestExAskJoinMPCC</c> (wire 0xD0 0x0D). ExClientPackets.java:95.</summary>
        public const ushort CommandChannelAskRequest = unchecked((ushort)(NetOpcodeMobius.ExClientBase | 0x0D));
        /// <summary>Client → zone: <c>RequestExAcceptJoinMPCC</c> (wire 0xD0 0x0E). ExClientPackets.java:96.</summary>
        public const ushort CommandChannelAcceptRequest = unchecked((ushort)(NetOpcodeMobius.ExClientBase | 0x0E));
        /// <summary>Client → zone: <c>RequestExOustFromMPCC</c> (wire 0xD0 0x0F). ExClientPackets.java:97.</summary>
        public const ushort CommandChannelOustRequest = unchecked((ushort)(NetOpcodeMobius.ExClientBase | 0x0F));
        /// <summary>Zone → client: <c>ExAskJoinMPCC</c> (wire 0xFE 0x27). ServerPackets.java:257.</summary>
        public const ushort CommandChannelAskJoin = unchecked((ushort)(NetOpcodeMobius.ExServerBase | 0x27));
        /// <summary>Zone → client: <c>ExOpenMPCC</c> (wire 0xFE 0x25). ServerPackets.java:255.</summary>
        public const ushort CommandChannelOpen = unchecked((ushort)(NetOpcodeMobius.ExServerBase | 0x25));
        /// <summary>Zone → client: <c>ExCloseMPCC</c> (wire 0xFE 0x26). ServerPackets.java:256.</summary>
        public const ushort CommandChannelClose = unchecked((ushort)(NetOpcodeMobius.ExServerBase | 0x26));
        /// <summary>Zone → client: <c>ExMultiPartyCommandChannelInfo</c> (wire 0xFE 0x30). ServerPackets.java:265.</summary>
        public const ushort CommandChannelInfo = unchecked((ushort)(NetOpcodeMobius.ExServerBase | 0x30));
        /// <summary>
        /// Client → zone: Interlude C2S <c>RequestItemList</c>, opcode <c>0x0F</c>, empty body.
        /// Mobius CT_0 Interlude <c>ClientPackets.REQUEST_ITEM_LIST(0x0F, RequestItemList::new)</c>
        /// (<c>L2J_Mobius_CT_0_Interlude/.../network/ClientPackets.java</c>). On that table <c>0x14</c> is
        /// <c>UseItem</c>, not the item-list request. The reply is <see cref="ItemList"/> at the current cursor.
        /// </summary>
        public const ushort RequestItemList = 0x000F;
        /// <summary>
        /// Zone → client: Mobius <c>AgitDecoInfo</c>.
        /// <c>ServerPackets.AGIT_DECO_INFO(0xF7)</c>,
        /// L2J_Mobius_CT_0_Interlude/java/org/l2jmobius/gameserver/network/ServerPackets.java:223.
        /// </summary>
        public const ushort AgitDecoInfo = 0xF7;

        // Boat packets use the Mobius CT_0 Interlude ids from ServerPackets.java.

        /// <summary>ServerPackets.java:101 <c>VEHICLE_INFO(0x59)</c>. Layout: VehicleInfo.java:47-52.</summary>
        public const ushort VehicleInfo = 0x59;
        /// <summary>ServerPackets.java:102 <c>VEHICLE_DEPARTURE(0x5A)</c>. Layout: VehicleDeparture.java:49-55.</summary>
        public const ushort VehicleDeparture = 0x5A;
        /// <summary>ServerPackets.java:103 <c>VEHICLE_CHECK_LOCATION(0x5B)</c>. Layout: VehicleCheckLocation.java:39-44.</summary>
        public const ushort VehicleCheckLocation = 0x5B;
        /// <summary>ServerPackets.java:104 <c>GET_ON_VEHICLE(0x5C)</c>. Layout: GetOnVehicle.java:48-53.</summary>
        public const ushort GetOnVehicle = 0x5C;
        /// <summary>ServerPackets.java:105 <c>GET_OFF_VEHICLE(0x5D)</c>. Layout: GetOffVehicle.java:53-58.</summary>
        public const ushort GetOffVehicle = 0x5D;
        /// <summary>ServerPackets.java:120 <c>MOVE_TO_LOCATION_IN_VEHICLE(0x71)</c>. Layout: MoveToLocationInVehicle.java:51-59.</summary>
        public const ushort MoveToLocationInVehicle = 0x71;
        /// <summary>ServerPackets.java:121 <c>STOP_MOVE_IN_VEHICLE(0x72)</c>. Layout: StopMoveInVehicle.java:46-52.</summary>
        public const ushort StopMoveInVehicle = 0x72;
        /// <summary>ServerPackets.java:122 <c>VALIDATE_LOCATION_IN_VEHICLE(0x73)</c>. Layout: ValidateLocationInVehicle.java:47-53.</summary>
        public const ushort ValidateLocationInVehicle = 0x73;
        /// <summary>ServerPackets.java:172 <c>VEHICLE_START(0xC0)</c>. Layout: VehicleStarted.java:45-47.</summary>
        public const ushort VehicleStarted = 0xC0;

        /// <summary>
        /// Mobius <c>ClientPackets.java</c>. A separate registry from the constants above:
        /// Interlude uses <c>0x5C</c> for both server <c>GET_ON_VEHICLE</c> and client
        /// <c>REQUEST_MOVE_TO_LOCATION_IN_VEHICLE</c>, and <c>0x5D</c> for both
        /// <c>GET_OFF_VEHICLE</c> and <c>CANNOT_MOVE_ANYMORE_IN_VEHICLE</c>.
        /// </summary>
        public static class ClientPackets
        {
            /// <summary>ClientPackets.java:85 <c>REQUEST_GET_ON_VEHICLE(0x42)</c>. readImpl RequestGetOnVehicle.java:40-43.</summary>
            public const ushort RequestGetOnVehicle = 0x42;
            /// <summary>ClientPackets.java:86 <c>REQUEST_GET_OFF_VEHICLE(0x43)</c>. readImpl RequestGetOffVehicle.java:36-42.</summary>
            public const ushort RequestGetOffVehicle = 0x43;
            /// <summary>ClientPackets.java:106 <c>REQUEST_MOVE_TO_LOCATION_IN_VEHICLE(0x5C)</c>. readImpl RequestMoveToLocationInVehicle.java:45-53.</summary>
            public const ushort RequestMoveToLocationInVehicle = 0x5C;
            /// <summary>ClientPackets.java:107 <c>CANNOT_MOVE_ANYMORE_IN_VEHICLE(0x5D)</c>. readImpl CannotMoveAnymoreInVehicle.java:35-42.</summary>
            public const ushort CannotMoveAnymoreInVehicle = 0x5D;
        }
    }
}
