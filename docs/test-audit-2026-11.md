# Test suite audit (November 2026)

Report-only audit of the Fineage C# backend xUnit suite. No test code was modified.

## Executive summary

| Metric | Value |
|--------|------:|
| Listed tests (`dotnet test --list-tests`) | **1864** |
| By assembly: Net / Login / Game / Zone | 47 / 114 / 433 / 1270 |
| Skipped / ignored (`Skip=`, `[Ignore]`) | **0** |
| CI configuration | `Fineage.Backend.slnx`, Release, .NET 10 |
| Full-suite wall time (this VM, Release) | ~7–8 min |
| Suite runs for flakiness (3× full solution) | **3/3 green**, identical pass counts |

### Findings by category (conservative counts)

| # | Category | Instances flagged | Likely actionable | Est. tests removable* |
|---|----------|------------------:|------------------:|----------------------:|
| 1 | Duplicates | 8 | 3–5 | 3–6 |
| 2 | Tautological / self-checking | 14 | 4–6 | 2–5 |
| 3 | Empty / weak assertions | 32 | 10–14 | 5–12 |
| 4 | Dead / obsolete | 0 confirmed | 0 | 0 |
| 5 | Over-coupled to implementation | 95+ | 10–15† | 0–5† |
| 6 | Slow tests (timing) | 30 listed | — | — |
| 7 | Flaky tests | 0 | 0 | 0 |

\* **Removable** = merge, delete, or fold into a stronger test without losing Mobius/Interlude parity coverage.  
† Most over-coupled cases are **intentional** (wire parity, Prometheus text); only metrics/logging tests are merge/refactor candidates.

**Overall estimate:** **15–30 tests** could be removed or merged conservatively; **50–70** only if you also dedupe cross-layer coverage (not recommended for Interlude parity).

---

## Methodology

1. **Inventory:** `dotnet test Fineage.Backend.slnx --list-tests` on `dev` (1864 cases).
2. **Static scan:** Regex/AST-style pass over `tests/**/*.cs` for assert patterns, duplicate method names, normalized body hashes, and legacy symbol references.
3. **Timing:** TRX logs from Release runs; durations parsed from `UnitTestResult/@duration`.
4. **Flakiness:** Three full Release suite runs (`run-timing`, `run2`, `run3`); compared pass counts and TRX outcomes where available.
5. **Manual review:** Spot-checked flagged items; quest packs, codec round-trips, and `NetObserverParityTests` treated as high-value unless clearly redundant.

---

## 1. Duplicates (same scenario / near-identical setup)

Most “duplication” in this repo is **layered on purpose**: `Fineage.Game.Tests` (rules/catalog/quest machine) vs `Fineage.Zone.Tests` (simulation + Net fanout). The entries below are the clearest merge candidates.

| File | Test | Category | Reason | Recommendation |
|------|------|----------|--------|----------------|
| `tests/Fineage.Login.Tests/NetLoginGuardTests.cs` | `Flooding_past_the_per_second_cap_is_rejected_with_rate_limit` | duplicate | Same guard policy as zone; differs only in post-Hello opcode (`ListCharacters` vs `Say`) | **keep** both (lobby vs zone); optional **extract** shared guard assertion helper |
| `tests/Fineage.Zone.Tests/NetZoneGuardTests.cs` | `Flooding_past_the_per_second_cap_is_rejected_with_rate_limit` | duplicate | Mirror of login guard test | **keep** (see above) |
| `tests/Fineage.Login.Tests/NetLoginGuardTests.cs` | `Silent_client_is_disconnected_after_the_idle_timeout` | duplicate | Same idle watchdog behaviour as zone | **keep** both services |
| `tests/Fineage.Zone.Tests/NetZoneGuardTests.cs` | `Silent_client_is_disconnected_after_the_idle_timeout` | duplicate | Mirror of login idle test | **keep** |
| `tests/Fineage.Zone.Tests/WeightPenaltyTests.cs` | `Level_zero_leaves_every_stat_untouched` (theory) | duplicate | Same `[InlineData]` rows duplicated in `GradePenaltySkillTests` | **merge** shared Mobius table into one parameterized source |
| `tests/Fineage.Zone.Tests/GradePenaltySkillTests.cs` | `Level_zero_leaves_every_stat_untouched` (theory) | duplicate | Duplicate theory rows vs weight penalty suite | **merge** into one theory data type |
| `tests/Fineage.Zone.Tests/WeightPenaltyTests.cs` | `Penalty_composes_with_a_speed_buff_instead_of_replacing_it` | duplicate | Same name and parallel rows in `GradePenaltySkillTests` | **merge** theory data |
| `tests/Fineage.Zone.Tests/GradePenaltySkillTests.cs` | `Penalty_composes_with_a_speed_buff_instead_of_replacing_it` | duplicate | Parallel grade/weight coverage | **merge** |
| `tests/Fineage.Zone.Tests/NetBossTests.cs` | `Status_codec_round_trips_every_field` | duplicate | Same test *name* as Olympiad codec test; bodies differ by message type | **keep** (rename for clarity only) |
| `tests/Fineage.Zone.Tests/NetOlympiadTests.cs` | `Status_codec_round_trips_every_field` | duplicate | Name collision only | **keep**; rename to disambiguate |
| `tests/Fineage.Zone.Tests/NetCraftTests.cs` | `Make_without_materials_is_refused` | duplicate | Same name as manufacture test; different code path (self craft vs recipe shop) | **keep** |
| `tests/Fineage.Zone.Tests/NetManufactureTests.cs` | `Make_without_materials_is_refused` | duplicate | Different scenario despite shared name | **keep** |

**Note:** Quest hunt files (`*QuestTests.cs`) share scaffolding (`Walk`, `GameData.LoadDefault`) but assert **different quest IDs** — not duplicates for Interlude purposes.

---

## 2. Tautological or self-checking tests

| File | Test | Category | Reason | Recommendation |
|------|------|----------|--------|----------------|
| `tests/Fineage.Game.Tests/GameDataLoadTests.cs` | `Load_rejects_a_kit_that_equips_an_unknown_or_bag_item` | tautological | Only `Assert.Throws` on load | **keep**; optionally assert exception message |
| `tests/Fineage.Game.Tests/GameDataLoadTests.cs` | `Load_rejects_duplicate_item_ids` | tautological | Only `Assert.Throws` | **keep** |
| `tests/Fineage.Login.Tests/HandoffSecretEnvTests.cs` | `FromEnvironment_throws_when_secret_missing_and_default_disallowed` | tautological | Exception-only guard | **keep** |
| `tests/Fineage.Net.Tests/FramingAndDedupTests.cs` | `Oversized_payload_rejected` | tautological | Exception-only on decode | **keep** |
| `tests/Fineage.Zone.Tests/NpcTemplateSpawnTests.cs` | `Object_ids_inside_the_character_or_item_range_are_rejected` | tautological | Exception-only validation | **keep** |
| `tests/Fineage.Zone.Tests/RegionSimulationDeterminismTests.cs` | `Slow_interval_that_is_not_a_step_multiple_is_rejected_with_the_variable_name` | tautological | Exception + message substring | **keep** |
| `tests/Fineage.Zone.Tests/RegionTickSchedulerTests.cs` | `RunNow_refuses_a_stage_that_was_never_registered` | tautological | Exception-only | **keep** |
| `tests/Fineage.Zone.Tests/WeightPenaltyTests.cs` | `Base_max_load_rejects_negative_inputs` | tautological | Exception-only math guard | **keep** |

No `Assert.True(true)` or literal self-`Equal` cases were found in production tests.

---

## 3. Empty or weak assertions

Expression-bodied tests using `=> Assert....` were mis-flagged by automation; manual review below.

| File | Test | Category | Reason | Recommendation |
|------|------|----------|--------|----------------|
| `tests/Fineage.Zone.Tests/NetPresenceHubTests.cs` | `Hub_register_lookup_unregister` | weak_assertion | Never calls `Register`; only double `TryGet`/`Unregister` | **delete** or **strengthen** with loopback socket pair |
| `tests/Fineage.Game.Tests/DeathDropRulesTests.cs` | `Karma_path_requires_pk_limit` | weak_assertion | `Assert.NotNull` on rule object after load | **strengthen** with explicit limit value |
| `tests/Fineage.Game.Tests/DeathDropRulesTests.cs` | `Npc_path_requires_level_above_4` | weak_assertion | `Assert.NotNull` only | **strengthen** |
| `tests/Fineage.Zone.Tests/ElvenMerchantExtrasTests.cs` | `Merchant_page_commands_all_have_handlers` | weak_assertion | Mostly `NotNull` per handler | **strengthen** with sample bypass execution |
| `tests/Fineage.Zone.Tests/NetAcquireSkillTests.cs` | `Acquire_skill_rejects_a_row_that_is_not_on_offer` | weak_assertion | Single refusal check; light on fanout | **keep**; add packet assert if desired |
| `tests/Fineage.Zone.Tests/NetMammonTests.cs` | `No_mammon_during_competition_and_results_spawn_the_merchant_too` | weak_assertion | Heavy `NotNull` on spawns | **keep** (integration smoke) |
| `tests/Fineage.Zone.Tests/NetNoblesseTests.cs` | `Precious_soul_1_wants_an_active_subclass` | weak_assertion | `NotNull` HTML / refusal | **strengthen** with phase assertion |
| `tests/Fineage.Zone.Tests/NetPvpTests.cs` | `You_cannot_attack_yourself` | weak_assertion | `NotNull` on attack result | **strengthen** with damage=0 or opcode absent |
| `tests/Fineage.Zone.Tests/NetTargetSelectTests.cs` | `Panel_attack_action_reaches_the_world` | weak_assertion | Minimal outcome check | **keep**; add target id assert |
| `tests/Fineage.Game.Tests/AutoPortedQuestSmokeTests.cs` | `Catalog_loads_and_startable_quests_appear_on_quest_list` | weak_assertion | Threshold counts only (`>=300`, `90%` listed) | **keep** (broad Mobius smoke) |
| `tests/Fineage.Login.Tests/FineageLoggingTests.cs` | `Log_level_parses_env_text` | weak_assertion | Expression-bodied `Assert.Equal` (valid) | **keep** |
| `tests/Fineage.Login.Tests/FineageLoggingTests.cs` | `Log_format_defaults_to_key_value` | weak_assertion | Expression-bodied (valid) | **keep** |
| `tests/Fineage.Zone.Tests/GmTeleportChatTests.cs` | `Command_parser_splits_verb_and_args` | weak_assertion | Theory with `Assert.Equal` (expression-bodied) | **keep** |
| `tests/Fineage.Game.Tests/KetraVarkaQuestTests.cs` | `Raid_head_quests_pay_the_totem` (and similar theories) | weak_assertion | `Assert.True(Walk(...))` — outcome via helper | **keep** (quest parity) |

---

## 4. Dead tests

| File | Test | Category | Reason | Recommendation |
|------|------|----------|--------|----------------|
| — | — | dead | No `[Skip]` / ignored tests; all 1864 run green | — |

**False positives (still live):** Static scan matched comments mentioning retired **V2 MessagePack** or type name `NetZoneServer`. Examples: `NetEnterWorldOrderTests`, `NetShopTests` file headers. These tests exercise the **current Net** stack and pass — **keep**.

No test-only helpers were found with zero production references beyond intentional fixtures (`WorldBench`, `NetTestKit`, `NetZoneSession`).

---

## 5. Over-coupled tests (implementation / exact strings)

| File | Test | Category | Reason | Recommendation |
|------|------|----------|--------|----------------|
| `tests/Fineage.Login.Tests/FineageLoggingTests.cs` | `Key_value_line_keeps_session_fields_and_drops_secrets` | over_coupled | Exact log line shape (`level=Information`, `password=[redacted]`) | **keep** (security contract) |
| `tests/Fineage.Login.Tests/NetLoginGuardTests.cs` | `Handler_exception_is_rejected_without_leaking_the_message` | over_coupled | Fixed user-facing string `An internal error occurred.` | **keep** (client-visible) |
| `tests/Fineage.Net.Tests/DeployPortGuardTests.cs` | `Deploy_health_check_probes_the_Net_ports` | over_coupled | Asserts env default strings in deploy script | **keep** |
| `tests/Fineage.Zone.Tests/MetricsSummaryTests.cs` | `Summary_line_uses_interval_deltas_and_current_gauges` | over_coupled | Full Prometheus/log line snapshot | **keep** or parse structurally later |
| `tests/Fineage.Zone.Tests/MetricsSummaryTests.cs` | `Prometheus_text_includes_counters_histogram_and_rates` | over_coupled | Exact exposition format | **keep** for ops |
| `tests/Fineage.Zone.Tests/RegionTickSchedulerTests.cs` | `Stages_run_in_registration_order_and_every_step_samples_one_time` | over_coupled | Ordered stage invocation list | **keep** (scheduler contract) |
| `tests/Fineage.Zone.Tests/NetObserverParityTests.cs` | `Player_melee_swings_reach_victim_and_watcher_byte_for_byte_...` | over_coupled | Byte-identical fanout for watchers | **keep** (Interlude wire parity) |
| `tests/Fineage.Zone.Tests/NetObserverParityTests.cs` | `A_cast_start_and_launch_are_identical_for_everyone_who_sees_the_caster` | over_coupled | Same | **keep** |
| `tests/Fineage.Zone.Tests/NetChatWireTests.cs` | `Local_say_bytes_are_identical_for_the_speaker_and_every_listener_in_range` | over_coupled | Payload byte equality | **keep** |
| `tests/Fineage.Zone.Tests/NetChatWireTests.cs` | `Whisper_by_name_over_the_wire_reaches_only_the_receiver_and_echoes_to_the_sender` | over_coupled | Wire + routing | **keep** |
| `tests/Fineage.Zone.Tests/WeightPenaltyTests.cs` | `Crossing_into_level_one_sends_etc_status_but_keeps_the_speed` | over_coupled | Exact `EtcStatus` projection fields | **keep** (client-visible) |

~**6 tests** in `NetObserverParityTests` and ~**9 tests** in `NetChatWireTests` intentionally pin bytes; do not relax without client agreement.

---

## 6. Slowest tests (Release, top 30)

Wall-clock from TRX `duration` (combined assemblies).

| Rank | Duration | Test |
|-----:|---------:|------|
| 1 | 20.73 s | `Fineage.Login.Tests.LoginServiceTests.Acceptance_RegistersHumanAndReturnsZoneToken` |
| 2 | 12.86 s | `Fineage.Zone.Tests.NetSummonTests.Ordered_attack_kills_the_monster_for_the_owner_with_the_servitor_exp_penalty` |
| 3 | 11.45 s | `Fineage.Zone.Tests.NetDisconnectDespawnTests.Disconnect_broadcasts_EntityDespawn_to_the_watcher` |
| 4 | 11.11 s | `Fineage.Zone.Tests.NpcHtmlTests.Talk_renders_the_page_and_bypass_follows_the_links` |
| 5 | 11.07 s | `Fineage.Zone.Tests.NetSevenSignsTests.An_unknown_action_is_a_bad_request` |
| 6 | 10.64 s | `Fineage.Zone.Tests.SiegeRulesTests.Castle_rows_and_hasCastle_round_trip_and_capture_updates_the_clan` |
| 7 | 10.38 s | `Fineage.Zone.Tests.NetObserverParityTests.Player_melee_swings_reach_victim_and_watcher_byte_for_byte_and_damage_lines_stay_private` |
| 8 | 10.36 s | `Fineage.Zone.Tests.NetPledgeWireTests.War_enemies_that_walk_into_view_get_the_war_relation_after_the_spawn` |
| 9 | 10.30 s | `Fineage.Zone.Tests.NpcTemplateSpawnTests.Enabled_maps_include_the_Talking_Island_village` |
| 10 | 10.14 s | `Fineage.Zone.Tests.NetSubclassTests.Level_75_and_every_subclass_at_75_are_required_and_three_is_the_cap` |
| 11 | 9.69 s | `Fineage.Zone.Tests.NetDisconnectDespawnTests.A_leaver_is_gone_from_the_watchers_knownlist` |
| 12 | 9.25 s | `Fineage.Zone.Tests.NetMoveVisibilityTests.Seated_player_is_seen_seated_by_peers_late_joiners_and_walkers_by` |
| 13 | 8.95 s | `Fineage.Zone.Tests.NetEnchantTests.Enchant_cancel_fans_the_cancel_result` |
| 14 | 8.82 s | `Fineage.Zone.Tests.NetSevenSignsTests.Status_join_and_contribution_follow_the_record` |
| 15 | 8.60 s | `Fineage.Zone.Tests.NetPvpPkWireTests.A_plain_click_on_a_white_player_does_nothing_and_ctrl_flags_the_attacker_for_every_watcher` |
| 16 | 8.37 s | `Fineage.Zone.Tests.NetObserverParityTests.A_cast_start_and_launch_are_identical_for_everyone_who_sees_the_caster` |
| 17 | 8.21 s | `Fineage.Zone.Tests.NetChatWireTests.Whisper_by_name_over_the_wire_reaches_only_the_receiver_and_echoes_to_the_sender` |
| 18 | 7.97 s | `Fineage.Zone.Tests.NetObserverParityTests.An_aborted_cast_is_cancelled_on_every_screen_that_saw_it_start` |
| 19 | 7.93 s | `Fineage.Zone.Tests.NetNpcMoveAoiTests.Moving_npc_leaves_and_reenters_net_view` |
| 20 | 7.89 s | `Fineage.Zone.Tests.ZonePositionalCritAndMirageTests.Focus_chance_never_scales_the_cached_crit_rate` |
| 21 | 7.87 s | `Fineage.Zone.Tests.NetPvpPkWireTests.The_flag_blinks_for_its_last_20_seconds_then_clears_for_watchers` |
| 22 | 7.81 s | `Fineage.Zone.Tests.NetChatWireTests.Local_say_bytes_are_identical_for_the_speaker_and_every_listener_in_range` |
| 23 | 7.66 s | `Fineage.Zone.Tests.NetPvpPkWireTests.Killing_a_flagged_player_is_a_pvp_kill_without_karma` |
| 24 | 7.64 s | `Fineage.Zone.Tests.ZoneEffectHandlerTests.Run_away_fears_an_attackable_and_starts_the_flee_step` |
| 25 | 7.43 s | `Fineage.Zone.Tests.NetMoveVisibilityTests.Teleport_despawns_the_jumper_for_old_peers_and_spawns_it_once_for_new_ones` |
| 26 | 7.29 s | `Fineage.Zone.Tests.NetMoveVisibilityTests.Peer_in_view_gets_the_run_start_and_the_stop_lands_on_the_server_position_without_extra_frames` |
| 27 | 7.18 s | `Fineage.Zone.Tests.NetPvpPkWireTests.Hitting_back_a_flagged_attacker_flags_the_defender_too` |
| 28 | 7.12 s | `Fineage.Zone.Tests.NetMoveVisibilityTests.Disconnect_mid_run_despawns_once_for_peers_and_never_reaches_outsiders` |
| 29 | 7.11 s | `Fineage.Zone.Tests.NetObserverParityTests.A_mob_skill_reaches_everyone_who_sees_the_mob_identically` |
| 30 | 7.10 s | `Fineage.Zone.Tests.NetPvpPkWireTests.Mob_exp_burns_karma_and_the_name_turns_white_at_zero` |

**Observation:** 29/30 slowest are `Fineage.Zone.Tests` integration tests (`WorldBench` + simulated ticks). The single slow Login test is an end-to-end acceptance path with Postgres. Consider `[Trait("Category", "Integration")]` and optional split jobs if CI time becomes painful — not a delete list.

---

## 7. Flaky tests

| Run | Net | Login | Game | Zone | Failed |
|-----|----:|------:|-----:|-----:|-------:|
| 1 (`run-timing`) | 47 | 114 | 433 | 1270 | 0 |
| 2 (`run2`) | 47 | 114 | 433 | 1270 | 0 |
| 3 (`run3`) | 47 | 114 | 433 | 1270 | 0 |

TRX outcome comparison on Zone (1270 tests × 3 runs): **no differing outcomes**.

| File | Test | Category | Reason | Recommendation |
|------|------|----------|--------|----------------|
| — | — | flaky | No order-dependent failures observed | — |

---

## Appendix: suite health positives

- **Zero skipped tests** — no hidden debt in CI.
- **Codec coverage** (`Fineage.Net.Tests` round-trips) is fast (~0.6 s total) and complements Zone integration tests.
- **`AutoPortedQuestSmokeTests`** provides a coarse safety net over 300+ ported quests without replacing per-quest parity tests.
- **Guard tests** duplicated across Login and Zone reflect two live processes, not copy-paste waste.

---

*Generated: 2026-11-20. Environment: Cloud Agent VM, .NET SDK 10.0.404, `Release` configuration.*