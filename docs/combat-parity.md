# Combat parity vs L2J Mobius (Interlude)

## Done
- Melee range 80 (atkRange 40 + collisions), server moveToPawn approach / follow re-path.
- Server-authoritative hitTime: Attack swing broadcast, HP drops at timeAtk/2 (PendingMeleeHit), range re-check at hit.
- Attack reuse from Pspd: `timeAtk = max(100, 500000 / pAtkSpd)` for players (class template) and NPCs (npcs.json `attackSpeed`).
- Physical damage core `70 * pAtk / pDef` (min 1): player template P.Atk vs NPC `defence physical`; NPC `attack physical` vs player template P.Def.
- npcs.json carries Mobius `pAtk`, `pDef`, `attackSpeed`, `attackRange` for all 6519 templates.
- Player stat pipeline (`ZoneWorld.PlayerStats`, also feeds UserInfo): P.Atk = (weapon pAtk | fist) * STR * levelMod; M.Atk = (weapon | base) * INT² * levelMod²; P.Def = (base − naked slot share + armor pDef) * levelMod (fighter 31/18/12/7/8/3/1, mystic chest 15 / legs 8, full armor covers legs); M.Def = (41 − jewel shares + jewel mDef) * MEN * levelMod; Pspd = (weapon | 300) * DEX; accuracy = sqrt(DEX)*6 + level; crit = base*10*DEX; levelMod = (lvl+89)/100.
- Weapon range: reach = weapon `attack_range` + 40 (melee 80, polearm 120, bow 540). items.json carries weapon pAtk/mAtk/pAtkSpd/critRate/attackRange/randomDamage and armor pDef/mDef from Mobius.
- Hit rolls (player and mob melee): calcHitMiss chance (80 + 2·(acc − eva))·10 ‰ clamped 200..980; acc/eva = base + sqrt(DEX)·6 + level (NPC base accuracy/evasion from npcs.json); crit = rate ‰ (player base·10·DEX, NPC critical·10·DEX, cap 500) → damage ×2 + Attack flag 0x20; miss → damage 0, flag 0x80, no HP/StatusUpdate (hate + stance still start); spread × (1 + Rnd[−r, r]/100), r = weapon random_damage / player 10 / NPC random.
- Buff/debuff stat funcs: `data/skill-stat-effects.json` (372 Mobius non-passive skills, per-level mul/add/sub/set on pAtk, pDef, mAtk, mDef, pAtkSpd, mAtkSpd, accCombat, rEvas, critRate) folded from live abnormals into `PlayerStats` and NPC pAtk/pDef/Pspd/acc/eva/crit (set → mul → add). Abnormals store the casting skill level. Stat-changing buff applied/expired → V2 UserInfo refresh to self.
- Soulshots: UseItem on a soulshot (items.json `soulshot`/`shotGrade`) charges the equipped weapon if grades match, consuming weapon `soulshots` (SysMsg 337/338/339/342, MagicSkillUse 2039 glow, InventoryUpdate V2+Net). Next player swing doubles P.Atk, Attack flag 0x10|grade, charge cleared (also on weapon change).
- Shields: player LHand shield (sDef/rShld/−rEvas) and NPC `shieldDef/shieldRate`: calcShldUse rate·DEX (×1.3 vs bow); 5% perfect block → 1 dmg; block → P.Def + sDef; Attack flag 0x40.
- Mob AI: aggro/hate on hit, chase on timed mover, returnHome, retaliation; Die + 7 s corpse; cast hitTime + cast move lock.

## Remaining gaps (next slices)
1. Stat pipeline leftovers: passive masteries beyond the `using` kinds (hp/behind/zone conditions), maxHp/regen/critDmg/pvp buff stats, Net PlayerStats has no combat-stat fields (Net status window not refreshed on buff), enchant bonuses, grade penalty on stats (the `runSpd` part now lands), shield P.Def/block, bow arrows consumption + bow reuse timing (Mobius bow 1500*345/Pspd), dual/pole AoE, class-transfer base stats. (Weight penalty 4270 is done — see slice 9.)
2. calcPhysDam extras still missing: auto-soulshot toggle (needs client RequestAutoSoulShot wire), NPC soulshots, spiritshots/blessed, shield facing angle, position bonus (back/side crit & hit from hitConditionBonus.xml), crit SystemMessages to attacker, level-difference penalties, weapon-type vulnerabilities, crit damage stat mods.
3. NPC attack range per template (`attackRange`, polearm/archer mobs) — still constant 80.
4. Skills: real power formulas (pAtk/mAtk based), reuse/hit/cool times from skills.json, MP consumption parity, skill range per skill; still `power + 10` stub.
5. Magic damage (`calcMagicDam`), M.Atk/M.Def, cast speed from MAtkSpd.
6. Attack stance/AutoAttackStart/Stop packets and client Attack hit flags (crit/miss/shot) on the Net wire.
7. Aggressive mob aggro range, social/faction help, hate list with multiple attackers.
8. PvP/flagging melee (players attacking players).

## Slice 6 — attack-skill damage and timing
- `data/skill-combat.json` (374 Mobius skills): power/mpConsume/mpInitialConsume per level, hit/cool/reuse, castRange.
- Physical skill: 70·(P.Atk·ss + power)/P.Def (shield roll applies); magic: 91·√M.Atk/M.Def·power.
- Hit/cool: magic t·333/Cspd, physical t·300/Pspd; reuse t·333/speed. Initial MP at cast start, MP at launch.
- Skills outside the catalog keep the legacy power+10 stub.
- Gaps: magic crit/fail, spiritshots, skill crit; blow/energy use the physical formula; caster MP StatusUpdate V2-only.

## Slice 7 — passive masteries and per-mob attack range
- `data/skill-passives.json` (166 Mobius operateType P skills, `scripts/gen_skill_passives.py`): stat funcs with optional `using` kinds.
- Known skills' passive funcs fold into the live player stat pipeline with buffs (set → mul → add).
- `using` follows Mobius ConditionUsingItemType: weapon kinds = right-hand type; LIGHT/HEAVY/MAGIC = chest type plus matching legs unless full-body; SHIELD = left-hand shield.
- NPC reach = max(40, template atkRange) + collision allowance (melee 80, archers 1140); chase stop and hit-timer reach use it.
- Gaps: passive funcs with non-`using` conditions (hp, behind, zone…) and non-combat stats (atkCountMax) are skipped; NPC passive skills not applied. (`weightLimit`/`weightPenalty`/`regHp`/`regMp` are collected now — see slice 9.)

## Slice 8 — aggro range, hate list / clan help, attack stance on Net
- `npcs.json` gains Mobius `ai` aggroRange, clanHelpRange, clans, ignoreNpcIds.
- Aggressive mobs (isAggressive and aggroRange > 0) scan every 500 ms and attack the first live player inside aggroRange (3D).
- AggroList: hate += damage·100/(level+7) (min 1; aggro/clan call/miss = 1). The mob attacks the most hated valid attacker; when that one dies or leaves, it switches to the next; empty list → returnHome.
- Clan help: once a second while fighting, idle same-clan mobs within clanHelpRange (minus ignoreNpcIds) join on the same target.
- Net `AutoAttackStart` 0x0353 / `AutoAttackStop` 0x0354 (entity id): players = 15 s combat window, NPCs = has a target. The client reuses the legacy AutoAttackStart/Stop world apply.
- Gaps: no LOS/geo check on aggro, no peace-zone or level-gap rules, no stance re-send to a watcher who comes into view mid-fight, V2 gets no stance packets, mob-vs-mob hate not modelled.

## Slice 9 — auto soulshot, spiritshots / blessed spiritshots
- Net `RequestAutoSoulShot` 0x0355 (item id, enable) → zone; `AutoSoulShot` 0x0356 (item id, on/off) → client, plus SysMsg 1433/1434.
- Enabling charges at once; every discharge (melee swing, physical skill, magic skill) recharges from auto stacks (Mobius rechargeShots). One auto stack per kind (soul vs spirit). An empty or short stack turns auto off.
- `items.json`: weapon `spiritshots`, shot items `spiritshot` / `blessedSpiritshot` (+ shotGrade). Spiritshot ×2, blessed ×4 M.Atk in magic damage (Mobius calcMagicDam); item_skill glow 2047/2061 (+D..S).
- Re-equipping a weapon clears its charges and recharges from auto.
- Client: right-click an item shortcut toggles auto; state is tracked in NetSession (no slot highlight yet).
- Gaps: spiritshot cast-time / MP effects, magic crit, D–S shot items absent from items.json, no auto-shot slot highlight, V2 has no auto request.

## Slice 10 — magic crit / magic failure (PvE)
- Player magic skills vs mobs now follow Mobius calcMagicDam (MagicFailures=True): mcrit rolled first (`calcMCrit`, rate ‰ = (int)(mCritRate fold · WIT bonus [armed only]) · 10, i.e. ~1% base without buffs); then `calcMagicSuccess` rate = 100 − round(1.3^(targetLvl − magicLvl)).
- Failed → second roll succeeds and level gap ≤ 9 → ½ damage + SysMsg 158 "Your attack has failed."; else 1 damage + SysMsg 139 "<mob> has resisted your <skill>.". Success + mcrit → ×3 (×2.5 PvP) · `MAGIC_CRIT_DMG` (`mCritPower`) + SysMsg 1280 "Magic Critical Hit!".
- `skill-combat.json` carries Mobius per-level `magicLevel` for all 375 combat skills (`scripts/gen_skill_magic_levels.py`); missing table still falls back to attacker level.
- Buff/passive `mCritRate` / `mCritPower` fold through `CombatStat.MCritRate` / `MagicCritDmg` (Wild Magic 1303, item Wild Magic, Valakas necklace…).
- Gaps: no MAGIC_SUCCESS_RES / attribute bonus; no magic shield block; Drain 50% message variant; mobs casting at players not rolled; Fineage keeps the simplified WIT·10 ‰ base (Mobius `FuncMAtkCritical` also ·10 inside the calculator before the final ·10 — not ported).

## Slice 11 — hitConditionBonus (position / height)
- Mobius calcHitMiss: chance = (80 + 2·(acc − eva))·10, `chance *= HitConditionBonusData` (Java int truncation), clamp 200..980. Applies to player→mob and mob→player melee.
- Bonus = (100 + high +3 if attacker Δz > 50 / low −3 if < −50 + back +10 / front 0 / side +5) / 100 (hitConditionBonus.xml).
- Back/front use Mobius isBehind / isInFrontOf (60° cone vs target heading). Entity heading is now live: set from the move direction each movement tick and turned to the target on every swing (doAttack), so a mob fighting you faces you (front), while one walking away is hit from behind.
- Gaps: no game clock yet → night −10% not applied (wired as a parameter); rain unsupported in Mobius too; back/side crit bonuses (blow/critical position mods) and skill hit position not applied; V2/Net heading not re-broadcast from these server-side turns.

## Slice 12 — mob aggro / attack line of sight
- Port of Mobius `GeoEngine.canSeeTarget`: 3D grid line (GridLineIterator3D) from the higher end; per cell LOS Z = nearest layer if NSWE passable (anti-corner-cut) else next higher layer; blocked when above eye line + 48 (first 2 cells: start Z + 48); diagonal steps check both orthogonal neighbours; no-geo cells see-through. Closed doors between also block (DoorData.checkIfDoorsBetween).
- Aggressive mobs only take aggro on a player they can see (isAggressiveTowards). In thinkAttack a mob without LOS to its most hated target keeps moving to it instead of swinging.
- Peace zone: Mobius default `AltMobAgroInPeaceZone = True` (and the rule needs PEACE + NO_PVP), so mobs may aggro in towns — no change needed. Level-gap aggro: Mobius Interlude has none in isAggressiveTowards — no change.
- Gaps: no fences; silent move / fake-death / GM no-aggro not modelled; LOS not applied to player attacks/casts yet (Mobius: "Cannot see target"); getNearestZ >1000 lower-layer rule skipped.

## Slice 13 — blow skills (FatalBlow / Backstab / Lethal)
- `scripts/gen_skill_blows.py` adds Mobius blow data to `skill-combat.json` for player blows 16 Mortal Blow, 30 Backstab (new row), 263 Deadly Blow, 321 Blinding Blow, 344 Lethal Blow, 409 Critical Blow: `blowEffect`, `blowChance`, `fullLethal`/`halfLethal`, per-level `magicLevel`.
- calcBlowSuccess: Rnd(100) < blowChance · DEX bonus · (front 1 / side 1.5 / behind 2); Backstab also fails from the front. Failed blow = no damage (MP and shot still spent).
- calcBlowDamage: 77·(power + P.Atk·ss1.458)/P.Def · proximity (behind 1.2 / side 1.1 / front 1); Backstab: 77·(power + P.Atk)/P.Def · ss1.458 · proximity. Shield block/perfect, weapon random spread, skill crit (baseCritRate·10·STR ‰) ×2 + SysMsg 44.
- Lethal (after damage): skipped if magicLvl < mob level − 6 or raid/grand boss; Rnd(1e6)/1e4 < full → HP 1 (SysMsg 1668), < full+half → HP ×0.5 ("Half-Kill!"). Lethal only rolls when the blow lands (Mobius effect-order nuance not verified).
- Gaps: CRITICAL_DAMAGE / BLOW_RATE / P_SKILL_EVASION stat mods, DAGGER weapon condition, NPC blow skills (no blowChance data), trait/attribute bonuses, enchant routes, Mortal Blow MP/reuse in the existing catalog row differ from Mobius XML (kept untouched).

## Slice 14 — player LOS on attack / cast; level-gap damage
- Player melee swing (doAttack) and attack-skill cast (doCast) check the Slice 12 `canSeeTarget` port (geo + closed doors). No LOS → SysMsg 181 "Cannot see target.", attack intention and approach cleared, no swing / no MP or reuse spent. Covers auto-attack ticks and casts fired on arrival.
- Level-gap damage penalty: Mobius CT_0 Interlude `Formulas` has none for physical/magic damage vs mobs (level gap only enters magic failure and Lethal's magicLvl gate, both modelled, plus debuff land rate, not modelled yet) — nothing to port.
- Gaps: buffs/support skills don't check LOS; out-of-reach approach doesn't path around a blocking wall before the check (Mobius moveToPawn uses pathfinding).

## Slice 15 — skill weapon conditions
- `scripts/gen_skill_conditions.py` copies plain Mobius `<conditions><using kind=".."/></conditions>` into `skill-combat.json` `usingKinds` (41 skills: DAGGER blows, BOW, DUALFIST, SWORD/BLUNT Power Strike…; 10 compound and/or/not conditions skipped).
- Attack-skill use checks them with the passive-mastery `Wearing` rules (ConditionUsingItemType); mismatch → SysMsg 113 "<skill> cannot be used due to unsuitable terms.", no cast / MP / reuse.
- Weapon vulnerabilities (calcWeaponTraitBonus): done — `scripts/gen_npc_weapon_traits.py` folds NPC skillList `DefenceTrait` passives into `npcs.json` `defenceTraits`; melee / phys skill / blow multiply by `ZoneCombat.WeaponTraitBonus`.
- Gaps: compound conditions, buff/debuff skill conditions, NPC soulshots.

## Slice 16 — cast ↔ movement (Mobius CreatureAI.thinkCast / PlayerAI / Player.useMagic)
- Bug: casting while running kept the Net timed move (server and client) — you could run and cast. Root cause: the cast path never called `StopTimedMove`, and range was measured from a stale (un-advanced) position.
- Now: every cast (attack / buff / debuff / support) advances the pawn, stops the run (`clientStopMoving`), faces the target and sends watchers a zero-length MoveState; the client stops its click-to-move on MagicSkillUse (`PlayerWorldApply`).
- Move order mid-cast (Net): saved as next intention (`session.QueuedMove`), runs when the cast ends (V2 keeps the old "casting" refuse).
- Skill used mid-cast: queued (`setQueuedSkill`) via PendingCast, fires at `CastingUntilMs`.
- Buff/debuff/support now hold `CastingUntilMs = hitTime` too (move/skill queue and sit block apply); their effect is still instant at cast start (Mobius applies at hitTime) — open.
- Attack mid-cast: already waits and swings after the cast (auto-attack tick). Sit mid-cast → refused; sit while running → sits on arrival (RequestActionUse NextAction EVT_ARRIVED).
- Open: no cast interruption by damage/stun (Interlude has no move-cancels-cast — the client simply can't move); MagicSkillCanceled on target death/out of range; channeling skills; sit blocked by combat stance (stricter than Mobius sitDown); teleport/gatekeeper doesn't clear queued cast/move; buff hitTime-delayed effect.

## Still open (PvE priority)
1. ~~Physical-skill crit/evasion stat mods (CRITICAL_DAMAGE, BLOW_RATE, P_SKILL_EVASION)~~ — done (see below).
2. ~~Night game clock (GameTimeTaskManager) for the −10% dark bonus~~ — done (`ZoneGameTime`, HitConditionBonus night).
3. ~~magicLvl in skill-combat.json + mcrit stat mods~~ — done (slice 10 follow-up).
4. ~~NPC soulshots~~ — stock + chance on melee (`gen_npc_shots.py`); spiritshots / SS glow / boss materialize still open.
5. ~~Weapon vulnerabilities (NPC passive traits)~~ — done (#261).
6. NPC blow/magic skills on players (#259): attack via `gen_npc_ai_skills.py`; HEAL/BUFF/DEBUFF/IMMOBILIZE/silence + clan heal via `gen_npc_ai_support.py`. Sleep/stun/paralyze block move+skills; root blocks move; silence blocks skills. RES / suicide still open.

## Slice — NPC soulshots (Mobius `Npc.rechargeShots`)

- `npcs.json` carries `soulShot` / `soulShotChance` / `spiritShot` / `spiritShotChance` from Mobius `<param name="SoulShot|…">` (~1355 templates).
- On NPC melee, `TryNpcSoulshot`: if stock &gt; 0 and `Rnd(100) &lt; chance`, decrement stock and double P.Atk for that swing (Attack soulshot flag).
- Gaps: no MagicSkillUse 2154 glow fan-out; spiritshots unused until NPC magic cast; raid boss materialize path does not copy shot stock from template.

## Slice — CRITICAL_DAMAGE / BLOW_RATE / P_SKILL_EVASION + IG night

- `CombatStat.CritDmg` / `BlowRate` / `PSkillEvasion` from Mobius `critDmg` / `blowRate` / `pSkillEvas` (buff + passive catalogs regenerated).
- Melee crit and blow skill-crit damage · `CRITICAL_DAMAGE`; blow success folds `BLOW_RATE` over chance·DEX·side.
- Physical attack skills (incl. blows): `calcPhysicalSkillEvasion` before damage; non-blow skills with `baseCritRate` roll STR crit ·2 · CritDmg (Mobius `PhysicalAttack`).
- `ZoneGameTime`: 6 IG days / real day, night while IG hour &lt; 6; wired into `HitConditionBonus(..., night: IsNight)`.

## Spoil / Sweeper (Mobius CT0 parity)

Rules in `data/spoil.json` + `Fineage.Game.Services.SpoilRules`; applied by `ZoneWorldSpoil.cs` through the normal `SkillUse` path.

| Step | Mobius | Fineage |
|---|---|---|
| Spoil 254 | `Spoil` effect: alive monster (109), not spoiled (357), `calcMagicSuccess` with skill magicLevel table 10…72 (resist 139), set spoiler, SysMsg 612, EVT_ATTACKED | Same; hate added to spoiler. Resisted cast still costs MP/reuse; 357/109 cost nothing. Applied at cast start (not after the 1.8 s hitTime). |
| Death | `RollCorpse` spoil list separate from droplist | Unchanged: `data/drops` corpse entries rolled only when spoiled |
| Corpse time | DefaultCorpseTime 7 s + SpoiledCorpseExtendTime 10 s when spoiled | Same (17 s). **Fixed:** spoiled corpses no longer wait forever; unswept spoil decays with the corpse. |
| Sweeper 42 | `ConditionPlayerCanSweep`: spoiled (343), spoiler or looter party (683), not old corpse — ≥2 s before decay (1247), slots/weight | Same; bag full 195 / weight 196 |
| Loot | `takeSweep` → sweeper (party: `distributeItem`) + ConsumeBody | Done (#260): FINDERS_KEEPERS / RANDOM* / BY_TURN* via `PartyLootRules`; spoil respects *INCLUDING_SPOIL; adena split in range. Ground pickup uses the same rules with `spoil=false`. |

Stubs: Spoil Festival 302 / Sweeper Festival 444 (AoE) not wired; spoil success ignores Mobius MEN/level-bonus extras (base `calcMagicSuccess` only). Admin hooks (opcodes 30/31, `FINEAGE_ZONE_ADMIN`) reuse the same core (admin spoil skips the roll and hate).

## Herbs (auto-use on pickup)

Data: `data/herbs.json` (`HerbCatalog`), from Mobius CT0 items 8600–8614 / 8952–8953 (`immediate_effect`, `ItemSkills`) and skills 2278–2285. Handler: `ZoneWorldHerbs.cs`, hooked in `TryPickup` after the drop-protection / range / dead checks.

- Mobius `Player.doPickupItem`: a herb is used on the picker and destroyed — never enters the inventory, no "You have obtained" message.
- Herb of Life 2278 (15/25/100 % HP), Herb of Mana 2279 (11/18/100 % MP): HealPercent capped at max, SysMsg 1066 / 1068, StatusUpdate fan-out. A full-HP player still consumes it (Mobius has no refusal); dead players can't pick up.
- Buff herbs 2280 P.Atk ×1.15, 2281 M.Atk ×1.75, 2282 Atk.Spd ×1.33, 2283 Cast.Spd ×1.3, 2284 Crit ×1.3, 2285 Speed +33 — 300 s abnormals via the shared `CastBuff` path; Warrior 8612 = 2280+2282+2284, Mystic 8613 = 2281+2283, Recovery 8614 = 100 % HP+MP.
- 60 s ground lifetime (HerbAutoDestroyTime) now uses the same catalog (8954 Blue Primeval Crystal is no longer treated as a herb).
- Drops: 20 Elven Starting monsters (e.g. 20336 Green Dryad, 20019 Dryad Elder, 20468–20472 Kaboo orcs, 20534 Red Keltir) carry herb groups in `data/drops`; Elven Village itself has none.
- Stubs: same-abnormalType replacement (e.g. Herb of Power vs Might) follows the existing per-skill abnormal list, not Mobius abnormalType groups. A herb stack with count > 1 applies once per pickup.

## Cast interrupt / cancel at launch

Rules: `Fineage.Game.Combat.CastRules`; Zone: `ZoneWorldCastInterrupt.cs` + `ResolvePendingHit`.

- **Break on hit** (Mobius `Formulas.calcAtkBreak`, `AltGameCancelByHit=Cast`): a player hit by a mob melee swing while a **magic** attack skill is in flight rolls `15 + √(13·dmg) − (MEN bonus·100 − 100)`, clamped 1–99 %. On break: no hit, casting window cleared, queued cast dropped, SysMsg 27 "Your casting has been interrupted.". Physical skills (Power Strike…) are never broken (`breakCast` is magic/static only).
- **Abort at launch** (`onMagicLaunchedTimer`): at hitTime the target must still exist and be alive, be within the skill's **effectRange** (now in `skill-combat.json`, 314 skills via `scripts/gen_skill_effect_range.py`; castRange fallback) and be in line of sight. Otherwise the cast aborts silently: no damage, `mpConsume` not spent (only `mpInitialConsume` was), reuse stays.
- **Teleport** (`Relocate`, used by gatekeepers / dungeons / travel token): aborts the cast and clears queued cast, pickup, auto-attack and approach (Mobius `teleToLocation`).
- Client gap: the Net contract has no `MagicSkillCanceled`, so the client's cast bar/animation runs out on its own; only SysMsg 27 tells the player. Adding the packet needs a client change (deferred).
- Stubs / deferred: stun/sleep/paralyze interrupts (no control abnormals hit players yet; `AbortCast` is the hook), legacy non-`skill-combat` attack skills aren't interruptible, mob skills (none on players yet), full channeling, magicLvl/land-rate polish, buffs on magic crit.

## Buff / debuff / heal / Spoil at hitTime

Mobius `Creature.doCast` → `onMagicLaunchedTimer` → `onMagicHitTimer` → `callSkill`: effects land when the cast completes. Fineage now does the same for player casts of buffs, debuffs, heals/support and Spoil (`ScheduleEffect` / `ResolveDeferredEffect` in `ZoneWorldCastInterrupt.cs`).

- At cast start: target/range checks, MP + reuse, MagicSkillUse fan-out, casting window. Spoil's free pre-checks (109 invalid, 357 already spoiled) stay at start.
- At hitTime: the same launch checks as attack skills — target still alive (player target for friendly skills, NPC for bad ones), within escape range (`effectRange`, else `castRange`; self casts skip range/LoS) and in sight — then the effect (abnormal, HP heal + StatusUpdate, spoil roll + 612/139). A failed check aborts silently (MP already spent at start, as before).
- Interrupts: `skills.json` now carries Mobius `isMagic` as `magicType` (0 physical, 1 magic, 2 static) and `effectRange` (`scripts/gen_skill_cast_meta.py`, 366 / 264 of 622 skills). `breakCast` only interrupts magic/static casts, so Shield/Might/Heal/curses can be broken by a mob hit (SysMsg 27, effect lost) while Spoil (physical) cannot.
- Still immediate: Sweeper 42 (static, 0.5 s — loot on cast start), NPC-cast newbie buffs / blessing, herbs, playtest abnormals.
- Deferred: full channeling model, magicLvl/land-rate polish, buffs on magic crit, client cast-bar cancel packet.
## Movement speed (Mobius `Stat.MOVE_SPEED` / `runSpd`)

Mobius has **one** speed stat: `Stat.MOVE_SPEED("runSpd")` (`Stat.java:99`). Every speed getter folds that same calculator
onto a different base, so a Wind Walk really changes run, walk and swim speed alike:

| Java | Base | Notes |
|------|------|-------|
| `CreatureStat.java:426-434` `getRunSpeed()` | `baseRunSpd` (or the fast-swim base in water) | `baseRunSpd <= 0` → 0, no stat fold |
| `CreatureStat.java:439-447` `getWalkSpeed()` | `baseWalkSpd` (or slow-swim in water) | same `MOVE_SPEED` calculator |
| `CreatureStat.java:452-460` `getSwimRunSpeed()` | `baseMoveSpd/fastSwim` | player templates: 50 |
| `CreatureStat.java:465-473` `getSwimWalkSpeed()` | `baseMoveSpd/slowSwim` | player templates: 50 |
| `CreatureStat.java:487-495` `getMoveSpeed()` | swim pair in water else run/walk, by `isRunning()` | read every tick by `MovementTaskManager` |
| `PlayerStat.java:512-565` | — | `+ Config.RUN_SPD_BOOST`, then `MaxRunSpeed`, then mount penalties |

Evaluation order inside `calcStat(Stat.MOVE_SPEED, base)`:

1. **`FuncMoveSpeed`** (order 1) multiplies the base by `BaseStat.DEX.calcBonus(effector)` — `FuncMoveSpeed.java:34-45`,
   registered for **players and summons only** (`Formulas.java:206,234`). NPC calculator sets have no `MOVE_SPEED` entry
   (`Formulas.getStdNPCCalculators`), so NPC bases are never DEX-scaled.
2. XML stat funcs by `order` (`StatFunction.java:24-31`): `SET` 0 → `DIV`/`MUL` 20 → `ADD`/`SUB` 30.
3. Players only: `+ Config.RUN_SPD_BOOST` (`Config.java:1655`, `Character.ini RunSpeedBoost`, default **0**) then the
   `Config.MAX_RUN_SPEED` cap (`Config.java:1765`, `Character.ini MaxRunSpeed`, default **250**).

There is **no lower clamp**: `MOVE_SPEED` is absent from the `calcStat` "never ≤ 0" switch (`CreatureStat.java:99-125`),
so 0 is a legal speed — which is exactly what Weight Penalty level 4 produces.

Fineage ports this as:

- `data/skill-stat-effects.json` (`scripts/gen_skill_stat_effects.py`) and `data/skill-passives.json`
  (`scripts/gen_skill_passives.py`, `using` kinds kept) carry the `runSpd` stat — 140 non-passive skills (Wind Walk 1204
  +20/+33, Slow 1160 ×0.7, Berserker Spirit 1062 +5/+8, Paralyze 4164 ×0.5, Herb of Speed 2285 +33, Super Haste 7029 ×5…) and
  13 passives (Quick Step 169 +7/+11, Agile Movement 319 +5/+10 LIGHT-only, Clan March 389, Elven Mithril/Blue Wolf/Doom
  Robe/Dark Crystal/Major Arcana set +7, Grade/Weight/Death Penalty ×…). Scalars are expanded per skill level so a
  single-value function applies at every level.
- `ZoneEntity` keeps the template bases (`BaseRunSpeed`, `BaseWalkSpeed`, `BaseSwimRunSpeed`, `BaseSwimWalkSpeed` — the last
  two default to the run/walk base exactly like `CreatureTemplate.java:145-148`) apart from the cached live values
  (`RunSpeed`, `WalkSpeed`, `SwimRunSpeed`, `SwimWalkSpeed`).
- `ZoneWorld.FoldSpeed` runs one `MOVE_SPEED` evaluation: `dexBonus × base` (players) → set → mul → add/sub →
  `+ ZoneSpeedSettings.RunSpeedBoost` (players) → `MaxRunSpeed` cap (players only, so Valakas keeps 300). No floor.
- `ZoneSpeedSettings` exposes the ini knob as `FINEAGE_RUN_SPEED_BOOST` (default 0, negative/unparsable → 0).
- `ZoneWorld.MoveSpeedOf` is Mobius `getMoveSpeed()`: swim run/walk in water by `Running`, else run/walk. The old hardcoded
  `50` for swimmers is gone — swim speed now carries buffs, DEX and penalties.
- Recomputed (and ChangeMoveType fanned out to self + watchers when either gait moved) on: every abnormal change
  (`FanoutAbnormal`), `RefreshKnownSkills` (passives after enter / trainer learn / class transfer), gear equip-unequip
  (`PublishGear`), class change, death (`Abnormals.Clear()`) and water enter/exit. Mid-run the next movement tick already
  uses the new speed — no re-path needed. `ChangeMoveType` carries both speeds, so the client animates walk speed too.
- Wire values are the folded stat (Mobius `UserInfo.java:52-53` rounds `getRunSpeed()/getWalkSpeed()`), which is why
  enter-world UserInfo for Human Fighter now shows 126.5 / 88 instead of the raw template 115 / 80.
- Tests: `ZoneRunSpeedTests` — catalog has runSpd; `FuncMoveSpeed` DEX multiplier on run+walk; Wind Walk raises **both**
  gaits and the ChangeMoveType packet carries both; Slow ×0.7 on run/walk/swim; swim run ≠ swim walk and buffed in water;
  no lower floor (0 is legal, tiny multiplier is not clamped to 1); player 250 cap vs NPC 300 uncapped and not DEX-scaled;
  `RunSpeedBoost` as a flat `+N` before the cap; `FINEAGE_RUN_SPEED_BOOST` parsing; and a 1 s travel check that the buffed
  pawn covers more ground.
- **Weight Penalty 4270 — done** (`Player.java:1970-2015`). See slice 9 below; the absent lower clamp is exactly what
  makes level 4 correct (`runSpd ×0` → 0 stays 0).
- Also open: `Grade Penalty` 4267 is in the catalog but no player has it as a known skill (the grade-penalty slice still
  uses its own `GradePenalty` numbers); `Herb of Speed` abnormalType replacement is still per-skill; V2 clients only get the
  gait flag (`ChangeMoveTypeMsg` has no speeds — the client keeps its own run speed).

## Slice 9 — inventory weight and Weight Penalty 4270

Reference: `ref/mobius-ct0-interlude` — `model/actor/Player.java:1970-2015` (`refreshOverloaded`),
`model/actor/Creature.java:6278-6309` (`getMaxLoad` / `getBonusWeightPenalty` / `getCurrentLoad`),
`data/stats/skills/04200-04299.xml:1329-1346` (skill 4270), `model/itemcontainer/PlayerInventory.java:749-753`
(`refreshWeight` → `_owner.refreshOverloaded()`), `network/clientpackets/EnterWorld.java:208`,
`network/GameClient.java:493`, `network/serverpackets/EtcStatusUpdate.java:32-45`.

### Formula

```
maxLoad      = floor(Stat.CON.calcBonus(creature) * 69000 * Config.ALT_WEIGHT_LIMIT)
               then folded through calcStat(Stat.WEIGHT_LIMIT)          -- Creature.java:6284-6285
bonusPenalty = calcStat(Stat.WEIGHT_PENALTY, 1)                          -- Creature.java:6294
load         = inventory.getTotalWeight()                                -- Creature.java:6306
weightproc   = (load - bonusPenalty) * 1000 / maxLoad                    -- Java int division, truncates
```

| `weightproc` | level | skill 4270 `regHp`/`regMp` | skill 4270 `runSpd` |
|---|---|---|---|
| `< 500` | 0 (no skill) | — | — |
| `500..665` | 1 | ×0.5 | ×1.0 |
| `666..799` | 2 | ×0.5 | ×0.5 |
| `800..999` | 3 | ×0.5 | ×0.5 |
| `>= 1000` | 4 | ×0.5 | ×0.0 |

`setOverloaded(load > maxLoad)` is a separate strict comparison (`Player.java:2004`), and `_dietMode` (admin
"ignore weight penalty", `Player.java:638`) forces level 0 — not modelled, there is no admin system yet.

### Fineage port

- `Fineage.Game.WeightPenalty` — pure maths: `BaseMaxLoad(conBonus, altWeightLimit)`, `Level(load, maxLoad, bonus)`,
  `WeightPercent`, `IsOverloaded`, `SkillId = 4270`, `MaxLevel = 4`. `WarehouseRules.MaxLoad` and `ShopTrade.MaxLoad`
  now delegate here instead of keeping their own `69000` literal, so shop/warehouse guards and the penalty share one limit.
- `ZoneWeightSettings` — `FINEAGE_ALT_WEIGHT_LIMIT` (`Character.ini AltWeightLimit`, `Config.java:1654`, default **1**).
  Parsed with the invariant culture, since the ini always uses a dot.
- `ZoneWorld.MaxLoadOf` = `Stat.WEIGHT_LIMIT` folded over the CON base, so Weight Limit 150 (×2/×3/×4), Quiver of Holding
  418 and the robe-set enchants (×1.3) all move the limit through the normal passive pipeline.
  `ZoneWorld.BonusWeightPenaltyOf` = `Stat.WEIGHT_PENALTY` with default 1 (armor sets 3512/3521/3531/3539/3555/3556 add
  +5759…+5795). `ZoneWorld.CurrentLoadOf` = `Inventory.TotalWeight`.
- **Skill 4270 is folded as a virtual passive source**, not written into `ZoneEntity.KnownSkills`: that dictionary is the
  persisted skill book and `RefreshKnownSkills` rebuilds it from the skill tree on every class change / trainer purchase,
  which would silently drop the injected level. `ZoneWorld.FoldWeightPenalty` applies the same funcs from
  `data/skill-passives.json` into the same `CombatStatMods`, so 4270 composes with buffs, masteries and the DEX factor in
  one `MOVE_SPEED` evaluation (mul 20 then add 30 — Wind Walk's `+20` lands *after* the ×0.5).
- **Recompute points.** Mobius funnels every add/remove/equip/trade through `PlayerInventory.refreshWeight()`
  (`PlayerInventory.java:749-753`); every Fineage inventory write funnels through `ZoneWorld.SaveInventory`, so
  `RefreshWeightPenaltyAfterInventoryChange` lives there: pickup, drop, shop buy/sell, private trade (both sides),
  warehouse deposit/withdraw, enchant, equip/unequip, quest reward, sweep, shot consumption, olympiad grant.
  The one add path that never calls `SaveInventory` is `GrantItemRequest` (the bag is persisted on leave), so
  `TryGrantItem` calls the recompute explicitly — otherwise an authoritative grant across 50 % left the pawn
  unpenalised. `TryEnterAsync` also recomputes after `RefreshKnownSkills` (so Weight Limit / armor-set passives are
  already in `KnownSkills` when the limit and bonus are read) — that is Mobius `EnterWorld.java:208` /
  `GameClient.java:493` running `refreshOverloaded()` before the first `UserInfo`. Nothing is persisted: the level is a
  function of the bag.
- **Level change** → `RefreshRunSpeedAndFanout` (recompute, then `ChangeMoveType` to self + knownlist when a gait moved)
  and `FanoutEtcStatusUpdate` (self only, `Player.java:2011-2012`). Level 1 keeps `runSpd ×1.0`, so it legitimately sends
  no `ChangeMoveType` — only `EtcStatusUpdate`. Levels 4 → 0 free the pawn again.
- **Client packet** `EtcStatusUpdate` (`0x035D`, `NetOpcode`/`NetCodec`/`EtcStatusUpdateNetMsg`): Mobius field order
  (`EtcStatusUpdate.java:39-45`) — entity, charges, weight penalty 0–4, chat-blocked, danger-area, grade-penalty *flag*,
  charm-of-courage, death-penalty. Sent on enter (`NetZoneServer` ahead of `UserInfo`) and on every level change. Fields
  Fineage does not model yet (force charges, chat ban, danger area, charm of courage, death penalty) go out as 0, exactly
  like a clean Mobius character.
- **Regen** — `TickRegen` folds `CombatStat.RegHp`/`RegMp` through `PlayerStatMods`, so 4270 halves the rate. Fineage keeps
  vitals as ints where Mobius keeps doubles, so the fractional remainder is carried per entity (`RegenCarryHp/RegenCarryMp`):
  a halved 1 HP/tick pays out 1 HP every second tick instead of being floored to 0 forever, and a leftover carry is not
  discarded when the penalty clears.
- **Config note:** `AltWeightLimit` is the only weight knob in `Character.ini`; the `Player.ini` slot caps
  (80/100 inventory, 100/120 warehouse) are honoured by `WarehouseRules`/`ShopTrade` and are independent of the load.
- Tests: `WeightPenaltyTests` (48) — thresholds 499→0 / 500→1 / 665→1 / 666→2 / 799→2 / 800→3 / 999→3 / 1000→4, Java
  truncation, bonus subtraction, `maxLoad <= 0`, `IsOverloaded` strictness, CON×69000×AltWeightLimit,
  `FINEAGE_ALT_WEIGHT_LIMIT` parsing; catalog carries the 4270 funcs; speeds ×1/×0.5/×0.5/×0 and regen ×0.5 at every level;
  composition with Wind Walk; NPCs never penalised; live `ZoneWorld` — enter, relog, level-1 vs level-2 fanout shapes,
  recovery, level 4 stops the pawn, give/drop/grant through the bag hooks, `MaxLoadOf` folding 150, `BonusWeightPenaltyOf`
  folding 3512, `EtcStatusUpdate` projection + codec roundtrip, and regen halving with carry.
- **Observation worth knowing:** the shipped starter kit (`initialEquipment.xml:33-45`: 5000 soulshots at weight 4, 50
  Blessed Scrolls of Escape at 120, potions) weighs 46080 against an 88320 elven-fighter limit — **52%**, so a brand-new
  character is already at weight-penalty level 1 and regenerates at half rate. That is genuine Mobius behaviour.
- Still open here: pets (Mobius `Pet.refreshOverloaded`, `Pet.java:1205`) — no summon/pet inventory yet; `_dietMode`;
  `EtcStatusUpdate` charges/danger-area/charm/death-penalty stay 0 until those systems exist.
