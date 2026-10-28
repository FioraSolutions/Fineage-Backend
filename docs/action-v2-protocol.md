# Zone V2 character actions (Backend #141)

Mobius Interlude references: client `RequestActionUse` (opcode 0x56), server fan-out `ChangeWaitType`, `SocialAction`, and movement mode on the actor. Fineage uses MessagePack opcodes instead of binary Interlude packets; action **ids** match the Mobius action table for the MVP set below.

## Opcodes

| Direction | Id | Name | Body |
|-----------|-----|------|------|
| C→S | **51** | `CharacterActionRequest` | `CharacterActionRequestMsg` |
| S→C | **1067** | `ChangeWaitType` | `ChangeWaitTypeMsg` |
| S→C | **1068** | `SocialAction` | `SocialActionMsg` |
| S→C | **1069** | `ChangeMoveType` | `ChangeMoveTypeMsg` |

Skills occupy **50** and **1065–1066** (`docs/skill-v2-protocol.md`). Siege **46–49** / **1064**. **1067–1069** were free before this slice.

## MVP action ids (Mobius table)

| Id | Mobius name | Server behavior |
|----|-------------|-----------------|
| 0 | Sit / Stand | Toggle `Sitting`; fan-out **1067**; doubles regen while seated (`ZoneRegen`) |
| 1 | Walk / Run | Toggle `Running`; fan-out **1069** |
| 2 | Attack | Same as `AttackRequest` **6** (uses selected target when body target is 0) |
| 4 | Next target | Nearest visible attackable NPC ≠ current target → `MyTargetSelected` **1008** |
| 12–14 | Greeting / Victory / Advance | Fan-out **1068** (animation cue only) |

Private store (10, 28, …), mount, pet, and full social catalog are out of scope (#141).

## Validation (Mobius-style)

- Dead actors: `action_dead`.
- Sit while in combat stance (`CombatUntilUnixMs`): `action_blocked`.
- `MoveTo` while sitting: `action_sitting`.
- Unknown or unsupported action id: `action_unknown` / `action_unsupported`.

## Client twin

Fineage **#465** — hotkeys and apply **1067–1069** on local and remote pawns.
