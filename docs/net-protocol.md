# Fineage Net (server)

PacketEnvelope login/zone path. Durable accounts use PostgreSQL by default for local Net.

## Ports

| Stack | Login | Zone |
|-------|-------|------|
| Net (`PacketEnvelope`) | 9106 | 9777 |

## Persistence

PostgreSQL is the only account store. There is no file-backed mode: `--store`, `FINEAGE_LOGIN_STORE`,
and `FINEAGE_STORE` are gone, and a JSON file is never read or written at runtime.

| Env | Meaning |
|-----|---------|
| `FINEAGE_DATABASE_URL` | Npgsql connection string (default `Host=127.0.0.1;Port=5432;Database=fineage;Username=fineage;Password=fineage`) |
| `FINEAGE_STATE_DIR` | Directory for world sidecars: `seven-signs.json`, `olympiad.json`, `boss-schedule.json` (default `data`) |

Login and Zone both fail fast at boot with a readable message when PostgreSQL is unreachable.

### Postgres layout (Mobius-shaped)

| Table | Role |
|-------|------|
| `store_snapshot` | Backup / import-export document only (full JSON document) — **not** runtime SoT for characters |
| `accounts` | Credentials |
| `characters` | Identity, appearance, `x/y/z`, vitals, level/exp/sp, clan flags |
| `character_items` | Durable `object_id`, `item_id`, `count`, `enchant_level`, `loc` (`inventory` / `paperdoll` / `warehouse`), `loc_data` (paperdoll slot) |
| `character_quests` + `character_quest_vars` | Quest state |
| `clans` | Payload jsonb: roster + Block C details (reputation, sub-units, rank privileges, wars, member grade / sponsor / apprentice / academy join level, ally, pending leader, clan warehouse rows) |
| `clan_crests` | Crest blobs (`kind` 0 pledge, 1 large, 2 ally) |
| `clan_halls` | Hall owner, lease paid-until, auction end, bids (payload) |
| `store_meta` / sequences | `next_character_id`, `next_clan_id`, `next_item_object_id` |

`PostgresAccountStore.FindById` / `List` read **normalized tables** (then hydrate the in-process mirror for hot writes). Login and Zone are separate processes, and both read the same database.

Hot paths (`TrySavePresence`, vitals, inventory replace) update normalized rows without truncating the whole store. Skills are still derived at zone enter (AutoSkills), not persisted.

Repair:

```bash
docker compose up -d postgres
dotnet run --project src/Fineage.Login -- backfill-normalized
```

## Lobby roster (CharSelect)

`CharacterRoster` / `CreateResult` carry a **Mobius CharSelectionInfo-shaped** summary (same fields as V2 `CharacterSummaryMsg` keys used by the lobby UI):

- identity / appearance / level
- position `x,y,z` (saved or class spawn preview)
- vitals `hp/maxHp`, `mp/maxMp`, `curCp/maxCp` (CP from class template)
- `exp`, `sp`, `karma` (karma 0 until persisted)
- `weaponItemId` + `paperdoll[17]` item ids (not the full bag)
- `deleteSecondsLeft`, `isLastPlayed`

Skills are still derived at zone enter, not listed on CharSelect.

## Character delete / restore (lobby Net)

`DeleteCharacter` `0x0209` and `RestoreCharacter` `0x020B` take the same body — `slot` (`-1` when deleting by id) and `characterId` (`0` when deleting by slot), matching the V2 messages. `LoginService` resolves whichever identifier is supplied.

| Opcode | Dir | Payload |
|--------|-----|---------|
| `DeleteCharacter` `0x0209` | C→S | `slot` int32 (-1 = by id), `characterId` int32 (0 = by slot) |
| `RestoreCharacter` `0x020B` | C→S | same body |
| `DeleteResult` `0x020A` | S→C | `ok` byte, `code`, `message`, `slot`, `characterId` |

`FINEAGE_DELETE_CHAR_AFTER_DAYS` (default `7`) decides hard vs soft delete: `0` removes the record, `> 0` sets a delete timer and the roster keeps the row flagged `pendingDelete` with `deleteSecondsLeft`. Restore clears that timer. On success the server sends `DeleteResult` then a fresh `CharacterRoster`, so the client redraws from server truth instead of patching its list locally.

The char-select client currently treats delete/restore as a no-op on Net (`CharSelectWindow.DeletePressed`, `TODO(#131)`); it can now send these opcodes.

## EnterWorld (zone Net)

After handoff auth, `EnterWorld` runs **one** world join: `ZoneWorld.TryEnterAsync` (same Mobius path as V2 — inventory restore, vitals, AutoSkills, AOI). Net then **projects the live `ZoneEntity`** (no second store read with Human defaults):

| Order | Opcode | Payload |
|-------|--------|---------|
| 1 | `EntitySpawn` `0x0304` | self player: name, classId, **race/sex**, xyz, speeds, **MaxHp + CurHp** (CurHp optional wire tail; absent ⇒ full MaxHp) |
| 2 | `PlayerStats` `0x0305` | level/exp/sp, hp/mp/cp |
| 3 | `ItemList` `0x030A` | bag + paperdoll rows (`objectId`, `itemId`, `count`, `enchant`, `equippedSlot`) |
| 4 | `SkillList` `0x030B` | AutoSkills (`skillId`, `level`, `reuseRemainingMs`) |
| 5 | `ShortCutInit` `0x035A` | saved hotbar: `count` + rows (`type`, `slot`, `id`, `level`) |

(`UserInfo` `0x0357` goes between `PlayerStats` and `ItemList`.)

Client: stash `PlayerInfo` from spawn → load tile → spawn; apply ItemList via `ZoneV2GearSession`, SkillList via existing skill apply services.

## Presence fanout (Net multiplayer)

V2 MessagePack flushes still go to a discarded stream on the Net path. Net peers get PacketEnvelope fanout via `NetPresenceHub`:

| Event | Opcodes |
|-------|---------|
| Enter | Self bootstrap (above). Then `EntitySpawn` for each known **player and NPC** (`IsSelf=false`; NPC `Kind=Npc`, `TemplateId=npcId`, **MaxHp + CurHp** so select `StatusUpdate` is not a false HP drop). Entrant CharInfo-shaped spawn to Net player peers. Every remote `EntitySpawn` (enter, AOI entry, CharInfo re-send) is followed on the same ordered lane by the pawn's live state: `MoveState` (origin = current server position, dest = live destination) if it is moving, `ChangeWaitType` `0x035E` sitting if it is seated (Mobius `sendInfo`). |
| Move | `MoveState` to self + stayed known Net **players** once per leg (new order, geo waypoint, gait toggle) — never per tick; arrival is client-extrapolated. Newly visible player/NPC → `EntitySpawn` (+ state above). Forgotten → `EntityDespawn` `0x030C`. Server position advances at exactly run/walk speed (sub-unit carry), so stop corrections match peer extrapolation. |
| Stop | `StopMove` `0x0307` while moving → `PositionCorrection` to self + zero-length `MoveState` (origin == dest == server stop point) to peers. Idle `StopMove` fans nothing. `ValidatePosition` / refused `MoveInput` only ever correct the sender. |
| Sit / social | `RequestActionUse` `0x0351`: 0 sit/stand → `ChangeWaitType` to self + peers (gait unchanged; sit while running = sit on arrival, cancelled by a new move order; sitting refuses move/attack/cast). 1 walk/run → `ChangeMoveType` `0x0352` (+ re-targeted `MoveState` if mid-run). Panel socials → `SocialAction` `0x035F`, refused (`Reject blocked`) while sitting, moving, casting, attacking, in a private store, or with a trade open/pending. |
| NPC chase (sim) | `MoveState` to Net watchers that already know the NPC (`INetPresenceBridge`). |
| Select / Attack | `SelectTarget` `0x030D` → `TargetSelected` `0x030F`. `AttackRequest` `0x030E` → world combat; `AttackState` `0x0310` + `StatusUpdate` `0x0311` via NetBridge (incl. mob swings / hit timer). Approach uses the same MoveState bridge as player move. |
| Attack audience / hits | Mobius `broadcastPacket`: `AttackState` goes to the attacker, its player target and every player whose knownlist holds the **attacker** (not the target's watchers). Optional tail `int32 count` + rows `int32 damage, byte flags` = further hits of the same swing (dual / fist second hit); absent ⇒ single hit (old decoders ignore it). Flags: `0x10` soulshot, `0x20` crit, `0x40` shield, `0x80` miss. |
| Combat lines | Private `SystemMessage` only, never to watchers (they read the numbers from `AttackState`): attacker — 43 "You have missed." / 44 "Critical hit!" / 2264 "You hit $s1 for $s2 damage." at swing (also after a damage skill lands); victim — 42 "You have avoided $s1's attack." / 111 "Your shield defense has succeeded." / 36 "$s1 hit you for $s2 damage." at hit time (melee, player or mob skills; not DoT). |
| PvP attack | `AttackRequest` `0x030E` = `int32 targetObjectId` + optional **`byte forced`** (Ctrl). Absent (old clients) ⇒ treated as forced (legacy meaning). `forced = 0` on a player that is not auto-attackable — white (no flag, karma 0) or a party mate — is refused silently (ActionFailed, no swing/approach). Flagged / karma players (and anyone when both stand in an arena / active siege) are auto-attackable. `UseSkill.CtrlPressed` plays the same role for offensive skills (damage, debuff, cancel, mana burn). Peace zones refuse playable-vs-playable regardless of Ctrl. AoE debuffs skip white players and party mates other than the picked target. `TargetSelected.colorHint` follows the same auto-attackable rule. |
| PvP flag / karma | No new opcode: nameplates ride on `EntitySpawn` (`karma`, `pvpFlag`) re-sent to watchers as CharInfo and on the owner's `UserInfo` (`karma`, `pvpFlag`, + new tail **`int32 pvpKills`, `int32 pkKills`**; absent ⇒ 0). `pvpFlag`: 0 white, 1 purple, **2 purple blinking** (last 20 s). Any swing (hit or miss) or negative skill on a karma-0 player flags for 120 s (60 s if the target was already flagged; timer overwritten on each act). Hitting a flagged attacker back flags you too; hitting a karma player never flags; healing/buffing a flagged or karma player flags (normal time). Arena / active siege: no flag. Kill of a flagged victim ⇒ `pvpKills+1`; kill of a white victim ⇒ `pkKills+1`, karma += Mobius CT_0 `calculateKarmaGain` (720, 1080, …); karma victim ⇒ nothing. Karma: −exp/30 (÷RateXp) on gained exp; ×¾ on death (<200 ⇒ 0); persisted in `characters.karma / pk_kills / pvp_kills`. |
| PvP death | Exp loss on PvP/PK deaths outside arenas (Mobius `calculateDeathExpPenalty`); arena ⇒ no exp loss, no drops, no counters. Death Penalty buff from a player kill only if the victim has karma. Karma ≥ 1 and `pkKills ≥ 5` ⇒ karma drop rates (incl. equipped). Gatekeepers refuse karma players (`SystemMessage` 210 "Go away, you're not welcome here."); guards auto-aggro karma players in their aggro range. |
| Die / drop / despawn | `EntityDie` `0x0312` (death anim). `DropItem` `0x0313` (loot + known ground items). Corpse decay / pickup → `EntityDespawn` `0x030C`. |
| Pickup | `PickupRequest` `0x0314` → `InventoryUpdate` `0x0315` (add/modify/remove rows) + `EntityDespawn` of ground object. |
| Bag drop / gear | `DropItemRequest` `0x0316` → bag `InventoryUpdate` + world `DropItem`. `UseItem` `0x0317` / `UnequipItem` `0x0318` → `InventoryUpdate` (paperdoll modify). |
| Chat | `Say` `0x0319` (`string text, int32 chatType` + optional `string target` for whisper) → `CreatureSay` `0x031A`. Enter + flood notices → `SystemMessage` `0x031B`. Text ≤ 105 chars; one accepted line per second per session. |
| Chat channels | Mobius CT_0 handlers. **All 0**: visible players within 1250 + speaker. **Shout 1 / Trade 8**: every player in the speaker's map region (`MapRegion.locId`) and instance; trade needs level 20 ("Players can use Trade chat after Lv. 20."); shout also skips players the speaker blocks. **Tell 2**: one online player by name (case-insensitive); receiver gets the speaker's name, sender gets `->Receiver`; offline → 145, blocked / message refusal / receiver chat-banned → 176. **Party 3 / Clan 4**: online members anywhere ("You are not in a party." / "You are not in a clan."). **Alliance 9**: "You are not in an alliance." (no alliances yet). **Hero 17**: heroes only, everyone online, one line per 10 s. **Petition 6/7**: 745 (no petitions yet). **Party rooms 14–16**: ignored (no rooms / command channels yet). GM 5, boat, friend, MSN, announcements from players → "not supported". Every receiver drops lines from players it blocks (block list or message refusal). Chat ban → 147 on All / Shout / Trade / Hero / Tell (party and clan stay open). Server announcements: `CreatureSay` type 10 (critical 18), objectId 0, empty name, to everyone online. |
| Skills | `UseSkill` `0x031C` → world cast; `MagicSkillUse` `0x031D` + `StatusUpdate` (MP / heal HP). Hit damage uses existing vitals fanout. |
| Cast visuals | Same audience as attacks (caster + player target + everyone who sees the caster). `MagicSkillUse` `0x031D` at cast start; `MagicSkillLaunched` `0x03AC` (`int32 casterId, skillId, level, int32 count, int32 targetIds…`) at hitTime when the skill fires (players and NPCs); `MagicSkillCanceled` `0x03AD` (`int32 objectId`) when a cast is aborted (hit-break, move, target gone / escaped / out of sight, stun…). Skill sounds and effects are client-side, keyed by `skillId` (+ level); soulshot / spiritshot glow is `MagicSkillUse` 2039/2150–2154 / 2047/2155–2159 / 2061/2160–2164 to the shooter and its watchers. |
| Sounds | `PlaySound` `0x03AE` (`int32 type` 0 sound / 1 music, `string file`, `int32 objectId` 0 = unbound, `x, y, z`, `int32 delayMs`). Server-driven sounds only: quest jingles (`ItemSound.quest_accept / middle / itemget / finish / giveup`) to the quest owner only (Mobius `QuestState.playSound`). Everything else (swings, hits, skills, footsteps, UI) is played by the client from the packets above. Not yet sent (features not on the server): Siege_Victory, B04_S01 duel, SF_S_01 fishing, S_Race. |
| Revive | `ReviveRequest` `0x031E` (village) → `Revive` `0x031F` + `TeleportToLocation` `0x0320` + vitals; AOI spawn/despawn after relocate. |
| Kill reward | Killer gets `SystemMessage` (id 95), `StatusUpdate`, and `PlayerStats` `0x0305` (exp/sp/level). |
| Buffs | Skill / playtest abnormals → `AbnormalStatusUpdate` `0x0321` (full effect list). |
| Buffs on entering view | A pawn that enters a player's AOI with live abnormals (or a spoil mark) is followed by its full `AbnormalStatusUpdate` right after `EntitySpawn` (Mobius `sendInfo`), so a late arrival sees the same effects as watchers that were there all along. |
| NPC dialog | `NpcTalk` `0x0322` / `BypassRequest` `0x0323` → `NpcHtml` `0x0324` (quest choice HTML reused). EntitySpawn carries `Attackable` for talk vs attack. |
| Shop | Bypass opens `BuyList` `0x0325` / `SellList` `0x0326` / `MultiSellList` `0x0327` + `ShopBalance` `0x0328`. Buy/sell/multisell → `RequestBuyItem` `0x0329` / `RequestSellItem` `0x032A` / `RequestMultiSellChoose` `0x032B` (+ inventory / system lines). |
| Warehouse | Bypass opens `WarehouseDepositList` `0x032C` / `WarehouseWithdrawList` `0x032D`. Confirm → `WarehouseDepositRequest` `0x032E` / `WarehouseWithdrawRequest` `0x032F`. |
| Quests | Journal → `RequestQuestList` `0x0330` / abort → `RequestQuestAbort` `0x0331` → `QuestList` `0x0332`. Quest HTML still via `NpcHtml`. |
| Party | Invite/answer/leave/kick → `PartyInviteRequest` `0x0333` / `PartyAnswerJoinRequest` `0x0334` / `PartyLeaveRequest` `0x0335` / `PartyKickRequest` `0x0336` / `PartyChangeLeaderRequest` `0x03A8` (int32 new leader objectId; leader-only). Fanout `PartyAskJoin` `0x0337` / `PartyJoinResult` `0x0338` / `PartyMemberList` `0x0339`. Roster on join/leave/kick/leader change only; leader is always row 0; rows carry an optional **CP tail** after all rows (`int32 count` + per row `curCp`, `maxCp`; absent ⇒ 0). Disconnect = leave (leadership passes to the next member). |
| Party bars | `PartyMemberUpdate` `0x03A9` (Interlude `PartySmallWindowUpdate`) to the **other** members: `objectId, level, classId, curHp, maxHp, curMp, maxMp, curCp, maxCp` (9 × int32). Coalesced: ≤ 1 per member per 300 ms, unchanged values never resent; first snapshot right after a join. CP is not depleted server-side yet (`curCp = maxCp`, same as `UserInfo`). |
| Party effects | `PartySpelled` `0x03AA` (Interlude `PartySpelled`) — same body as `AbnormalStatusUpdate` (`objectId`, rows `skillId`, `level`, `remainingSeconds`, `slot`) to the other members on apply / remove / expire, and on join (newcomer gets everyone's, others get the newcomer's). |
| Party map | `PartyMemberPosition` `0x03AB` (Interlude `PartyMemberPosition`): `int32 count` + rows `objectId, x, y, z`. Every 3 s, independent of AOI, only rows that moved since the last send, never your own row. |
| Trade | Request/answer/add/done → `TradeRequest` `0x033A` / `TradeAnswerRequest` `0x033B` / `TradeAddItemRequest` `0x033C` / `TradeDoneRequest` `0x033D`. Fanout `TradeAsk` `0x033E` / `TradeRequestResult` `0x033F` / `TradeStart` `0x0340` / `TradeOfferUpdate` `0x0341` / `TradeConfirmState` `0x0342` / `TradeFinished` `0x0343` (+ inventory on exchange). `TradeDoneRequest` response 0 = cancel. Disconnect or death of either side → `TradeFinished` 0 to both, nothing moves; range (150) is re-checked every movement tick, on teleport and on add/confirm — drifting apart closes the window for both. |
| Private store | Sell: `SetPrivateStoreSell` `0x038D` / `PrivateStoreBuy` `0x0390` / `PrivateStoreListSell` `0x0392`. Buy: `SetPrivateStoreBuy` `0x0393` / `PrivateStoreSellTo` `0x0394` / `PrivateStoreListBuy` `0x0395`. Shared: `PrivateStoreQuit` `0x038E` / `PrivateStoreListRequest` `0x038F` / `PrivateStoreInfo` `0x0391`. See `docs/private-store-v2-protocol.md`. |
| Craft | `RecipeBookOpen` `0x0396` / `RecipeMakeSelf` `0x0397`. Fanout `RecipeBookList` `0x0398` / `RecipeItemMakeInfo` `0x0399`. See `docs/craft-v2-protocol.md`. |
| Manufacture | `SetRecipeShop` `0x039A` / `RecipeShopMakeInfo` `0x039B` / `RecipeShopMakeItem` `0x039C`. Fanout `RecipeShopSellList` `0x039D` / `RecipeShopItemInfo` `0x039E`. See `docs/craft-manufacture-v2-protocol.md`. |
| Friends / block | `FriendInvite` `0x039F` … `BlockRequest` `0x03A3`. Fanout `FriendInviteAsk` `0x03A4` / `FriendList` `0x03A5` / `FriendUpdate` `0x03A6` / `BlockList` `0x03A7`. See `docs/friends-v2-protocol.md`. |
| Hotbar (shortcuts) | See **Shortcuts** below. `RequestShortCutReg` `0x0358` → `ShortCutRegister` `0x035B`; `RequestShortCutDel` `0x0359` → `ShortCutDelete` `0x035C`. |
| Leave / disconnect | `EntityDespawn` of leaver to known Net peers (exactly once). |
| Teleport (revive / gatekeeper / SoE / dungeon) | `TeleportToLocation` to self + old peers; AOI diff (`EntitySpawn` / `EntityDespawn`, both directions) fanned once by the world relocate. |

## Clans (Block C, Mobius CT_0 parity)

Lifecycle opcodes `ClanCreateRequest` `0x0368` … `ClanCancelDissolveRequest` `0x036E` are unchanged (see `docs/clan-v2-protocol.md`). Additions, all backward compatible (optional tails; old peers stop early):

| Wire | Shape |
|------|-------|
| `ClanInviteRequest` `0x0369` | + optional `int32 pledgeType` tail: 0 main, −1 academy, 100/200 royal guard, 1001/1002/2001/2002 knights. Absent ⇒ 0. |
| `PledgeStatus` | + tail `int32 reputation, largeCrestId, allyId, string allyName, int32 allyCrestId, castleId, hallId, byte atWar, int32 newLeaderObjectId`, sub-units (`int32 n` × `type, string name, leaderObjectId`), `int32[] rankPrivileges` (grades 1–9), clan skills (`int32 n` × `skillId, level`). |
| `PledgeMemberList` | + tail `int32 n` × `powerGrade, sponsorObjectId, apprenticeObjectId, pledgeClass` (same order as rows). |
| `UserInfo` | + tail `string title, int32 clanCrestId, largeCrestId, allyId, allyCrestId, clanPrivileges, pledgeClass, pledgeType, byte clanLeader`. |
| `EntitySpawn` | + tail `string title, int32 clanId, clanCrestId, allyId, allyCrestId, largeCrestId, pledgeClass`. |
| `WarehouseDepositList` / `WarehouseWithdrawList` | + tail `int32 warehouseType` (1 private, 2 clan). |
| `PledgeCommand` `0x03AF` (C → S) | `int32 kind, targetObjectId, intArg1, intArg2, int64 amount, string name, string text, int32 blobLen, bytes blob` (≤ 8192). Kinds in `PledgeCommandKind`. Errors come back as `Reject` codes + `SystemMessage`. |
| `CrestData` `0x03B0` | `int32 kind, crestId, int32 len, bytes` (empty ⇒ unknown id). |
| `PledgeWarList` `0x03B1` | two lists (declared, under attack): `int32 n` × `clanId, string name, byte mutual`. |
| `RelationChanged` `0x03B2` | `int32 objectId, relation, byte autoAttackable, int32 karma, byte pvpFlag`. Bits: 0x2 flag, 0x4 karma, 0x80 leader, 0x800 ally, 0x8000 mutual war, 0x10000 one-sided war. Sent to both sides on war changes and right after a player's `EntitySpawn` when war/ally bits are set. |
| `PledgeInfo` `0x03B3` | `int32 clanId, string name, int32 crestId, allyId, string allyName, int32 allyCrestId`. |
| `AllyInfo` `0x03B4` / `AllyAskJoin` `0x03B5` | Alliance (Block D). |
| `ClanHallList` `0x03B6` | `int32 n` × `hallId, string name, string location, int32 grade, int64 lease, int32 ownerClanId, string ownerName, int64 auctionEndMs, startingBid, highestBid, int32 bidCount, int64 ownBid`. |

`SystemMessage` id 0 = free text (Mobius `sendMessage`).

| `PledgeCommandKind` | Rules (Mobius) |
|------|-------|
| 1 SetTitle (`targetObjectId` or `name`, `text` ≤ 16) | ASSIGN_TITLE, clan level ≥ 3 (271), online clan member (234 / 145). Persisted; member gets 214; watchers get a fresh `EntitySpawn`. Leaving / kicked members lose the title. |
| 2 SetMemberPowerGrade (`name`, grade 2–8) | MODIFY_RANKS; never the leader or academy (1754). |
| 3 SetRankPrivileges (grade 2–9, mask) / 4 RequestRankPrivileges | Leader only; grade 9 keeps only warehouse + hall/castle doors. Reply = `PledgeStatus` (`rankPrivileges`). |
| 5 ReorganizeMember (`name`, new type, `text` = swap member) | MODIFY_RANKS; target unit must exist; without swap the unit must have room (233); captains and the leader stay in the main clan; academy only by invite. |
| 6 SetSponsor (`name`, `text`, 1 set / 0 clear) | DISMISS_MENTEE (1762); one academy apprentice ↔ one regular sponsor (1755 / 1763). |
| 7 DeclareWar / 8 StopWar / 9 SurrenderWar (`name` = clan) / 10 RequestWarList | WAR privilege; both clans level ≥ 3 with ≥ 15 members (1564); not allied (1569); stop refused while any member is in combat (1677). Mutual war: members are hostile without Ctrl and a war kill is PvP (+1 pvp, no karma), clan reputation ±1 (killer +1 if victim clan > 0; victim −1 if killer clan > 0). Academy members never count. |
| 11 SetCrest (≤ 256 B) / 12 SetLargeCrest (≤ 2176 B), empty blob deletes | CHANGE_CREST, clan level ≥ 3 (272), refused while dissolving (552), size (209). Members re-spawned with the new crest id. |
| 13 RequestCrest (`intArg1` id, `intArg2` kind) / 14 RequestPledgeInfo (`intArg1` clan) / 15 RequestMemberList | Any player (member list: own clan). |
| 30 HallList / 31 HallBid (`intArg1` hall, `amount`) / 32 HallCancelBid | Clan level ≥ 2, HALL_AUCTION, no hall, one auction at a time; adena from the **clan warehouse**; bid ≥ starting bid and > highest (677); raising pays the difference; cancel refunds 90%. Auction lasts 7 days; at the end the highest bidder owns the hall (`hallId` in status / `UserInfo`), losers get 90% back; no bids ⇒ restart. Disbanded clans release the hall. |
| 20–26 Alliance | Block D. |

Village master bypasses (`npc_%id%_<verb>`, pages `data/html/village_master/ClanMaster`): `create_clan`, `increase_clan_level` (Mobius `levelUpClan` SP / adena / Blood Mark / Manifesto / Seal of Aspiration; 6–8 by reputation + members), `dissolve_clan`, `recover_clan`, `change_clan_leader` (applied at next start), `cancel_clan_leader_change`, `create_academy` (lvl 5), `create_royal` (lvl 6, 5000 rep, captain), `create_knight` (lvl 7, 10000 rep, captain), `rename_pledge`, `assign_subpl_leader`, `learn_clan_skills`. Warehouse `DepositC` / `WithdrawC` (clan level ≥ 1, withdraw leader-only, 200 slots).

Academy: invite needs level ≤ 40 and no 2nd class (1734/1735); grade 9; leaving / kicked ⇒ no join penalty (the clan still gets the recruit penalty). On the 2nd class transfer the member graduates: clan +650…190 reputation by join level (1752), member leaves penalty-free (1749) and gets Academy Circlet 8181.

Clan skills: pledge skill tree (`data/pledge-skills.json`), learned at the village master with reputation + items; applied to members whose pledge class reaches the skill's social class; disabled while reputation < 0 (1789 / 1862).

## Shortcuts (hotbar, Mobius ShortCuts)

Row (`ShortCutNetMsg`, 4 × int32 LE): `type`, `slot`, `id`, `level`.

- `slot` = client world slot = `slot + page * 12` (slot 0..11, page 0..9 → 0..119).
- `type`: 1 item (`id` = item **object id**, level 0), 2 skill (`id` = skill id, `level` = known level), 3 action, 4 macro, 5 recipe.

| Opcode | Dir | Payload | Notes |
|--------|-----|---------|-------|
| `RequestShortCutReg` `0x0358` | C→S | row | Overwrites the slot. Item must be in the bag; skill must be known (level is replaced by the known level). |
| `RequestShortCutDel` `0x0359` | C→S | `slot` int32 | Clears the slot. |
| `ShortCutInit` `0x035A` | S→C | `count` int32 + rows | Enter world, after `SkillList`. |
| `ShortCutRegister` `0x035B` | S→C | row | Server truth for one slot. Reply to an accepted reg, **and** to a rejected reg when the slot already held a saved row (client rolls back). |
| `ShortCutDelete` `0x035C` | S→C | `slot` int32 | Slot empty on the server. Reply to del, or to a rejected reg on an empty slot. |

Persistence: `ZoneEntity.Shortcuts` → `IAccountStore.TryReplaceShortcuts` on every reg/del (Mobius writes `character_shortcuts` immediately). Postgres table `character_shortcuts (character_id, slot, page, type, shortcut_id, level)` from migration `003_character_shortcuts.sql`; JSON snapshot key `shortcuts`. On enter, item rows whose object is no longer in the bag are dropped and skill rows take the current known level (Mobius `ShortCuts.restoreMe`).

Client: drag applies locally at once (optimistic), then the server echo (`ShortCutRegister` / `ShortCutDelete`) is applied as truth. `ShortCutInit` is held until the skillbar and the ItemList are applied.

AOI (who is on the knownlist):

| Env | Effect |
|-----|--------|
| `FINEAGE_ZONE_AOI_CELL` (default **3000**) | `GridAoi` with that cell size |
| `FINEAGE_ZONE_AOI_CELL=0` and `FINEAGE_ZONE_INTEREST_RANGE` &gt; 0 | `RangeAoi` |
| `FINEAGE_ZONE_AOI_CELL=0` and no interest range | `SingleCellAoi` (tiny test hosts only) |

How much of the datapack is spawned:

| Env | Effect |
|-----|--------|
| *(default)* | every `enabled` map under `data/spawns/` — the whole Interlude world (~34k NPCs) |
| `FINEAGE_ZONE_INCLUDE_ENABLED_MAPS=0` | single active map only (`ti_cedric` unless overridden) — tests / emergency |
| `FINEAGE_ZONE_MAPS=a,b` | whitelist: only those spawn maps |
| `FINEAGE_ZONE_BOUNDS=x1,y1,x2,y2[;...]` | keep only rows inside those XY boxes |

Hosted Zone defaults to the full enabled datapack with `GridAoi(3000)`, so a player only receives
NPCs around them, not all 34k. Do not pair the whole world with `SingleCellAoi`.

## Scale seams

- `IAccountStore` — Postgres (normalized character domain) now, JSON for tests
- `IHandoffStore` / `MemoryHandoffStore` — HMAC handoff with one-time consume on the Zone process; Redis/Postgres for multi-zone later
- `IAoi` — SingleCell / Range / Grid (`FINEAGE_ZONE_AOI_CELL`)

## Local

```bash
./scripts/run-local-net.sh start   # sole local SoT (see README)
./scripts/run-local-net.sh status
./scripts/run-local-net.sh stop
```
