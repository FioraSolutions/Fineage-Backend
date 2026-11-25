#!/usr/bin/env bash
# Download L2J-format geodata regions (XX_YY.l2j) into data/geodata (gitignored).
# Source: public L2J geodata mirrored in github.com/artemijan/L2Shablya (config/data/geo).
# Default list is data/geodata-regions.json. A missing file is named on stderr and the script exits 1.
# Usage: scripts/fetch-geodata.sh [16_24 17_25 ...]
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIR="${FINEAGE_GEODATA_DIR:-$ROOT/data/geodata}"
mkdir -p "$DIR"
REGIONS=("$@")
if [[ ${#REGIONS[@]} -eq 0 ]]; then
  MANIFEST="${FINEAGE_GEODATA_MANIFEST:-}"
  if [[ -z "$MANIFEST" ]]; then
    for candidate in \
      "$ROOT/data/geodata-regions.json" \
      "$SCRIPT_DIR/zone/data/geodata-regions.json" \
      "$ROOT/zone/data/geodata-regions.json" \
      "$ROOT/../data/geodata-regions.json"
    do
      if [[ -f "$candidate" ]]; then
        MANIFEST="$candidate"
        break
      fi
    done
  fi
  if [[ -z "$MANIFEST" || ! -f "$MANIFEST" ]]; then
    echo "missing region manifest geodata-regions.json" >&2
    exit 1
  fi
  if ! command -v python3 >/dev/null 2>&1; then
    echo "python3 is required to read $MANIFEST" >&2
    exit 1
  fi
  mapfile -t REGIONS < <(python3 -c 'import json,sys; print("\n".join(json.load(open(sys.argv[1]))["regions"]))' "$MANIFEST")
fi
if [[ ${#REGIONS[@]} -eq 0 ]]; then
  echo "missing region file list (geodata-regions.json regions is empty)" >&2
  exit 1
fi
missing=()
for r in "${REGIONS[@]}"; do
  if [[ ! "$r" =~ ^[0-9]+_[0-9]+$ ]]; then
    echo "missing region file ${r}.l2j" >&2
    missing+=("$r")
    continue
  fi
  if [[ -s "$DIR/$r.l2j" ]]; then
    echo "have $r"
    continue
  fi
  url="https://raw.githubusercontent.com/artemijan/L2Shablya/master/config/data/geo/${r}.l2j"
  if curl -fsSL -o "$DIR/$r.l2j" "$url"; then
    echo "got $r"
  else
    rm -f "$DIR/$r.l2j"
    echo "missing region file ${r}.l2j" >&2
    missing+=("$r")
  fi
done
if [[ ${#missing[@]} -gt 0 ]]; then
  echo "missing region file(s): $(printf '%s.l2j ' "${missing[@]}")" >&2
  exit 1
fi
