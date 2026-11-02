# Combat parity vs L2J Mobius (Interlude)

## Done
- Melee range 80 (atkRange 40 + collisions), server moveToPawn approach / follow re-path.
- Server-authoritative hitTime: Attack swing broadcast, HP drops at timeAtk/2 (PendingMeleeHit), range re-check at hit.
- Attack reuse from Pspd: `timeAtk = max(100, 500000 / pAtkSpd)` for players (class template) and NPCs (npcs.json `attackSpeed`).
- Physical damage core `70 * pAtk / pDef` (min 1): player template P.Atk vs NPC `defence physical`; NPC `attack physical` vs player template P.Def.
- npcs.json carries Mobius `pAtk`, `pDef`, `attackSpeed`, `attackRange` for all 6519 templates.
- Mob AI: aggro/hate on hit, chase on timed mover, returnHome, retaliation; Die + 7 s corpse; cast hitTime + cast move lock.

## Remaining gaps (next slices)
1. Player stat pipeline: STR/DEX/level mod `(lvl+89)/100`, weapon P.Atk/Pspd/random/range (bows, polearms), armor P.Def, buffs. Today player uses level-1 class template values (same as UserInfo).
2. calcPhysDam extras: random variance (weapon/NPC `random`), critical (critRate, x2 + Attack hit flag), miss/evasion (accuracy vs evasion), shield block, soulshots.
3. NPC attack range per template (`attackRange`, polearm/archer mobs) — still constant 80.
4. Skills: real power formulas (pAtk/mAtk based), reuse/hit/cool times from skills.json, MP consumption parity, skill range per skill; still `power + 10` stub.
5. Magic damage (`calcMagicDam`), M.Atk/M.Def, cast speed from MAtkSpd.
6. Attack stance/AutoAttackStart/Stop packets and client Attack hit flags (crit/miss/shot) on the Net wire.
7. Aggressive mob aggro range, social/faction help, hate list with multiple attackers.
8. PvP/flagging melee (players attacking players).
