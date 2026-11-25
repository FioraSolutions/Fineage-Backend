#!/usr/bin/env python3
"""Print enter/commands histogram stats from load-test report.json."""
import json
import sys
from pathlib import Path


def main() -> None:
    path = Path(sys.argv[1])
    data = json.loads(path.read_text())
    s = data.get("Server") or {}
    print(f"report: {path}")
    print(
        f"  commands (scrape peak p99): {s.get('CommandsStageP99Ms')} ms; "
        f"peak-scrape histogram p50/p99/mean: {s.get('CommandsStageP50Ms')}/"
        f"{s.get('CommandsStageP99Ms')} / {s.get('CommandsStageMeanMs')}"
    )
    stats = s.get("EnterPhaseStats") or {}
    enter_cmds = stats.get("fineage_tick_stage_ms/commands")
    if enter_cmds:
        print(
            f"  commands @ peak ingress scrape: count={enter_cmds['Count']} "
            f"mean={enter_cmds['Mean']:.2f} p50={enter_cmds['P50']} p99={enter_cmds['P99']}"
        )
    for name in sorted(k for k in stats if k.startswith("fineage_enter")):
        v = stats[name]
        print(f"  {name}: count={v['Count']} mean={v['Mean']:.2f} p50={v['P50']} p99={v['P99']}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("usage: print-enter-phase-stats.py <report.json>", file=sys.stderr)
        raise SystemExit(2)
    main()