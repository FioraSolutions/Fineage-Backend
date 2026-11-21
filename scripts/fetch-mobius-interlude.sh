#!/usr/bin/env bash
# Clone and build L2J Mobius Interlude (not committed to this repo).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="${MOBIUS_DIR:-$ROOT/.cache/l2j-mobius}"
MODULE="${MOBIUS_MODULE:-L2J_Mobius_CT_0_Interlude}"
BRANCH="${MOBIUS_BRANCH:-master}"

mkdir -p "$(dirname "$DEST")"
if [[ ! -d "$DEST/.git" ]]; then
  git clone --depth 1 --branch "$BRANCH" https://gitlab.com/MobiusDevelopment/L2J_Mobius.git "$DEST"
else
  git -C "$DEST" fetch origin "$BRANCH" --depth 1
  git -C "$DEST" checkout "$BRANCH"
  git -C "$DEST" pull --ff-only origin "$BRANCH" || true
fi

cd "$DEST/$MODULE"
if [[ ! -f build.xml ]]; then
  echo "Missing $DEST/$MODULE/build.xml — check MOBIUS_MODULE." >&2
  exit 1
fi
if command -v ant >/dev/null 2>&1; then
  ant -q jar
else
  sudo apt-get install -y -qq ant >/dev/null
  ant -q jar
fi

echo "Mobius built under $DEST (not added to git)."