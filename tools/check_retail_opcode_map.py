#!/usr/bin/env python3
"""Parse docs/wire-coverage.md, check it against the machine-readable map and NetOpcode.cs.

The markdown tables are the human-readable coverage report. docs/data/retail-net-opcode-map.json
is the same mapping in a form tests and CI can load. Disagreement is an error.

  python3 tools/check_retail_opcode_map.py           # verify
  python3 tools/check_retail_opcode_map.py --write    # regenerate the JSON from the markdown
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
COVERAGE = REPO / "docs" / "wire-coverage.md"
MAP_PATH = REPO / "docs" / "data" / "retail-net-opcode-map.json"
OPCODE_CS = REPO / "src" / "Fineage.Net.Contract" / "Opcodes" / "NetOpcode.cs"

SECTION_KEYS = {
    "Client → server": "c2s",
    "Client → server, extended 0xD0": "c2s-ex",
    "Server → client": "s2c",
}

HANDLER_VALUES = {"yes", "partial", "no", "n/a", "contract"}
IDENT = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
BACKTICK_NAME = re.compile(r"`([A-Za-z_][A-Za-z0-9_]*)`")
CONST_USHORT = re.compile(
    r"public const ushort ([A-Za-z_][A-Za-z0-9_]*)\s*=",
)


def parse_fineage_opcodes(cell: str) -> list[str]:
    cell = cell.strip()
    if not cell:
        return []
    if "`" in cell:
        names = BACKTICK_NAME.findall(cell)
        if not names:
            raise ValueError(f"cannot parse Fineage opcode cell: {cell!r}")
        return names
    names: list[str] = []
    for part in cell.split(","):
        part = part.strip()
        if not part:
            continue
        if not IDENT.match(part):
            raise ValueError(f"cannot parse Fineage opcode cell: {cell!r}")
        names.append(part)
    return names


def _cells(line: str) -> list[str]:
    return [c.strip() for c in line.strip().strip("|").split("|")]


def parse_coverage(text: str) -> tuple[list[dict], dict[str, dict[str, int]]]:
    entries: list[dict] = []
    counts: dict[str, dict[str, int]] = {}
    section: str | None = None
    in_counts = False
    for line in text.splitlines():
        if line.startswith("## "):
            title = line[3:].strip()
            in_counts = title.startswith("Counts")
            section = SECTION_KEYS.get(title)
            continue
        if not line.startswith("|"):
            continue
        cells = _cells(line)
        if not cells or set(cells[0]) <= set("-:"):
            continue
        if in_counts:
            if cells[0] in {"Direction", "C2S", "C2S extended (0xD0)", "S2C"}:
                if cells[0] != "Direction":
                    key = {
                        "C2S": "c2s",
                        "C2S extended (0xD0)": "c2s-ex",
                        "S2C": "s2c",
                    }[cells[0]]
                    counts[key] = {
                        "packets": int(cells[1]),
                        "serverYes": int(cells[2]),
                        "serverPartial": int(cells[3]),
                        "serverNo": int(cells[4]),
                        "serverNA": int(cells[5]),
                        "clientYes": int(cells[6]),
                        "clientContract": int(cells[7]),
                        "clientNo": int(cells[8]),
                        "clientNA": int(cells[9]),
                    }
            continue
        if section is None:
            continue
        if cells[0] in {"Opcode", "Direction"}:
            continue
        if section == "s2c":
            if len(cells) < 7:
                raise ValueError(f"s2c row needs 7 columns: {line}")
            entry = {
                "direction": section,
                "retailOpcode": cells[0],
                "mobiusEnum": cells[1],
                "mobiusClass": cells[2],
                "serverHandler": cells[3],
                "clientHandler": cells[4],
                "fineageOpcodes": parse_fineage_opcodes(cells[5]),
                "gap": cells[6],
            }
        else:
            if len(cells) < 6:
                raise ValueError(f"{section} row needs 6 columns: {line}")
            entry = {
                "direction": section,
                "retailOpcode": cells[0],
                "mobiusClass": cells[1],
                "serverHandler": cells[2],
                "clientHandler": cells[3],
                "fineageOpcodes": parse_fineage_opcodes(cells[4]),
                "gap": cells[5],
            }
        for key in ("serverHandler", "clientHandler"):
            if entry[key] not in HANDLER_VALUES:
                raise ValueError(f"unexpected {key} {entry[key]!r} on {entry['retailOpcode']}")
        entries.append(entry)
    return entries, counts


def opcode_names(cs_text: str) -> set[str]:
    return set(CONST_USHORT.findall(cs_text))


def tally(entries: list[dict]) -> dict[str, dict[str, int]]:
    out: dict[str, dict[str, int]] = {}
    for direction, group in _by_direction(entries).items():
        server = Counter(e["serverHandler"] for e in group)
        client = Counter(e["clientHandler"] for e in group)
        out[direction] = {
            "packets": len(group),
            "serverYes": server.get("yes", 0),
            "serverPartial": server.get("partial", 0),
            "serverNo": server.get("no", 0),
            "serverNA": server.get("n/a", 0),
            "clientYes": client.get("yes", 0),
            "clientContract": client.get("contract", 0),
            "clientNo": client.get("no", 0),
            "clientNA": client.get("n/a", 0),
        }
    return out


def _by_direction(entries: list[dict]) -> dict[str, list[dict]]:
    grouped: dict[str, list[dict]] = {"c2s": [], "c2s-ex": [], "s2c": []}
    for entry in entries:
        grouped[entry["direction"]].append(entry)
    return grouped


def normalize(entries: list[dict]) -> list[dict]:
    """Stable JSON shape (drop empty optional keys consistently)."""
    out = []
    for e in entries:
        item = {
            "direction": e["direction"],
            "retailOpcode": e["retailOpcode"],
        }
        if e.get("mobiusEnum"):
            item["mobiusEnum"] = e["mobiusEnum"]
        item["mobiusClass"] = e["mobiusClass"]
        item["serverHandler"] = e["serverHandler"]
        item["clientHandler"] = e["clientHandler"]
        item["fineageOpcodes"] = list(e["fineageOpcodes"])
        item["gap"] = e.get("gap", "")
        out.append(item)
    return out


def load_map(path: Path) -> list[dict]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict) or "entries" not in data:
        raise ValueError(f"{path} must be an object with an 'entries' array")
    return normalize(data["entries"])


def build_document(entries: list[dict], counts: dict[str, dict[str, int]]) -> dict:
    mapped = sum(1 for e in entries if e["fineageOpcodes"])
    unmapped = len(entries) - mapped
    names: set[str] = set()
    for e in entries:
        names.update(e["fineageOpcodes"])
    return {
        "version": 1,
        "source": {
            "wireCoverage": "docs/wire-coverage.md",
            "netOpcode": "src/Fineage.Net.Contract/Opcodes/NetOpcode.cs",
        },
        "notes": (
            "Machine-readable retail Interlude opcode → Fineage NetOpcode mapping. "
            "A non-empty fineageOpcodes list is a name mapping only: it does not mean "
            "gameplay behaviour has been verified (see docs/mobius-parity-matrix.md). "
            "Handler cells are wiring coverage from docs/wire-coverage.md."
        ),
        "summary": {
            "packets": len(entries),
            "mapped": mapped,
            "unmapped": unmapped,
            "uniqueFineageOpcodes": len(names),
            "counts": tally(entries),
        },
        "entries": normalize(entries),
    }


def compare_entries(from_md: list[dict], from_json: list[dict]) -> list[str]:
    errors: list[str] = []
    if len(from_md) != len(from_json):
        errors.append(f"entry count markdown={len(from_md)} json={len(from_json)}")
    for i, (a, b) in enumerate(zip(from_md, from_json)):
        if a != b:
            errors.append(
                f"entry {i} {a.get('direction')}:{a.get('retailOpcode')} disagrees "
                f"(markdown={a} json={b})"
            )
            if len(errors) > 20:
                errors.append("… further disagreements omitted")
                break
    if len(from_md) != len(from_json) and not any("entry count" in e for e in errors):
        pass
    extra = from_json[len(from_md) :]
    missing = from_md[len(from_json) :]
    if extra:
        errors.append(f"json has {len(extra)} extra entries starting at {extra[0]}")
    if missing:
        errors.append(f"json is missing {len(missing)} entries starting at {missing[0]}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="regenerate the JSON from the markdown")
    args = parser.parse_args()

    text = COVERAGE.read_text(encoding="utf-8")
    entries, stated = parse_coverage(text)
    computed = tally(entries)
    errors: list[str] = []

    if stated != computed:
        errors.append(f"wire-coverage.md summary table does not match the packet tables:\n  stated={stated}\n  computed={computed}")

    seen: dict[tuple[str, str], int] = {}
    for e in entries:
        key = (e["direction"], e["retailOpcode"])
        if key in seen:
            errors.append(f"duplicate mapping {key}")
        seen[key] = 1

    names = opcode_names(OPCODE_CS.read_text(encoding="utf-8"))
    unknown: list[str] = []
    for e in entries:
        for name in e["fineageOpcodes"]:
            if name not in names:
                unknown.append(f"{e['direction']} {e['retailOpcode']} → {name}")
    if unknown:
        errors.append("Fineage opcode names not in NetOpcode.cs:\n  " + "\n  ".join(unknown))

    document = build_document(entries, computed)

    if args.write:
        MAP_PATH.parent.mkdir(parents=True, exist_ok=True)
        MAP_PATH.write_text(json.dumps(document, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"wrote {MAP_PATH.relative_to(REPO)} ({document['summary']['packets']} entries, "
              f"{document['summary']['mapped']} mapped, {document['summary']['unmapped']} unmapped)")
        if errors:
            print("warnings/errors still present:", file=sys.stderr)
            for err in errors:
                print(err, file=sys.stderr)
            return 1
        return 0

    if not MAP_PATH.is_file():
        errors.append(f"missing {MAP_PATH.relative_to(REPO)} — run with --write")
    else:
        disk = json.loads(MAP_PATH.read_text(encoding="utf-8"))
        # Compare entries and the summary counters that must stay honest.
        errors.extend(compare_entries(document["entries"], normalize(disk.get("entries", []))))
        want = document["summary"]
        got = disk.get("summary") or {}
        for key in ("packets", "mapped", "unmapped", "uniqueFineageOpcodes"):
            if want.get(key) != got.get(key):
                errors.append(f"JSON summary.{key} is {got.get(key)!r}, expected {want.get(key)!r}")
        if want.get("counts") != got.get("counts"):
            errors.append(f"JSON summary.counts disagree: json={got.get('counts')} expected={want.get('counts')}")

    if errors:
        print("retail opcode map check failed:", file=sys.stderr)
        for err in errors:
            print(err, file=sys.stderr)
        return 1

    print(
        f"retail opcode map ok: {document['summary']['packets']} packets, "
        f"{document['summary']['mapped']} mapped, {document['summary']['unmapped']} unmapped, "
        f"{document['summary']['uniqueFineageOpcodes']} unique NetOpcode names"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
