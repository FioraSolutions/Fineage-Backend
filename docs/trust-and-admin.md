# Trust and admin contract (#991)

Status: implemented on `dev`. This is the shared security boundary for GM/admin actions, the
append-only audit history, operation idempotency, and the client trust boundary. Mobius CT0 Interlude
remains the gameplay reference; this document covers server authority, not gameplay formulas.

## 1. Role policy

`ZoneAdmin` resolves one explicit role per character:

| Role | Meaning |
|------|---------|
| `Player` | no admin surface |
| `Helper` | read-only admin menus (`admin_html`, teleport menus, `//loc`, `//towns`) |
| `Gm` | ordinary GM actions: teleport, buff, kick, spawn, setlevel/class, non-account punishments |
| `Admin` | everything Gm can do, plus economy grants, account bans, boss/siege/Olympiad admin opcodes |

`data/admin-characters.json` keeps the legacy form:

```json
{ "names": ["KuK"] }
```

Legacy names grant `Admin`. The extended form is:

```json
{
  "names": ["LegacyAdmin"],
  "roles": {
    "HelperOne": "helper",
    "GmOne": "gm",
    "KuK": "admin"
  }
}
```

Unknown role words, an empty allowlist, a missing file, or an unreadable file are **fail-closed**: no
character gets a role, and the concrete path/error is logged. There is no silent `KuK` fallback.

### Environment semantics

`FINEAGE_ENV` is authoritative (falling back to `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT`):

- `production`, `prod`, `live` → production
- `development`, `dev`, `local`, `test`, `testing` → development
- unset → development
- anything else → startup error naming the accepted values

A wildcard `*` allowlist is accepted only in development (local playtests). Production rejects it,
logs the file path and reason, and grants no wildcard role. Production deployments set
`FINEAGE_ENV=production`; local scripts set `development`.

## 2. Central dispatch

All client-facing admin surfaces go through the shared role map in
`src/Fineage.Zone/Security/AdminCommandCatalog.cs`:

- `ZoneWorld.TryAdminBypass` — Mobius `admin_*` bypass verbs.
- `ZoneWorld.TryGmChat` — `//teleport`, `//kick`, … GM chat verbs.
- `WorldEventPacketHandler` — `AdminBossSchedule`, `AdminCaptureCastle`,
  `AdminReportOlympiadResult` Net opcodes.

The env flag `FINEAGE_ZONE_ADMIN` only enables a surface; it never authorizes a character. Net admin
opcodes additionally require `ZoneAdmin.RoleOf(session.Name) >= catalog minimum role`. Direct
`ZoneWorld.TryAdmin*Async` APIs remain trusted internal/test calls and are not a client boundary;
the Net handlers enforce RBAC before submitting their commands.

## 3. Audit contract

`GmAuditLog` appends one JSON object per line to `gm-audit.jsonl` under the state directory. Every
client admin surface uses the same sink as punishments.

```json
{
  "version": 1,
  "atUnixMs": 1800000000000,
  "gm": "KuK",
  "gmObjectId": 1,
  "action": "admin_setlevel",
  "target": "5",
  "before": "{\"ObjectId\":1,\"Level\":1,...}",
  "after": "{\"ObjectId\":1,\"Level\":5,...}",
  "source": "client-bypass",
  "detail": "ok"
}
```

- `version` is the record contract version (`CurrentVersion = 1`).
- `gm` / `gmObjectId` identify who acted.
- `action` is the verb or Net opcode action; `target` is the argument, selected object, or request key.
- `before` / `after` are compact server-state snapshots (actor plus an online target where available).
- `when` is `atUnixMs`, read from the injected `IZoneTime` so audits follow the same wall clock as the
  simulation (not a separate `DateTimeOffset.UtcNow`).
- `source` is `client-bypass`, `client-chat`, `net-opcode`, `punishment`, or `internal`.
- `detail` is `ok`, the refusal reason, or `denied role=... required=...`.

The file is append-only and bounded: `maxBytes` default 8 MiB, `maxArchives` default 2. On overflow
the live file rotates to `.1`, `.1` to `.2`, and the oldest archive is overwritten. Writes are
serialized per path and a failed audit append is logged with the path and action; it is never silently
dropped. Denied attempts are audited too.

## 4. Operation idempotency

The Net protocol has **no semantic operation key**. `PacketEnvelope` carries only `RequestSequence`
(plus opcode/payload). `CommandDeduper` is the shared retry key: a sequence less than or equal to the
connection high-water mark is dropped before the command reaches the world. Economic commands reuse
the existing `EconomyLedger` idempotency (`LedgerTxId = tick + sequence within tick`).

Consequently:

- Ten duplicate `RequestBuyItem` frames carrying the same `RequestSequence` on one connection are
  accepted once and post one `ShopBuy` ledger transaction. This is covered by
  `OperationIdempotencyTests`.
- A retry after reconnect, or the same purchase with a fresh sequence, cannot be proven duplicate
  without a client-supplied operation id. The protocol does not carry one, so this milestone does not
  invent one; the blocker is explicit here rather than adding a fake per-handler cache.
- A future semantic idempotency key requires a versioned envelope field and client support. Ledger
  ids remain the authoritative committed-move index until then.

## 5. Client trust boundary

Client requests carry intent only. The server owns authoritative state:

| Client input | Server rule |
|--------------|-------------|
| `MoveInput` destination | starts a timed leg at server speed/path; never teleports |
| `ValidatePosition` XYZ | compared against the authoritative position; server sends correction, never adopts client XYZ/Z |
| `AttackRequest` | target/forced only; damage, crit, and HP are server rolls |
| `UseSkill` | skill/target/ctrl only; damage and reuse come from skill data + server `SkillReuseUntilMs` |
| item `Count` | validated against the live stack; an oversized destroy/drop is refused without mutation |
| `ShopBuy` count | validated against inventory/weight/slots/stock; client cannot mint |
| cooldown | server reuse timestamps; a second press cannot clear or shorten them |

Tests: `ClientTrustBoundaryTests` (position, damage/cooldown contract, count, cooldown) and
`AdminRbacTests` (role matrix, production wildcard, all registered bypass verbs, audit shape/rotation).

## 6. Focused verification

```bash
dotnet build Fineage.Backend.slnx
dotnet test tests/Fineage.Zone.Tests/Fineage.Zone.Tests.csproj \
  --filter "FullyQualifiedName~AdminRbacTests|FullyQualifiedName~ClientTrustBoundaryTests|FullyQualifiedName~OperationIdempotencyTests"
dotnet format Fineage.Backend.slnx --verify-no-changes
```

## Document history

| Date | Note |
|------|------|
| 2026-11-27 | #991 RBAC, versioned GM audit + rotation, idempotency boundary, trust tests. |
