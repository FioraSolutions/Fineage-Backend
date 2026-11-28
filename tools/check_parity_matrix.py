#!/usr/bin/env python3
"""Validate the canonical Mobius parity matrix (docs/mobius-parity-matrix.md).

The matrix has three independent levels: Implemented, Parity verified and Production verified.
A verified (or partially implemented) cell must carry a concrete evidence link. The counters in
the `## Counts` section are derived from the rows, so a stale or inflated counter fails here.

This is a fast structural check; it does not run gameplay tests. A passing check means the
document is honest about *which* evidence it has, not that any behaviour is correct.

Usage: tools/check_parity_matrix.py [--matrix docs/mobius-parity-matrix.md]
"""
from __future__ import annotations

import argparse
import os
import re
import sys
from collections import Counter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MARKERS = {"✅": "verified", "🟡": "partial", "❌": "missing", "unverified": "unverified", "n/a": "n/a"}
STATUS_COLUMNS = ("Implemented", "Parity verified", "Production verified")
LINK_RE = re.compile(r"\[[^\]]+\]\(([^)]+)\)")


def fail(errors: list[str], message: str) -> None:
    errors.append(message)


def cells(line: str) -> list[str]:
    return [c.strip() for c in line.strip().strip("|").split("|")]


def marker(cell: str) -> str | None:
    for key, value in MARKERS.items():
        if key in cell:
            return value
    return None


def slug(heading: str) -> str:
    text = heading.strip().lower()
    text = re.sub(r"[`*_~]", "", text)
    text = re.sub(r"[^a-z0-9\- ]+", "", text)
    return text.replace(" ", "-")


def section(lines: list[str], heading: str) -> tuple[int, int] | None:
    start = None
    for i, line in enumerate(lines):
        if line.strip() == heading:
            start = i + 1
            break
    if start is None:
        return None
    end = len(lines)
    for i in range(start, len(lines)):
        if lines[i].startswith("## "):
            end = i
            break
    return start, end


def evidence_targets(cell: str) -> list[str]:
    return LINK_RE.findall(cell)


def check_link(target: str, lines: list[str], anchors: set[str], errors: list[str], where: str) -> None:
    if target.startswith(("http://", "https://")):
        return
    if target.startswith("#"):
        if target[1:] not in anchors:
            fail(errors, f"{where}: anchor link {target} has no matching heading")
        return
    path = target.split("#", 1)[0]
    resolved = os.path.normpath(os.path.join(ROOT, "docs", path))
    if not os.path.exists(resolved):
        fail(errors, f"{where}: evidence path does not exist: {target}")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--matrix", default=os.path.join(ROOT, "docs", "mobius-parity-matrix.md"))
    args = ap.parse_args()

    lines = open(args.matrix, encoding="utf-8").read().splitlines()
    anchors = {slug(line.lstrip("# ")) for line in lines if line.startswith("#")}
    bounds = section(lines, "## Matrix")
    if bounds is None:
        print("FAIL: no '## Matrix' section", file=sys.stderr)
        return 1

    errors: list[str] = []
    rows: list[dict[str, object]] = []
    start, end = bounds
    for i in range(start, end):
        raw = lines[i]
        if not raw.startswith("|"):
            continue
        parts = cells(raw)
        if not parts or not re.fullmatch(r"\d+", parts[0]):
            continue
        if len(parts) != 7:
            fail(errors, f"matrix line {i + 1}: expected 7 columns, got {len(parts)}")
            continue
        number = int(parts[0])
        statuses = [marker(parts[j]) for j in (2, 3, 4)]
        if any(s is None for s in statuses):
            fail(errors, f"row {number}: status cell missing a recognised marker")
            continue
        row = {
            "number": number,
            "system": parts[1],
            "statuses": statuses,
            "cells": parts[2:5],
        }
        rows.append(row)

        for level, status, cell in zip(STATUS_COLUMNS, statuses, parts[2:5]):
            where = f"row {number} {level}"
            targets = evidence_targets(cell)
            if status in ("verified", "partial") and not targets:
                fail(errors, f"{where}: '{status}' cell has no evidence link")
            for target in targets:
                check_link(target, lines, anchors, errors, where)

        impl, parity, production = statuses
        if impl == "n/a" and (parity != "n/a" or production != "n/a"):
            fail(errors, f"row {number}: n/a implementation requires n/a parity and production")
        if impl == "missing" and (parity != "unverified" or production != "unverified"):
            fail(errors, f"row {number}: missing implementation cannot have parity/production evidence")
        if (parity == "verified" or production == "verified") and impl != "verified":
            fail(errors, f"row {number}: parity/production verified requires a verified implementation")

    numbers = [int(r["number"]) for r in rows]
    duplicates = sorted(n for n, count in Counter(numbers).items() if count > 1)
    if duplicates:
        fail(errors, f"duplicate row numbers: {duplicates}")
    if numbers != list(range(1, len(rows) + 1)):
        fail(errors, "matrix row numbers must be contiguous from 1")

    impl = Counter(str(r["statuses"][0]) for r in rows)
    parity = Counter(str(r["statuses"][1]) for r in rows)
    production = Counter(str(r["statuses"][2]) for r in rows)
    expected = {
        "Implemented": [impl["verified"], impl["partial"], impl["missing"], impl["n/a"], len(rows)],
        "Parity verified": [parity["verified"], parity["partial"], parity["unverified"], parity["n/a"], len(rows)],
        "Production verified": [production["verified"], production["partial"], production["unverified"], production["n/a"], len(rows)],
    }

    counts_bounds = section(lines, "## Counts")
    if counts_bounds is None:
        fail(errors, "no '## Counts' section")
    else:
        got: dict[str, list[int]] = {}
        for i in range(*counts_bounds):
            raw = lines[i]
            if not raw.startswith("|"):
                continue
            parts = cells(raw)
            if len(parts) != 6 or parts[0] not in expected:
                continue
            try:
                got[parts[0]] = [int(p) for p in parts[1:]]
            except ValueError:
                fail(errors, f"counts line {i + 1}: non-integer count")
        for name, values in expected.items():
            if name not in got:
                fail(errors, f"counts table is missing the '{name}' row")
            elif got[name] != values:
                fail(errors, f"counts table '{name}' is {got[name]}, rows say {values}")
        for name in got:
            if name not in expected:
                fail(errors, f"counts table has unknown row '{name}'")

    if errors:
        print(f"FAIL: {len(errors)} parity-matrix error(s)", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print(
        "OK: {rows} rows; implemented {i}/parity {p}/production {v} verified; "
        "no verified cell lacks evidence".format(
            rows=len(rows),
            i=expected["Implemented"][0],
            p=expected["Parity verified"][0],
            v=expected["Production verified"][0],
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
