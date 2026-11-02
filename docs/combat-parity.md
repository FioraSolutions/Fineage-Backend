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
