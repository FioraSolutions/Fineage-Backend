#!/usr/bin/env python3
"""Vendor Mobius CT0 Interlude quest dialog HTML for hand-port generators.

Copies *.htm / *.html from the Mobius quest script tree into
data/mobius-quest-dialogs/scripts/quests/ (tracked in git; ref/ is local-only).

Source (first match):
  .cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/scripts/quests
  ref/mobius-ct0-interlude/dist/game/data/scripts/quests

Usage:
  python3 scripts/import_mobius_quest_html.py              # all quest folders
  python3 scripts/import_mobius_quest_html.py --folder Q00169_OffspringOfNightmares
  python3 scripts/import_mobius_quest_html.py --dry-run
"""
from __future__ import annotations

import argparse
import pathlib
import shutil
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio  # noqa: E402

HTML_SUFFIXES = {".htm", ".html"}


def copy_folder(src: pathlib.Path, dest: pathlib.Path, *, dry_run: bool) -> int:
    if not src.is_dir():
        raise SystemExit(f"source folder missing: {src}")
    dest.mkdir(parents=True, exist_ok=True)
    copied = 0
    for path in sorted(src.iterdir()):
        if not path.is_file() or path.suffix.lower() not in HTML_SUFFIXES:
            continue
        target = dest / path.name
        copied += 1
        if dry_run:
            continue
        shutil.copy2(path, target)
    return copied


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--folder",
        action="append",
        dest="folders",
        metavar="Q00xxx_Name",
        help="Import only this Mobius quest script folder (repeatable).",
    )
    parser.add_argument("--dry-run", action="store_true", help="Report counts only.")
    args = parser.parse_args()

    source_root = questio.mobius_quest_source_root()
    vendored_root = questio.VENDORED_MOBIUS_QUEST_DIALOGS
    questio.ensure_meta()

    if args.folders:
        folders = args.folders
    else:
        folders = sorted(
            p.name
            for p in source_root.iterdir()
            if p.is_dir() and p.name.startswith("Q")
        )

    total_files = 0
    for name in folders:
        n = copy_folder(source_root / name, vendored_root / name, dry_run=args.dry_run)
        if n:
            print(f"{name}: {n} html file(s)")
        total_files += n

    action = "would copy" if args.dry_run else "copied"
    print(f"{action} {total_files} dialog file(s) → {vendored_root.relative_to(ROOT)}")


if __name__ == "__main__":
    main()