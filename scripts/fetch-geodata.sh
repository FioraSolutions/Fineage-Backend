#!/usr/bin/env bash
# Download L2J-format geodata regions (XX_YY.l2j) into data/geodata (gitignored).
# Source: public L2J geodata mirrored in github.com/artemijan/L2Shablya (config/data/geo).
# Default = Talking Island area. Usage: scripts/fetch-geodata.sh [16_24 17_25 ...]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIR="${FINEAGE_GEODATA_DIR:-$ROOT/data/geodata}"
mkdir -p "$DIR"
REGIONS=("$@")
if [[ ${#REGIONS[@]} -eq 0 ]]; then
  MANIFEST="$ROOT/data/geodata-regions.json"
  if [[ ! -f "$MANIFEST" ]]; then
    echo "missing $MANIFEST" >&2
    exit 1
  fi
  if ! command -v python3 >/dev/null 2>&1; then
    echo "python3 is required to read $MANIFEST" >&2
    exit 1
  fi
  mapfile -t REGIONS < <(python3 -c 'import json,sys; print("\n".join(json.load(open(sys.argv[1]))["regions"]))' "$MANIFEST")
fi
for r in "${REGIONS[@]}"; do
  [[ -s "$DIR/$r.l2j" ]] && { echo "have $r"; continue; }
  curl -fsSL -o "$DIR/$r.l2j" "https://raw.githubusercontent.com/artemijan/L2Shablya/master/config/data/geo/$r.l2j" && echo "got $r" || { rm -f "$DIR/$r.l2j"; echo "missing $r"; }
done
