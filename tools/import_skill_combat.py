#!/usr/bin/env python3
"""Pin data/skill-combat.json's transcribed fields to Mobius CT 0 Interlude.

skill-combat.json rows carry hand-added fields (kind, blow*, lethal*, castCondition) that
no XML supplies, but these fields are straight transcriptions of CT 0 stats/skills and
must equal it:

  name, levels, power[], mpConsume[], mpInitialConsume[], hitTimeMs, coolTimeMs,
  castRange, effectRange, magic, overHit, magicLevel[]

(`magicLevel` -1 is Fineage's "no level check" sentinel and equals CT 0 0/absent.)
usingKinds, reuseDelayMs and the hand-added fields are not touched or checked here.

The source must be the pinned CT 0 commit (tools/mobius_pin.py). Epilogue/HighFive input is
refused: the Nov 4 import had mixed their NPC skill numbers (mp folded with the initial
cost, Valakas/Frintezza power, Cubic Drain with 16 ranks) into this file.

Usage:
  python3 tools/import_skill_combat.py <L2J_Mobius_CT_0_Interlude> [--write] [--allow-unpinned]
Default is --check: exit 1 and list every differing row.
"""
from __future__ import annotations

import copy
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "tools"))
sys.path.insert(0, str(REPO / "scripts"))
import mobius_pin  # noqa: E402
import sync_skill_defs_from_mobius as sync  # noqa: E402

SKILLS_REL = "dist/game/data/stats/skills"
COMBAT_JSON = REPO / "data" / "skill-combat.json"
GUARDED = (
    "name", "levels", "power", "mpConsume", "mpInitialConsume", "hitTimeMs", "coolTimeMs",
    "castRange", "effectRange", "magic", "overHit", "magicLevel",
)


def ct0_row(row: dict, ms: dict) -> dict:
    """`row` with every guarded field replaced by its CT 0 value."""
    out = copy.deepcopy(row)
    sync.apply_combat(out, ms)
    for key in GUARDED:
        if key == "magicLevel" and all(v < 0 for v in row.get("magicLevel") or [0]) \
                and all(v <= 0 for v in out.get("magicLevel") or []):
            out["magicLevel"] = row["magicLevel"] if "magicLevel" in row else out.get("magicLevel")
        elif key == "castRange" and row.get("castRange") == -1:
            out["castRange"] = -1  # AoE/self sentinel when CT 0 publishes none (apply_combat keeps it)
    # CT 0 publishes no table for some fields (e.g. Cubic Drain mpConsume): the per-level arrays must
    # still have exactly `levels` entries, so cut leftovers from a longer chronicle and pad with zeros.
    n = out["levels"]
    for key in ("power", "mpConsume", "mpInitialConsume"):
        vals = out.get(key)
        if isinstance(vals, list) and len(vals) != n:
            out[key] = vals[:n] + [type(vals[0])(0)] * (n - len(vals)) if vals else vals
    return out


def drift(raw_rows: list[dict], mobius: dict[int, dict]) -> dict[int, list[str]]:
    bad: dict[int, list[str]] = {}
    for row in raw_rows:
        ms = mobius.get(row["skillId"])
        if ms is None:
            bad[row["skillId"]] = ["not in CT 0 stats/skills"]
            continue
        want = ct0_row(row, ms)
        fields = [k for k in GUARDED if want.get(k) != row.get(k)]
        if fields:
            bad[row["skillId"]] = fields
    return bad


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    flags = {a for a in sys.argv[1:] if a.startswith("--")}
    if len(args) != 1 or flags - {"--write", "--allow-unpinned"}:
        raise SystemExit(__doc__)
    skills_dir = Path(args[0]) / SKILLS_REL
    if not skills_dir.is_dir():
        # accept the datapack root or the skills dir itself
        skills_dir = Path(args[0]) if Path(args[0]).name == "skills" else skills_dir
    verified = mobius_pin.require_pinned_ct0(skills_dir, SKILLS_REL, allow_unpinned="--allow-unpinned" in flags)
    mobius = sync.parse_mobius_skills(skills_dir)

    raw = COMBAT_JSON.read_text(encoding="utf-8")
    doc = json.loads(raw)
    bad = drift(doc["skills"], mobius)
    print(f"skill-combat.json vs Mobius CT 0 [{verified}]: {len(doc['skills'])} rows, {len(bad)} differ")
    for sid in sorted(bad):
        print(f"  {sid} {next(r['name'] for r in doc['skills'] if r['skillId'] == sid)}: {', '.join(bad[sid])}")
    if "--write" not in flags:
        return 1 if bad else 0
    if not bad:
        return 0
    rows = {r["skillId"]: r for r in doc["skills"]}
    updated = {sid: ct0_row(rows[sid], mobius[sid]) for sid in bad if sid in mobius}
    COMBAT_JSON.write_text(sync.replace_skill_objects(raw, updated, indent=1, obj_indent=2), encoding="utf-8")
    print(f"rewrote {len(updated)} rows")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
