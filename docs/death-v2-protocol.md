# Zone V2 player death and respawn (Backend #139)

Mobius Interlude references: server `Die`, `Revive`, `TeleportToLocation`; client restart to the nearest village restart point. Player death is **`ZoneEntity.Dead`**, not low `CurHp` (regen still heals wounded living players).

## Opcodes

| Direction | Id | Name | Body |
|-----------|-----|------|------|
| C→S | **52** | `ReviveRequest` | `ReviveRequestMsg` |
| C→S | **53** | `AdminKillPlayer` | `AdminKillPlayerMsg` (empty; admin only) |
| S→C | **1074** | `Die` | `DieMsg` |
| S→C | **1075** | `Revive` | `ReviveMsg` |
| S→C | **1076** | `TeleportToLocation` | `TeleportToLocationMsg` |

Character actions use **51** / **1067–1069** (`docs/action-v2-protocol.md`). Skills **50** / **1065–1066**. Olympiad **60–64** / **1070–1073** (`docs/olympiad-v2-protocol.md`). Boss **1093**, dungeon **1092**.

## Death

1. Server sets `Dead`, `CurHp = 0`, clears target, combat stance, abnormals, enchant window.
2. AOI watchers (and self) receive **`Die` (1074)** and **`StatusUpdate`** with 0 HP.
3. The player object **stays** in the world (no `DeleteObject` on the corpse). Peers still see the dead body until revive or disconnect.

MVP kill path: **`AdminKillPlayer` (53)** when `FINEAGE_ZONE_ADMIN` is set. Mob damage to players is a follow-up.

## Revive (MVP)

1. Client sends **`ReviveRequest`** with `Action = 0` (`DeathProtocol.ReviveToVillage`).
2. Server clears `Dead`, restores HP/MP to max (XP loss stub — no penalty yet).
3. Relocates to **Talking Island village restart** (`TalkingIslandRestart`: Mobius TI town restart, not Cedric hall).
4. Fan-out order: **`Revive`**, **`MoveToAck`** + AOI enter/leave (**`DeleteObject`** / **`CharInfo`** as needed), **`TeleportToLocation`**, then self **`UserInfo`** + vitals **`StatusUpdate`**.

## Errors

| Code | Meaning |
|------|---------|
| `dead` | Action blocked while dead (move, attack, equip, …) |
| `revive_not_dead` | Revive while alive |
| `death_already` | Admin kill when already dead |
| `revive_unsupported` | Non-village restart (castle, clan hall, …) — out of scope |

## Out of scope (#139)

Full restart list, karma/PK, olympiad death, XP/charm loss formulas.

## Client twin

Fineage client death UI and apply path — separate from this backend slice.
