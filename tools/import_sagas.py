#!/usr/bin/env python3
"""Transcribe the Mobius saga tables (quests 70-100) into data/quests/_sagas.json and copy their pages.

Each Mobius saga is a subclass that only fills arrays (npc, items, mob, classId, prevClass, x/y/z, text);
the step logic lives in Fineage.Game.Quests.SagaScripts (our own C# implementation). Pages go to
data/quests/000xx_saga_<name>/ with script bypasses rewritten to quest_event:<id>:<event>.
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
MOBIUS = pathlib.Path("/workspace/mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/scripts/quests")
LINK = re.compile(r'bypass -h Script Q\d{5}_\w+ ([\w.-]+)"')


def array(src: str, name: str, strings: bool = False):
    m = re.search(rf"{name}\s*=\s*new\s+\w+\[\]\s*\{{(.*?)\}};", src, re.S)
    if not m:
        raise SystemExit(f"no {name}")
    body = m.group(1)
    if strings:
        return [json.loads('"' + s + '"') for s in re.findall(r'"((?:[^"\\]|\\.)*)"', body)]
    return [int(v, 0) for v in re.findall(r"-?0x[0-9a-fA-F]+|-?\d+", body)]


def snake(cls: str) -> str:
    return re.sub(r"(?<!^)(?=[A-Z])", "_", cls).lower()


def main() -> None:
    sagas = []
    for java in sorted(MOBIUS.glob("Q000[7-9]?_SagaOf*/*.java")) + sorted(MOBIUS.glob("Q00100_SagaOf*/*.java")):
        src = java.read_text(encoding="utf-8")
        m = re.search(r'super\((\d+),\s*"([^"]+)"\)', src)
        qid, name = int(m.group(1)), m.group(2)
        row = {
            "id": qid,
            "name": name,
            "npc": array(src, "_npc"),
            "items": array(src, "_items"),
            "mob": array(src, "_mob"),
            "classId": array(src, "_classId")[0],
            "prevClass": array(src, "_prevClass")[0],
            "x": array(src, "_x"),
            "y": array(src, "_y"),
            "z": array(src, "_z"),
            "text": array(src, "_text", strings=True),
        }
        assert len(row["npc"]) == 12 and len(row["items"]) == 12 and len(row["mob"]) == 3 and len(row["text"]) == 18, qid
        sagas.append(row)

        folder_name = f"{qid:05d}_" + snake(java.parent.name.split("_", 1)[1])
        out = ROOT / "data/quests" / folder_name
        for old in ROOT.glob(f"data/quests/{qid:05d}_*"):
            if old != out:
                for f in old.iterdir():
                    f.unlink()
                old.rmdir()
        out.mkdir(parents=True, exist_ok=True)
        for f in out.iterdir():
            f.unlink()
        for page in sorted(java.parent.glob("*.htm")):
            text = page.read_text(encoding="utf-8", errors="replace")
            text = LINK.sub(lambda mm: f'quest_event:{qid}:{mm.group(1)}"', text)
            (out / page.name).write_text(text, encoding="utf-8")
        row["folder"] = folder_name

    path = ROOT / "data/quests/_sagas.json"
    path.write_text(json.dumps({"sagas": sagas}, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{len(sagas)} sagas -> {path}", file=sys.stderr)


if __name__ == "__main__":
    main()
