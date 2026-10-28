# Fineage-Backend

Fineage C# Login+Zone game server (V2 MessagePack primary, Interlude bridge).

C# login and zone servers for Fineage. V2 MessagePack is the primary wire. The Unity client’s Interlude listener stays in [Fineage `tools/login-stub`](https://github.com/FioraSolutions/Fineage/tree/dev/tools/login-stub) so overnight play on `2106` / `7777` keeps working.

`src/Fineage.Protocol` holds the shared codecs (V2 MessagePack, pipeline TCP helpers, Interlude Blowfish).

Login issues an HMAC handoff token. The zone on **9777** accepts it as `Auth.Password` (`Auth.Account` is the account) and checks it with `HandoffTokens.TryValidate`. Both processes share `FINEAGE_HANDOFF_SECRET` (dev default `FineageLoginHandoffV1`).

## Flow

`Hello` → `Register` or `Auth` → `CreateCharacter` (Human fighter `0` or mystic `10`, male) → `SelectCharacter` → `Handoff`.

Then, on the zone: `Hello` → `Auth` (account + handoff token) → `EnterWorld` on Talking Island (`-84318, 244578, -3730`, tile 17_25) → three static Gremlin `NpcInfo` (template **20001**) → `MoveTo` / `MoveToAck`.

`Handoff.Token` is the zone session key. Login listens on **9106**. Zone listens on **9777**. Characters are stored in a local JSON file (`data/accounts.json`, or `--store` / `FINEAGE_LOGIN_STORE`).

## Run

```bash
dotnet test
dotnet run --project src/Fineage.Login -- --selftest
dotnet run --project src/Fineage.Zone -- --selftest
dotnet run --project src/Fineage.Login -- --port 9106
dotnet run --project src/Fineage.Zone -- --port 9777
```

There is no deploy workflow in this repo. CI is `.github/workflows/build-and-test.yml` (`dotnet test` on pull requests and pushes).

Login CLI, same store:

```bash
dotnet run --project src/Fineage.Login -- play <account> <password> <name> fighter
```

## Related

- Client: https://github.com/FioraSolutions/Fineage
- [Fineage #14](https://github.com/FioraSolutions/Fineage/issues/14) — LoginServer v2 auth + session handoff
