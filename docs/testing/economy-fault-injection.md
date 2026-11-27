# Economy fault-injection matrix and report (#955)

Architecture review §7 requires proving **0 dupes** and **0 ledger divergence** after failures. This document is the
acceptance checklist, the map from each scenario to the test that proves it, and the report of the last Debug run of
the economy hammer with restarts, `kill -9` and SIGTERM.

## Reconciliation tools

| Tool | What it compares | Where it runs |
|------|------------------|---------------|
| `EconomyInvariantChecker` | ledger only: sources − sinks, no negative owned balance | tests, inside every reconcile |
| `StoredStateAudit` | saved character / pet / clan rows against the replayed ledger and the data catalogs | every zone start (`state audit …` line; `FINEAGE_STATE_AUDIT=block` refuses to start on an error) |
| `ZoneWorld.ReconcileEconomy(phase)` | ledger ↔ store ↔ live world: every saved inventory / warehouse / freight / pet bag / clan warehouse, every online bag, loaded warehouse and freight, summoned pet, loaded clan warehouse and ground stack, both ways; one place per item instance id (ledger and saved rows) | every zone start after the audit and every graceful shutdown after the final commit (`economy reconcile phase=startup|shutdown … violations=N` line, one `economy reconcile violation …` line per finding); tests |

Every violation names its code, container and entity id, for example
`store-divergence inventory:34: character 34 'Bot0033' (account loadbot00033): ledger projects {…} pieces …, store holds {…} pieces …`
or `ground-without-entity ground:100001: the ledger holds {57x33} on the ground but no ground stack 100001 is in the world`.
`ReconcileEconomy` refuses to compare a world with batches in flight or dirty containers (`not-quiescent`);
`ReconcileEconomyFlushed` / `ReconcileEconomyAndLog` commit first. The ground does not survive a restart: its
accounts leave through an explicit `Expire` move at boot, never silently.

## Matrix

| Scenario | Injection | Expected | Proof |
|----------|-----------|----------|-------|
| Crash before COMMIT | failed `CommitEconomy` / `FlakyLedgerJournal` | dirty bags restored; store unchanged | `EconomyFaultInjectionTests.Commit_failure_restores_dirty_and_preserves_store` |
| Crash during COMMIT, then restart | durable store throws inside `CommitEconomy`, world dropped | neither journal rows nor bag land; restart reconcile clean | `EconomyReconcileTests.Crash_inside_the_commit_loses_nothing_and_duplicates_nothing_after_restart`, `StoredStateAuditTests` |
| Crash after COMMIT, before publish | worker committed, world dropped before draining | committed state survives; restart reconcile clean | `EconomyReconcileTests.Crash_after_the_commit_before_its_publish_keeps_the_committed_state` |
| Batch queued behind a slow commit, crash before the later batch | gate on the first commit, later batch fails | a batch writes only the journal rows of the bags it carries | `EconomyReconcileTests.A_commit_writes_only_the_journal_rows_of_the_bags_it_carries` (red before the fix) |
| Batch queued behind a failed one | first batch fails, the next runs before the restore | the next batch is held back; both retry together | `EconomyReconcileTests.A_batch_behind_a_failed_one_does_not_write_the_failed_rows` (red before the fix) |
| Graceful restart | leave, stop, boot on the same store and journal | ledger = store = world before and after; players re-enter with what the ledger says | `EconomyReconcileTests.Ledger_store_and_world_agree_before_and_after_a_restart` |
| Divergence is loud | ground stack removed, store rows edited behind the ledger | violations with container and entity id; `ThrowIfViolations` lists them | `EconomyReconcileTests.Divergences_fail_loudly_with_the_container_and_entity_id` |
| Last-item race | 4 clients pick up the same ground id, 8 rounds (piece and adena) | one winner per stack; totals conserved | `EconomyConcurrencyTests.Pickup_races_have_one_winner_per_stack` |
| Warehouse races | 8 parallel deposits, 6 parallel withdrawals of one stack | each unit moves once; fees match; bag + warehouse conserved | `EconomyConcurrencyTests.Warehouse_deposit_and_withdraw_races_move_each_unit_once` |
| Trade + drop race | piece offered in a trade and dropped at once, 12 rounds both orders | the piece ends on the ground or with the buyer, never both, never lost | `EconomyConcurrencyTests.A_piece_offered_in_a_trade_and_dropped_at_once_ends_in_exactly_one_place` |
| Private-store purchase race | 3 buyers for one listed piece and for 5 scrolls in lots of 2 | one sale of the piece, two of the scrolls; adena once each | `EconomyConcurrencyTests.Buyers_racing_for_one_private_store_listing_pay_once` |
| Disconnect at commit | deposit + drop + grant racing the logout, 10 rounds, then restart | no loss, no dupe; reconcile clean every round and after restart | `EconomyConcurrencyTests.Disconnecting_while_its_economy_commit_is_in_flight_loses_and_duplicates_nothing` |
| Off-step logout batch in flight | gated logout commit | the world turn is not held across the wait | `EconomyPersistWorkerTests.Off_step_logout_commit_does_not_hold_the_world_turn_while_its_batch_is_in_flight` |
| Duplicate request | resend the same ledger transaction | idempotent no-op | `EconomyFaultInjectionTests.Duplicate_ledger_tx_id_is_idempotent_not_double_applied` |
| Postgres down mid-op | throw from `CommitEconomy` | failure metrics; dirty retained; no partial publish | `EconomyFaultInjectionTests.Postgres_failure_restores_dirty_without_partial_store_write`, `EconomyCommitTests` |
| Store lookups after a commit | commit a bag, read it by id and through the full list, reopen | the committed bag, not the account snapshot | `EconomyCommitTests.Lookups_by_id_and_the_character_list_see_the_committed_bag` |
| Prefix commit on Postgres | `CommitThrough(position)` with a later row buffered; a failing write | only the prefix lands; a failure puts it back | `PostgresLedgerJournalTests.CommitThrough_writes_the_buffered_prefix_and_keeps_later_rows` |

The concurrency tests run on the production shape: the simulation loop and the persist worker running, every client
on its own thread through the command ingress, and the real shutdown path (save, stop the worker, final commit) before
the closing reconcile. Mob death idempotence is covered by #1059 (`ZoneDeathDropTests`, mob death tests).

## Bugs found and fixed (#955)

1. **A batch wrote journal rows that were not its own.** The persist worker handed each batch every row buffered when
   it ran, not the rows buffered when its bags were taken. A batch queued behind a slow commit wrote a later move's
   row without that move's bag; a crash before the later batch left the journal ahead of the store. Seen on the local
   server as two `DeathDrop` rows (characters 34 and 40) whose items were still in the saved bags: a dupe on restart.
   Fix: the batch records the ledger position with its bags (`EconomyPersistBatch.LedgerThrough`) and the worker calls
   `ILedgerJournal.CommitThrough(position, …)`; a batch behind a failed one is held back until the world restores the
   failed bags.
2. **Store lookups returned stale bags.** `PostgresAccountStore.ListAllCharacters` and `FindByCharacterId` returned the
   account snapshot's containers, which `CommitEconomy` never updates. The startup audit and the reconcile read
   pre-commit bags (41 false divergences after the first hammer restart) and offline-bag ledger observation read stale
   rows. Both now read the normalized tables.
3. **SIGTERM skipped the shutdown.** Only SIGINT cancelled the shutdown token; SIGTERM ended the process with no
   shutdown save, no final economy commit and no shutdown reconcile.
4. Harness: `run-load-test.sh` exported the wrong variable name for the economy-hammer Zone kit, and a manufacture
   customer kept ordering after its two-order material kit was used up (`craft_materials`).

## Report: Debug run, 2026-11-27 (Europe/Minsk 19:49–20:01)

Local stack (Login + Zone, Debug, Postgres 17, fresh database), branch `econ-955`. Commands:
`FINEAGE_BUILD_CONFIGURATION=Debug ./scripts/run-load-test.sh bots-economy-hammer`, then the zone restarted, stopped,
and the hammer rerun twice against the same database with the zone killed 45 s in (`kill -9`, then `kill -TERM`).

| Step | Result |
|------|--------|
| Economy hammer, 24 bots, 90 s + 20 s ramp | runner **PASS**, #867 gates **PASS**; economy flows 511 / 875 succeeded; **0 hard issues, 0 unexplained rejects**; 106 explained rejects (`not_owned` drop races), 239 expected domain rejects (manor_product 75, trade_busy 91, store_item 36, store_busy 18, store_range 14, store_closed 3, trade_range 2); ingress rejected 0; tick p99 49.7 ms, max 56.9 ms; 0 invariant / ledger log lines; 338 journal rows |
| Restart after the hammer | `state audit characters=24 errors=0 notices=0`; `economy reconcile phase=startup ledger-accounts=45 stored-owners=24 violations=0 notices=0` |
| `kill -9` 45 s into a hammer run (24 bots in world, 465 / 655 flows done) | restart: `state audit characters=24 errors=0`; `economy reconcile phase=startup ledger-accounts=46 violations=0`; graceful stop: `phase=shutdown violations=0` |
| SIGTERM 45 s into a hammer run (24 bots in world, 370 / 539 flows done) | `economy reconcile phase=shutdown ledger-accounts=98 stored-owners=24 live-containers=52 ground-stacks=52 violations=0`; restart: `state audit errors=0`, `phase=startup violations=0` (the 52 ground stacks left through `Expire`) |
| Journal after all runs | 1110 rows, hash chain intact (it replays at every boot) |

First-move `ledger adjustment move=… account=character:N/inventory` warnings during the first hammer are the explicit
`Adjustment` source adopting each new character's starting kit (written by Login before any ledger move); they stop
once the kit is journaled (none on any restart).

Before fix 2 the same sequence reported 41 `store-divergence` violations at the first restart while the database
itself matched the ledger; before fix 1 the local database carried the two `DeathDrop` divergences above.

### Known limits

- The client consistency oracle reports `StalePosition` (peer view vs ground) during the hammer: AOI presence, tracked
  as P0 in #1047, not an economy finding.
- Trade and private-store transfers have no escrow container: they settle atomically inside one step, so there is no
  in-between owner to reconcile.
- A Release soak of the hammer with restarts is not part of this report (Debug only).
