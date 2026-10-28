# Fineage-Backend

Fineage C# Login+Zone game server (V2 MessagePack primary, Interlude bridge).

C# login and zone servers for Fineage. V2 MessagePack is the primary wire. The Unity client’s Interlude listener stays in [Fineage `tools/login-stub`](https://github.com/FioraSolutions/Fineage/tree/dev/tools/login-stub) (Blowfish on `2106` / `7777`, loopback only). This repo does not speak that wire.

`src/Fineage.Protocol` holds the shared codecs (V2 MessagePack, pipeline TCP helpers, Interlude Blowfish).

Login issues an HMAC handoff token. The zone on **9777** accepts it as `Auth.Password` (`Auth.Account` is the account) and checks it with `HandoffTokens.TryValidate`. Both processes share `FINEAGE_HANDOFF_SECRET` (dev default `FineageLoginHandoffV1`).

## Flow

`Hello` → `Register` or `Auth` → `CreateCharacter` (Human fighter `0` or mystic `10`, male) → `ListCharacters` → `SelectCharacter` → `Handoff`. `DeleteCharacter` / `RestoreCharacter` sit on that same lobby connection.

### Lobby messages

The Unity client speaks the legacy login set (`FineageLoginSession`). Ids and MessagePack keys are documented on `Fineage.Protocol.LobbyProtocol`. Do not renumber them. New fields are appended.

| Direction | Id | Name |
|-----------|----|------|
| C → S | 20 | `Register` |
| C → S | 21 | `CreateCharacter` |
| C → S | 22 | `ListCharacters` |
| C → S | 23 | `SelectCharacter` (0 id, 1 name, 2 slot) |
| C → S | 24 | `DeleteCharacter` (0 slot, 1 characterId) |
| C → S | 25 | `RestoreCharacter` |
| S → C | 1020–1024 | `RegisterOk`, `CharacterCreated`, `CharacterList`, `Handoff`, `LoginOk` |
| S → C | 1025 | `CharacterDeleted` (0 slot, 1 characterId, 2 hardDeleted), then a fresh `CharacterList` |
| S → C | 1026 | `CharacterDeleteFailed` (0 slot, 1 characterId, 2 code, 3 message) |
| both | 1099 | `Error` |

`CreateCharacter` keys 0–3 are name, classId, race, sex. Appended: 4 hairStyle (0–4 male, 0–6 female), 5 hairColor (0–3), 6 face (0–2). Success is `CharacterCreated` (0 id, 1 name, 2 classId, 3 race, 4 sex, 5 hairStyle, 6 hairColor, 7 face, 8 x, 9 y, 10 z) then a fresh `CharacterList`. Failure is `Error`. Create codes: `name_too_long` (empty or over 16), `invalid_name` (charset, a forbidden fragment, or the name template), `creation_failed` (appearance, a non-base class, or a race turned off in config), `create_not_allowed`, plus `race_not_supported`, `class_not_supported`, `sex_not_supported`, `name_taken`, and `character_limit`. Defaults: Human fighter and mystic, both sexes, forbidden fragments `annou,ammou,amnou,anmou,anou,amou`, name template `.*`, cap 7 (0 is unlimited), starting adena 0. A new character is level 1 with full HP/MP (fighter 126/38, mystic 99/59), an empty title, the class starter kit, and the class creation point stored as its position. `FINEAGE_CHARACTER_CREATE=0` turns creation off. `FINEAGE_FORBIDDEN_NAMES`, `FINEAGE_CNAME_TEMPLATE`, `FINEAGE_CHAR_MAX_NUMBER`, `FINEAGE_ALLOWED_RACES`, `FINEAGE_ALLOWED_CLASSES`, `FINEAGE_STARTING_ADENA`, `FINEAGE_STARTING_LEVEL`, and `FINEAGE_STARTING_SP` override the defaults.

`CharacterSummary` keys 0–5 are id, name, classId, race, sex, level. Appended: 6 slot, 7 hairStyle, 8 hairColor, 9 face, 10 x, 11 y, 12 z, 13 deleteSecondsLeft, 14 isLastPlayed. The roster is creation order. Slot 0 is the first character. Position is the saved world point, or the class creation point until the zone has stored one. `deleteSecondsLeft` is whole seconds until hard delete, or 0. `isLastPlayed` marks the greatest `lastAccess`. Pending deletes still occupy a slot and count toward the 7-character cap. `FINEAGE_DELETE_CHAR_AFTER_DAYS` defaults to 7; 0 hard-deletes immediately and drops that character's items. An expired timer is purged when the roster is loaded, and the name is free again. Restore of an unknown slot sends nothing. Select rejects `character_not_found`, `character_pending_delete`. A second select on the same session after a handoff is a no-op, and selects are flood-gated (1 second).

V2 lobby replies that used to share ids with the zone moved off that range. `CharacterRoster` is **1030** (was 1005 / `CharInfo`), `CreateCharacterResult` is **1031** (was 1006 / `NpcInfo`), retired `EntityLeft` is **1032** (was 1007 / `DeleteObject`, not sent), `ServerList` is **1033** (was 1010 / `StatusUpdate`). `PlayToken` stays **1011**. Inventory stays **1014–1016**. The client `dev` branch does not send the V2 lobby ids; 1005–1007 there are zone `CharInfo`, `NpcInfo`, and `DeleteObject`. V2 request opcodes 5 and 6 remain login-port only and still numerically overlap zone `SelectTarget` / `AttackRequest`.

Then, on the zone: `Hello` → `Auth` (account + handoff token) → `EnterWorld`. Creation stores the class creation point (human fighter `-71338, 258271, -3104`, human mystic `-90875, 248162, -3570`), and the zone spawns there. A character with no saved position still picks one of the four points around that spot. Enter and leave write the position, and enter updates last access. A character that is already in the world is stored and removed when that same character enters again. Shared mobs on the active map (`ti_cedric` by default) load from `data/spawns/*.json` (Mobius-style fixed `npcId` + x/y/z + respawn seconds); disabled maps stay in the datapack until multi-map init lands. The entering client then receives, in order, `UserInfo` (**1016**), `ItemList` (**1014**), `PlayerSpawned`, `NpcInfo` for each shared Gremlin still in interest (template **20001**, Cedric's Training Hall `-72674, 256819, -3112`, one object id each for the whole zone), and `CharInfo` for every other player already there. Those players receive your `CharInfo`, including paperdoll item ids. `MoveTo` is answered with `MoveToAck` to you and to anyone who still has you in knownlist. Leaving the zone, or walking out of interest, sends `DeleteObject`. `SelectTarget` → `MyTargetSelected`; `AttackRequest` applies flat melee damage to shared Gremlins and fans out `Attack` + `StatusUpdate` (`CUR_HP` / `MAX_HP`) to every player who sees the mob. At **0 HP** the server removes the NPC and sends `DeleteObject` to those clients, then schedules a datapack respawn (same object id, fresh `NpcInfo` when due). Mob death loot rolls from `data/drops/*.json` (Mobius-style death groups + optional corpse/spoil lines); each stack becomes ground `SpawnItem` (**1018**). `PickupRequest` (**14**) picks up ground items. With `FINEAGE_ZONE_ADMIN` enabled, opcodes **30** / **31** (`AdminSpoilTarget`, `AdminSweepCorpse`) exercise spoil/sweep before skills **254** / **42** land. `Say` (opcode **8**, key 0 text, key 1 chat type) delivers `CreatureSay` (**1012**). Type **0** reaches the speaker and players in knownlist within **1250**. Type **1** reaches the whole zone. Other types, including tell, party, and clan, answer with `SystemMessage` (**1040**) `not supported yet`. Enter world ends with a welcome `SystemMessage`. Text is trimmed, rejected when empty or over **100** characters, and limited to one accepted line per second. Layout: `docs/chat-v2-protocol.md`. `InventoryUpdate` (**1015**) is an add, modify, or remove row. Enter world sends the full `ItemList` instead. `UseItem` (**15**, key 0 object id) equips a bag item or unequips that same object when it is already worn. `UnequipItem` (**16**, key 0 paperdoll index 0..16) clears one slot. Success is one `InventoryUpdate` of modify rows for every changed item, then `UserInfo` to the owner, and `CharInfo` key 6 to known players. Replaced gear goes back to the bag. `GiveItem` (**17**, key 0 item id, key 1 count) adds to the bag and is off unless `FINEAGE_ZONE_ADMIN` is `1`, `true`, or `yes`. The layout and error codes (`not_owned`, `not_equippable`, `dead`, `blocked`, `bad_slot`, `empty`, `forbidden`, plus the equip condition codes) are on `Fineage.Protocol.InventoryProtocol`. Equip rejects `class_restricted`, `race_restricted`, `sex_restricted`, `hero_restricted`, `clan_leader_restricted`, `pledge_restricted`, `pvp_restricted`, `level_restricted`, `castle_restricted`, `clan_hall_restricted`, `pk_restricted`, `grade_restricted`, and `condition_restricted` without changing paperdoll. A higher crystal grade still equips; UserInfo keys 10–12 then carry the expertise penalty. A gear change is saved immediately and again on leave.

Item object ids start at **100000** and are unique across the zone. Creating a character stores the starter kit once (item id, count, equipped slot) on that character in `accounts.json`. The zone loads it on enter. A character saved before kits existed is granted the kit once, then saved. Disconnect writes the bag back. Zone uses the same `--store` / `FINEAGE_LOGIN_STORE` path as login (default `data/accounts.json`). A human fighter wears Squire's Sword **2369** in paperdoll slot **7**.

### Enter-world gear

MessagePack keys are additive. `CharInfo` keys 0–5 are unchanged; key **6** is new.

`UserInfo` (1016): `0` objectId, `1` name, `2` classId, `3` x, `4` y, `5` z, `6` runSpeed, `7` walkSpeed, `8` running, `9` paperdoll, `10` weaponGradePenalty, `11` armorGradePenalty, `12` gradePenalty. Paperdoll is 17 slots in order Under, Lear, Rear, Neck, LFinger, RFinger, Head, RHand, LHand, Gloves, Chest, Legs, Feet, Cloak, Face, Hair, Hair2. Each slot is `0` objectId, `1` itemId (both 0 when empty). Right hand is index **7**. Keys 10 and 11 are 0–4. Key 12 is the greater of the two, or 0. They stay 0 when `FINEAGE_EXPERTISE_PENALTY` is `0`, `false`, or `no` (the default is on).

`ItemList` (1014): `0` items. Each item is `0` objectId, `1` itemId, `2` count, `3` equippedSlot (paperdoll index, or **-1** in the bag), `4` bodySlot (`none`, `rhand`, `lhand`, `lrhand`, `chest`, `legs`, …), `5` kind (`weapon`, `armor`, `etc`), `6` icon, `7` weaponType (`sword`, `dagger`, `blunt`, …, or empty), `8` armorType (`light`, `heavy`, `magic`, or empty).

`InventoryUpdate` (1015): `0` changes. Each change is `0` change (**0** add, **1** modify, **2** remove), then the same item fields as `ItemList` shifted by one: `1` objectId, `2` itemId, `3` count, `4` equippedSlot, `5` bodySlot, `6` kind, `7` icon, `8` weaponType, `9` armorType.

`CharInfo` (1005) key `6` is an `int[17]` of item ids in the same paperdoll order. **0** means empty. Other clients still read keys 0–5.

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

See the commented block in `deploy/fineage.env.example`. Export the same meter with the OpenTelemetry .NET SDK (`OTEL_SERVICE_NAME`, `OTEL_EXPORTER_OTLP_ENDPOINT`) if you want a collector instead of `/metrics`.

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
