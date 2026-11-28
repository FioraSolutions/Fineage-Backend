"""Shared guard for generators that read the Mobius CT 0 Interlude datapack.

One pin for every data importer: PINNED_MOBIUS_COMMIT in tools/import_npc_templates.py.
A generator calls require_pinned_ct0(path, "stats/skills", ...) before it reads anything:

  * the path must sit inside an L2J_Mobius_CT_0_Interlude directory (Epilogue, HighFive,
    Classic and every other chronicle are refused outright), and
  * the checkout's git HEAD must be the pinned commit, or a later commit whose given
    sub-trees are byte-identical to the pinned one. --allow-unpinned relaxes only this
    second check, never the chronicle check.
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from import_npc_templates import CHRONICLE_DIR, PINNED_MOBIUS_COMMIT  # noqa: E402


def ct0_root(path: Path) -> Path:
    """The L2J_Mobius_CT_0_Interlude directory containing `path`, or exit."""
    resolved = path.resolve()
    for candidate in (resolved, *resolved.parents):
        if candidate.name == CHRONICLE_DIR:
            return candidate
    raise SystemExit(
        f"refusing {path}: not inside an {CHRONICLE_DIR} datapack "
        "(Epilogue/HighFive/other chronicles must never feed data/)"
    )


def require_pinned_ct0(path: Path, *subtrees: str, allow_unpinned: bool = False) -> str:
    """Refuse non-CT0 input; require the pinned commit (or identical `subtrees`).

    `subtrees` are paths relative to the CT0 root, e.g. "dist/game/data/stats/skills".
    Returns a short description of what was verified.
    """
    root = ct0_root(path)

    def git(*args: str) -> subprocess.CompletedProcess:
        return subprocess.run(["git", "-C", str(root), *args], capture_output=True, text=True)

    head = git("rev-parse", "HEAD")
    if head.returncode != 0:
        if allow_unpinned:
            return "unpinned (not a git checkout, --allow-unpinned)"
        raise SystemExit(
            f"{root} is not a git checkout; cannot prove it is Mobius {PINNED_MOBIUS_COMMIT[:12]} "
            "(clone the pinned commit or pass --allow-unpinned)"
        )
    sha = head.stdout.strip()
    if sha == PINNED_MOBIUS_COMMIT:
        return f"pinned {sha[:12]}"
    top = git("rev-parse", "--show-toplevel").stdout.strip() or str(root)
    rels = [f"{CHRONICLE_DIR}/{s.strip('/')}" for s in subtrees]
    same = subprocess.run(
        ["git", "-C", top, "diff", "--quiet", PINNED_MOBIUS_COMMIT, sha, "--", *rels],
        capture_output=True,
        text=True,
    )
    if same.returncode == 0:
        return f"{sha[:12]} ({', '.join(subtrees)} identical to pinned {PINNED_MOBIUS_COMMIT[:12]})"
    if allow_unpinned:
        return f"unpinned {sha[:12]} (--allow-unpinned)"
    raise SystemExit(
        f"Mobius checkout is at {sha[:12]} and {', '.join(subtrees)} differ from pinned "
        f"{PINNED_MOBIUS_COMMIT[:12]} (or the pinned commit is not fetched). Update "
        "PINNED_MOBIUS_COMMIT deliberately, or pass --allow-unpinned."
    )
