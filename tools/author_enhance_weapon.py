#!/usr/bin/env python3
"""Quest 350 Enhance Your Weapon (Mobius CT_0 behaviour, step table written by hand).

Three Grand Magisters hand out level-0 soul crystals; levelling them on monsters is engine code
(ZoneWorldSoulCrystals.cs + data/soul-crystals.json). Usage: python3 tools/author_enhance_weapon.py
"""
from questkit import DONE, START, event, give, mobius_page, talk, write

SCRIPT = "Q00350_EnhanceYourWeapon"
NPCS = {30115: "Jurek", 30856: "Winonin", 30194: "Gideon"}
CRYSTALS = list(range(4629, 4662)) + list(range(5577, 5583)) + [5908, 5911, 5914]  # every soul crystal grade
RED, GREEN, BLUE = 4629, 4640, 4651


def main():
    d, pages = [], {}
    exit_html = mobius_page(SCRIPT, "exit.htm")
    for npc in NPCS:
        # Mobius fills %npcname% at runtime; each magister gets a copy with the name in place.
        pages[f"{npc}-exit.htm"] = (npc, exit_html.replace("%npcname%", NPCS[npc]))
        d += [
            talk(npc, f"{npc}-01", when="absent", minLevel=40),
            talk(npc, f"{npc}-lvl", when="absent"),
            event(npc, f"{npc}-04", ops=START, when="absent", minLevel=40),
            talk(npc, f"{npc}-03", hasAny=CRYSTALS),
            talk(npc, f"{npc}-21"),
            # One free level-0 crystal while the bag holds no soul crystal at all.
            *[event(npc, f"{npc}-{page}", ops=[give(item)], lacksItems=CRYSTALS)
              for page, item in (("09", RED), ("10", GREEN), ("11", BLUE))],
            *[event(npc, f"{npc}-{page}", f"{npc}-03") for page in ("09", "10", "11")],
            event(npc, "exit", f"{npc}-exit", ops=[DONE]),
        ]
    write(350, "00350_enhance_your_weapon", SCRIPT, "Enhance Your Weapon", [], d, repeatable=True, pages=pages)


if __name__ == "__main__":
    main()
