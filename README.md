# Fineage-Backend

Fineage C# Login+Zone game server (V2 MessagePack primary, Interlude bridge).

C# login and zone servers for Fineage. V2 MessagePack is the primary wire. The Unity client’s Interlude listener stays in [Fineage `tools/login-stub`](https://github.com/FioraSolutions/Fineage/tree/dev/tools/login-stub) (Blowfish on `2106` / `7777`, loopback only). This repo does not speak that wire.

`src/Fineage.Protocol` holds the shared codecs (V2 MessagePack, pipeline TCP helpers, Interlude Blowfish).

Login issues an HMAC handoff token. The zone on **9777** accepts it as `Auth.Password` (`Auth.Account` is the account) and checks it with `HandoffTokens.TryValidate`. Both processes share `FINEAGE_HANDOFF_SECRET` (dev default `FineageLoginHandoffV1`).

## Flow

`Hello` → `Register` or `Auth` → `CreateCharacter` (Human fighter `0` or mystic `10`, male) → `SelectCharacter` → `Handoff`.

Then, on the zone: `Hello` → `Auth` (account + handoff token) → `EnterWorld` on Talking Island at Cedric's Training Hall (`-72674, 256819, -3112`) → `PlayerSpawned` for yourself, `NpcInfo` for the three shared Gremlins (template **20001**, one object id each for the whole zone), and `CharInfo` for every other player already there. Those players receive your `CharInfo`. `MoveTo` is answered with `MoveToAck` to you and to anyone who still has you in knownlist. Leaving the zone, or walking out of interest, sends `DeleteObject`. `SelectTarget` → `MyTargetSelected`; `AttackRequest` applies flat melee damage to shared Gremlins and fans out `Attack` + `StatusUpdate` (`CUR_HP` / `MAX_HP`) to every player who sees the mob. At **0 HP** the server removes the NPC and sends `DeleteObject` to those clients. `Say` (opcode **8**) is echoed to everyone in the zone as `CreatureSay` (opcode **1012**).

`Handoff.Token` is the zone session key. Login TCP listens on **9106**. The same process serves play-landing HTTP on **5080** (`POST /api/auth/register`, `POST /api/auth/login`) against that store. Zone listens on **9777**. Both bind **0.0.0.0** unless `--bind` / `FINEAGE_BIND` says otherwise. Accounts live in `data/accounts.json` (`--store` / `FINEAGE_LOGIN_STORE`), including the email from HTTP register.

## Run

```bash
dotnet test
dotnet run --project src/Fineage.Login -- --selftest
dotnet run --project src/Fineage.Zone -- --selftest
dotnet run --project src/Fineage.Login -- --bind 0.0.0.0 --port 9106 --http-port 5080
dotnet run --project src/Fineage.Zone -- --bind 0.0.0.0 --port 9777
```

HTTP register body is `{ "username", "email", "password" }`. Login body is `{ "username", "password" }` where `username` may be the email. Success is `{ "ok": true, "username", "mode": "live" }` with **201** (register) or **200** (login). Failures are `{ "ok": false, "error" }`: **400** (`invalid_body`, `invalid_username`, `invalid_email`, `invalid_password`, `password_matches_username`), **409** (`username_taken`, `email_taken`), **401** (`invalid_credentials`). Usernames are a letter then letters, digits, or `_` (3–24). Register passwords are 8–72 characters with no whitespace. TCP `Auth` still accepts the shorter passwords already in the store.

The ticket’s zone address comes from `FINEAGE_ZONE_HOST` (default `127.0.0.1`) and `FINEAGE_ZONE_PORT` (default `9777`). Set the host to the address clients can dial. It is not the bind address.

## Raspberry Pi

Overnight V2 play on the Pi is Login + Zone from this repo, not the Interlude stub.

| Port | Process | Role |
|------|---------|------|
| 5080 | `Fineage.Login` | HTTP register/login. Playtest tunnel `https://api.fineage.evpoint.by` forwards here |
| 9106 | `Fineage.Login` | V2 MessagePack login |
| 9777 | `Fineage.Zone` | V2 zone (Talking Island) |

`2106` and `7777` stay with [Fineage `tools/login-stub`](https://github.com/FioraSolutions/Fineage/tree/dev/tools/login-stub). That stub binds `127.0.0.1` and accepts any password; it does not read `accounts.json`. A router forward of WAN `:2106` / `:7777` to the Pi does not reach this backend. When the client is still Interlude, run the stub (and change its bind in that repo if WAN clients must hit it). When the client is V2, forward `9106` and `9777` instead. Point the landing Worker’s `FINEAGE_LOGIN_API_URL` at `http://<pi>:5080` on a path the Worker can reach. This repo does not change Cloudflare or DNS.

Publish on a 64-bit Pi, or cross-publish from another machine. Self-contained needs no separate .NET install:

```bash
dotnet publish src/Fineage.Login/Fineage.Login.csproj -c Release -r linux-arm64 --self-contained true -o publish/login
dotnet publish src/Fineage.Zone/Fineage.Zone.csproj -c Release -r linux-arm64 --self-contained true -o publish/zone
```

Portable (needs the .NET 10 runtime on the Pi; drop `-r` and `--self-contained`, start with `dotnet Fineage.Login.dll`):

```bash
dotnet publish src/Fineage.Login/Fineage.Login.csproj -c Release -o publish/login
dotnet publish src/Fineage.Zone/Fineage.Zone.csproj -c Release -o publish/zone
```

On the Pi:

```bash
sudo useradd --system --home /var/lib/fineage --create-home fineage
sudo install -d -o fineage -g fineage /opt/fineage /var/lib/fineage
sudo cp -a publish/login /opt/fineage/login
sudo cp -a publish/zone /opt/fineage/zone
sudo install -m 600 -o root -g fineage deploy/fineage.env.example /etc/fineage.env
# edit /etc/fineage.env — set FINEAGE_HANDOFF_SECRET (required; both units read it)
# and FINEAGE_ZONE_HOST to the address clients use (LAN IP or DNS)
sudo cp deploy/systemd/fineage-login.service deploy/systemd/fineage-zone.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now fineage-login fineage-zone
```

`FINEAGE_HANDOFF_SECRET` must be set and identical for both processes. The dev default is only for local loops. Listening on `0.0.0.0` without it prints a warning. Sample unit files and env: `deploy/systemd/`, `deploy/fineage.env.example`.

Both processes emit OpenTelemetry metrics on meter **`Fineage.Server`** (`fineage.connections`, `fineage.frames.in` / `fineage.frames.out`, `fineage.outbound.dropped`, and zone-only `fineage.tick.ms` / `fineage.zone.online`). See the commented block at the bottom of `deploy/fineage.env.example` for instrument names and sample `OTEL_*` variables.

There is no deploy workflow in this repo. CI is `.github/workflows/build-and-test.yml` (`dotnet test` on pull requests and pushes).

Login CLI, same store:

```bash
dotnet run --project src/Fineage.Login -- play <account> <password> <name> fighter
```

## Related

- Client: https://github.com/FioraSolutions/Fineage
- [Fineage #14](https://github.com/FioraSolutions/Fineage/issues/14) — LoginServer v2 auth + session handoff

## License

Fineage-Backend is open source under the [Apache License 2.0](LICENSE), the same license as the [Fineage client](https://github.com/FioraSolutions/Fineage).
