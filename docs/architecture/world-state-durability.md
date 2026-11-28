# World state durability

Issue #984, updated for #996. Since #996 the world subsystems (bosses, Seven Signs, olympiad, lottery, raid points, punishments, monster race, fishing championship, PC cafe) are stored in PostgreSQL `world_state` and committed with the economy ledger. See [world-state-storage.md](world-state-storage.md) for the table, the write path, the sidecar cutover and the load rules. This page keeps the shutdown flush registry and the account-side startup audit.

`AtomicJsonFile` remains the durable writer for the files that are still files, and `AtomicJsonFile.LoadStrict` is how the #996 cutover reads a legacy sidecar exactly once.

## Shutdown flush registry

`Fineage.Zone.Persistence.WorldStateSystems` is the closed list of the nine world-state systems.
`ShutdownFlushRegistry` maps each system id to its save operation. `ZoneWorld` registers all nine and calls
`AssertComplete()` during construction, so the Zone cannot start if a subsystem is missing from the registry. A
future system must be added to the closed list and registered; a test also asserts the world exposes the full list.

`ZoneWorld.ShutdownGracefully()` detaches and persists online players, then calls `FlushAll()` on the registry.
`FlushAll()` attempts every subsystem even if one fails; failures are logged with the system id and the concrete
path/error and stay available for diagnostics. The `Program` shutdown path flushes the ledger in a `finally` so a
sidecar failure cannot skip the journal drain. The old late `bosses.Save()` after `ShutdownGracefully()` is gone:
the registry is the single shutdown save path.

## After a kill -9

The next start reads the last committed economy transaction. The world-state rows, ledger rows and bags of one commit land together, so there is never a lottery ticket in a bag without its lottery row, or the reverse. A graceful shutdown flushes first and then reaches the same state. The difference is only how recent that state is.

## World ↔ PostgreSQL coherence (startup state audit)

`Fineage.Zone.Persistence.StoredStateAudit` is the one check between saved state and the running world. The zone
runs it in `Program.cs` right after the world is built (the journal has been replayed into the ledger) and before
the simulation starts, through `ZoneWorld.AuditStoredStateAtStartup()`.

It reads every saved character, pet and clan (`IAccountStore.ListAllCharacters`, `ListAllPets`, `ListClans`) and
reports one `StoredStateIssue` per finding — `code owner container id=…: detail`, for example
`ledger-divergence character 12 'Hero' (account bob) inventory id=0: ledger projects …, store holds …`:

| Code | Severity | Meaning |
|------|----------|---------|
| `missing-reference` | error | A saved row names an item, skill, effect, recipe or henna id the loaded data does not have (inventory, warehouse, freight, pet bag, clan warehouse, learned skills, effects, recipes, dyes). |
| `missing-reference` (quest) | notice | A saved quest row names a quest not in the catalog. The journal keeps the row inactive and saves it back unchanged. |
| `ledger-divergence` | error | The replayed ledger projects different holdings than the store row (bag by template rule: stackables by count, the rest by instance id; warehouse, freight and clan warehouse by count). |
| `ledger-orphan` | error | A character / pet / clan ledger account holds value but the store has no such owner. |
| `ledger-invariant` | error | `EconomyInvariantChecker` found an overdraft or an instance owned twice. |
| `world-state-ledger` / `world-state-invalid` / `world-state-orphan` | error / error / notice | #996 world-state rows against the ledger tallies and their own rules; see world-state-storage.md. |
| `unjournaled` | notice | The store holds a container that has no journal history (pre-ledger rows). Its first ledger move aligns it with an `Untracked` adjustment, which is logged and counted as before. |

Every finding is logged (`state audit …`, errors at Error level), counted in the `fineage.state.audit_issues`
metric (tags `code`, `container`), and summed up in one line:
`state audit characters=N pets=N clans=N errors=N notices=N policy=log|block`.

Policy is `FINEAGE_STATE_AUDIT`:

- `log` (default) — the zone starts; the findings are in the log and the metric.
- `block` — the zone exits with `StoredStateException` listing the errors while any error remains. Notices never
  block. Any other value fails start with a message naming the variable.

The audit never writes. Nothing is corrected or deleted; an operator fixes the data (restore the template, or move
the rows) and restarts.

### Saved rows that name removed ids

The same check guards each enter: `LoadEnteringPlayer` calls `StoredStateAudit.RequireLoadable` before anything is
built, so a character with such a row is refused with every name in `EnterRefusal` and the log
(`enter refused: … StoredStateException: character 12 'Hero' (account bob) cannot be loaded: … learned-skill id=…`).
Before #984 inventory, pet-bag, recipe and effect rows like that were skipped with at most a warning and the next
save of that container deleted them; learned skills and dyes stayed in the row but were silently inactive. Summoning a pet whose saved bag names a missing item is
refused the same way. The loaders behind the check (`LoadInventory`, `EnterSelfPrepare`, `RefreshKnownSkills`,
`RestoreStoredEffects`, `LoadKnownRecipes`) now `Require` the id, so a missing one is an exception, never a skip.

### Offline container writes

Every economic write for a character who is not in the world goes through the economy commit, in the same
transaction as its journal rows: offline freight already did; the offline castle-regalia removal
(`RemoveStoredCastleRegalia`) used to call `TryReplaceItems` directly beside the ledger move and now queues the bag
(`_dirtyOfflineBags`, generalised from the freight-only queue). Ledger reads of an offline bag see that queue first.
A failed commit puts stolen offline writes back in front of anything queued since, so none is lost.

### Proof

`tests/Fineage.Zone.Tests/StoredStateAuditTests.cs` restarts a world over the same store and the same durable
journal (a journal with the Postgres contract: rows become durable only inside the economy commit):

- crash after COMMIT — the first world is dropped without leave or shutdown; the restart's audit is clean and the
  ledger matches the stored bag;
- crash during COMMIT — the commit throws; neither the journal rows nor the bag land; the restart's audit is clean;
- a store row edited behind the ledger is a named `ledger-divergence`, and `block` refuses start;
- an orphan ledger account, a pre-ledger bag (notice only), and saved rows with removed item / skill / effect /
  recipe ids (enter refused, rows kept).

The world-state cases (#996: kill after commit, fault-injected commit, cutover, version refusal, the world-state audit codes) are in `WorldStateDurabilityTests`. The `AtomicJsonFile` crash cases are in `AtomicJsonFileTests`: a crash hook before and after the replace, and a real child
process (`tests/Fineage.StateWriteProbe`) rewriting one snapshot in a loop and killed with SIGKILL at random moments;
every restart reads one complete snapshot.
