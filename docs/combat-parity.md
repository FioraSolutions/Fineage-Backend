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
1. Stat pipeline leftovers: passive masteries beyond the `using` kinds (hp/behind/zone conditions), maxHp/regen/critDmg/pvp buff stats, Net PlayerStats has no combat-stat fields (Net status window not refreshed on buff), enchant bonuses, grade penalty on stats (the `runSpd` part now lands), shield P.Def/block, bow arrows consumption + bow reuse timing (Mobius bow 1500*345/Pspd), dual/pole AoE, class-transfer base stats, weight penalty (Weight Penalty 4270 is data-only — weight is not tracked yet).
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
- Gaps: passive funcs with non-`using` conditions (hp, behind, zone…) and non-combat stats (atkCountMax, weight, regen) are skipped; NPC passive skills not applied.

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
- Player magic skills vs mobs now follow Mobius calcMagicDam (MagicFailures=True): mcrit rolled first (`calcMCrit`, rate ‰ = (int)(1 · WIT bonus [armed only]) · 10, i.e. ~1% base); then `calcMagicSuccess` rate = 100 − round(1.3^(targetLvl − magicLvl)).
- Failed → second roll succeeds and level gap ≤ 9 → ½ damage + SysMsg 158 "Your attack has failed."; else 1 damage + SysMsg 139 "<mob> has resisted your <skill>.". Success + mcrit → ×3 (×2.5 PvP) + SysMsg 1280 "Magic Critical Hit!".
- Gaps: skill-combat.json has no `magicLvl` (attacker level used); no MCRITICAL_RATE buff/passive mods (Wild Magic etc.) or MAGIC_CRIT_DMG; no MAGIC_SUCCESS_RES / attribute bonus; no magic shield block; Drain 50% message variant; mobs casting at players not rolled.

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
- Weapon vulnerabilities (calcWeaponTraitBonus): Mobius derives NPC defence traits from NPC passive skills (no direct npcs.xml field) — not ported; needs NPC passive skill → trait data first.
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
1. Physical-skill crit/evasion stat mods (CRITICAL_DAMAGE, BLOW_RATE, P_SKILL_EVASION) — small/medium.
2. Night game clock (GameTimeTaskManager) for the −10% dark bonus — small.
3. magicLvl in skill-combat.json + mcrit stat mods — small.
4. Weapon vulnerabilities (NPC passive traits data), NPC soulshots, NPC blow/magic skills on players — medium.

## Spoil / Sweeper (Mobius CT0 parity)

Rules in `data/spoil.json` + `Fineage.Game.Services.SpoilRules`; applied by `ZoneWorldSpoil.cs` through the normal `SkillUse` path.

| Step | Mobius | Fineage |
|---|---|---|
| Spoil 254 | `Spoil` effect: alive monster (109), not spoiled (357), `calcMagicSuccess` with skill magicLevel table 10…72 (resist 139), set spoiler, SysMsg 612, EVT_ATTACKED | Same; hate added to spoiler. Resisted cast still costs MP/reuse; 357/109 cost nothing. Applied at cast start (not after the 1.8 s hitTime). |
| Death | `RollCorpse` spoil list separate from droplist | Unchanged: `data/drops` corpse entries rolled only when spoiled |
| Corpse time | DefaultCorpseTime 7 s + SpoiledCorpseExtendTime 10 s when spoiled | Same (17 s). **Fixed:** spoiled corpses no longer wait forever; unswept spoil decays with the corpse. |
| Sweeper 42 | `ConditionPlayerCanSweep`: spoiled (343), spoiler or looter party (683), not old corpse — ≥2 s before decay (1247), slots/weight | Same; bag full 195 / weight 196 |
| Loot | `takeSweep` → sweeper (party: `distributeItem`) + ConsumeBody | Items to the sweeper, corpse despawned. **Stub:** party loot distribution mode not applied (party mate who sweeps keeps it). |

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
## Run speed from buffs / passives (Mobius runSpd stat)

Mobius `Creature.getStat().getRunSpeed()` = `baseRunSpeed + Σ runSpd` clamped to `Character.ini MaxRunSpeed` (250). The
`getMoveSpeed()` used by `MovementTaskManager` reads that value every tick, so a Wind Walk cast really makes the pawn travel
faster. Fineage now models it:

- `data/skill-stat-effects.json` (regenerated by `scripts/gen_skill_stat_effects.py`) and `data/skill-passives.json`
  (`scripts/gen_skill_passives.py`, `using` kinds kept) now carry the `runSpd` stat — 140 non-passive skills (Wind Walk 1204
  +20/+33, Slow 1160 ×0.7, Berserker Spirit 1062 +5/+8, Paralyze 4164 ×0.5, Herb of Speed 2285 +33, Super Haste 7029 ×5…) and
  13 passives (Quick Step 169 +7/+11, Agile Movement 319 +5/+10 LIGHT-only, Clan March 389, Elven Mithril/Blue Wolf/Doom
  Robe/Dark Crystal/Major Arcana set +7, Grade/Weight/Death Penalty ×…). Scalars are now expanded per skill level so a
  single-value function applies at every level (`Paralysis`, `BOSS Might`, `Curse of Fairy Mimyu` previously only at level 1).
- `ZoneWorld.RunSpeedStat` folds the funcs (set → mul → add) onto `ZoneEntity.BaseRunSpeed` and clamps to
  `ZoneCombat.MinRunSpeed` 1 / `ZoneCombat.MaxRunSpeed` 250. `ZoneEntity.RunSpeed` is the cached live value, so
  EntitySpawn / MoveState / UserInfo / ChangeMoveType keep carrying the buffed speed. Walk speed has no Mobius stat and is
  never changed.
- Recomputed (and ChangeMoveType fanned out to self + watchers when it moved) on: every abnormal change (`FanoutAbnormal`),
  `RefreshKnownSkills` (passives after enter / trainer learn / class transfer), gear equip-unequip (`PublishGear`), class
  change and death (`Abnormals.Clear()`). Mid-run the next movement tick already uses the new speed — no re-path needed.
- Tests: `ZoneRunSpeedTests` (catalog has runSpd, Wind Walk +20 → live speed and `MoveSpeedOf`, Slow ×0.7, MaxRunSpeed clamp,
  1 u/s floor, and a 1 s travel check that the buffed pawn covers more ground).
- Still open: `Weight Penalty` 4270 needs weight/SpeedPenaltyRate data (weight is not tracked yet), `Grade Penalty` 4267 is in
  the catalog but no player has it as a known skill (the grade-penalty slice still uses its own `GradePenalty` numbers),
  `Herb of Speed` is applied through the same path but abnormalType replacement is still per-skill, `RunSpeedBoost` config,
  and V2 clients only get the gait flag (`ChangeMoveTypeMsg` has no speeds — the client keeps its own run speed).
