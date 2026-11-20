# Fineage-Backend

Fineage C# Login+Zone game server (Net `PacketEnvelope` wire only).

C# login and zone servers for Fineage. The Net `PacketEnvelope` wire is the only client transport: the MessagePack V2 listener, its `ZoneServer`, and `Fineage.Protocol/V2` are deleted, so nothing in this repo can turn V2 back on. The Unity client’s Interlude listener stays in [Fineage `tools/login-stub`](https://github.com/FioraSolutions/Fineage/tree/dev/tools/login-stub) (Blowfish on `2106` / `7777`, loopback only). This repo does not speak that wire.

`src/Fineage.Net.Contract` + `src/Fineage.Net.Host` are the **live** client codec and TCP host (`NetOpcode`, `PacketEnvelope`). `src/Fineage.Protocol` keeps legacy `MsgType` / MessagePack-shaped DTOs and helpers for documentation and shared shapes — **not** the on-wire opcode table.

Login issues an HMAC handoff token. The zone on **9777** accepts it in the Net `ZoneAuth` payload and **consumes** it once via `IHandoffStore.TryConsume` (signature + TTL + one-time nonce). Both processes share `FINEAGE_HANDOFF_SECRET`. Non-loopback binds **refuse to start** without that env var; the well-known `DefaultSecret` (`FineageLoginHandoffV1`) is for loopback / `--selftest` only.

## Docs (what to trust)

| Doc | Role |
|-----|------|
| [`docs/net-protocol.md`](docs/net-protocol.md) | **Live wire SoT** — lobby/zone Net opcodes, presence, Postgres, enter-world order |
| [`docs/MOBIUS_XML_REF.md`](docs/MOBIUS_XML_REF.md) | Mobius CT0 XML → `data/` |
| [`docs/combat-parity.md`](docs/combat-parity.md) | Combat checklist vs Mobius |
| `docs/*-v2-protocol.md` | Feature **semantics** only; MsgType numbers inside are legacy MessagePack |
| Client [`Docs/DOC_CANON.md`](https://github.com/FioraSolutions/Fineage/blob/dev/Docs/DOC_CANON.md) | Cross-repo doc triage |
| Client [`Docs/REFACTOR_PLAN_2026-11.md`](https://github.com/FioraSolutions/Fineage/blob/dev/Docs/REFACTOR_PLAN_2026-11.md) | Harden / Zone modularize plan + “Agent start here” |
| [`docs/ZONE_MODULARIZATION.md`](docs/ZONE_MODULARIZATION.md) | **Status Now** for epic [#353](https://github.com/FioraSolutions/Fineage-Backend/issues/353) — what landed, what is next |
| Client [`Docs/ARCHITECTURE_NET.md`](https://github.com/FioraSolutions/Fineage/blob/dev/Docs/ARCHITECTURE_NET.md) | Target scale (gateway / 20k) — design, not “already shipped” |

Chronicle today: **Interlude (Mobius CT0)**. Near-term program: finish and stabilize Interlude — not High Five. See client [`Docs/DOC_CANON.md`](https://github.com/FioraSolutions/Fineage/blob/dev/Docs/DOC_CANON.md).

## Flow (Net)

Lobby (Login **9106**): `Hello` → `Auth` (register-if-missing) → `CreateCharacter` / roster → `SelectCharacter` → `Handoff`. `DeleteCharacter` / `RestoreCharacter` on the same connection. Create supports Interlude base classes (Human **0** / **10**, Elf **18** / **25**, Dark Elf **31** / **38**, Orc **44** / **49**, Dwarf **53**). Env knobs (`FINEAGE_CHARACTER_CREATE`, `FINEAGE_ALLOWED_RACES`, …) and roster fields are documented in [`docs/net-protocol.md`](docs/net-protocol.md).

Zone (**9777**): `Hello` → `Auth` (account + handoff) → `EnterWorld`. Projector sends Net bootstrap (`EntitySpawn` self, `PlayerStats`, `UserInfo`, `ItemList`, `SkillList`, `ShortCutInit`), then AOI fanout for peers/NPCs. Move / combat / loot / chat / shop / quest / party / trade / clan use **`NetOpcode` `0x03xx`** — full tables in [`docs/net-protocol.md`](docs/net-protocol.md). Game logic lives in `ZoneWorld*` + `Combat/`; Net is the projection layer (`NetZoneServer`).

Spawns: `data/spawns/*.json` (Mobius-style). Default map is the active spawn map (`ti_cedric` unless overridden); whole Interlude world needs `FINEAGE_ZONE_INCLUDE_ENABLED_MAPS` or a map whitelist. Starter kits: `data/starter-kits.json`. Accounts and characters: PostgreSQL only (`FINEAGE_DATABASE_URL`).

HTTP on Login **5080**: `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/status`. Bind defaults **0.0.0.0** unless `--bind` / `FINEAGE_BIND` says otherwise.

## Run (local SoT)

One script starts Postgres (docker), builds, and runs Login+Zone:

```bash
./scripts/run-local-net.sh start    # login Net 9106, HTTP 5080, zone 9777
./scripts/run-local-net.sh status
./scripts/run-local-net.sh stop     # also bootouts leftover local.fineage.* LaunchAgents
```

Shared `FINEAGE_HANDOFF_SECRET` defaults to `FineageLoginHandoffV1` for this local path. Do not use ad-hoc `dotnet run` for day-to-day play — that fights the script and any old LaunchAgents.

**GM commands:** the script sets `FINEAGE_ZONE_ADMIN=1` by default. Character name must be in [`data/admin-characters.json`](data/admin-characters.json) (`*` = any name for local/dev; use explicit names on shared hosts). In chat: `//gmshop`, `//setlevel 80`, `//setclass <id>`, `//give <itemId> [count]`, `//buff` / `//mbuff` / `//fbuff` / `//buff_clear`. **Teleport (chat, no client patch):** `//teleport x y z` (alias `//tele`), `//goto <charName>`, `//recall <charName>`, `//loc`, `//town <name>`, `//towns` — towns listed in [`data/gm-teleports.json`](data/gm-teleports.json). `//` lines are never broadcast; non-admins get a system refusal. Override with `FINEAGE_ZONE_ADMIN=0 ./scripts/run-local-net.sh start` to disable.

Tests / smoke (not the live world):

```bash
dotnet test
dotnet run --project src/Fineage.Login -- --selftest
dotnet run --project src/Fineage.Zone -- --selftest
```

HTTP register body is `{ "username", "email", "password" }`. Login body is `{ "username", "password" }` where `username` may be the email. Success is `{ "ok": true, "username", "mode": "live" }` with **201** (register) or **200** (login). Failures are `{ "ok": false, "error" }`: **400** (`invalid_body`, `invalid_username`, `invalid_email`, `invalid_password`, `password_matches_username`), **409** (`username_taken`, `email_taken`), **401** (`invalid_credentials`). Usernames are a letter then letters, digits, or `_` (3–24). Register passwords are 8–72 characters with no whitespace. TCP `Auth` still accepts the shorter passwords already in the store.

`GET /api/status` returns `{ "ok": true, "login": true, "zone": <bool>, "playersOnline": <int> }` with `Cache-Control: no-store`. `ok` and `login` are true when Login HTTP answers. `zone` is true when Zone answers its loopback status. `playersOnline` is the live count of players in the world, or **0** when Zone is down (an empty world is `zone: true` and `playersOnline: 0`). The response is **200** in both cases. The play-landing Worker should proxy this GET to `FINEAGE_LOGIN_API_URL` the same way it proxies `POST /api/auth/*`. This process does not send CORS headers.

The ticket’s zone address comes from `FINEAGE_ZONE_HOST` (default `127.0.0.1`) and `FINEAGE_ZONE_PORT` (default `9777`). Set the host to the address clients can dial. It is not the bind address.

## Live server (`homeserver`)

Live stack runs on the Debian host **`homeserver`** — an x86-64 Debian 13 box, reached over Tailscale
as **`100.118.63.28`** (`ssh homeserver`) from any network. Artifacts live in
**`/home/nikita/fineage-backend/deploy/`** (`login/`, `zone/`, `data/`, `logs/`, `fineage.env`);
the git checkout of this repo sits one level up in `/home/nikita/fineage-backend/`.

| Port | Process | Role |
|------|---------|------|
| 5080 | `Fineage.Login` | HTTP register/login and `GET /api/status`. Playtest tunnel `https://api-fineage.evpoint.by` forwards here |
| 2106 | `Fineage.Login` | Net login (public port; process listens **2106**, `FINEAGE_NET_LOGIN_PORT`) |
| 7777 | `Fineage.Zone` | Net zone (public port; process listens **7777**, `FINEAGE_NET_ZONE_PORT`) |

Zone also binds **127.0.0.1:7780** for `GET /status`. Login’s `GET /api/status` probes it. Set `FINEAGE_ZONE_HOST` / `FINEAGE_HANDOFF_SECRET` (or `handoff.secret`) in `fineage.env`. Deploy never overwrites `data/`, `fineage.env`, or `handoff.secret`.

Public names, both plain A records (DNS-only, no Cloudflare proxy — the orange cloud would break TCP):

| Name | Points at | Serves |
|------|-----------|--------|
| `login.fineage.evpoint.by` | dynamic WAN IPv4 of `homeserver` | Net login **2106** |
| `game.fineage.evpoint.by` | same | Net zone **7777** |

The WAN address is dynamic, so both records are kept current by the **DDNS timer** below. Router port
forwards **2106 → `192.168.1.13:2106`** and **7777 → `192.168.1.13:7777`**. When the ISP moves the
WAN IP, DNS catches up within five minutes and no redeploy is needed.

### systemd units (all `enabled`)

| Unit | Role |
|------|------|
| `fineage-login.service` | `deploy/login/Fineage.Login --bind 0.0.0.0 --http-port 5080` |
| `fineage-zone.service` | `deploy/zone/Fineage.Zone --bind 0.0.0.0 --state-dir deploy/data` |
| `cloudflared.service` | Cloudflare tunnel behind `api-fineage` / `fineage.evpoint.by` |
| `tailscaled.service` | Tailscale (`100.118.63.28`); `--accept-dns=false` so it leaves server DNS alone |
| `fineage-ddns.timer` | Runs `fineage-ddns.service` every 5 min |

`systemctl restart fineage-login fineage-zone` after replacing binaries; `journalctl -u fineage-login -n 50`.

### DDNS (keeps the A records on the right IP)

`/opt/fineage-ddns/update.sh`, driven by `fineage-ddns.timer` (`OnBootSec=2min`, then every 5 min):

1. Reads this host’s public IPv4 (ifconfig.me, then api.ipify.org, then icanhazip.com).
2. Compares it with `/var/lib/fineage-ddns/last-ip`; exits when unchanged, so the API is not hit every tick.
3. `PATCH`es `login.fineage.evpoint.by` and `game.fineage.evpoint.by` on change and stores the new IP.

The Cloudflare API token lives in `/etc/fineage-ddns.env` (`CF_API_TOKEN=…`, root `0600`) and is scoped to
`Zone → DNS → Edit` on `evpoint.by` only. Rotate it from the Cloudflare dashboard and overwrite that one
line. A revoked or expired token shows up as `HTTP 403` in `journalctl -u fineage-ddns`; DNS then simply
stops updating — nothing else breaks.

```bash
sudo systemctl start fineage-ddns.service   # force one run
journalctl -u fineage-ddns -n 20 --output=cat
```

This repo does not change Cloudflare or DNS records itself; the timer above does.

### Deploy

**Auto-deploy:** push to **`main`** → [`.github/workflows/deploy.yml`](.github/workflows/deploy.yml)
(or Actions → `deploy` → Run workflow). PRs never deploy. It publishes `linux-x64`, joins the tailnet as
`tag:github-actions`, and calls `deploy/deploy-to-host.sh`, which restarts the systemd units and
health-checks the listeners.

Workflow configuration (repository **variables**, not secrets, for the non-sensitive addresses):

| Name | Value |
|------|-------|
| `FINEAGE_HOST` | `homeserver` (or `homeserver.tailaf5692.ts.net`) |
| `FINEAGE_DEPLOY_USER` | `nikita` |
| `FINEAGE_APP_ROOT` | `/home/nikita/fineage-backend/deploy` |

Secrets: `FINEAGE_SSH_PRIVATE_KEY` (a key accepted by `nikita@homeserver`), `TS_OAUTH_CLIENT_ID`,
`TS_OAUTH_SECRET` (Tailscale OAuth client tagged `tag:github-actions`). The ACL must allow that tag to
reach `homeserver:22`.

**Manual** (from a checkout on `homeserver`, or cross-publish):

```bash
# on homeserver, x86-64
dotnet publish src/Fineage.Login/Fineage.Login.csproj -c Release -o publish/login
dotnet publish src/Fineage.Zone/Fineage.Zone.csproj -c Release -o publish/zone
./deploy/deploy-to-host.sh publish/login publish/zone
```

Cross-machine, same script with explicit targets:

```bash
FINEAGE_HOST=homeserver FINEAGE_DEPLOY_USER=nikita \
  FINEAGE_APP_ROOT=/home/nikita/fineage-backend/deploy \
  ./deploy/deploy-to-host.sh publish/login publish/zone
```

`deploy/deploy-to-host.sh` rsyncs the two publish trees, preserves `data/`, `logs/`, `fineage.env`,
restarts `fineage-login` / `fineage-zone` through systemd, then health-checks
`FINEAGE_NET_LOGIN_PORT` / `FINEAGE_NET_ZONE_PORT` / `FINEAGE_LOGIN_HTTP_PORT` on loopback.
Set `FINEAGE_RESTART=scripts` to fall back to `run-login.sh` / `run-zone.sh` on a host without units.

**Restart only** (binaries already in place):

```bash
cd /home/nikita/fineage-backend/deploy && ./run-login.sh && ./run-zone.sh
```

`deploy/deploy-to-host.sh` and `run-*.sh` keep their historical filenames; `run-*.sh` still restarts
without systemd, which is why the script defaults to `systemctl` on this host.

Env sample: [`deploy/fineage.env.example`](deploy/fineage.env.example).

## Logs and metrics

Both processes log through `Microsoft.Extensions.Logging`. The default console format is one key=value line per event. `FINEAGE_LOG_FORMAT=json` switches to JSON lines. `FINEAGE_LOG_LEVEL` sets the minimum (`trace`, `debug`, `info`, `warn`, `error`, `critical`, `off`; default `info`).

Connect, disconnect, enter world, and packet errors carry `session`, `account`, `character`, and `packet`. Passwords, handoff tokens, and session keys are not logged. A field named `password`, `token`, `secret`, or `session_key` is dropped, and `password=` / `token=` assignments inside a message are rewritten to `[redacted]`.

Meter **`Fineage.Server`** (OpenTelemetry, and the optional text endpoint below):

| Instrument | Kind | Meaning |
|------------|------|---------|
| `fineage.connections` | UpDownCounter | Open TCP sessions. Tag `endpoint` = `login` or `zone` |
| `fineage.zone.online` | Gauge | Players in the zone simulation |
| `fineage.frames.in` / `fineage.frames.out` | Counter | Packets by direction |
| `fineage.bytes.in` / `fineage.bytes.out` | Counter | Packet bytes, header included |
| `fineage.packets.per_second` | Gauge | Packets/s. Tag `direction` = `in` or `out`. Updated on each summary line and each `/metrics` scrape |
| `fineage.handler.ms` | Histogram | Packet handler duration |
| `fineage.tick.ms` | Histogram | Zone simulation tick (HP/MP regen). Zone only |
| `fineage.packets.rejected` | Counter | Rate limit, unknown opcode, or wrong port |
| `fineage.packets.errored` | Counter | Error response or handler failure |
| `fineage.connections.rejected` | Counter | Accept dropped at the connection cap |
| `fineage.outbound.dropped` | Counter | Outbound queue full |

`FINEAGE_METRICS_SUMMARY_SECONDS` prints one `fineage conn=…` line on that interval (default **60**). `0` or `off` disables it. Counts on that line are for the interval; `conn` and `online` are the current gauges. `in_pps` / `out_pps` are packets per second.

`FINEAGE_METRICS_PORT` serves Prometheus text at `GET /metrics` (disabled when unset or `0`). Login and zone are separate processes, so set `FINEAGE_LOGIN_METRICS_PORT` and `FINEAGE_ZONE_METRICS_PORT` when both should export; the role-specific variable wins over `FINEAGE_METRICS_PORT`. Bind is `FINEAGE_METRICS_BIND` (default `127.0.0.1`). Prometheus names use underscores (`fineage_frames_in_total`, `fineage_handler_ms`, `fineage_tick_ms`, `fineage_zone_online`, …).

The landing does not scrape Prometheus. Zone always publishes the same online gauge as JSON on loopback `GET /status` (default **127.0.0.1:7780**, `FINEAGE_ZONE_STATUS_PORT`; `0` or `off` disables it). Login probes that URL with a 500ms budget and folds it into `GET /api/status`. Set `FINEAGE_ZONE_STATUS_URL` on Login only when Zone is not on this machine's loopback; the URL must include the `/status` path. A refused connection or timeout is `zone: false`, `playersOnline: 0`, not an HTTP error.

See the commented block in `deploy/fineage.env.example`. Export the same meter with the OpenTelemetry .NET SDK (`OTEL_SERVICE_NAME`, `OTEL_EXPORTER_OTLP_ENDPOINT`) if you want a collector instead of `/metrics`.

CI is `.github/workflows/build-and-test.yml` (`dotnet test` on pull requests and pushes to `main` and `cursor/**`).

Login CLI (same Postgres store):

```bash
dotnet run --project src/Fineage.Login -- play <account> <password> <name> fighter
```

## Related

- Client: https://github.com/FioraSolutions/Fineage
- [Fineage #14](https://github.com/FioraSolutions/Fineage/issues/14) — LoginServer v2 auth + session handoff

## License

Fineage-Backend is open source under the [Apache License 2.0](LICENSE), the same license as the [Fineage client](https://github.com/FioraSolutions/Fineage).
