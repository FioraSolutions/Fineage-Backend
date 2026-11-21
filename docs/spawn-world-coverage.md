# Spawn world coverage (Mobius `SpawnData`)

Fineage stores Mobius territory spawns as `data/spawns/<mapId>.json`. Each file has an `enabled`
flag; **hosted Zone loads every enabled map** unless you narrow with env (see below).

## Default (local + production)

| Setting | Hosted value | Effect |
|---------|--------------|--------|
| *(unset)* `FINEAGE_ZONE_INCLUDE_ENABLED_MAPS` | on | All maps with `"enabled": true` in JSON |
| `FINEAGE_ZONE_MAPS` | unset | No whitelist — do not paste a manual map list on full-world hosts |
| `FINEAGE_ZONE_BOUNDS` | unset | No XY crop |
| `FINEAGE_ZONE_AOI_CELL` | **3000** (set in `scripts/run-local-net.sh` and recommended in `deploy/fineage.env`) | Grid AOI so clients only see nearby NPCs (~34k rows world-wide) |

`src/Fineage.Zone/Program.cs` passes `includeEnabledMaps: true` unless
`FINEAGE_ZONE_INCLUDE_ENABLED_MAPS=0|off|false|no`. Bosses follow the same rule via
`MapFilteredBossSpawnPolicy`.

Copy the commented block in [`deploy/fineage.env.example`](../deploy/fineage.env.example) for
`homeserver` / any Debian deploy. **Do not** set `FINEAGE_ZONE_MAPS` there unless you are
deliberately sharding one region onto a dedicated process.

Local SoT: `./scripts/run-local-net.sh start` leaves map filters empty and sets
`FINEAGE_ZONE_AOI_CELL=3000` (see `NerupaSpawnTests.Local_net_set_loads_the_whole_world_so_she_is_spawned`).

## Narrowing (optional)

| Env | Use when |
|-----|----------|
| `FINEAGE_ZONE_INCLUDE_ENABLED_MAPS=0` | Single-map smoke (`ti_cedric` active map) or fast unit worlds |
| `FINEAGE_ZONE_MAPS=a,b,c` | Explicit whitelist only (dev shards, load tests) |
| `FINEAGE_ZONE_BOUNDS=minX,minY,maxX,maxY[;…]` | Keep spawn rows inside boxes (multi-box `;` separated) |

Unknown map ids in `FINEAGE_ZONE_MAPS` fail at zone boot with a readable error.

## Starter continents & towns (enabled datapack)

Interlude starter villages and cathedral towns are enabled in the datapack and materialize under the
default config (no `FINEAGE_ZONE_MAPS` edit):

| Role | `mapId` (spawn JSON) |
|------|----------------------|
| Talking Island village | `ti_village`, road `ti_village_road`, training `ti_cedric`, mobs `ti_monsters` |
| Elven Village | `elven_village`, underground `elven_fortress`, mobs `elven_monsters` |
| Dark Elf Village | `dark_elven_village`, mobs `dark_elven_monsters` |
| Orc Village | `orc_village`, mobs `orc_monsters` |
| Dwarven Village | `dwarven_village`, mobs `dwarven_monsters` |
| Towns | `gludio`, `gludin`, `dion`, `giran`, `oren`, `aden`, `heine`, `hunters`, `goddard`, `rune`, `schuttgart`, `floran` |

Regional hunt maps (`others_*`, `aden_*`, `giran_*`, …) load with the same default. Gatekeeper
presence and teleport destinations for these maps are guarded by
`CityTeleportSpawnTests`.

## Day / night spawn groups

Mobius `periodOfDay` on a spawn row becomes `SpawnPeriodOfDay` (`day`, `night`, or any). On each
slow tick the zone calls `TickDayNightSpawns`: when in-game night flips, period-mismatched NPCs
despawn without starting respawn, and matching rows materialize. Respawn timers still apply to
attackable mobs when their period is active.

Tests: `DayNightSpawnTests` (synthetic rows), `SpawnMapEnablementTests` (real enabled maps),
`CityTeleportSpawnTests` (full enabled world at current game time).

## Related

- Wire / env summary: [`docs/net-protocol.md`](net-protocol.md) (Scale seams → spawn table)
- Import path: [`docs/MOBIUS_XML_REF.md`](MOBIUS_XML_REF.md) (`spawns/**/*.xml` → `data/spawns/*.json`)
