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
1. Stat pipeline leftovers: passive skills (weapon/armor mastery with conditions), runSpd/maxHp/regen/critDmg/pvp buff stats, Net PlayerStats has no combat-stat fields (Net status window not refreshed on buff), enchant bonuses, grade penalty on stats, shield P.Def/block, bow arrows consumption + bow reuse timing (Mobius bow 1500*345/Pspd), dual/pole AoE, class-transfer base stats, weight penalty.
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

## Still open (PvE priority)
1. Physical-skill crit/evasion stat mods (CRITICAL_DAMAGE, BLOW_RATE, P_SKILL_EVASION), weapon-kind skill conditions — small/medium.
2. Player attack/cast LOS check (SysMsg cannot see target) — small.
3. Night game clock (GameTimeTaskManager) for the −10% dark bonus — small.
4. magicLvl in skill-combat.json + mcrit stat mods — small.
5. Level-gap damage penalties, weapon vulnerabilities, NPC soulshots — small/medium.
