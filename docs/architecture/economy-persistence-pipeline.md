# Economy persistence pipeline (#955 follow-up)

Mobius CT0 Interlude economy moves are auditable through `EconomyLedger` + `LedgerMove`. Durable state is journal rows and `character_items` (and warehouse/freight projections) written through **one** path.

## Problem

`CheckCommit()` before mutation prevents rejected operations from changing bags, but crash consistency and player-visible confirmation require **durable commit** before treating an economy command as succeeded. Synchronous `IAccountStore.CommitEconomy` on the simulation thread (even after releasing `_economyLock`) still blocks ticks and contends with logout.

External architecture review (sections 6–7): `CheckCommit` alone is not enough; need **Reserve → durable commit → apply/publish**, with Postgres on a bounded worker and completions processed on the simulation thread.

## Current implementation (PR1)

| Phase | Behavior today |
|-------|----------------|
| Validate / mutate | Handlers call `CheckCommit`, apply bag/ground changes, `LedgerMove.Commit` (in-memory journal). |
| Steal | `TryCommitDirtyEconomy` takes dirty bags under `_economyLock`. |
| Durable commit | **`EconomyPersistService`** (`fineage-economy-persist` thread) runs `ILedgerJournal.CommitWith` + `CommitEconomy`. The simulation thread only `TryEnqueue`s; completions drain at step boundaries (or off-tick callers wait on their own thread). |
| Publish | After successful commit, `PublishStolenEconomy` sends `InventoryUpdate`. |

Non-economic character snapshots remain on [`CharacterPersistWorker`](../../src/Fineage.Zone/World/CharacterPersistWorker.cs) — same “one writer per concern” policy.

The simulation thread never blocks on durable commit: `TryEnqueue` only. A full bounded queue rejects with `persist_queue_full`. Characters with a batch in flight reject new ledger moves with `persist_busy`. Command replies and `InventoryUpdate` publish only after the worker posts a completion drained on the simulation thread.

### Metrics

- `fineage_economy_persist_pending`
- `fineage_economy_persist_oldest_age_ms`
- `fineage_economy_persist_batches_total{result=written}`
- `fineage_economy_persist_failures_total{reason=...}`

## Target model (next PRs)

1. **Reserve** on the simulation thread: lock adena/slots/accounts for the operation id (`LedgerTxId` idempotency).
2. **Durable commit** on `EconomyPersistService` (unchanged path).
3. **Apply / publish** only after worker completion (deferred client success until durable — trade/shop/private store first).
4. **Fault matrix** (integration tests): crash before/after COMMIT, disconnect before reply, duplicate request, last-item race, double-spend adena, Postgres down — expect **0 dupes** and **0 ledger divergence** via reconciliation.
5. **World reconciliation**: `EconomyWorldReconciler` extends ledger checks with live ground stacks, stores, and warehouse parity (audit E5–E7 closed).

## Coordination

- Do not add a second economy Postgres path (`Task.Run` pool commits, ad-hoc `CommitEconomy` from handlers).
- Perf work on `cursor/persist-off-sim-thread-*` covers **non-economic** snapshots only.