#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
dotnet run --project src/Fineage.MobiusImport -- \
  --out "$ROOT/data" \
  --npcs --npc-ids 20001 \
  --items --item-ids 57,2369 \
  --spawns --spawn-files TalkingIsland/TalkingIslandMonsters.xml \
  "$@"
