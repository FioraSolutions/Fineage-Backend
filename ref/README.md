# Original sources (local only — gitignored)

| Path | Origin | Use |
|------|--------|-----|
| `ref/mobius-ct0-interlude/` | `~/Downloads/L2J_Mobius_CT_0_Interlude_src` | Server gameplay SoT (L2J Mobius CT0 Interlude) |

Client Interlude tree for UI/assets lives in the Fineage repo: `Fineage/ref/l2-interlude-client/`.

**Rule:** Zone/Game business logic must match Mobius CT0 unless a ticket documents a deliberate Fineage divergence.

```bash
rsync -a --delete --exclude '.git' ~/Downloads/L2J_Mobius_CT_0_Interlude_src/ ref/mobius-ct0-interlude/
```
