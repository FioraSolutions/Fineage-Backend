# Zone cross-player consistency & stability review — 2026-11-22

## Scope

| | |
|---|---|
| **Fineage** | Branch `dev`, commit `7f8d3ce5a4a4e53b693f8685e24af4601052b3b2` |
| **Focus** | Login TCP/HTTP + **Zone** process: what different players see for the same world event, tick ordering, reconnect, persistence, session isolation, lifecycle leaks |
| **Method** | Read-only code review of AOI fan-out, simulation stages, enter/leave, ledger persist, and net bridge (no production fixes in this PR) |

Architecture rules for proposed fixes:

- No one-off `if (specialCase)` branches; use **shared pipelines** with per-entity-kind / per-race config tables.
- **No silent fallbacks** — log concrete missing data (skill id, object id, catalog row) and refuse or metric.

---

## Executive summary

The Zone couples authoritative state in `ZoneWorld` to clients through a **deferred fan-out sequencer** (`OutboxingWorldEventPublisher` → `WorldFanoutSequencer` → `NetWorldEventPublisher`). Most gameplay paths use `NetWatchersOf` / `ActorAudience`, but **ground drops**, **NPC death**, and **boats** still pick audiences ad hoc. That produces observer gaps that do not affect the acting player.

**Highest risk (P0):** same-session re-enter without persist (`capturePersist: false`), ledger flush failures that still allow inventory snapshot flush, and command exceptions after partial mutation.

**Highest visibility (P1):** knownlist diff frames prepared before same-tick despawn, AOI refresh without `MoveState`, party adena pickup ordering, and inconsistent die/drop audiences.

---

## Findings (ranked)

### P0 — economy / authority

#### P0-1 Same-session re-enter drops in-flight state and can leave stale deferred inventory

| | |
|---|---|
| **Location** | `src/Fineage.Zone/World/ZoneWorld.More.Enter.cs:142-148`, `ZoneWorld.Members.Persist.cs` (`_dirtyInventories`), `ZoneWorld.Members.cs` (`TryDetachPlayer`) |
| **Scenario** | Client sends a second EnterWorld while `session.InWorld` is true. World detaches with `capturePersist: false`, discarding vitals/inventory edits since the last flush. `_dirtyInventories[objectId]` may still reference the detached bag when `FlushDirtyInventories` runs after a new entity attached. Observers and DB can disagree with the live session. |
| **Fix** | Introduce `PlayerLeavePipeline` (leave, kick, re-enter): (1) flush or explicitly cancel deferred dirty rows for `objectId`; (2) detach; (3) rebuild from `CharacterRecord`. Re-enter either requires successful flush before detach or `capturePersist: true` with snapshot-driven rebuild. |
| **Acceptance** | Test: mutate inventory, re-EnterWorld without logout; DB and in-world bag match; no `_dirtyInventories` entry after detach; metric on forced flush at re-enter. |

#### P0-2 Ledger flush failure does not block inventory persist

| | |
|---|---|
| **Location** | `src/Fineage.Zone/World/ZoneWorldLedgerFlush.cs:17-28`, persist step calling `TryFlushLedger` then `FlushDirtyInventories` |
| **Scenario** | `EconomyLedger.Flush()` throws; catch logs and returns. Simulation step still replaces inventory in Postgres from dirty maps. After restart, journal and inventory disagree (duplication or loss vs auditable economy ledger). |
| **Fix** | Single **persist barrier**: if ledger flush fails, do not call `FlushDirtyInventories`; retain dirty maps; increment `LedgerFlushFailures` and surface degraded mode only via explicit config (default: fail closed). |
| **Acceptance** | Inject flush failure; inventory replace not called; dirty maps unchanged; restart sees consistent pair or explicit repair job. |

#### P0-3 Duplicate login persist ordering vs new session in-world

| | |
|---|---|
| **Location** | `ZoneWorld.More.Enter.cs:150-156` (evict + `pendingUnlink`), persist after enter step |
| **Scenario** | Character B enters while A’s session still bound. Evicted A is detached in the enter step; A’s persist may run after B is already visible. Crash between enter return and A’s flush yields ambiguous economy ordering. |
| **Fix** | Persist evicted snapshot inside the same `RunStep` as successful enter, before fan-out unlock; or gate B’s enter burst on A persist ACK. |
| **Acceptance** | Double-handoff test; exactly one live player entity per object id; DB reflects eviction before B’s packets drain. |

#### P0-4 Command handler exceptions after partial world mutation

| | |
|---|---|
| **Location** | `src/Fineage.Zone/World/ZoneWorldCommands.cs:36-53`, `ZoneWorldSimulation.cs:85-106` |
| **Scenario** | A zone command throws mid-apply after inventory or social mutation. Step completes; dirty flush may run; client gets generic reject while peers received partial fan-out. |
| **Fix** | Per-command **commit unit**: register touched ledger accounts; on exception, rollback in-world mutations for that command id; do not flush dirty rows for that session unless commit succeeded. |
| **Acceptance** | Command throws after inventory debit; bag unchanged in world and DB; no orphan fan-out frames for that tick. |

---

### P1 — cross-player visibility & ordering

#### P1-1 Knownlist diff snapshots entities at prepare time; same-tick despawn can still send spawn

| | |
|---|---|
| **Location** | `src/Fineage.Zone/Net/NetWorldEventPublisher.cs:68-136`, `OutboxingWorldEventPublisher.cs`, `WorldFanoutSequencer.cs:115-138` |
| **Scenario** | Early in tick, diff prepares spawn for entity E. Later, E is removed (`RemoveGroundItem`, death despawn). Drain still sends prepared spawn; despawn order depends on batch coalescing. Observers see ghost pawns or spawn-after-delete. |
| **Fix** | `PresenceLifecyclePipeline`: at drain, re-validate `TryGetEntityById` or register despawn cancel tokens keyed by object id. Shared for players, NPCs, ground items. |
| **Acceptance** | Same-tick spawn-then-despawn test; no spawn packet after despawn; watcher index matches client knownlist. |

#### P1-2 AOI refresh without MoveState when only knownlist changes mid-run

| | |
|---|---|
| **Location** | `src/Fineage.Zone/World/ZoneWorldMovement.cs:200-203`, `ZoneWorld.Members.cs` (`ReconcileKnownlist`, `ack: null`) |
| **Scenario** | Runner crosses AOI hysteresis band; `RefreshMovingKnownlist(e, null)` adds peers without `FanoutMoveState`. New observers spawn the mover at stale coordinates until next leg boundary. |
| **Fix** | On AOI-only refresh, emit position sync (`FanoutMoveState` or correction) from current move segment via same pipeline as leg end. |
| **Acceptance** | Two clients; runner crosses AOI mid-path; observer sees consistent position at spawn. |

#### P1-3 Party adena pickup removes ground item before ledger commit

| | |
|---|---|
| **Location** | `src/Fineage.Zone/World/ZoneWorldGround.More.cs:26-48` |
| **Scenario** | `RemoveGroundItem` runs before `GrantInventoryStack` and `move.Commit`. Observers despawn adena immediately; failure mid-loop leaves no ground stack and under-paid party members. |
| **Fix** | `GroundPickupPipeline`: validate → grant all receivers → commit ledger → remove ground → ordered fan-out batch (despawn + inventory updates). |
| **Acceptance** | Party adena at range edge; all members get system + inventory messages; ledger balances. |

#### P1-4 Simulation stage failure does not abort tick

| | |
|---|---|
| **Location** | `src/Fineage.Zone/Simulation/RegionTickScheduler.cs:78-95`, `ZoneWorldRegions.cs:14-19` |
| **Scenario** | `melee-hits` or `abnormals` throws; later stages (handoff, fan-out drain) still run. Some observers see damage/effects others do not in the same wall-clock tick. |
| **Fix** | Configurable `StageFailurePolicy`: abort remaining stages + handoff, or mark tick poisoned and skip fan-out (log policy name). |
| **Acceptance** | Injected stage throw; documented behavior for handoff and fan-out. |

#### P1-5 Fan-out drain continues after per-frame encode errors

| | |
|---|---|
| **Location** | `src/Fineage.Zone/World/WorldFanoutSequencer.cs:126-137` |
| **Scenario** | One watcher fails to encode; others receive full batch. Asymmetric Die + StatusUpdate across peers. |
| **Fix** | On send failure, mark session for **knownlist resync** or fail entire subject batch for the tick (config per opcode class). |
| **Acceptance** | Injected mid-batch failure; either all watchers fail consistently or resync path runs. |

#### P1-6 EnterWorld quest FX on a later command stage than enter burst

| | |
|---|---|
| **Location** | `src/Fineage.Zone/Net/SessionLifecyclePacketHandler.Enter.cs:38-44` |
| **Scenario** | Peers see spawn from enter step; quest marks/effects enqueue on next command stage. Reconnecting vs standing observers see different overlay ordering. |
| **Fix** | Fold quest client FX into `PlanEnter` fan-out on simulation thread, or gate peer spawns until FX queued in same batch. |
| **Acceptance** | Enter during active quest overlay; all clients match within one tick. |

#### P1-7 Entity generation not bumped on respawn (same objectId)

| | |
|---|---|
| **Location** | `src/Fineage.Zone/Net/EntityIdMap.cs:13-34`, `ZoneWorldCombat.Melee.cs` respawn paths |
| **Scenario** | NPC/ground item respawns reuse integer object id without generation bump. Clients may ignore spawn as stale; reconnect mismatch vs live observers. |
| **Fix** | `PresenceLifecyclePipeline`: call `Bump(objectId)` on every client-visible despawn that will respawn; encode generation in all presence packets. |
| **Acceptance** | NPC respawn test; client accepts new spawn; reconnect matches live session. |

#### P1-8 NPC death audience uses watcher index variant vs player death

| | |
|---|---|
| **Location** | `ZoneWorldCombat.Melee.cs:158-186` vs `ZoneWorldDeath.cs` (`NetWatchersOf`) |
| **Scenario** | NPC die packet audience from `WatchingPlayers` may omit players with combat target but no knownlist entry. Player death uses `NetWatchersOf`. Inconsistent target clear vs die visibility. |
| **Fix** | Unified `DieAudience(subject)` pipeline: `NetWatchersOf` + combat targeters table by entity kind config. |
| **Acceptance** | Combat pin without knownlist (test hook); die packet received. |

#### P1-9 Ground drop watcher discovery not aligned with AOI pipeline

| | |
|---|---|
| **Location** | `src/Fineage.Zone/World/ZoneWorldGround.Members.cs:166-185`, `359-385` |
| **Scenario** | Drop builds watchers via `Knownlist.Contains(dropper) || VisibleTo(...)` instead of AOI on ground cell. Players near item miss `DropItem`; distant players knowing dropper may be included. |
| **Fix** | `GroundItemFanoutPipeline`: audience = geographic AOI ∪ dropper relation watchers; same as despawn. |
| **Acceptance** | Drop at AOI edge; only valid observers receive drop/despawn. |

#### P1-10 NPC melee soulshot has no observer MagicSkillUse (glow)

| | |
|---|---|
| **Location** | `ZoneWorldCombat.Members.cs:36-37` (`TryNpcSoulshot`); contrast `FanoutNpcMagicSkillUse` in `ZoneWorldNpcSkills.Members.cs:257+` |
| **Scenario** | Observers see damage without grade soulshot animation on physical swings; magic NPC skills do fan out. Visual desync only but breaks combat readability. |
| **Fix** | `NpcShotFanoutPipeline` config: on physical SS proc, fan out grade skill id from `SoulshotSkillByGrade` (same table as players). |
| **Acceptance** | Observer test records MagicSkillUse on NPC melee SS. |

---

### P2 — drift, races, lifecycle

#### P2-1 Border snapshots one tick stale for cross-region aggression

| | |
|---|---|
| **Location** | `ZoneWorldBorders.cs:45-93`, `ZoneWorldRegions.cs` handoff stage |
| **Scenario** | Aggression uses previous tick border snapshot while players moved before handoff. Edge pulls disagree between adjacent region workers. |
| **Fix** | Publish borders with tick id; aggression reads live `OwnedNear` for same-tick moves or document one-tick lag in metrics. |

#### P2-2 Tick skip coalesces slow-period stages (DoT, regen)

| | |
|---|---|
| **Location** | `RegionTickScheduler.cs:85-94`, `ZoneWorldSimulation.cs:191-214` |
| **Scenario** | Lag skips multiple simulation ticks; slow stages run once per boundary. DoT/regen under-apply; observers see vitals jump. |
| **Fix** | `TickContext.TicksSkipped` drives repeated slow-stage invocations or accumulated `missedPeriods`. |

#### P2-3 `_lastPresenceSaveMs` not cleared on detach

| | |
|---|---|
| **Location** | `ZoneWorld.Members.Persist.cs:52-64`, `TryDetachPlayer` |
| **Scenario** | Unbounded growth of throttle map over uptime; minor wrong throttling if object ids reused. |
| **Fix** | Remove key on detach; optional cap + metric. |

#### P2-4 Boats fan-out without AOI refresh while moving

| | |
|---|---|
| **Location** | `ZoneWorldBoats.cs:128-140`, compare `ZoneWorldNpcMovement.cs` `RefreshNpcWatchers` |
| **Scenario** | Player walks into harbour mid-voyage; no boat spawn until unrelated AOI event. |
| **Fix** | Register boats on shared mover + `RefreshNpcWatchers` pipeline. |

#### P2-5 `ZoneHost` may use `SingleCellAoi` (whole-zone visibility)

| | |
|---|---|
| **Location** | `ZoneHost.cs:41`, `ZoneAoiSettings.cs:72-86` |
| **Scenario** | Local host tests pass with global AOI; production grid AOI fails fan-out assumptions. |
| **Fix** | Host boot must use `ZoneAoiSettings` factory; log AOI mode at startup. |

#### P2-6 MoveState coalescing drops intermediate legs

| | |
|---|---|
| **Location** | `OutboxingWorldEventPublisher.cs:58-64`, `WorldFanoutBatch.cs:33-37` |
| **Scenario** | Multiple `FanoutMoveState` same tick collapse to latest; observers shortcut corners on geo paths. |
| **Fix** | Coalesce policy per mover class (players: leg boundaries only). |

#### P2-7 Reconnect enter burst omits in-flight cast / abnormal state

| | |
|---|---|
| **Location** | `ZoneWorld.More.cs` (`SnapshotKnownEntities`), `SessionLifecyclePacketHandler.Enter.cs:94-123` |
| **Scenario** | Reconnecting client lags live peers on cast bar, sit, boat move until next diff. |
| **Fix** | `PresenceSnapshotService` shared by enter and `FanoutKnownlistDiff` includes all stateful follow-ups. |

#### P2-8 Net handler catch-all without world rollback

| | |
|---|---|
| **Location** | `NetZoneServer.Handle.cs:320-324` |
| **Scenario** | Handler throws; session may stay `InWorld`; presence hub and sim knownlist diverge. |
| **Fix** | Session FSM: unrecoverable error → sim leave + presence unregister pipeline. |

---

## AOI / event-type matrix (summary)

| Event | Typical fan-out API | Consistency risk |
|-------|-------------------|------------------|
| Player movement | `FanoutMoveState`, knownlist diff | P1-2, P2-6 |
| Melee / skill damage | `FanoutVitals`, hit lines | Stage failure P1-4 |
| Skill cast | `FanoutSkill` / `MagicSkillUse` | Enter FX ordering P1-6 |
| Abnormals | `FanoutAbnormal` | Stage failure P1-4 |
| Death / resurrect | `FanoutDie`, `FanoutRevive` | P1-8 audience |
| Ground drop/pickup | `DropItem`, `RemoveGroundItem` | P1-3, P1-9 |
| NPC state / shots | `FanoutNpcMove`, magic skill | P1-10 melee SS |
| Doors / boats | door broadcast, boat move | P2-4 boats AOI |
| Inventory | `FanoutInventoryUpdate` | P0-1, P0-2 with persist |

---

## Shared pipeline targets (for implementers)

1. **`PresenceFanoutPipeline`** — resolve audience once: `NetWatchersOf(subject)` + config extras (targeters, party, siege).
2. **`PresenceLifecyclePipeline`** — spawn / despawn / generation bump / knownlist diff validation at drain.
3. **`GroundEconomyPipeline`** — pickup, party adena, auto-loot: ledger commit before despawn.
4. **`PlayerLeavePipeline`** — flush or cancel deferred dirty state; knownlist forget on all peers.
5. **`PersistBarrier`** — ledger flush success required before inventory replace.

---

## Verification commands

```bash
dotnet test --filter "FullyQualifiedName~FreightWarehouse|CastInterrupt|GuardTown|NetObserver"
```

Add consistency regression tests alongside each P0/P1 fix (named in acceptance criteria above).

---

## Document history

| Date | Note |
|------|------|
| 2026-11-22 | Initial cross-player consistency review on `dev` @ `7f8d3ce5`. |