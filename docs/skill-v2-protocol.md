# Zone V2 skill protocol (Backend #137 slice 1)

Mobius Interlude references: `SkillList` on enter, `RequestMagicSkillUse`, fan-out `MagicSkillUse`. Catalog: `data/skills.json`, `data/skill-trees.json` (import: `tools/import_skills.py`).

## Opcodes

| Direction | Id | Name | Body |
|-----------|-----|------|------|
| C→S | **50** | `SkillUseRequest` | `SkillUseRequestMsg` |
| S→C | **1065** | `SkillList` | `SkillListMsg` (after `ItemList` on enter) |
| S→C | **1066** | `MagicSkillUse` | `MagicSkillUseMsg` (AOI + caster) |

Siege occupies **46–49** and **1064** (`docs/siege-v2-protocol.md`). Quest band **42–45** / **1061–1063**. **1067–1069** remain free.

## Enter world order

1. `UserInfo` (1016)  
2. `ItemList` (1014)  
3. **`SkillList` (1065)** — auto-get tree rows for class + level (NPC-tree rows included at level until trainer wire lands)  
4. `PlayerSpawned` (1003)  
5. AOI (`NpcInfo`, `CharInfo`, …)

## SkillUse (MVP)

- Validates known skill, reuse timer, MP (`mpConsume` from Mobius level row), melee range (same as basic attack stub), attackable NPC target.  
- Physical damage skills use Mobius `power` plus a small stub bonus.  
- Errors: `skill_unknown`, `skill_passive`, `skill_reuse`, `skill_mp`, `skill_target`, `skill_range`, `skill_dead`.

## Follow-ups (#137)

- `RequestAcquireSkill` / trainer NPC, persistence, buff effects from skill data (#138), toggle/passive apply.
