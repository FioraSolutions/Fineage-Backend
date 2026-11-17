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
| `castles` + `siege_clans` | Castle owner, tax %, treasury, siege date, payload (door upgrades, schedule); registered siege clans and side (migration 011) |
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
| Servitors | Mobius CT_0 `Summon` effect (data/summons.json from stats/skills): the summon skill is a static cast; at hit time it takes `itemConsumeId × count` (351 when short) and spawns the NPC 20 units from the owner. One summon per player (543). Corpse skills (Reanimated / Corrupted / Cursed Man) need a monster corpse and use it up. Every 5 s: −5 s lifetime (1520 at the end), upkeep item each `consumeItemInterval` (default 240 s; 1029 used / 1143 gone). Orders by `SummonCommand`; it follows the owner (>150 → runs to 70), teleports with the owner, leaves on logout. Its hits and kills count for the owner (damage share, quests, drops); while out the owner's kill exp × `expMultiplier` (solo and party share). Monsters hate and hit the servitor itself. A hit by or on a summon is a hit by / on its owner for the PvP flag; a player kill by a summon is the owner's kill (karma). Dead servitor: `EntityDie`, corpse 7 s, then `SummonGone` reason 3. |
| Pets | Mobius CT_0 `SummonItems` / `Pet` (data/pets.json from Mobius pets XML + collar items): using a collar (`UseItem` on the control item) casts 2046 for the template's summon time (5 s), refused while sitting (31), with a summon (543), in combat (578) or trade / private store (577). The pet is keyed by the collar's object id (`pets` table, migration 014): level, exp, HP / MP, meal meter and a 12-slot bag survive unsummon and relog; the collar's enchant shows the pet level. `SummonInfo` kind 2: `exp*` = pet exp, `feed/maxFeed` = meal meter, `load/maxLoad` = bag weight. Every 10 s it eats `meal_normal` (`meal_battle` in combat); below `hungry_limit` it eats food from its bag (1527, cast 2048); at 0 it ignores orders (1864) and leaves with 30 % per tick (593). Return to collar refused below 40 % meal (594), in battle (579) or dead (589). Kill exp: owner keeps `get_exp_type` %, pet gets the rest (1014), level-up = social 2122. Bag: `SummonCommand` 9 / 10 / 11 (544 / 545 / 546 / 972 refusals), 8 = pick up (1020 / 1021 / 1023). Death: exp penalty (−0.07 × level + 6.5) % of the level span outside arenas, corpse 20 min; Wolf/Pet Resurrection (scroll 6387 / skill 2179) restores part of the lost exp; after 20 min the collar is destroyed (301) and the bag goes to the owner, then `SummonGone` reason 3. Pet gear (weapon / armor / jewels) is not equipped yet. |
| Cubics | Mobius CT_0 `SummonCubic` / `Cubic` / `CubicAction` / `CubicHeal` (data/cubics.json from stats/cubics + skills, tools/import_cubics.py). The summon skill is a static cast that takes its D crystals at hit time (351 when short); mass skills (1328–1330) give the cubic to every party member within range. One cubic per kind (same kind replaces itself); at most 1, or the Cubic Mastery (143) count, the oldest leaves first. Offensive cubics act only while the owner or its summon is in attack stance: first action at once, then every `delay` s with the skill's chance, on the owner's target within 900 (a monster that hates the owner / summon; an Olympiad opponent; a flagged or PvP-zone enemy player while the owner is flagged or in a PvP zone, never party / clan / ally or anyone in a peace zone). Damage `91 × (cubicPower + skillPower) / mDef` credited to the owner (drain heals the owner 40 %); debuffs / Poison / Icy Air / Paralysis / Shock / Hold roll the normal land chance (activateRate, stat, trait, level). Life Cubic heals the most hurt party member (or party summon) within 900 at once and every 13 s when the missing HP is above the heal. After `maxCount` actions in one fight the cubic stops (leaves when `useUp`). Lifetime 900 s (3600 s for the newbie Life Cubic 4338); cubics leave on death and logout. No new opcode: each action is `MagicSkillUse` (caster = owner, target, cubic skill id, hitTime 0) to watchers of owner and target; the list rides on `EntitySpawn` / `UserInfo` tails below. |
| Duels | Mobius CT_0 `DuelManager` / `Duel` / `RequestDuelStart` / `RequestDuelAnswerStart` (reimplemented). `DuelCommand` 1 = /duel or /partyduel on `targetName` (empty ⇒ selected target): no target 1926; requester can't duel 1940; target can't (`canDuel`: battle 2021, HP or MP < 50 % 2019, already duelling 2022, Olympiad 2024, karma / cursed weapon 2023, private store 2017, fishing 2018, peace / PvP / siege zone or instance 2020) → that line with its name; farther than 250 → 2028; busy (pending ask, trade, store, refused asks) → 153. Party duel: only the party leader asks, the target must be in another party (1937), any member unable → 1940 / 1942, the ask goes to the other leader. Target gets `DuelEvent` 1 (ask) + 1938 / 1939, requester 1927 / 1928; the ask lives 15 s. `DuelCommand` 2 answers: 1 accept (1929/1930, party 1933/1934; everyone is checked again), 0 decline (1931 / 1936), −1 decline and refuse further asks this session. Countdown: 3 s, then (party: 1944 and teleport into a private arena instance at −83760, −238825, −3331, teams 300 apart; 20 s wait) 1945 × 3, 1949 and `DuelEvent` 2 + 3 with the team; team circles ride on `EntitySpawn` / `UserInfo` (tail below); each fighter's bars go to the other side as `DuelUserInfo` on start and on every HP / MP change. In a duel: the other side is hostile without Ctrl, hits never flag and never give karma; a lethal hit leaves 1 HP and the fighter is beaten (no death, cannot attack or cast, takes no more damage); fighters cannot target players outside the duel (144). End checks every tick: a whole side beaten → win (losers kneel, social 7; 5 s later HP / MP restored, party back home, 1950 / 1951); `DuelCommand` 3 surrender (whole team) → 1955 / 1956; 120 s (party 300 s) → tie 1952 with restore; 1v1 cancels (1952, no restore) when a fighter logs out, is hit by / hits an outsider (player, monster or another player's summon), gets PvP-flagged, enters a peace / PvP zone or the two are more than 1600 apart. Party duel: logout cancels, a leader leaving the party cancels, a member leaving just drops out (sent home). Cubics of a fighter only act on the other side. `DuelEvent` 4 ends it (`name` = winner, empty on tie / cancel). Not ported: debuff cleanup on end, summons stopping, duel-zone check for 1v1 water / Seven Signs areas. |
| PvP death | Exp loss on PvP/PK deaths outside arenas (Mobius `calculateDeathExpPenalty`); arena ⇒ no exp loss, no drops, no counters. Death Penalty buff from a player kill only if the victim has karma. Karma ≥ 1 and `pkKills ≥ 5` ⇒ karma drop rates (incl. equipped). Gatekeepers refuse karma players (`SystemMessage` 210 "Go away, you're not welcome here."); guards auto-aggro karma players in their aggro range. |
| Die / drop / despawn | `EntityDie` `0x0312` (death anim). `DropItem` `0x0313` (loot + known ground items). Corpse decay / pickup → `EntityDespawn` `0x030C`. |
| Pickup | `PickupRequest` `0x0314` → `InventoryUpdate` `0x0315` (add/modify/remove rows) + `EntityDespawn` of ground object. |
| Bag drop / gear | `DropItemRequest` `0x0316` → bag `InventoryUpdate` + world `DropItem`. `UseItem` `0x0317` / `UnequipItem` `0x0318` → `InventoryUpdate` (paperdoll modify). |
| Chat | `Say` `0x0319` (`string text, int32 chatType` + optional `string target` for whisper) → `CreatureSay` `0x031A`. Enter + flood notices → `SystemMessage` `0x031B`. Text ≤ 105 chars; one accepted line per second per session. |
| Chat channels | Mobius CT_0 handlers. **All 0**: visible players within 1250 + speaker. **Shout 1 / Trade 8**: every player in the speaker's map region (`MapRegion.locId`) and instance; trade needs level 20 ("Players can use Trade chat after Lv. 20."); shout also skips players the speaker blocks. **Tell 2**: one online player by name (case-insensitive); receiver gets the speaker's name, sender gets `->Receiver`; offline → 145, blocked / message refusal / receiver chat-banned → 176. **Party 3 / Clan 4**: online members anywhere ("You are not in a party." / "You are not in a clan."). **Alliance 9**: online members of every clan in the speaker's alliance; no alliance → "You are not in an alliance.". **Hero 17**: heroes only, everyone online, one line per 10 s. **Petition 6/7**: 745 (no petitions yet). **Party rooms 14–16**: ignored (no rooms / command channels yet). GM 5, boat, friend, MSN, announcements from players → "not supported". Every receiver drops lines from players it blocks (block list or message refusal). Chat ban → 147 on All / Shout / Trade / Hero / Tell (party and clan stay open). Server announcements: `CreatureSay` type 10 (critical 18), objectId 0, empty name, to everyone online. |
| Skills | `UseSkill` `0x031C` → world cast; `MagicSkillUse` `0x031D` + `StatusUpdate` (MP / heal HP). Hit damage uses existing vitals fanout. |
| Cast visuals | Same audience as attacks (caster + player target + everyone who sees the caster). `MagicSkillUse` `0x031D` at cast start; `MagicSkillLaunched` `0x03AC` (`int32 casterId, skillId, level, int32 count, int32 targetIds…`) at hitTime when the skill fires (players and NPCs); `MagicSkillCanceled` `0x03AD` (`int32 objectId`) when a cast is aborted (hit-break, move, target gone / escaped / out of sight, stun…). Skill sounds and effects are client-side, keyed by `skillId` (+ level); soulshot / spiritshot glow is `MagicSkillUse` 2039/2150–2154 / 2047/2155–2159 / 2061/2160–2164 to the shooter and its watchers. |
| Sounds | `PlaySound` `0x03AE` (`int32 type` 0 sound / 1 music, `string file`, `int32 objectId` 0 = unbound, `x, y, z`, `int32 delayMs`). Server-driven sounds only: quest jingles (`ItemSound.quest_accept / middle / itemget / finish / giveup`) to the quest owner only (Mobius `QuestState.playSound`). Everything else (swings, hits, skills, footsteps, UI) is played by the client from the packets above. `Siege_Victory` (type 1) to the clan that takes a castle mid-siege (Mobius `Siege.midVictory`). Not yet sent (features not on the server): B04_S01 duel, SF_S_01 fishing, S_Race. |
| Revive | `ReviveRequest` `0x031E` (village) → `Revive` `0x031F` + `TeleportToLocation` `0x0320` + vitals; AOI spawn/despawn after relocate. |
| Kill reward | Killer gets `SystemMessage` (id 95), `StatusUpdate`, and `PlayerStats` `0x0305` (exp/sp/level). |
| Buffs | Skill / playtest abnormals → `AbnormalStatusUpdate` `0x0321` (full effect list). |
| Buffs on entering view | A pawn that enters a player's AOI with live abnormals (or a spoil mark) is followed by its full `AbnormalStatusUpdate` right after `EntitySpawn` (Mobius `sendInfo`), so a late arrival sees the same effects as watchers that were there all along. |
| Enchant glow / announce | `EntitySpawn` gains an optional tail **`int32 weaponEnchant`** after `pledgeClass` (Mobius CharInfo `getEnchantEffect`: right-hand weapon enchant, capped 127; absent ⇒ 0) so peers render the weapon glow; it is re-sent as CharInfo after every enchant change. Mobius milestones (armor/jewelry +6, weapon +7 and +15) send the text line `<name> has successfully enchanted a +N <item>.` (`SystemMessage` id 0) and a Firework `MagicSkillUse` (skill 2024) to the enchanter and every known player. Refusals at confirm send `EnchantResult` cancel plus line 355 (conditions) or 1688 (private store / trade open). |
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
| 20–26 Alliance | 20 invite (`TargetObjectId`, alliance leader → clan leader) → `AllyAskJoin`; 21 answer (`IntArg1` 1/0, conditions re-checked); 22 leave (clan leader, not the ally leader); 23 expel (`Name` = clan); 24 dissolve (ally leader); 25 crest (`Blob` ≤ 192 bytes, empty deletes); 26 info → `AllyInfo`. See *Alliances*. |

Village master bypasses (`npc_%id%_<verb>`, pages `data/html/village_master/ClanMaster`): `create_clan`, `increase_clan_level` (Mobius `levelUpClan` SP / adena / Blood Mark / Manifesto / Seal of Aspiration; 6–8 by reputation + members), `dissolve_clan`, `recover_clan`, `change_clan_leader` (applied at next start), `cancel_clan_leader_change`, `create_academy` (lvl 5), `create_royal` (lvl 6, 5000 rep, captain), `create_knight` (lvl 7, 10000 rep, captain), `rename_pledge`, `assign_subpl_leader`, `learn_clan_skills`. Warehouse `DepositC` / `WithdrawC` (clan level ≥ 1, withdraw leader-only, 200 slots).

Academy: invite needs level ≤ 40 and no 2nd class (1734/1735); grade 9; leaving / kicked ⇒ no join penalty (the clan still gets the recruit penalty). On the 2nd class transfer the member graduates: clan +650…190 reputation by join level (1752), member leaves penalty-free (1749) and gets Academy Circlet 8181.

Clan skills: pledge skill tree (`data/pledge-skills.json`), learned at the village master with reputation + items; applied to members whose pledge class reaches the skill's social class; disabled while reputation < 0 (1789 / 1862).

## Alliances (Block D, Mobius CT_0 parity)

| Rule | Behaviour |
|------|-----------|
| Create | Village master `create_ally <name>`: clan leader (504), clan level ≥ 5 (549), not allied (502), not dissolving (550), no 10-day post-dissolve penalty (505); name alphanumeric (506), 2–16 chars (507), unique (508). Alliance id = leader clan id. |
| Join | Alliance leader only (464); target online clan leader (152/234/9), not allied (691), not at war either way (469), not both in siege (723), ≤ 3 clans (466). Leave penalty 1 day (761), expelled 1 day (468), leader after expelling cannot accept for 1 day (467). |
| Leave / expel / dissolve | Leave 470/471/519; expel 474/473/521; dissolve (`dissolve_ally` or kind 24) clears every member, 523 to all, no new alliance for 10 days; refused during a siege (722). An allied clan cannot be dissolved (554, master and direct request). |
| Crest / info | 8x12 crest ≤ 192 bytes (476), kind 2 in `clan_crests`, id on every member clan and in CharInfo. `AllyInfo` lists clan id, name, level, leader, members, online, crest. |
| Relations / chat | `RelationAlly` (0x800) between members of different allied clans; `$` chat (type 9) to all allied online players. |
| Persistence | `AllyId`, `AllyName`, `AllyCrestId`, `AllyPenaltyExpiry`, `AllyPenaltyType` in the clan payload. |

## Castles and sieges (Block E1, Mobius CT_0 parity)

Data: `data/castles.json` (written by `tools/import_castles.py` from Mobius Siege.ini, Doors.xml, castle zones and `castle_siege_guards` with isHired = 0): the 9 Interlude castles, artifact npc ids, control / flame towers, gates and walls (HP, defence, wall flag), HQ zones, owner restart points, NPC garrison.

| Opcode | Payload |
|--------|---------|
| `DoorStatus` `0x03B7` (S → C) | `int32 objectId, doorId, bool open, int32 curHp, maxHp, bool attackable, bool wall, int32 castleId, upgradeRatio`. Comes after the gate's `EntitySpawn` (NpcType `Door`, npcId 0, name = door name) and again on every HP / open change. A broken gate stays dead and open. |
| `CastleCommand` `0x03B8` (C → S) | `int32 kind, castleId, intArg1, intArg2, int64 amount`. Kinds (`CastleCommandKind`): 0 info, 1 set tax (`intArg1` %), 2 withdraw, 3 deposit (`amount`), 4 upgrade door (`intArg1` door id, `intArg2` ratio 2/3/5), 5 take Lord's Crown, 6 take circlet, 7 open / close gate (`intArg1` door, `intArg2` 1 open). `castleId` 0 = the caller's own castle. Errors: `Reject` codes `siege_*`. |
| `CastleInfo` `0x03B9` (S → C) | `int32 castleId, string name, int32 ownerClanId, string ownerName, int32 taxPercent, maxTaxPercent, int64 treasury, int64 siegeUnixMs, bool inProgress, int32 n` × `doorId, upgradeType (1 outer, 2 inner, 3 wall), upgradeRatio, curHp, maxHp, bool open`. Treasury only for the leader / CASTLE_VAULT, doors only for the owner clan. |
| `ManorCommand` `0x03BA` (C → S) | `int32 kind, castleId, npcObjectId, bool next, int32 n` × `itemId, objectId, manorId, int64 count, price, int32 rewardType`. Kinds (`ManorCommandKind`): 0 seed info, 1 crop info, 2 default info, 3 buy list, 4 buy seeds (`itemId`, `count`), 5 sell list, 6 sell crops (`objectId`, `itemId`, `manorId` = buying castle, `count`), 7 set next seeds (`itemId`, `count`, `price`), 8 set next crops (+ `rewardType` 1/2), 9 seed setting page, 10 crop setting page. Shop kinds need a manor manager within 250 (`npcObjectId`); setting kinds need the owner clan leader or CS_MANOR_ADMIN near the chamberlain, only while the manor is modifiable. Errors: `Reject` codes `manor_*`. |
| `ManorInfo` `0x03BB` (S → C) | `int32 view (= kind), castleId, bool next, int32 mode (0 off, 1 modifiable, 2 maintenance, 3 approved), int64 nextChangeUnixMs, treasury, manorCost, adena, int32 n` × seed `seedId, cropId, level, reward1, reward2, bool alternative, int64 limit, referencePrice, minPrice, maxPrice, amount, startAmount, price, currentStartAmount, currentPrice`, `int32 m` × crop `cropId, seedId, level, reward1, reward2, int64 limit, referencePrice, minPrice, maxPrice, int32 manorId, int64 amount, startAmount, price, int32 rewardType, inventoryObjectId, int64 inventoryCount, currentStartAmount, currentPrice, int32 currentRewardType`. Treasury and cost only on setting pages. |
| `FishingEvent` `0x03BC` (S → C) | `byte kind (0 start, 1 combat, 2 hp, 3 end), int32 fisherObjectId, fishGroup (−1 = nothing bites), x, y, z (bait), bool nightLure, int32 time (s left), hp (combat: max hp), byte mode (0 resting → pump, 1 fighting → reel), lureType (0 beginner, 1 normal, 2 upper), deceptive (1 = mode reversed), goodUse (0 none, 1 good, 2 bad), anim (0, 1 pump, 2 reel), int32 penalty, bool win`. Start / combat / pulls / end reach the fisher and watchers; plain HP ticks only the fisher. Fishing (1312), Pumping (1313), Reeling (1314) are normal `SkillUse`; the guild window is `AcquireSkillList` type 1 (`FishSkillList` bypass), learned with `AcquireSkill` type 1. While fishing the fisher cannot move, drop or use items except fishing shots (6535–6540). |
| `EntitySpawn` summon tail | after `noble, hero`: optional **`byte summonKind` (0 none, 1 servitor, 2 pet), `int32 ownerObjectId`** (absent ⇒ none). A summon is `Kind = Npc` with `templateId` = its NPC, `title` = the owner's name (Mobius SummonInfo / PetInfo), `attackable = false` (another player attacks it with Ctrl; colour follows the owner's PvP state). |
| `EntitySpawn` / `UserInfo` cubic tail | after the summon tail (`EntitySpawn`) / after `classIndex` (`UserInfo`): optional **`int32[] cubics`** (uint16 count + ids; 1 Storm, 2 Vampiric, 3 Life, 4 Viper, 5 Phantom, 6 Binding, 7 Aqua, 8 Spark, 9 Attractive). Absent ⇒ none. Re-sent (UserInfo to the owner, CharInfo to peers) whenever the list changes. |
| `EntitySpawn` / `UserInfo` duel team tail | after the cubic list: optional **`byte team`** (0 none, 1 blue = challengers, 2 red; Mobius setTeam). Absent ⇒ 0. Re-sent when a duel starts and ends. |
| `SummonCommand` `0x03BD` (C → S) | `byte kind, int32 targetObjectId, skillId, x, y, z, bool forced` + optional tail `int32 itemObjectId, count`. Kinds (`SummonCommandKind`): 1 follow, 2 stay, 3 attack (`targetObjectId`, 0 = owner's target; `forced` = Ctrl), 4 stop, 5 move to x/y/z, 6 use skill (`skillId` from `SummonInfo.skillIds` on the target), 7 dismiss, 8 pick up, 9 give item to pet, 10 take item from pet, 11 pet uses item (8–11 pets only). Refused orders answer with `SystemMessage` 652 (wrong target), 1724 (dismiss in battle); no `Reject`. |
| `SummonInfo` `0x03BE` (S → owner) | `int32 objectId, byte kind, int32 npcId, string name, int32 level, hp, maxHp, mp, maxMp, int64 exp, expLevelStart, expLevelEnd, int32 feed, maxFeed, pAtk, pDef, mAtk, mDef, accuracy, evasion, critical, pAtkSpd, castSpd, runSpeed, bool following, int32 targetObjectId, load, maxLoad, int32[] skillIds, bool dead`. Servitor: `feed/maxFeed` = lifetime left / total in seconds (Mobius SetSummonRemainTime), exp 0. Sent on spawn, every 5 s life tick, after orders and on each hit taken. |
| `SummonGone` `0x03BF` (S → owner) | `int32 objectId, byte kind, int32 reason` (0 dismissed, 1 lifetime over → SysMsg 1520, 2 upkeep missing → 1143, 3 corpse decayed, 4 owner left). Watchers get the normal `EntityDespawn`. |
| `PetInventory` `0x03C0` (S → owner) | `int32 petObjectId, int32 n` × `objectId, itemId, count, enchant, bool equipped` (Mobius PetItemList). Sent to the owner on pet spawn and after every bag change (give / take / pick up / eat). |
| `DuelCommand` `0x03C1` (C → S) | `byte kind, string targetName, bool party, int32 response`. Kinds (`DuelCommandKind`): 1 request (/duel, /partyduel; `targetName` empty ⇒ selected target), 2 answer the pending ask (`response` 1 accept / 0 decline / −1 decline + refuse asks), 3 surrender. Refusals are `SystemMessage` lines only; no `Reject`. |
| `DuelEvent` `0x03C2` (S → C) | `byte kind, bool party, string name, byte team` (`DuelEventKind`): 1 ask (`name` = requester / its leader), 2 ready, 3 start (`team` = yours, 1 blue / 2 red), 4 end (`name` = winner, empty on tie or cancel). Mobius ExDuelAskStart / ExDuelReady / ExDuelStart / ExDuelEnd. |
| `DuelUserInfo` `0x03C3` (S → C) | `string name, int32 objectId, classId, level, hp, maxHp, mp, maxMp, cp, maxCp` (Mobius ExDuelUpdateUserInfo): an opponent's bars, to the other side only, on duel start and each HP / MP change. CP = max CP (no CP pool yet). |
| `EntityDie` tail | optional **`byte restartFlags`**: bit0 castle, bit1 siege HQ (absent ⇒ 0). `ReviveRequest` action 3 = to castle, 4 = to siege HQ (`revive_no_castle` / `revive_no_hq`). |

| Rule | Behaviour |
|------|-----------|
| Start | At the siege time (tick) or admin start. No attackers ⇒ cancelled (148 NPC castle, 295 owned) and the next date is picked. Otherwise everybody but the owner clan leaves the siege zone, 146 + time left, gates respawn closed at full HP, 3 control + 2 flame towers spawn, the NPC garrison spawns for an NPC-owned castle only. |
| Countdown / end | 358 hours while > 1 h, 359 at 60 / 10 / 5 min, 360 at 10 s. End: 147, then 291 (clan won) or 856 (draw), reputation, flags removed, non-owners teleported, towers and guards removed, gates respawn. |
| Zone | Siege zone = PvP while the siege runs (no flag, no karma; exp is lost as Mobius). Siege relation bits on `RelationChanged`: 0x200 in siege, 0x400 attacker, 0x800 same side, 0x1000 enemy side. |
| Seal of Ruler (246) | Leader of an attacker clan, clan level ≥ 4, target = this castle's artifact, ≤ 200 range, not below it and ≤ 45 Z apart (else 748), 3 min static cast; 287 to defenders. Landing ⇒ ownership change (149 with clan + castle), `Siege_Victory` to the new owner, defenders ↔ attackers swap (alliance attackers join the defence), attackers back to their HQ or town, gates back at 50% HP, towers respawn. An NPC castle whose attackers are all in the winner's alliance ends at once. |
| Headquarters (247) | Leader of an attacker clan, inside an HQ zone (else 290), 1 per clan (else 113), 300 × item 2131, 30 min reuse. Flag (npc 35062) can be hit by anyone but its clan; 286 to the clan when hit (30 s throttle). |
| Gates / walls | Attackable during a siege by anyone but the owner clan; walls only take siege-weapon damage (players cannot break them). A broken gate opens the geo door and sends 288. |
| Towers / guards | Control towers: registered attackers only. Each guard registers to the closest control tower; dead guards respawn while it stands. Guards scan every 3 s and attack any non-defender in aggro range; no random walk. |
| Reputation | Lost castle: former owner −3000 (1772), new owner + min(1500, former's reputation); retaken by the former owner +750; NPC castle +1500 (1773). Circlets / crown are removed from the losing clan's online members. |
| Chamberlain | Owner clan only, near the chamberlain when the world spawned one. Tax 0–15% (leader or CASTLE_VAULT, not during a siege). Deposit / withdraw without tax. Door upgrade ratio 2/3/5 (CASTLE_MANAGE_FUNCTIONS, not during a siege): outer 3M/4M/5M, inner 750k/900k/1M, wall 1.6M/1.8M/2M from the caller's adena. Lord's Crown (6841) for the leader, castle circlet for members, one each. Gates open / close (CASTLE_OPEN_DOOR). |
| Taxes | Buy: castle share (price × tax%) to the merchant's castle treasury; multisell: entry tax × amount. Goddard / Schuttgart pay Rune's rate to Rune, the other castles pay Aden's rate to Aden, when that castle has an owner. |
| Message ids | Registration replies 140–144 and the five pre-castle announcements 145–149 keep their legacy ids; everything newer uses Mobius `SystemMessageId` ids (Mobius equivalents of 145–149: 293, 711, 712, 846, 285). |

Not yet: mercenaries, flame tower traps, siege golems (walls), castle functions / residential skills, noble / castle siege skills (326, 844, 845), circlet removal for offline members, manor (Block E2).

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
