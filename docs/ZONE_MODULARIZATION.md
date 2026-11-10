# Zone modularization — Status Now

Living SoT for Backend epic [#353](https://github.com/FioraSolutions/Fineage-Backend/issues/353). Cross-repo plan: Fineage [`Docs/REFACTOR_PLAN_2026-11.md`](https://github.com/FioraSolutions/Fineage/blob/dev/Docs/REFACTOR_PLAN_2026-11.md) (“Agent start here”).

**As of 2026-11-10 evening.**

## Done (acceptance largely met)

- Opcode groups live in `*PacketHandler` (session / knownlist / world-events / …); `NetZoneServer` stays dispatch + lifecycle.
- Domain events: `IWorldEventPublisher` in World; Net encode in `NetWorldEventPublisher` (World free of `Fineage.Net.Contract`).
- Focused `*Rules` / `*Service` extracts through Move / Warehouse / Enchant / Gear / Shop / … — residual opportunistic only.
- `ZoneEntity` composition: ground / spoil / aggro / player identity+status / NPC template payloads; leftover Session/Inventory/… refs remain on purpose for now.
- `NetCodec` domain partials + round-trip tests mandatory after contract changes.
- Hub file-splits continued through Backend PRs **#551–#561** (Login Handle.Roster, Cli.Listen, ZoneEntity Identity/Status, WorldEventPacketHandler, Abnormals.Tick, ToNet.Social, Skills.Members.Support, SessionLifecycle.Enter, Warehouse.Helpers, ZoneWorld.More.Enter, …). Most primaries are under ~350 lines (third-party `BlowfishEngine` excepted).

## Next

| Priority | Work | Notes |
|----------|------|--------|
| Opportunistic | Residual hub file-splits only if a primary still balloons | Prefer small PRs; no behavior change |
| Later | Simulation actor / retire global `_gate` | Measured; pairs with Fineage #116 — not current default work |
| Out of scope | Gateway / TLS / Redis / multi-zone / 20k | Fineage #112–#114 |

## Agent rules

- Prefer epic **#353** residual only after checking this Status Now + client plan “Agent start here”.
- Do not invent gameplay rules; Mobius CT0 under `ref/mobius-ct0-interlude/` is SoT.
- After any new `NetCodec.*` file: client must `python3 scripts/sync-net-contract.py --ref origin/dev` (default `--ref origin/main` can pollute).
- Merge gate: `build-and-test` green. Flaky Zone tests (`AdminShopTests.Arcana_SA…`, `SkillUse_fire_vortex…`) → `gh run rerun --failed` before assuming a real regression.
