#!/usr/bin/env python3
"""Mobius playerXpPercentLost.xml + Character.ini -> data/xp-percent-lost.json.

Mobius PlayerXpPercentLostData.getInstance().getXpPercent(level) is the share of the exp bar between the current
level and the next one that death takes away (Player.calculateDeathExpPenalty, Player.java:5321).

The character-config knobs that go with it come from the reference deployment's Character.ini, so the written values
are what ref/mobius-ct0-interlude actually runs with, not just the Config.java fallbacks:
  * DeathPenaltyChance   -> Player.calculateDeathPenaltyBuffLevel (:11716)
  * RespawnRestoreHP/MP  -> Creature.doRevive (:2540-2547), stored here as fractions of the maxima
"""
import json
import pathlib
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ref/mobius-ct0-interlude/data/stats/chars/playerXpPercentLost.xml"
CHARACTER_INI = ROOT / "ref/mobius-ct0-interlude/config/Character.ini"
TARGET = ROOT / "data/xp-percent-lost.json"


def read_character_ini() -> dict[str, str]:
    """Flat Mobius .ini: `key = value` lines, `#` comments, and multi-line continuation rows we do not need."""
    values: dict[str, str] = {}
    for raw in CHARACTER_INI.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or line.endswith("\\") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        values[key.strip()] = value.strip()
    return values


def percent_of(values: dict[str, str], key: str, fallback: float) -> float:
    """Mobius stores a percent (65); the zone multiplies by a fraction (0.65)."""
    raw = values.get(key, "")
    try:
        return float(raw) / 100.0
    except ValueError:
        return fallback


def main() -> None:
    tree = ET.parse(SOURCE)
    rows = {}
    for node in tree.getroot().findall("xpLost"):
        rows[int(node.get("level"))] = float(node.get("val"))

    ini = read_character_ini()
    payload = {
        "reference": (
            "Percent of the current level's exp bar lost on death, transcribed from L2J Mobius CT 0 Interlude "
            "dist/game/data/stats/chars/playerXpPercentLost.xml (PlayerXpPercentLostData.getXpPercent). "
            "Key is the character level; a level above the table uses the last row. "
            "deathPenaltyChance is Character.ini DeathPenaltyChance (Config.java:1656), the share of deaths that raise "
            "Death Penalty by one level. respawnRestoreHp/respawnRestoreMp are Character.ini RespawnRestoreHP/MP as "
            "fractions of the maxima (Creature.doRevive, Creature.java:2540-2547)."
        ),
        "deathPenaltyChance": int(float(ini.get("DeathPenaltyChance", "20"))),
        "respawnRestoreHp": percent_of(ini, "RespawnRestoreHP", 0.65),
        "respawnRestoreMp": percent_of(ini, "RespawnRestoreMP", 0.0),
        "perLevel": {str(level): rows[level] for level in sorted(rows)},
    }
    TARGET.write_text(json.dumps(payload, indent=1) + "\n", encoding="utf-8")
    print(
        f"wrote {TARGET.relative_to(ROOT)}: {len(rows)} levels, "
        f"chance={payload['deathPenaltyChance']}, "
        f"respawn hp/mp={payload['respawnRestoreHp']}/{payload['respawnRestoreMp']}"
    )


if __name__ == "__main__":
    main()
