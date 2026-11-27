# Economy fault-injection matrix (#955)

Architecture review §7 requires proving **0 dupes** and **0 ledger divergence** after failures. This document is the acceptance checklist; automation grows in `EconomyFaultInjectionTests` and load-test oracle extensions.

| Scenario | Injection | Expected |
|----------|-----------|----------|
| Crash before COMMIT | `FlakyLedgerJournal` / failed `CommitEconomy` | Dirty bags restored; store unchanged; `EconomyWorldReconciler` clean |
| Crash during COMMIT | `StoredStateAuditTests` durable store throws inside `CommitEconomy` | Neither journal rows nor bag land; restart audit clean |
| Crash after COMMIT | `StoredStateAuditTests`: world dropped without leave/shutdown, new world over the same store + durable journal | Journal + items durable; restart reload matches ledger (`StoredStateAudit` clean) |
| After COMMIT, before apply | `EconomyPersistService` completion queue | Publish only after `DrainCompletions` |
| Disconnect before reply | Net test timeout | Idempotent retry via `LedgerTxId` |
| Duplicate request | Resend same command | Second ledger submit rejected or no-op |
| Last-item race | Two pickups same ground id | One winner; ledger conservation |
| Double-spend adena | Two spends same tick | Second `CheckCommit` / ledger rejection |
| Postgres down mid-op | Throw from `CommitEconomy` | Failure metrics; dirty retained; no partial publish |

Reconciliation after each scenario: `EconomyInvariantChecker` + `EconomyWorldReconciler` (online) + `StoredStateAudit` (store rows vs the replayed ledger, run at every zone start; see `docs/architecture/world-state-durability.md`).