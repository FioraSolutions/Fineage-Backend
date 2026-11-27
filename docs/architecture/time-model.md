# Zone time model

Status: implemented for issue #983. Scope: `src/Fineage.Zone` (simulation, calendar schedules, in-game clock,
timer persistence). Mobius CT0 Interlude is the reference for every gameplay rule below.

## 1. Why one port

The zone used to read `DateTimeOffset.UtcNow` in a few systems and passed a bare
`Func<DateTimeOffset>` into `ZoneWorld`/`NetZoneServer`. That could not answer two questions:

- does moving the host clock forward/back change a combat cooldown?
- does a process restart keep a raid respawn, a period boundary, a siege date, or an auction deadline?

The shared port is `Fineage.Zone.Time.IZoneTime` (`src/Fineage.Zone/Time/ZoneTime.cs`). Production constructs
`SystemZoneTime.Instance`, backed by `TimeProvider.System`; tests may pass a deterministic implementation or the
legacy `clock: () => wall` adapter. `ZoneWorld` holds exactly one port and `ZoneAoiSettings.CreateWorld`/`Program`
pass the same instance to every boot-time sidecar.

## 2. The time domains

| Domain | Source | Used for | Examples |
|---|---|---|---|
| Simulation time | monotonic side of `IZoneTime` (`Timestamp` + `Elapsed`), pinned per step | gameplay durations and deadlines | auto-attacks, casts, DoT ticks, crowd-control, skill/item reuse, NPC respawn countdown, boss respawn duration after a kill |
| Wall UTC | `IZoneTime.UtcNow` (production `TimeProvider.System.GetUtcNow()`) | calendar schedules, persistence timestamps, metadata, logs | seven signs weekday boundaries, siege day/hour, manor 20:00/20:06/04:30, lottery Saturday draw, olympiad month, auction end, last-access, GM audit |
| In-game time | `ZoneGameTime` anchored once from the wall sample | game day/night, client `ClientSetTime` | night spawns, dark hit-condition bonus |
| Persisted absolute UTC | sidecars and Postgres columns (`*UnixMs`) | restart recovery | `respawnUnixMs`, `nextChangeUnixMs`, `olympiadEndUnixMs`, `validationEndUnixMs`, `nextWeeklyUnixMs`, `siegeUnixMs`, `endsUnixMs`, `auctionEndUnixMs`, `endUnixMs` |

`SimulationClock` converts the monotonic timestamp into the fixed-step tick:
`tick = floor((anchorWall + elapsed) / step)`. Tick numbers are still `t × 200 ms` since the Unix epoch, so replay
and ledger transaction ids stay comparable across restarts. Inside a step `TickContext.NowUnixMs` is the tick time
and `TickContext.WallUnixMs` is the one wall sample of that step; both are pinned for the step thread.

### Wall-clock jumps

- **Forward jump:** wall samples move, calendar actors see the jump; simulation time advances only by the real
  monotonic elapsed time. A live loop therefore does not run the world ahead, and `MillisecondsUntilNextSimulationTick`
  ignores the wall jump (no busy spin).
- **Backward jump:** wall samples move backwards; simulation still advances one tick per step. Calendar comparisons
  simply do not fire until their stored UTC deadline is reached again.
- **Real stall/GC pause:** monotonic elapsed grows, the next tick jumps and `ISimulationObserver.TicksSkipped`
  reports the skipped simulation ticks (existing behaviour).

## 3. Direct wall-clock inventory (#983)

`rg "DateTimeOffset.UtcNow|DateTime.UtcNow" src/Fineage.Zone` returns **0**. The former 11 reads were classified:

| Place | Class | Resolution |
|---|---|---|
| `Program.cs` ×5 (seven signs, bosses, olympiad, lottery, fishing championship) | wall boot timestamps | one `SystemZoneTime.Instance` sample passed to the sidecar constructors |
| `Punishment/PunishmentManager.cs` ×2 | gameplay expiry load/snapshot | injected `IZoneTime`; expiry stays absolute UTC |
| `Punishment/GmAuditLog.cs` ×1 | wall-only telemetry | injected `IZoneTime`; comment marks the row wall-only |
| `World/ZoneWorld.Members.Persist.cs` ×1 | wall-only account metadata (`LastAccess`) | `_clock.UtcNow`; comment marks it wall-only |
| `World/ZoneWorld.cs` ×1 | default clock fallback | `SystemZoneTime.Instance` / legacy wall adapter |
| `Net/NetZoneServer.cs` ×1 | wall-only auth handoff/idle | `SystemZoneTime.Instance.UtcNow`; comment marks it wall-only |
| `ZoneAcceptance.cs` ×1 | wall-only selftest handoff seed | `SystemZoneTime.Instance.UtcNow` |

Networking session lifetime (handoff token expiry, idle timeout) is real time and deliberately stays wall-based;
it never feeds a gameplay rule.

## 4. Timer persistence and restart policy

Only absolute UTC timestamps are persisted; no parallel timer store was added.

| Timer | Existing seam | Restart / overdue policy |
|---|---|---|
| Raid/grand boss respawn | `boss-schedule.json` (`BossScheduleRow.RespawnUnixMs`) | future deadline stays dead; overdue becomes alive once at boot, logs `[boss] saved respawn overdue`, then spawns through `StartupOrders` |
| Buffs/debuffs and skill reuse | Postgres `character_skills_save` (`StoredEffect`) | Mobius semantics: effect remaining time is frozen offline and restored on enter; reuse `systime` continues while offline. Not a world timer. |
| Regular NPC respawns | spawn catalog, process-local `SpawnSlotState.RespawnAt` | Mobius recreates the spawn table at boot; a mob corpse does not survive a restart. Durations use simulation time. |
| Olympiad | `olympiad.json` (`OlympiadEndUnixMs`, `ValidationEndUnixMs`, `NextWeeklyUnixMs`) | one missed period/validation/weekly step is applied at load or first step and logs `[olympiad] ... overdue`; it is not replayed repeatedly |
| Seven Signs | `seven-signs.json` (`NextChangeUnixMs`) | stored boundary is preserved; an overdue boundary advances one period and logs `[seven-signs] ... overdue`; the next boundary is computed from now |
| Siege schedule | Postgres castles (`SiegeUnixMs`, `EndsUnixMs`, `InProgress`) | overdue scheduled siege/end is handled once at `Restore` and logs `[siege] ... overdue`; `SiegeSchedule.NextFrom` moves it into the future |
| Manor cycles | derived from wall at boot (Mobius also derives mode from the current time); rows persist through `castle_manor_*` | mode is recomputed with `ManorRules.ModeAt`, next change with `ManorRules.NextChange`; no invented timer row |
| Clan hall auctions / hall function leases | Postgres `clan_halls` (`AuctionEndUnixMs`, function `EndUnixMs`) | due auctions settle once, log `[hall-auction] ... overdue`, then restart a 7-day auction or assign the winner |
| Lottery | `lottery.json` (`EndUnixMs`) | one draw is run for an overdue board, log `[lottery] draw overdue`, next draw is computed from now (missed weeks are skipped, not replayed) |
| Fishing championship | `fishing-championship.json` (`PeriodEndUnixMs`) | one overdue settlement, log, next period starts from now |
| Playable quests/items/skills cooldowns | character durable state / live entity | gameplay deadlines are simulation-time; session cooldowns follow Mobius logout rules |

## 5. Scheduling rules from Mobius

- `RaidBossSpawnManager` stores `respawn_time` in the DB and spawns once when it is in the past.
- `SevenSigns` logs a next change in the past and changes periods once at boot, then recomputes the next boundary.
- `Olympiad` persists `olympiad_end`, `validation_end`, `next_weekly_change` and schedules from the current time.
- `CastleManorManager` recomputes its mode from the current wall clock at construction; only manor rows are stored.
- `Lottery` finishes an overdue draw once and creates the next lottery; it does not replay every missed week.
- `Siege` persists the next siege/end dates and reschedules past dates from now.

## 6. Tests

`tests/Fineage.Zone.Tests/TimeModelTests.cs` covers:

- forward and backward wall jumps leave the simulation tick monotonic (combat scheduling independence);
- raid boss respawn is an absolute UTC deadline that survives a restart without re-rolling;
- seven signs keeps its persisted boundary and advances a missed period once;
- olympiad closes one missed period and does not replay it on the next restart;
- siege reschedules a past schedule once into the future;
- manor recomputes mode/next change after a wall jump;
- lottery draws once after three missed weeks;
- hall auction due is read from absolute UTC and fires once.

Existing regression suites also cover `RegionTickScheduler`, `BossScheduleTests`, `ZoneEffectPersistTests`,
`ZoneSpawnRespawnTests`, and the sidecar stores.

## 7. Known acceptance gaps

- Regular NPC respawn deadlines are not persisted across process restart; this matches Mobius' boot-time spawn
  table rather than the literal wording of #983. Adding a spawn-timer sidecar would be a deliberate parity change
  and is not part of this milestone.
- Manor mode/next-change is not stored separately; it is derived from wall time exactly as Mobius does. Only the
  manor production/procure rows are durable.
- In-game time is still derived from the wall sample (Mobius `GameTimeController`), not from simulation ticks. A
  wall jump can therefore move game day/night; this is intentional and documented here.
