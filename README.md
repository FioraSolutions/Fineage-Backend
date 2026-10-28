# Fineage-Backend

Fineage C# Login+Zone game server (V2 MessagePack primary, Interlude bridge).

C# login server for Fineage. V2 MessagePack is the primary wire. The Unity client’s Interlude listener stays in [Fineage `tools/login-stub`](https://github.com/FioraSolutions/Fineage/tree/dev/tools/login-stub) so overnight play on `2106` / `7777` keeps working. Zone gameplay stays in Fineage `tools/zone-server` until that process moves here.

`src/Fineage.Protocol` holds the shared codecs (V2 MessagePack, pipeline TCP helpers, Interlude Blowfish).

Login issues a handoff token the current ZoneServer can accept: put the account in `Auth.Account` and the token in `Auth.Password`, then connect to `127.0.0.1:9777`. Zone today accepts any password. The token is HMAC-signed so a later zone build can call `HandoffTokens.TryValidate` without a new auth shape.

## Flow

`Hello` → `Register` or `Auth` → `CreateCharacter` (Human fighter `0` or mystic `10`, male) → `SelectCharacter` → `Handoff`.

`Handoff.Token` is the zone session key. Default listen port is **9106**. Characters are stored in a local JSON file (`data/accounts.json`, or `--store` / `FINEAGE_LOGIN_STORE`).

## Run

```bash
dotnet test
dotnet run --project src/Fineage.Login -- --selftest
dotnet run --project src/Fineage.Login -- play <account> <password> <name> fighter
dotnet run --project src/Fineage.Login -- --port 9106
```

Override the HMAC secret with `FINEAGE_HANDOFF_SECRET`. The dev default is `FineageLoginHandoffV1`.

## Related

- Client: https://github.com/FioraSolutions/Fineage
- [Fineage #14](https://github.com/FioraSolutions/Fineage/issues/14) — LoginServer v2 auth + session handoff
