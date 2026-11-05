#!/usr/bin/env python3
"""Mobius CT0 Interlude village_master scripts -> data/village-masters.json.

The reference ships the whole profession chain as Java scripts under
`data/scripts/village_master/<Script>/<Script>.java` with their pages next to them:

  * `FirstClassTransferTalk`   — headmasters of the newbie villages, they only talk;
  * `*Change1` / `*Change2`    — Grand Masters of the towns: the first and the second
                                 profession change (DarkElf*, Dwarf*, ElfHuman*, Orc*).

Every script has the same shape, so the JSON keeps that shape instead of one-off fields:
  npcs            addStartNpc/addTalkId
  pagePrefix      "npc" when the pages are `<npcId>-NN.htm`, otherwise the fixed id the
                  script hardcodes (e.g. `30109-42.htm` is served by all seven masters)
  guards          ordered `isInCategory` branches of ClassChangeRequested (already a
                  third-class character gets "you are limited to two changes")
  options         one row per (fromClass -> classId): proofs, min level and the four pages
                  (low level with proof, low level without, success, no proof)
  talk            ordered rules of onTalk: race / class / category / class level -> page,
                  the first match wins and the last rule is the mismatch page

The class-level guards use Mobius CategoryType names, where FIRST_CLASS_GROUP is a base
class, SECOND is the first profession, THIRD the second and FOURTH the third — i.e. exactly
the depth of the class in the parent chain. `categories` carries CategoryData.xml so the
other groups (FIGHTER_GROUP, WARSMITH_GROUP, ORC_FALL_CLASS, ...) are real membership sets.

Pages are copied to data/html/village_master/<Script>/ (HTML only, no Java).
Usage: gen_profession_transfers.py [--mobius ref/mobius-ct0-interlude]
"""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import shutil
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
TARGET = ROOT / "data/village-masters.json"
HTML_TARGET = ROOT / "data/html/village_master"
FILE_NAME = "village-masters.json"

# Scripts that carry a change (FirstClassTransferTalk is handled separately below).
CHANGE_SCRIPTS = [
    "DarkElfChange1", "DarkElfChange2",
    "DwarfBlacksmithChange1", "DwarfBlacksmithChange2",
    "DwarfWarehouseChange1", "DwarfWarehouseChange2",
    "ElfHumanClericChange2",
    "ElfHumanFighterChange1", "ElfHumanFighterChange2",
    "ElfHumanWizardChange1", "ElfHumanWizardChange2",
    "OrcChange1", "OrcChange2",
]

# onTalk rules transcribed from the reference (the if/else trees are too irregular to parse
# mechanically without guessing). Categories are Mobius CategoryType names, `classes` are
# class ids, `classLevels` the depth in the parent chain, `else` is the mismatch fallback.
TALK = {
    "DarkElfChange1": [
        {"races": [2], "classes": [31], "page": "01"},
        {"races": [2], "classes": [38], "page": "08"},
        {"races": [2], "classLevels": [1], "page": "32"},
        {"races": [2], "classLevels": [2, 3], "page": "31"},
        {"else": True, "page": "33"},
    ],
    "DarkElfChange2": [
        {"races": [2], "classes": [32], "page": "01"},
        {"races": [2], "classes": [42], "page": "08"},
        {"races": [2], "classes": [35], "page": "12"},
        {"races": [2], "classes": [39], "page": "19"},
        {"races": [2], "classLevels": [0], "page": "55"},
        {"races": [2], "classLevels": [2, 3], "page": "54"},
        {"else": True, "page": "56"},
    ],
    "DwarfBlacksmithChange1": [
        {"categories": ["WARSMITH_GROUP"], "page": "01"},
        {"else": True, "page": "05"},
    ],
    "DwarfBlacksmithChange2": [
        {"categories": ["FOURTH_CLASS_GROUP"], "page": "01"},
        {"categories": ["WARSMITH_GROUP"], "classes": [56, 57], "page": "02"},
        {"categories": ["WARSMITH_GROUP"], "page": "06"},
        {"else": True, "page": "07"},
    ],
    "DwarfWarehouseChange1": [
        {"categories": ["BOUNTY_HUNTER_GROUP"], "page": "01"},
        {"else": True, "page": "05"},
    ],
    "DwarfWarehouseChange2": [
        {"categories": ["FOURTH_CLASS_GROUP", "BOUNTY_HUNTER_GROUP"], "page": "01"},
        {"categories": ["BOUNTY_HUNTER_GROUP"], "classes": [54, 55], "page": "02"},
        {"categories": ["BOUNTY_HUNTER_GROUP"], "page": "06"},
        {"else": True, "page": "07"},
    ],
    "ElfHumanClericChange2": [
        {"categories": ["CLERIC_GROUP", "FOURTH_CLASS_GROUP"], "anyCategory": ["HUMAN_CALL_CLASS", "ELF_CALL_CLASS"], "page": "01"},
        {"categories": ["CLERIC_GROUP"], "anyCategory": ["HUMAN_CALL_CLASS", "ELF_CALL_CLASS"], "classes": [15, 16, 17], "page": "02"},
        {"categories": ["CLERIC_GROUP"], "anyCategory": ["HUMAN_CALL_CLASS", "ELF_CALL_CLASS"], "classes": [29, 30], "page": "09"},
        {"categories": ["CLERIC_GROUP"], "anyCategory": ["HUMAN_CALL_CLASS", "ELF_CALL_CLASS"], "page": "13"},
        {"else": True, "page": "14"},
    ],
    "ElfHumanFighterChange1": [
        {"categories": ["FIGHTER_GROUP"], "races": [0], "page": "01"},
        {"categories": ["FIGHTER_GROUP"], "races": [1], "page": "11"},
        {"else": True, "page": "18"},
    ],
    "ElfHumanFighterChange2": [
        {"categories": ["FIGHTER_GROUP", "FOURTH_CLASS_GROUP"], "anyCategory": ["HUMAN_FALL_CLASS", "ELF_FALL_CLASS"], "page": "01"},
        {"categories": ["FIGHTER_GROUP"], "classes": [1, 2, 3], "page": "02"},
        {"categories": ["FIGHTER_GROUP"], "classes": [4, 5, 6], "page": "09"},
        {"categories": ["FIGHTER_GROUP"], "classes": [7, 8, 9], "page": "16"},
        {"categories": ["FIGHTER_GROUP"], "classes": [19, 20, 21], "page": "23"},
        {"categories": ["FIGHTER_GROUP"], "classes": [22, 23, 24], "page": "30"},
        {"categories": ["FIGHTER_GROUP"], "page": "37"},
        {"else": True, "page": "38"},
    ],
    "ElfHumanWizardChange1": [
        {"categories": ["MAGE_GROUP"], "races": [0], "page": "01"},
        {"categories": ["MAGE_GROUP"], "races": [1], "page": "08"},
        {"else": True, "page": "15"},
    ],
    "ElfHumanWizardChange2": [
        {"categories": ["WIZARD_GROUP", "FOURTH_CLASS_GROUP"], "anyCategory": ["HUMAN_MALL_CLASS", "ELF_MALL_CLASS"], "page": "01"},
        {"categories": ["WIZARD_GROUP"], "anyCategory": ["HUMAN_MALL_CLASS", "ELF_MALL_CLASS"], "classes": [11, 12, 13, 14], "page": "02"},
        {"categories": ["WIZARD_GROUP"], "anyCategory": ["HUMAN_MALL_CLASS", "ELF_MALL_CLASS"], "classes": [26, 27, 28], "page": "12"},
        {"categories": ["WIZARD_GROUP"], "anyCategory": ["HUMAN_MALL_CLASS", "ELF_MALL_CLASS"], "page": "19"},
        {"else": True, "page": "20"},
    ],
    "OrcChange1": [
        {"races": [3], "categories": ["FIGHTER_GROUP"], "page": "01"},
        {"races": [3], "categories": ["MAGE_GROUP"], "page": "06"},
        {"else": True, "page": "23"},
    ],
    "OrcChange2": [
        {"categories": ["FOURTH_CLASS_GROUP"], "anyCategory": ["ORC_MALL_CLASS", "ORC_FALL_CLASS"], "page": "01"},
        {"anyCategory": ["ORC_MALL_CLASS", "ORC_FALL_CLASS"], "classes": [45, 46], "page": "02"},
        {"anyCategory": ["ORC_MALL_CLASS", "ORC_FALL_CLASS"], "classes": [47, 48], "page": "06"},
        {"anyCategory": ["ORC_MALL_CLASS", "ORC_FALL_CLASS"], "classes": [50, 51, 52], "page": "10"},
        {"anyCategory": ["ORC_MALL_CLASS", "ORC_FALL_CLASS"], "page": "17"},
        {"else": True, "page": "18"},
    ],
}

# Race ids are Mobius Race ordinals: HUMAN 0, ELF 1, DARK_ELF 2, ORC 3, DWARF 4.
RACE = {"HUMAN": 0, "ELF": 1, "DARK_ELF": 2, "ORC": 3, "DWARF": 4}

# A base class -> its races, used for the talk rules that filter on `player.getRace()`.
BASE_RACE = {0: 0, 10: 0, 18: 1, 25: 1, 31: 2, 38: 2, 44: 3, 49: 3, 53: 4}


def parse_class_ids(java: pathlib.Path) -> dict[str, int]:
    text = java.read_text(encoding="utf-8")
    return {m.group(1): int(m.group(2)) for m in re.finditer(r"^\t([A-Z_0-9]+)\((\d+),", text, re.M)}


def parse_categories(xml: pathlib.Path) -> dict[str, list[int]]:
    root = ET.parse(xml).getroot()
    out: dict[str, list[int]] = {}
    for category in root.findall("category"):
        ids = [int(node.text) for node in category.findall("id")]
        out[category.get("name")] = sorted(set(ids))
    return out


def resolve(token: str, class_ids: dict[str, int]) -> int:
    token = token.strip()
    return int(token) if token.isdigit() else class_ids[token]


def page(name: str, prefix: str, extension: str) -> str:
    """`npc.getId() + "-21.htm"` -> `{npc}-21.htm` (resolved per master), or the fixed id the script hardcodes."""
    return f"{{npc}}-{name}{extension}" if prefix == "npc" else f"{prefix}-{name}{extension}"


def check_pages(script: str, npcs: list[int], pages: list[str], folder: pathlib.Path) -> None:
    """A page the script names but the datapack does not ship is a generator bug, not a runtime fallback."""
    present = {p.name for p in folder.glob("*.htm*")}
    for name in pages:
        for npc in npcs:
            if name.replace("{npc}", str(npc)) not in present:
                raise SystemExit(f"{script}: {name.replace('{npc}', str(npc))} is not in {folder}")


def parse_script(src: pathlib.Path, class_ids: dict[str, int]) -> dict:
    text = src.read_text(encoding="utf-8")
    script = src.stem
    npcs = [int(n) for n in re.findall(r"(\d{5}),", re.search(r"int\[\] NPCS =\s*\{(.*?)\};", text, re.S).group(1))]
    # Pages: dynamic `<npc>-NN.htm` or one id hardcoded for every master.
    dynamic = re.search(r'htmltext = npc\.getId\(\) \+ "-(\d+)\.html?"', text) is not None
    fixed = re.search(r'htmltext = "(\d{5})-\d+\.html?"', text)
    prefix = "npc" if dynamic else fixed.group(1)
    extension = ".html" if re.search(r'htmltext = [^;]+\.html"', text) else ".htm"

    reward_items = {m.group(1): int(m.group(2)) for m in re.finditer(r"private static (?:final )?int ([A-Z_0-9]+) = (\d+);", text)}
    proof_items = {name: value for name, value in reward_items.items() if not name.startswith("SHADOW")}
    give = re.search(r"giveItems\(player, (SHADOW\w*), (\d+)\)", text)
    reward = (reward_items[give.group(1)] if give else 0, int(give.group(2)) if give else 0)
    # Guards: the `isInCategory` branches in front of the per-class options.
    # DarkElfChange1/2 inline their request in onAdvEvent, where an unknown class id simply
    # echoes the event, and onTalk covers "already changed" through the class depth.
    guards = []
    if "ClassChangeRequested" in text:
        request_start = text.index("ClassChangeRequested")
        first_option = text.find("else if ((classId ==", request_start)
        head = text[request_start:first_option if first_option > 0 else len(text)]
        for m in re.finditer(r'(?:else )?if \(player\.isInCategory\(CategoryType\.(\w+)\)\)\s*\{\s*htmltext = ([^;]+);', head):
            guards.append({"categories": [m.group(1)], "page": page_of(m.group(2), prefix, extension)})

    # Options: one `else if ((classId == X) && (player.getClassId() == ClassId.Y))` block each.
    options = []
    if re.search(r"private static (?:final )?int\[\]\[\] CLASSES", text):
        options = parse_classes_table(text, prefix, extension, class_ids, script, proof_items)
    else:
        blocks = re.finditer(
            r"else if \(\(classId == ([\w]+)\) && \(player\.getClassId\(\) == ClassId\.([\w]+)\)\)\s*\{(.*?)\n\t\t\}\n",
            text, re.S)
        for block in blocks:
            options.append(parse_block(block.group(1), block.group(2), block.group(3), prefix, extension, class_ids, proof_items))
    # Static pages the script's `switch (event)` echoes straight back (case "30037-09.htm": htmltext = event).
    event_pages = sorted({f"{npc}-{num}.{ext}" for npc, num, ext in
                          re.findall(r'case "(\d{5})-(\d+)\.(html?)"', text)})
    return {
        "script": script,
        "npcs": npcs,
        "pagePrefix": prefix,
        "extension": extension,
        "rewardItemId": reward[0],
        "rewardCount": reward[1],
        "guards": guards,
        "options": options,
        "eventPages": event_pages,
        "talk": [{"page": page(rule["page"], prefix, extension), **{k: v for k, v in rule.items() if k != "page"}}
                 for rule in TALK[script]],
    }


def page_of(expr: str, prefix: str, extension: str) -> str:
    """`npc.getId() + "-21.htm"` is resolved per master; `"30499-12.htm"` is a fixed file the script names."""
    expr = expr.strip()
    number = re.search(r'"-(\d+)\.html?"', expr) or re.search(r'"\d{5}-(\d+)\.html?"', expr)
    if "npc.getId()" in expr:
        return page(number.group(1), prefix, extension)
    return re.search(r'"(\d{5}-\d+\.html?)"', expr).group(1)


def parse_block(target: str, source: str, body: str, prefix: str, extension: str, class_ids: dict[str, int],
                consts: dict[str, int]) -> dict:
    proofs = [consts[p.strip()] for p in re.search(r"hasQuestItems\(player, ([A-Z0-9_, ]+)\)", body).group(1).split(",")]
    min_level = int(re.search(r"getLevel\(\) < (\d+)", body).group(1))
    pages = [page_of(expr, prefix, extension) for expr in re.findall(r"htmltext = ([^;]+);", body)]
    if len(pages) != 4:
        raise ValueError(f"{prefix}: expected four pages in the class block, got {pages}")
    return {
        "event": str(resolve(target, class_ids)),
        "classId": resolve(target, class_ids),
        "fromClass": resolve(source, class_ids),
        "proofItemIds": proofs,
        "minLevel": min_level,
        "low": pages[0],
        "lowNoProof": pages[1],
        "success": pages[2],
        "noProof": pages[3],
    }


def parse_classes_table(text: str, prefix: str, extension: str, class_ids: dict[str, int], script: str,
                        consts: dict[str, int]) -> list[dict]:
    """DarkElfChange1/2 keep `{ to, from, lowNoProof, low, noProof, success, proof... }` rows."""
    rows = re.search(r"int\[\]\[\] CLASSES\s*=\s*\{(.*?)\n\t\};", text, re.S).group(1)
    options = []
    for index, row in enumerate(re.findall(r"\{([^}]*)\}", rows)):
        parts = [part.strip() for part in row.split(",")]
        numbers = [int(part) for part in parts if part.isdigit()]
        proofs = [consts[part] for part in parts if not part.isdigit()]
        to_class, from_class = numbers[0], numbers[1]
        low_no_proof, low, no_proof, success = numbers[2], numbers[3], numbers[4], numbers[5]
        options.append({
            "event": str(index),
            "classId": to_class,
            "fromClass": from_class,
            "proofItemIds": proofs,
            "minLevel": 20 if script.endswith("Change1") else 40,
            "low": page(low, prefix, extension),
            "lowNoProof": page(low_no_proof, prefix, extension),
            "success": page(success, prefix, extension),
            "noProof": page(no_proof, prefix, extension),
        })
    return options


def copy_pages(source: pathlib.Path, script: str) -> int:
    folder = source / script
    if not folder.is_dir():
        return 0
    out = HTML_TARGET / script
    out.mkdir(parents=True, exist_ok=True)
    copied = 0
    for page_file in folder.glob("*.htm*"):
        target = out / page_file.name
        text = page_file.read_text(encoding="utf-8", errors="replace")
        if not target.exists() or target.read_text(encoding="utf-8", errors="replace") != text:
            target.write_text(text, encoding="utf-8")
            copied += 1
    return copied


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--mobius", default="ref/mobius-ct0-interlude")
    args = parser.parse_args()
    mobius = ROOT / args.mobius
    scripts_dir = mobius / "data/scripts/village_master"
    class_ids = parse_class_ids(mobius / "java/org/l2jmobius/gameserver/enums/ClassId.java")
    categories = parse_categories(mobius / "data/CategoryData.xml")
    current = json.loads(TARGET.read_text(encoding="utf-8"))

    changes = [parse_script(scripts_dir / name / f"{name}.java", class_ids) for name in CHANGE_SCRIPTS]
    for change in changes:
        pages = [rule["page"] for rule in change["talk"]]
        pages += [guard["page"] for guard in change["guards"]]
        pages += change["eventPages"]
        for option in change["options"]:
            pages += [option["low"], option["lowNoProof"], option["success"], option["noProof"]]
        check_pages(change["script"], change["npcs"], pages, scripts_dir / change["script"])
    copied = sum(copy_pages(scripts_dir, name) for name in CHANGE_SCRIPTS + ["FirstClassTransferTalk"])

    payload = {
        "reference": (
            "Mobius CT0 Interlude village_master scripts as data. transferTalk = FirstClassTransferTalk "
            "(newbie-village headmasters only talk about the first transfer). changes = every *Change1/*Change2 "
            "Grand Master script: onTalk class lists, ClassChangeRequested guards (already second/third/fourth class), "
            "and one option per (fromClass -> classId) with its proof items, minimum level and four pages. "
            "classLevels/categories in talk rules mirror player.isInCategory; classLevel is the depth of the class "
            "in the parent chain (FIRST_CLASS_GROUP = 0 ... FOURTH_CLASS_GROUP = 3). Pages are copied to "
            "data/html/village_master/<Script>/."
        ),
        "transferTalk": current["transferTalk"],
        "mageRoots": current["mageRoots"],
        "categories": categories,
        "changes": changes,
    }
    TARGET.write_text(json.dumps(payload, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"wrote {TARGET.relative_to(ROOT)}: {len(changes)} scripts, "
          f"{sum(len(c['options']) for c in changes)} options, {copied} pages copied")


if __name__ == "__main__":
    main()
