# Wire coverage — Mobius CT_0 Interlude packets

Fineage `dev` @ `8daf4209`. Mobius `L2J_Mobius_CT_0_Interlude` @ `5c019037`. Unity client `FioraSolutions/Fineage` `dev` @ `c1e79fe` (`l2-unity`, Net contract via `scripts/sync-net-contract.py`).

Fineage speaks its own Net opcodes (`src/Fineage.Net.Contract/Opcodes/NetOpcode.cs`), not the retail byte ids. A packet is **server yes** when a Zone/Login/Protocol handler implements that message (often folded into one Net opcode). **partial** means the gameplay exists only as a subset, an HTML substitute, or a push with no matching request. **no** means nothing handles it. **n/a** is out of the Interlude Net target (GameGuard, HackShield, GM snoop, Kamael slideshow, debug primitives, player mail, retail NetPing).

The Unity client **handles** a packet when a file outside `l2-unity/Assets/Scripts/**/Net/Contract` references the mapped `NetOpcode` (decoder, sender, or apply service). **contract** means the synced contract defines the opcode and no gameplay file reads or writes it. Client and server contracts match (252 opcode constants on both sides).

## Counts

| Direction | Packets | Server yes | partial | no | n/a | Client yes | contract-only | client no | client n/a |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| C2S | 158 | 104 | 34 | 17 | 3 | 91 | 28 | 36 | 3 |
| C2S extended (0xD0) | 46 | 32 | 5 | 9 | 0 | 13 | 23 | 10 | 0 |
| S2C | 270 | 178 | 54 | 31 | 7 | 210 | 6 | 47 | 7 |

**In-scope gaps:** server not fully implemented (partial+no) **150**, of which server absent (**no**) **57**. Client does not apply the mapped opcode (contract-only+no, excluding n/a) **150**, of which client absent (**no**) **93**.

Geodata LOS and pathfinding are server-side region files (`scripts/fetch-geodata.sh`). They have no retail packet of their own.

`BlockBuffSlot`, `NoblesseBless` and `ProtectionBlessing` are server-side effect rules. They have no retail packet of their own; the buff bar is the existing abnormal update.

Retail opcode ids below are Mobius `ClientPackets` / `ExClientPackets` / `ServerPackets`.

## Client → server

| Opcode | Mobius class | Server | Client | Fineage opcode | Gap |
|---|---|---|---|---|---|
| 0x00 | ProtocolVersion | yes | yes | Hello | Fineage Hello, not retail 0x00 |
| 0x08 | AuthLogin | yes | yes | Auth,ZoneAuth | Login Auth + zone ZoneAuth |
| 0x09 | Logout | partial | no |  | TCP close persists in ZoneWorld.Members.Leave; no logout opcode |
| 0x0b | CharacterCreate | yes | yes | CreateCharacter | Login CreateCharacter |
| 0x0c | CharacterDelete | yes | yes | DeleteCharacter |  |
| 0x0d | CharacterSelect | yes | yes | SelectCharacter |  |
| 0x0e | NewCharacter | partial | no |  | templates are server-side; no NewCharacterSuccess roster |
| 0x62 | CharacterRestore | yes | yes | RestoreCharacter |  |
| 0x68 | RequestPledgeCrest | yes | yes | CrestData |  |
| 0x03 | EnterWorld | yes | yes | EnterWorld |  |
| 0x01 | MoveToLocation | yes | yes | MoveInput | ZoneWorldMovement |
| 0x04 | Action | yes | yes | SelectTarget | target select; sit/social are RequestActionUse |
| 0x0a | AttackRequest | yes | yes | AttackRequest |  |
| 0x0f | RequestItemList | yes | yes | RequestItemList |  |
| 0x11 | RequestUnEquipItem | yes | yes | UnequipItem |  |
| 0x12 | RequestDropItem | yes | yes | DropItemRequest |  |
| 0x14 | UseItem | yes | yes | UseItem |  |
| 0x15 | TradeRequest | yes | yes | TradeRequest |  |
| 0x16 | AddTradeItem | yes | yes | TradeAddItemRequest |  |
| 0x17 | TradeDone | yes | yes | TradeDoneRequest |  |
| 0x1b | RequestSocialAction | yes | yes | RequestActionUse | folded into RequestActionUse |
| 0x1c | ChangeMoveType2 | yes | yes | RequestActionUse | walk/run is action id 1 |
| 0x1d | ChangeWaitType2 | yes | yes | RequestActionUse | sit/stand is action id 0 |
| 0x1e | RequestSellItem | yes | yes | RequestSellItem |  |
| 0x1f | RequestBuyItem | yes | yes | RequestBuyItem |  |
| 0x20 | RequestLinkHtml | yes | yes | BypassRequest | LinkBypassHandler |
| 0x21 | RequestBypassToServer | yes | yes | BypassRequest |  |
| 0x22 | RequestBBSwrite | no | no |  | community board |
| 0x24 | RequestJoinPledge | yes | yes | ClanInviteRequest |  |
| 0x25 | RequestAnswerJoinPledge | yes | yes | ClanAnswerJoinRequest |  |
| 0x26 | RequestWithdrawalPledge | yes | yes | ClanLeaveRequest |  |
| 0x27 | RequestOustPledgeMember | yes | yes | ClanKickRequest |  |
| 0x29 | RequestJoinParty | yes | yes | PartyInviteRequest |  |
| 0x2a | RequestAnswerJoinParty | yes | yes | PartyAnswerJoinRequest |  |
| 0x2b | RequestWithDrawalParty | yes | yes | PartyLeaveRequest |  |
| 0x2c | RequestOustPartyMember | yes | yes | PartyKickRequest |  |
| 0x2f | RequestMagicSkillUse | yes | yes | UseSkill |  |
| 0x30 | Appearing | no | no |  | post-teleport Appearing is not handled |
| 0x31 | SendWareHouseDepositList | yes | yes | WarehouseDepositRequest |  |
| 0x32 | SendWareHouseWithDrawList | yes | yes | WarehouseWithdrawRequest |  |
| 0x33 | RequestShortcutReg | yes | yes | RequestShortCutReg |  |
| 0x35 | RequestShortcutDel | yes | yes | RequestShortCutDel |  |
| 0x36 | CannotMoveAnymore | yes | contract | StopMove |  |
| 0x37 | RequestTargetCanceld | yes | yes | TargetCancelRequest |  |
| 0x38 | Say2 | yes | yes | Say | ZoneWorldChat channels including hero and friend |
| 0x3c | RequestPledgeMemberList | partial | yes | PledgeMemberList | roster is pushed; no explicit refresh request |
| 0x3f | RequestSkillList | partial | yes | SkillList | SkillList is pushed on enter; no refresh request |
| 0x41 | MoveWithDelta | no | no |  | relative-move packet unused |
| 0x42 | RequestGetOnVehicle | yes | contract | RequestGetOnVehicle | NetOpcode.ClientPackets |
| 0x43 | RequestGetOffVehicle | yes | contract | RequestGetOffVehicle |  |
| 0x44 | AnswerTradeRequest | yes | yes | TradeAnswerRequest |  |
| 0x45 | RequestActionUse | yes | yes | RequestActionUse | sit, walk/run, social ids |
| 0x46 | RequestRestart | yes | no | RequestRestart | persist, keep account, then CharacterRoster |
| 0x47 | RequestSiegeInfo | yes | contract | SiegeListRequest |  |
| 0x48 | ValidatePosition | yes | yes | ValidatePosition |  |
| 0x4a | StartRotating | no | no |  |  |
| 0x4b | FinishRotating | no | no |  |  |
| 0x4d | RequestStartPledgeWar | yes | yes | PledgeCommand | ZoneWorldPledge.Wars |
| 0x4e | RequestReplyStartPledgeWar | yes | yes | PledgeCommand |  |
| 0x4f | RequestStopPledgeWar | yes | yes | PledgeCommand |  |
| 0x50 | RequestReplyStopPledgeWar | yes | yes | PledgeCommand |  |
| 0x51 | RequestSurrenderPledgeWar | yes | yes | PledgeCommand |  |
| 0x52 | RequestReplySurrenderPledgeWar | yes | yes | PledgeCommand |  |
| 0x53 | RequestSetPledgeCrest | yes | yes | PledgeCommand | ZoneWorldPledge.Crests |
| 0x55 | RequestGiveNickName | yes | yes | PledgeCommand | ZoneWorldPledge.Commands |
| 0x57 | RequestShowBoard | no | no |  | community board |
| 0x58 | RequestEnchantItem | yes | yes | EnchantRequest | confirm/cancel are EnchantConfirm / EnchantCancel |
| 0x59 | RequestDestroyItem | yes | yes | DestroyItemRequest |  |
| 0x5b | SendBypassBuildCmd | partial | no |  | GM commands are admin_* HTML bypasses, not this packet |
| 0x5c | RequestMoveToLocationInVehicle | yes | contract | RequestMoveToLocationInVehicle |  |
| 0x5d | CannotMoveAnymoreInVehicle | yes | contract | CannotMoveAnymoreInVehicle |  |
| 0x5e | RequestFriendInvite | yes | contract | FriendInvite |  |
| 0x5f | RequestAnswerFriendInvite | yes | contract | FriendAnswer |  |
| 0x60 | RequestFriendList | yes | contract | FriendListRequest |  |
| 0x61 | RequestFriendDel | yes | contract | FriendDelete |  |
| 0x63 | RequestQuestList | yes | yes | RequestQuestList |  |
| 0x64 | RequestQuestAbort | yes | yes | RequestQuestAbort |  |
| 0x66 | RequestPledgeInfo | yes | yes | PledgeInfo | PledgeCommand / PledgeInfo |
| 0x67 | RequestPledgeExtendedInfo | partial | yes | PledgeInfo | folded into pledge info |
| 0x6b | RequestAcquireSkillInfo | yes | yes | RequestAcquireSkillInfo |  |
| 0x6c | RequestAcquireSkill | yes | yes | RequestAcquireSkill |  |
| 0x6d | RequestRestartPoint | yes | yes | ReviveRequest | ZoneWorldDeath castle/HQ/town restart |
| 0x6e | RequestGMCommand | partial | no |  | ZoneWorldAdmin verbs, not RequestGMCommand |
| 0x6f | RequestPartyMatchConfig | no | no |  | party matching room |
| 0x70 | RequestPartyMatchList | no | no |  |  |
| 0x71 | RequestPartyMatchDetail | no | no |  |  |
| 0x72 | RequestCrystallizeItem | yes | no | RequestCrystallizeItem | `CrystallizeRules` + auditable economy ledger. Unity client does not send retail 0x72 yet |
| 0x73 | RequestPrivateStoreManageSell | yes | contract | SetPrivateStoreSell |  |
| 0x74 | SetPrivateStoreListSell | yes | contract | SetPrivateStoreSell |  |
| 0x76 | RequestPrivateStoreQuitSell | yes | yes | PrivateStoreQuit |  |
| 0x77 | SetPrivateStoreMsgSell | partial | contract | SetPrivateStoreSell | message is part of the sell list, no separate packet |
| 0x79 | RequestPrivateStoreBuy | yes | contract | PrivateStoreBuy |  |
| 0x7b | RequestTutorialLinkHtml | partial | yes | TutorialUi | TutorialUi covers show/question; link html is bypass |
| 0x7c | RequestTutorialPassCmdToServer | partial | yes | TutorialUi |  |
| 0x7d | RequestTutorialQuestionMark | partial | yes | TutorialUi |  |
| 0x7e | RequestTutorialClientEvent | partial | yes | TutorialUi |  |
| 0x7f | RequestPetition | no | no |  | Say petition types return NotInPetitionChat |
| 0x80 | RequestPetitionCancel | no | no |  |  |
| 0x81 | RequestGmList | partial | no |  | GmListTowns is an admin bypass, not the petition GM list |
| 0x82 | RequestJoinAlly | yes | yes | AllyInfo | ZoneWorldAlly |
| 0x83 | RequestAnswerJoinAlly | yes | yes | AllyAskJoin |  |
| 0x84 | AllyLeave | yes | yes | AllyInfo |  |
| 0x85 | AllyDismiss | yes | yes | AllyInfo |  |
| 0x86 | RequestDismissAlly | yes | yes | AllyInfo |  |
| 0x87 | RequestSetAllyCrest | yes | yes | PledgeCommand | crest blob |
| 0x88 | RequestAllyCrest | yes | yes | CrestData |  |
| 0x89 | RequestChangePetName | no | no |  | name field exists; no rename request |
| 0x8a | RequestPetUseItem | partial | contract | SummonCommand | pet item use is not a separate opcode |
| 0x8b | RequestGiveItemToPet | partial | yes | PetInventory | pet inventory exists; give/get not split |
| 0x8c | RequestGetItemFromPet | partial | yes | PetInventory |  |
| 0x8e | RequestAllyInfo | yes | yes | AllyInfo |  |
| 0x8f | RequestPetGetItem | partial | contract | SummonCommand |  |
| 0x90 | RequestPrivateStoreManageBuy | yes | contract | SetPrivateStoreBuy |  |
| 0x91 | SetPrivateStoreListBuy | yes | contract | SetPrivateStoreBuy |  |
| 0x93 | RequestPrivateStoreQuitBuy | yes | yes | PrivateStoreQuit |  |
| 0x94 | SetPrivateStoreMsgBuy | partial | contract | SetPrivateStoreBuy |  |
| 0x96 | RequestPrivateStoreSell | yes | contract | PrivateStoreSellTo |  |
| 0x9d | RequestSkillCoolTime | partial | no |  | reuse rides SkillList; no SkillCoolTime packet |
| 0x9e | RequestPackageSendableItemList | yes | contract | PackageSendableListRequest |  |
| 0x9f | RequestPackageSend | yes | contract | PackageSendRequest |  |
| 0xa0 | RequestBlock | yes | contract | BlockRequest |  |
| 0xa2 | RequestSiegeAttackerList | partial | yes | SiegeInfo | folded into SiegeInfo |
| 0xa3 | RequestSiegeDefenderList | partial | yes | SiegeInfo |  |
| 0xa4 | RequestJoinSiege | yes | contract | SiegeRegisterRequest |  |
| 0xa5 | RequestConfirmSiegeWaitingList | yes | contract | SiegeDefenderAnswerRequest |  |
| 0xa7 | MultiSellChoose | yes | yes | RequestMultiSellChoose |  |
| 0xa8 | RequestNetPing | n/a | n/a |  | Fineage Net session, not retail NetPing |
| 0xaa | BypassUserCmd | yes | no | BypassUserCmd | `UserCommandRegistry`. Unity client does not send `BypassUserCmd` / retail 0xaa yet |
| 0xab | SnoopQuit | n/a | n/a |  | GM snoop is not a Fineage target |
| 0xac | RequestRecipeBookOpen | yes | yes | RecipeBookOpen |  |
| 0xad | RequestRecipeBookDestroy | yes | yes | Net `RecipeBookDestroy` 0x03E6 | #935 |
| 0xae | RequestRecipeItemMakeInfo | partial | yes | RecipeBookOpen | make info is RecipeItemMakeInfo S2C |
| 0xaf | RequestRecipeItemMakeSelf | yes | yes | RecipeMakeSelf |  |
| 0xb1 | RequestRecipeShopMessageSet | partial | yes | SetRecipeShop |  |
| 0xb2 | RequestRecipeShopListSet | yes | yes | SetRecipeShop |  |
| 0xb3 | RequestRecipeShopManageQuit | yes | yes | PrivateStoreQuit |  |
| 0xb5 | RequestRecipeShopMakeInfo | yes | yes | RecipeShopMakeInfo |  |
| 0xb6 | RequestRecipeShopMakeItem | yes | yes | RecipeShopMakeItem |  |
| 0xb7 | RequestRecipeShopManagePrev | partial | no |  | no prev-page packet; list is RecipeShopSellList |
| 0xb8 | ObserverReturn | partial | contract | OlympiadLeaveRequest | olympiad observer end only |
| 0xb9 | RequestEvaluate | no | no |  | no recommendation / evaluate |
| 0xba | RequestHennaItemList | partial | no |  | ZoneWorldSymbolMaker HTML, not HennaEquipList |
| 0xbb | RequestHennaItemInfo | partial | no |  | HTML symbol maker |
| 0xbc | RequestHennaEquip | partial | no |  | HTML draw |
| 0xbd | RequestHennaRemoveList | partial | no |  | HTML |
| 0xbe | RequestHennaItemRemoveInfo | partial | no |  | HTML |
| 0xbf | RequestHennaRemove | partial | no |  | HTML remove |
| 0xc0 | RequestPledgePower | yes | yes | PledgeCommand |  |
| 0xc1 | RequestMakeMacro | no | no |  |  |
| 0xc2 | RequestDeleteMacro | no | no |  |  |
| 0xc4 | RequestBuySeed | yes | contract | ManorCommand |  |
| 0xc5 | DlgAnswer | partial | no |  | confirms are per-feature (party, trade, augment), no general ConfirmDlg |
| 0xc6 | RequestPreviewItem | yes | yes |  | Wear → ShopPreviewList / ShopPreviewInfo (#924) |
| 0xc7 | RequestSSQStatus | yes | contract | SevenSignsStatusRequest |  |
| 0xca | GameGuardReply | n/a | n/a |  | GameGuard is not part of Fineage Net |
| 0xcc | RequestSendFriendMsg | yes | yes | Say | ChatProtocol.Friend / Tell |
| 0xcd | RequestShowMiniMap | no | no |  |  |
| 0xcf | RequestRecordInfo | partial | no |  | knownlist pushes spawns; no client resend request |

## Client → server, extended 0xD0

| Opcode | Mobius class | Server | Client | Fineage opcode | Gap |
|---|---|---|---|---|---|
| 0xd0/0x01 | RequestOustFromPartyRoom | no | no |  |  |
| 0xd0/0x02 | RequestDismissPartyRoom | no | no |  |  |
| 0xd0/0x03 | RequestWithdrawPartyRoom | no | no |  |  |
| 0xd0/0x04 | RequestChangePartyLeader | yes | contract | PartyChangeLeaderRequest |  |
| 0xd0/0x05 | RequestAutoSoulShot | yes | yes | RequestAutoSoulShot |  |
| 0xd0/0x06 | RequestExEnchantSkillInfo | yes | contract | RequestEnchantSkillInfo |  |
| 0xd0/0x07 | RequestExEnchantSkill | yes | contract | RequestEnchantSkill |  |
| 0xd0/0x08 | RequestManorList | yes | contract | ManorCommand |  |
| 0xd0/0x09 | RequestProcureCropList | yes | contract | ManorCommand |  |
| 0xd0/0x0a | RequestSetSeed | yes | contract | ManorCommand |  |
| 0xd0/0x0b | RequestSetCrop | yes | contract | ManorCommand |  |
| 0xd0/0x0c | RequestWriteHeroWords | no | no |  |  |
| 0xd0/0x0d | RequestExAskJoinMPCC | yes | contract | CommandChannelAskRequest |  |
| 0xd0/0x0e | RequestExAcceptJoinMPCC | yes | contract | CommandChannelAcceptRequest |  |
| 0xd0/0x0f | RequestExOustFromMPCC | yes | contract | CommandChannelOustRequest |  |
| 0xd0/0x10 | RequestExPledgeCrestLarge | yes | yes | CrestData |  |
| 0xd0/0x11 | RequestExSetPledgeCrestLarge | yes | yes | PledgeCommand |  |
| 0xd0/0x12 | RequestOlympiadObserverEnd | yes | contract | OlympiadLeaveRequest |  |
| 0xd0/0x13 | RequestOlympiadMatchList | partial | contract | OlympiadStatusRequest | status sheet, not the match list packet |
| 0xd0/0x14 | RequestAskJoinPartyRoom | no | no |  |  |
| 0xd0/0x15 | AnswerJoinPartyRoom | no | no |  |  |
| 0xd0/0x16 | RequestListPartyMatchingWaitingRoom | no | no |  |  |
| 0xd0/0x17 | RequestExitPartyMatchingWaitingRoom | no | no |  |  |
| 0xd0/0x18 | RequestGetBossRecord | yes | contract | BossStatusRequest | RaidBossPoints + BossStatus |
| 0xd0/0x19 | RequestPledgeSetAcademyMaster | yes | yes | PledgeCommand |  |
| 0xd0/0x1a | RequestPledgePowerGradeList | yes | yes | PledgeCommand |  |
| 0xd0/0x1b | RequestPledgeMemberPowerInfo | yes | yes | PledgeCommand |  |
| 0xd0/0x1c | RequestPledgeSetMemberPowerGrade | yes | yes | PledgeCommand |  |
| 0xd0/0x1d | RequestPledgeMemberInfo | yes | yes | PledgeCommand |  |
| 0xd0/0x1e | RequestPledgeWarList | yes | yes | PledgeWarList |  |
| 0xd0/0x1f | RequestExFishRanking | partial | no |  | FishingChampionshipBoard has no client ranking packet |
| 0xd0/0x20 | RequestPCCafeCouponUse | no | no |  | PC cafe points |
| 0xd0/0x22 | RequestCursedWeaponList | partial | yes | CursedWeaponList | S2C list exists; no C2S refresh |
| 0xd0/0x23 | RequestCursedWeaponLocation | partial | yes | CursedWeaponLocation | S2C locations; no C2S refresh |
| 0xd0/0x24 | RequestPledgeReorganizeMember | yes | yes | PledgeCommand |  |
| 0xd0/0x26 | RequestExMPCCShowPartyMembersInfo | partial | contract | CommandChannelInfo | info is server-pushed |
| 0xd0/0x27 | RequestDuelStart | yes | contract | DuelCommand |  |
| 0xd0/0x28 | RequestDuelAnswerStart | yes | contract | DuelCommand |  |
| 0xd0/0x29 | RequestConfirmTargetItem | yes | contract | AugmentConfirmTarget |  |
| 0xd0/0x2a | RequestConfirmRefinerItem | yes | contract | AugmentConfirmRefiner |  |
| 0xd0/0x2b | RequestConfirmGemStone | yes | contract | AugmentConfirmGem |  |
| 0xd0/0x2c | RequestRefine | yes | contract | AugmentRefine |  |
| 0xd0/0x2d | RequestConfirmCancelItem | yes | contract | AugmentCancelWindow |  |
| 0xd0/0x2e | RequestRefineCancel | yes | contract | AugmentRefineCancel |  |
| 0xd0/0x2f | RequestExMagicSkillUseGround | yes | yes | UseSkill | ground target on UseSkill |
| 0xd0/0x30 | RequestDuelSurrender | yes | contract | DuelCommand |  |

## Server → client

| Opcode | Mobius enum | Java class | Server | Client | Fineage opcode | Gap |
|---|---|---|---|---|---|---|
| 0x00 | KEY_PACKET | KeyPacket | partial | yes | HelloAck | login hello ack, not a cipher key packet |
| 0x01 | CHAR_MOVE_TO_LOCATION | MoveToLocation | yes | yes | MoveState |  |
| 0x02 | NPC_SAY | NpcSay | yes | yes | CreatureSay |  |
| 0x03 | CHAR_INFO | CharInfo | yes | yes | EntitySpawn | players |
| 0x04 | USER_INFO | UserInfo | yes | yes | UserInfo |  |
| 0x05 | ATTACK | Attack | yes | yes | AttackState |  |
| 0x06 | DIE | Die | yes | yes | EntityDie |  |
| 0x07 | REVIVE | Revive | yes | yes | Revive |  |
| 0x0b | SPAWN_ITEM | SpawnItem | yes | yes | DropItem |  |
| 0x0c | DROP_ITEM | DropItem | yes | yes | DropItem |  |
| 0x0d | GET_ITEM | GetItem | yes | yes | InventoryUpdate | pickup deletes the ground item and updates inventory |
| 0x0e | STATUS_UPDATE | StatusUpdate | yes | yes | StatusUpdate |  |
| 0x0f | NPC_HTML_MESSAGE | NpcHtmlMessage | yes | yes | NpcHtml |  |
| 0x10 | SELL_LIST | SellList | yes | yes | SellList |  |
| 0x11 | BUY_LIST | BuyList | yes | yes | BuyList |  |
| 0x12 | DELETE_OBJECT | DeleteObject | yes | yes | EntityDespawn |  |
| 0x13 | CHAR_SELECT_INFO | CharSelectionInfo | yes | yes | CharacterRoster |  |
| 0x14 | LOGIN_FAIL | LoginFail | yes | yes | AuthResult |  |
| 0x15 | CHAR_SELECTED | CharSelected | yes | yes | Handoff |  |
| 0x16 | NPC_INFO | AbstractNpcInfo | yes | yes | EntitySpawn | NPCs and summons share EntitySpawn |
| 0x17 | CHAR_TEMPLATES | NewCharacterSuccess | partial | no |  | creation uses server templates; packet not sent |
| 0x19 | CHAR_CREATE_OK | CharCreateOk | yes | yes | CreateResult |  |
| 0x1a | CHAR_CREATE_FAIL | CharCreateFail | yes | yes | CreateResult |  |
| 0x1b | ITEM_LIST | ItemList | yes | yes | ItemList |  |
| 0x1c | SUNRISE | SunRise | partial | yes | ClientSetTime | ClientSetTime carries game minutes, not SunRise/SunSet |
| 0x1d | SUNSET | SunSet | partial | yes | ClientSetTime |  |
| 0x1e | TRADE_START | TradeStart | yes | yes | TradeStart |  |
| 0x20 | TRADE_OWN_ADD | TradeOwnAdd | yes | yes | TradeOfferUpdate |  |
| 0x21 | TRADE_OTHER_ADD | TradeOtherAdd | yes | yes | TradeOfferUpdate |  |
| 0x22 | TRADE_DONE | TradeDone | yes | yes | TradeFinished |  |
| 0x23 | CHAR_DELETE_OK | CharDeleteSuccess | yes | yes | DeleteResult |  |
| 0x24 | CHAR_DELETE_FAIL | CharDeleteFail | yes | yes | DeleteResult |  |
| 0x25 | ACTION_FAIL | ActionFailed | partial | yes | SystemMessage | failures are SystemMessage, not ActionFail |
| 0x26 | SERVER_CLOSE | ServerClose | partial | yes | Reject |  |
| 0x27 | INVENTORY_UPDATE | InventoryUpdate | yes | yes | InventoryUpdate |  |
| 0x28 | TELEPORT_TO_LOCATION | TeleportToLocation | yes | yes | TeleportToLocation |  |
| 0x29 | TARGET_SELECTED | TargetSelected | yes | yes | TargetSelected |  |
| 0x2a | TARGET_UNSELECTED | TargetUnselected | yes | yes | TargetUnselected |  |
| 0x2b | AUTO_ATTACK_START | AutoAttackStart | yes | yes | AutoAttackStart |  |
| 0x2c | AUTO_ATTACK_STOP | AutoAttackStop | yes | yes | AutoAttackStop |  |
| 0x2d | SOCIAL_ACTION | SocialAction | yes | yes | SocialAction |  |
| 0x2e | CHANGE_MOVE_TYPE | ChangeMoveType | yes | yes | ChangeMoveType |  |
| 0x2f | CHANGE_WAIT_TYPE | ChangeWaitType | yes | yes | ChangeWaitType |  |
| 0x30 | MANAGE_PLEDGE_POWER | ManagePledgePower | partial | yes | PledgeStatus |  |
| 0x32 | ASK_JOIN_PLEDGE | AskJoinPledge | yes | yes | ClanAskJoin |  |
| 0x33 | JOIN_PLEDGE | JoinPledge | yes | yes | ClanJoinResult |  |
| 0x39 | ASK_JOIN_PARTY | AskJoinParty | yes | yes | PartyAskJoin |  |
| 0x3a | JOIN_PARTY | JoinParty | yes | yes | PartyJoinResult |  |
| 0x41 | WAREHOUSE_DEPOSIT_LIST | WareHouseDepositList | yes | yes | WarehouseDepositList |  |
| 0x42 | WAREHOUSE_WITHDRAW_LIST | WareHouseWithdrawalList | yes | yes | WarehouseWithdrawList |  |
| 0x44 | SHORT_CUT_REGISTER | ShortcutRegister | yes | yes | ShortCutRegister |  |
| 0x45 | SHORT_CUT_INIT | ShortcutInit | yes | yes | ShortCutInit |  |
| 0x47 | STOP_MOVE | StopMove | yes | contract | StopMove |  |
| 0x48 | MAGIC_SKILL_USE | MagicSkillUse | yes | yes | MagicSkillUse |  |
| 0x49 | MAGIC_SKILL_CANCELD | MagicSkillCanceled | yes | yes | MagicSkillCanceled | Mobius enum spelling; Fineage MagicSkillCanceled |
| 0x4a | CREATURE_SAY | CreatureSay | yes | yes | CreatureSay |  |
| 0x4b | EQUIP_UPDATE | EquipUpdate | yes | yes | InventoryUpdate |  |
| 0x4c | DOOR_INFO | DoorInfo | yes | yes | DoorStatus |  |
| 0x4d | DOOR_STATUS_UPDATE | DoorStatusUpdate | yes | yes | DoorStatus |  |
| 0x4e | PARTY_SMALL_WINDOW_ALL | PartySmallWindowAll | yes | yes | PartyMemberList |  |
| 0x4f | PARTY_SMALL_WINDOW_ADD | PartySmallWindowAdd | yes | yes | PartyMemberList |  |
| 0x50 | PARTY_SMALL_WINDOW_DELETE_ALL | PartySmallWindowDeleteAll | yes | yes | PartyMemberList |  |
| 0x51 | PARTY_SMALL_WINDOW_DELETE | PartySmallWindowDelete | yes | yes | PartyMemberList |  |
| 0x52 | PARTY_SMALL_WINDOW_UPDATE | PartySmallWindowUpdate | yes | yes | PartyMemberUpdate |  |
| 0x53 | PLEDGE_SHOW_MEMBER_LIST_ALL | PledgeShowMemberListAll | yes | yes | PledgeMemberList |  |
| 0x54 | PLEDGE_SHOW_MEMBER_LIST_UPDATE | PledgeShowMemberListUpdate | yes | yes | PledgeMemberList |  |
| 0x55 | PLEDGE_SHOW_MEMBER_LIST_ADD | PledgeShowMemberListAdd | yes | yes | PledgeMemberList |  |
| 0x56 | PLEDGE_SHOW_MEMBER_LIST_DELETE | PledgeShowMemberListDelete | yes | yes | PledgeMemberList |  |
| 0x58 | SKILL_LIST | SkillList | yes | yes | SkillList |  |
| 0x59 | VEHICLE_INFO | VehicleInfo | yes | yes | VehicleInfo |  |
| 0x5a | VEHICLE_DEPARTURE | VehicleDeparture | yes | yes | VehicleDeparture |  |
| 0x5b | VEHICLE_CHECK_LOCATION | VehicleCheckLocation | partial | yes | VehicleCheckLocation |  |
| 0x5c | GET_ON_VEHICLE | GetOnVehicle | partial | yes | GetOnVehicle |  |
| 0x5d | GET_OFF_VEHICLE | GetOffVehicle | partial | yes | GetOffVehicle |  |
| 0x5e | SEND_TRADE_REQUEST | SendTradeRequest | yes | yes | TradeAsk |  |
| 0x5f | RESTART_RESPONSE | RestartResponse | yes | no | RestartResponse | ok flag, then CharacterRoster |
| 0x60 | MOVE_TO_PAWN | MoveToPawn | partial | yes | MoveState | interact approach is movement, not MoveToPawn |
| 0x61 | VALIDATE_LOCATION | ValidateLocation | yes | yes | PositionCorrection |  |
| 0x62 | BEGIN_ROTATION | StartRotation | no | no |  |  |
| 0x63 | STOP_ROTATION | StopRotation | no | no |  |  |
| 0x64 | SYSTEM_MESSAGE | SystemMessage | yes | yes | SystemMessage |  |
| 0x65 | START_PLEDGE_WAR | StartPledgeWar | yes | yes | PledgeWarList |  |
| 0x67 | STOP_PLEDGE_WAR | StopPledgeWar | yes | yes | PledgeWarList |  |
| 0x69 | SURRENDER_PLEDGE_WAR | SurrenderPledgeWar | yes | yes | PledgeWarList |  |
| 0x6c | PLEDGE_CREST | PledgeCrest | yes | yes | CrestData |  |
| 0x6d | SETUP_GAUGE | SetupGauge | partial | yes | MagicSkillUse | cast time is inside MagicSkillUse |
| 0x6e | SHOW_BOARD | ShowBoard | no | no |  | community board |
| 0x6f | CHOOSE_INVENTORY_ITEM | ChooseInventoryItem | partial | yes | EnchantResult | enchant window is EnchantRequest/Result, not ChooseInventoryItem |
| 0x71 | MOVE_TO_LOCATION_IN_VEHICLE | MoveToLocationInVehicle | partial | yes | MoveToLocationInVehicle |  |
| 0x72 | STOP_MOVE_IN_VEHICLE | StopMoveInVehicle | partial | yes | StopMoveInVehicle |  |
| 0x73 | VALIDATE_LOCATION_IN_VEHICLE | ValidateLocationInVehicle | partial | yes | ValidateLocationInVehicle |  |
| 0x74 | TRADE_UPDATE | TradeUpdate | yes | yes | TradeOfferUpdate |  |
| 0x75 | TRADE_PRESS_OWN_OK | TradeDone | yes | yes | TradeConfirmState |  |
| 0x76 | MAGIC_SKILL_LAUNCHED | MagicSkillLaunched | yes | yes | MagicSkillLaunched |  |
| 0x7c | TRADE_PRESS_OTHER_OK | TradeOtherDone | yes | yes | TradeConfirmState |  |
| 0x7d | ASK_JOIN_FRIEND | FriendAddRequest | yes | yes | FriendInviteAsk |  |
| 0x7e | LEAVE_WORLD | LeaveWorld | partial | yes | EntityDespawn | despawn on disconnect |
| 0x7f | ABNORMAL_STATUS_UPDATE | AbnormalStatusUpdate | yes | yes | AbnormalStatusUpdate |  |
| 0x80 | QUEST_LIST | QuestList | yes | yes | QuestList |  |
| 0x81 | ENCHANT_RESULT | EnchantResult | yes | yes | EnchantResult |  |
| 0x82 | PLEDGE_SHOW_MEMBER_LIST_DELETE_ALL | PledgeShowMemberListDeleteAll | yes | yes | PledgeMemberList |  |
| 0x83 | PLEDGE_INFO | PledgeInfo | yes | yes | PledgeInfo |  |
| 0x86 | RIDE | Ride | yes | yes | Ride |  |
| 0x88 | PLEDGE_SHOW_INFO_UPDATE | PledgeShowInfoUpdate | yes | yes | PledgeStatus |  |
| 0x8a | ACQUIRE_SKILL_LIST | AcquireSkillList | yes | yes | AcquireSkillList |  |
| 0x8b | ACQUIRE_SKILL_INFO | AcquireSkillInfo | yes | yes | AcquireSkillInfo |  |
| 0x8c | SERVER_OBJECT_INFO | ServerObjectInfo | partial | yes | DoorStatus | doors/siege objects; not every static object |
| 0x8e | ACQUIRE_SKILL_DONE | AcquireSkillDone | yes | yes | SkillList |  |
| 0x8f | GM_VIEW_CHARACTER_INFO | GMViewCharacterInfo | partial | no |  | admin HTML, not GMView packets |
| 0x90 | GM_VIEW_PLEDGE_INFO | GMViewPledgeInfo | partial | no |  |  |
| 0x91 | GM_VIEW_SKILL_INFO | GMViewSkillInfo | partial | no |  |  |
| 0x93 | GM_VIEW_QUEST_LIST | GmViewQuestInfo | partial | no |  |  |
| 0x94 | GM_VIEW_ITEM_LIST | GMViewItemList | partial | no |  |  |
| 0x95 | GM_VIEW_WAREHOUSE_WITHDRAW_LIST | GMViewWarehouseWithdrawList | partial | no |  |  |
| 0x96 | PARTY_MATCH_LIST | ListPartyWating | no | no |  |  |
| 0x97 | PARTY_MATCH_DETAIL | PartyMatchDetail | no | no |  |  |
| 0x98 | PLAY_SOUND | PlaySound | yes | yes | PlaySound |  |
| 0x99 | STATIC_OBJECT | StaticObjectInfo | partial | yes | DoorStatus | doors only |
| 0x9a | PRIVATE_STORE_MANAGE_LIST_SELL | PrivateStoreManageListSell | yes | yes | PrivateStoreListSell |  |
| 0x9b | PRIVATE_STORE_LIST_SELL | PrivateStoreListSell | yes | yes | PrivateStoreListSell |  |
| 0x9c | PRIVATE_STORE_MSG_SELL | PrivateStoreMsgSell | yes | yes | PrivateStoreInfo |  |
| 0x9d | SHOW_MINI_MAP | ShowMiniMap | no | no |  |  |
| 0xa0 | TUTORIAL_SHOW_HTML | TutorialShowHtml | yes | yes | TutorialUi |  |
| 0xa1 | TUTORIAL_SHOW_QUESTION_MARK | TutorialShowQuestionMark | yes | yes | TutorialUi |  |
| 0xa2 | TUTORIAL_ENABLE_CLIENT_EVENT | TutorialEnableClientEvent | yes | yes | TutorialUi |  |
| 0xa3 | TUTORIAL_CLOSE_HTML | TutorialCloseHtml | yes | yes | TutorialUi |  |
| 0xa6 | MY_TARGET_SELECTED | MyTargetSelected | yes | yes | TargetSelected |  |
| 0xa7 | PARTY_MEMBER_POSITION | PartyMemberPosition | yes | yes | PartyMemberPosition |  |
| 0xa8 | ASK_JOIN_ALLIANCE | AskJoinAlly | yes | yes | AllyAskJoin |  |
| 0xae | ALLIANCE_CREST | AllyCrest | yes | yes | CrestData |  |
| 0xb0 | PET_STATUS_SHOW | PetStatusShow | yes | yes | SummonInfo |  |
| 0xb1 | PET_INFO | PetInfo | yes | yes | SummonInfo |  |
| 0xb2 | PET_ITEM_LIST | PetItemList | yes | yes | PetInventory |  |
| 0xb3 | PET_INVENTORY_UPDATE | PetInventoryUpdate | yes | yes | PetInventory |  |
| 0xb4 | ALLIANCE_INFO | AllianceInfo | yes | yes | AllyInfo |  |
| 0xb5 | PET_STATUS_UPDATE | PetStatusUpdate | yes | yes | SummonInfo |  |
| 0xb6 | PET_DELETE | PetDelete | yes | yes | SummonGone |  |
| 0xb7 | PRIVATE_STORE_BUY_MANAGE_LIST | PrivateStoreManageListBuy | yes | yes | PrivateStoreListBuy |  |
| 0xb8 | PRIVATE_STORE_BUY_LIST | PrivateStoreListBuy | yes | yes | PrivateStoreListBuy |  |
| 0xb9 | PRIVATE_STORE_BUY_MSG | PrivateStoreMsgBuy | yes | yes | PrivateStoreInfo |  |
| 0xc0 | VEHICLE_START | VehicleStarted | partial | yes | VehicleStarted |  |
| 0xc1 | SKILL_COOL_TIME | SkillCoolTime | partial | no |  | reuse is on SkillList |
| 0xc2 | PACKAGE_TO_LIST | PackageToList | yes | yes | PackageToList |  |
| 0xc3 | PACKAGE_SENDABLE_LIST | PackageSendableList | yes | yes | PackageSendableList |  |
| 0xc4 | EARTHQUAKE | Earthquake | no | no |  | rift schedules the warning; comment says the packet is not sent |
| 0xc7 | SPECIAL_CAMERA | SpecialCamera | no | no |  |  |
| 0xc8 | NORMAL_CAMERA | NormalCamera | no | no |  |  |
| 0xc9 | SIEGE_INFO | SiegeInfo | yes | yes | SiegeInfo |  |
| 0xca | SIEGE_ATTACKER_LIST | SiegeAttackerList | partial | yes | SiegeInfo |  |
| 0xcb | SIEGE_DEFENDER_LIST | SiegeDefenderList | partial | yes | SiegeInfo |  |
| 0xcc | NICK_NAME_CHANGED | NicknameChanged | partial | yes | UserInfo | title rides UserInfo |
| 0xcd | PLEDGE_STATUS_CHANGED | PledgeStatusChanged | yes | yes | PledgeStatus |  |
| 0xce | RELATION_CHANGED | RelationChanged | yes | yes | RelationChanged |  |
| 0xcf | EVENT_TRIGGER | OnEventTrigger | no | no |  |  |
| 0xd0 | MULTI_SELL_LIST | MultiSellList | yes | yes | MultiSellList |  |
| 0xd1 | SET_SUMMON_REMAIN_TIME | SetSummonRemainTime | partial | yes | SummonInfo |  |
| 0xd3 | NET_PING | NetPing | n/a | n/a |  | not retail NetPing |
| 0xd4 | DICE | Dice | no | no |  | RollingDice item handler absent |
| 0xd5 | SNOOP | Snoop | n/a | n/a |  | GM snoop |
| 0xd6 | RECIPE_BOOK_ITEM_LIST | RecipeBookItemList | yes | yes | RecipeBookList |  |
| 0xd7 | RECIPE_ITEM_MAKE_INFO | RecipeItemMakeInfo | yes | yes | RecipeItemMakeInfo |  |
| 0xd8 | RECIPE_SHOP_MANAGE_LIST | RecipeShopManageList | yes | yes | RecipeShopSellList |  |
| 0xd9 | RECIPE_SHOP_SELL_LIST | RecipeShopSellList | yes | yes | RecipeShopSellList |  |
| 0xda | RECIPE_SHOP_ITEM_INFO | RecipeShopItemInfo | yes | yes | RecipeShopItemInfo |  |
| 0xdb | RECIPE_SHOP_MSG | RecipeShopMsg | partial | yes | RecipeShopItemInfo |  |
| 0xdc | SHOW_CALCULATOR | ShowCalculator | no | no |  |  |
| 0xdd | MON_RACE_INFO | MonRaceInfo | yes | yes | MonRaceInfo |  |
| 0xde | SHOW_TOWN_MAP | ShowTownMap | no | no |  |  |
| 0xdf | OBSERVATION_MODE | ObservationEnter | partial | yes | OlympiadMatch | olympiad observe only |
| 0xe0 | OBSERVATION_RETURN | ObservationExit | partial | yes | OlympiadResult |  |
| 0xe1 | CHAIR_SIT | ChairSit | partial | yes | ChangeWaitType | sit pose, not a chair static object |
| 0xe2 | HENNA_EQUIP_LIST | HennaEquipList | partial | no |  | HTML symbol maker |
| 0xe3 | HENNA_ITEM_INFO | HennaItemDrawInfo | partial | no |  |  |
| 0xe4 | HENNA_INFO | HennaInfo | partial | no |  | dyes ride UserInfo |
| 0xe5 | HENNA_REMOVE_LIST | HennaRemoveList | partial | no |  |  |
| 0xe6 | HENNA_ITEM_REMOVE_INFO | HennaItemRemoveInfo | partial | no |  |  |
| 0xe7 | SEND_MACRO_LIST | SendMacroList | no | no |  |  |
| 0xe8 | BUY_LIST_SEED | BuyListSeed | yes | yes | ManorInfo |  |
| 0xe9 | SELL_LIST_PROCURE | SellListProcure | yes | yes | ManorInfo |  |
| 0xea | GM_VIEW_HENNA_INFO | GMHennaInfo | no | no |  |  |
| 0xeb | RADAR_CONTROL | RadarControl | yes | yes | QuestDirection |  |
| 0xec | CLIENT_SET_TIME | ClientSetTime | yes | yes | ClientSetTime |  |
| 0xed | CONFIRM_DLG | ConfirmDlg | partial | no |  | no general confirm dialog |
| 0xee | PARTY_SPELLED | PartySpelled | yes | yes | PartySpelled |  |
| 0xef | SHOP_PREVIEW_LIST | ShopPreviewList | yes | yes |  | Net 0x03E8 (#924) |
| 0xf0 | SHOP_PREVIEW_INFO | ShopPreviewInfo | yes | yes |  | Net 0x03E9 (#924) |
| 0xf1 | CAMERA_MODE | CameraMode | no | no |  |  |
| 0xf2 | SHOW_XMAS_SEAL | ShowXMasSeal | no | no |  | SpecialXMas item |
| 0xf3 | ETC_STATUS_UPDATE | EtcStatusUpdate | yes | yes | EtcStatusUpdate | danger area follows an enabled EffectZone (`showDangerIcon`) |
| 0xf4 | SHORT_BUFF_STATUS_UPDATE | ShortBuffStatusUpdate | partial | yes | AbnormalStatusUpdate |  |
| 0xf5 | SSQ_STATUS | SSQStatus | yes | yes | SevenSignsStatus |  |
| 0xf7 | AGIT_DECO_INFO | AgitDecoInfo | yes | yes | AgitDecoInfo |  |
| 0xf8 | SIGNS_SKY | SignsSky | yes | yes | SevenSignsSky |  |
| 0xf9 | GAME_GUARD_QUERY | GameGuardQuery | n/a | n/a |  | GameGuard |
| 0xfa | FRIEND_LIST | FriendList | yes | yes | FriendList |  |
| 0xfb | L2_FRIEND | L2Friend | yes | yes | FriendList |  |
| 0xfc | FRIEND_STATUS | FriendStatusPacket | yes | yes | FriendUpdate |  |
| 0xfd | FRIEND_RECV_MSG | L2FriendSay | yes | yes | CreatureSay |  |
| 0xfe/0x01 | EX_REGEN_MAX | ExRegenMax | partial | yes | UserInfo | regen is in the stat sheet |
| 0xfe/0x09 | EX_COLOSSEUM_FENCE_INFO | ExColosseumFenceInfo | no | no |  | Fence / coliseum fence |
| 0xfe/0x0e | EX_PARTY_ROOM_MEMBER | ExPartyRoomMember | no | no |  |  |
| 0xfe/0x0f | EX_CLOSE_PARTY_ROOM | ExClosePartyRoom | no | no |  |  |
| 0xfe/0x10 | EX_MANAGE_PARTY_ROOM_MEMBER | ExManagePartyRoomMember | no | no |  |  |
| 0xfe/0x12 | EX_AUTO_SOUL_SHOT | ExAutoSoulShot | yes | yes | AutoSoulShot |  |
| 0xfe/0x13 | EX_FISHING_START | ExFishingStart | yes | yes | FishingEvent |  |
| 0xfe/0x14 | EX_FISHING_END | ExFishingEnd | yes | yes | FishingEvent |  |
| 0xfe/0x15 | EX_FISHING_START_COMBAT | ExFishingStartCombat | yes | yes | FishingEvent |  |
| 0xfe/0x16 | EX_FISHING_HP_REGEN | ExFishingHpRegen | yes | yes | FishingEvent |  |
| 0xfe/0x17 | EX_ENCHANT_SKILL_LIST | ExEnchantSkillList | yes | yes | EnchantSkillList |  |
| 0xfe/0x18 | EX_ENCHANT_SKILL_INFO | ExEnchantSkillInfo | yes | yes | EnchantSkillInfo |  |
| 0xfe/0x19 | EX_QUEST_INFO | ExShowQuestInfo | yes | yes | QuestList |  |
| 0xfe/0x1a | EX_SHOW_QUEST_MARK | ExShowQuestMark | yes | yes | QuestNpcMarks |  |
| 0xfe/0x1b | EX_SEND_MANOR_LIST | ExSendManorList | yes | yes | ManorInfo |  |
| 0xfe/0x1c | EX_SHOW_SEED_INFO | ExShowSeedInfo | yes | yes | ManorInfo |  |
| 0xfe/0x1d | EX_SHOW_CROP_INFO | ExShowCropInfo | yes | yes | ManorInfo |  |
| 0xfe/0x1e | EX_SHOW_MANOR_DEFAULT_INFO | ExShowManorDefaultInfo | yes | yes | ManorInfo |  |
| 0xfe/0x1f | EX_SHOW_SEED_SETTING | ExShowSeedSetting | yes | yes | ManorInfo |  |
| 0xfe/0x20 | EX_SHOW_CROP_SETTING | ExShowCropSetting | yes | yes | ManorInfo |  |
| 0xfe/0x21 | EX_SHOW_SELL_CROP_LIST | ExShowSellCropList | yes | yes | ManorInfo |  |
| 0xfe/0x22 | EX_SHOW_PROCURE_CROP_DETAIL | ExShowProcureCropDetail | yes | yes | ManorInfo |  |
| 0xfe/0x23 | EX_HERO_LIST | ExHeroList | partial | yes | OlympiadStatus | hero sheet, not ExHeroList |
| 0xfe/0x24 | EX_SERVER_PRIMITIVE | ExServerPrimitive | n/a | n/a |  | debug draw primitive |
| 0xfe/0x25 | EX_OPEN_MPCC | ExOpenMPCC | yes | contract | CommandChannelOpen |  |
| 0xfe/0x26 | EX_CLOSE_MPCC | ExCloseMPCC | yes | contract | CommandChannelClose |  |
| 0xfe/0x27 | EX_ASK_JOIN_MPCC | ExAskJoinMPCC | yes | contract | CommandChannelAskJoin |  |
| 0xfe/0x28 | EX_PLEDGE_EMBLEM | ExPledgeEmblem | yes | yes | CrestData |  |
| 0xfe/0x29 | EX_OLYMPIAD_USER_INFO | ExOlympiadUserInfo | yes | yes | OlympiadMatch |  |
| 0xfe/0x2a | EX_OLYMPIAD_SPELLED_INFO | ExOlympiadSpelledInfo | partial | yes | OlympiadMatch | abnormals during a match use AbnormalStatusUpdate |
| 0xfe/0x2b | EX_OLYMPIAD_MODE | ExOlympiadMode | yes | yes | OlympiadMatch |  |
| 0xfe/0x2c | EX_OLYMPIAD_MATCH_END | ExOlympiadMatchEnd | yes | yes | OlympiadResult |  |
| 0xfe/0x2d | EX_MAIL_ARRIVED | ExMailArrived | n/a | n/a |  | player mail is not a Fineage Net target |
| 0xfe/0x2e | EX_STORAGE_MAX_COUNT | ExStorageMaxCount | partial | yes | InventoryUpdate | slot caps are server-side |
| 0xfe/0x30 | EX_MULTI_PARTY_COMMAND_CHANNEL_INFO | ExMultiPartyCommandChannelInfo | yes | contract | CommandChannelInfo |  |
| 0xfe/0x31 | EX_PC_CAFE_POINT_INFO | ExPCCafePointInfo | no | no |  |  |
| 0xfe/0x32 | EX_SET_COMPASS_ZONE_CODE | ExSetCompassZoneCode | no | no |  | zone rules are server-side (`ZoneTypeRegistry`); compass byte is still not sent |
| 0xfe/0x33 | EX_GET_BOSS_RECORD | ExGetBossRecord | yes | yes | BossStatus |  |
| 0xfe/0x34 | EX_ASK_JOIN_PARTY_ROOM | ExAskJoinPartyRoom | no | no |  |  |
| 0xfe/0x35 | EX_LIST_PARTY_MATCHING_WAITING_ROOM | ExListPartyMatchingWaitingRoom | no | no |  |  |
| 0xfe/0x37 | EX_SHOW_ADVENTURER_GUIDE_BOOK | ExShowAdventurerGuideBook | no | no |  | RaidbossInfo 31775/31776 still unavailable HTML |
| 0xfe/0x38 | EX_SHOW_SCREEN_MESSAGE | ExShowScreenMessage | partial | yes | SystemMessage |  |
| 0xfe/0x39 | PLEDGE_SKILL_LIST | PledgeSkillList | partial | yes | SkillList | clan skills are on the skill list |
| 0xfe/0x3a | PLEDGE_SKILL_LIST_ADD | PledgeSkillListAdd | partial | yes | SkillList |  |
| 0xfe/0x3b | PLEDGE_POWER_GRADE_LIST | PledgePowerGradeList | partial | yes | PledgeStatus |  |
| 0xfe/0x3c | PLEDGE_RECEIVE_POWER_INFO | PledgeReceivePowerInfo | partial | yes | PledgeStatus |  |
| 0xfe/0x3d | PLEDGE_RECEIVE_MEMBER_INFO | PledgeReceiveMemberInfo | partial | yes | PledgeMemberList |  |
| 0xfe/0x3e | PLEDGE_RECEIVE_WAR_LIST | PledgeReceiveWarList | yes | yes | PledgeWarList |  |
| 0xfe/0x3f | PLEDGE_RECEIVE_SUB_PLEDGE_CREATED | PledgeReceiveSubPledgeCreated | partial | yes | PledgeStatus | academy / royal guard |
| 0xfe/0x40 | EX_RED_SKY | ExRedSky | yes | yes | SevenSignsSky |  |
| 0xfe/0x43 | SHOW_PC_CAFE_COUPON_SHOW_UI | ShowPCCafeCouponShowUI | no | no |  |  |
| 0xfe/0x44 | EX_SEARCH_ORC | ExCaptureOrc | no | no |  | capture-the-orc event packet |
| 0xfe/0x45 | EX_CURSED_WEAPON_LIST | ExCursedWeaponList | yes | yes | CursedWeaponList |  |
| 0xfe/0x46 | EX_CURSED_WEAPON_LOCATION | ExCursedWeaponLocation | yes | yes | CursedWeaponLocation |  |
| 0xfe/0x47 | EX_RESTART_CLIENT | ExRestartClient | no | no |  |  |
| 0xfe/0x48 | EX_REQUEST_HACK_SHIELD | ExRequestHackShield | n/a | n/a |  | HackShield |
| 0xfe/0x49 | EX_USE_SHARED_GROUP_ITEM | ExUseSharedGroupItem | partial | no |  | shared reuse groups are not a separate packet |
| 0xfe/0x4a | EX_MPCC_SHOW_PARTY_MEMBER_INFO | ExMPCCShowPartyMemberInfo | yes | contract | CommandChannelInfo |  |
| 0xfe/0x4b | EX_DUEL_ASK_START | ExDuelAskStart | yes | yes | DuelEvent |  |
| 0xfe/0x4c | EX_DUEL_READY | ExDuelReady | yes | yes | DuelEvent |  |
| 0xfe/0x4d | EX_DUEL_START | ExDuelStart | yes | yes | DuelEvent |  |
| 0xfe/0x4e | EX_DUEL_END | ExDuelEnd | yes | yes | DuelEvent |  |
| 0xfe/0x4f | EX_DUEL_UPDATE_USER_INFO | ExDuelUpdateUserInfo | yes | yes | DuelUserInfo |  |
| 0xfe/0x50 | EX_SHOW_VARIATION_MAKE_WINDOW | ExShowVariationMakeWindow | yes | yes | AugmentShowMakeWindow |  |
| 0xfe/0x51 | EX_SHOW_VARIATION_CANCEL_WINDOW | ExShowVariationCancelWindow | yes | yes | AugmentShowCancelWindow |  |
| 0xfe/0x52 | EX_PUT_ITEM_RESULT_FOR_VARIATION_MAKE | ExPutItemResultForVariationMake | yes | yes | AugmentPutItemResult |  |
| 0xfe/0x53 | EX_PUT_INTENSIVE_RESULT_FOR_VARIATION_MAKE | ExPutIntensiveResultForVariationMake | yes | yes | AugmentPutRefinerResult |  |
| 0xfe/0x54 | EX_PUT_COMMISSION_RESULT_FOR_VARIATION_MAKE | ExPutCommissionResultForVariationMake | yes | yes | AugmentPutGemResult |  |
| 0xfe/0x55 | EX_VARIATION_RESULT | ExVariationResult | yes | yes | AugmentVariationResult |  |
| 0xfe/0x56 | EX_PUT_ITEM_RESULT_FOR_VARIATION_CANCEL | ExPutItemResultForVariationCancel | yes | yes | AugmentPutCancelItemResult |  |
| 0xfe/0x57 | EX_VARIATION_CANCEL_RESULT | ExVariationCancelResult | yes | yes | AugmentCancelResult |  |
| 0xfe/0x5b | EX_SHOW_SLIDESHOW_KAMAEL | ExShowSlideshowKamael | n/a | n/a |  | Kamael is not an Interlude race |

