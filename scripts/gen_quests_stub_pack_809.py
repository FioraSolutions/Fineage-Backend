#!/usr/bin/env python3
"""Hand-port Mobius stub pack #809: Q21, Q23, Q24, Q120, Q334, Q335, Q336, Q501, Q503, Q620.

After: python3 scripts/gen_quests_stub_pack_809.py
       python3 tools/quest_status.py --mobius .cache/l2j-mobius/L2J_Mobius_CT_0_Interlude
       dotnet test tests/Fineage.Game.Tests/Fineage.Game.Tests.csproj -c Release --filter QuestStubPack809
"""
from __future__ import annotations

import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio  # noqa: E402

from quest_pack_809_helpers import (  # noqa: E402
    START,
    DONE,
    EXIT,
    cond,
    setv,
    give,
    take,
    take_all,
    adena,
    exp_sp,
    drop,
    summon_at,
    html_loader,
    kill_stubs,
    mobius_kill_ids,
    mobius_talk_ids,
)

SCRIPT = {
    21: "Q00021_HiddenTruth",
    23: "Q00023_LidiasHeart",
    24: "Q00024_InhabitantsOfTheForestOfTheDead",
    120: "Q00120_PavelsLastResearch",
    334: "Q00334_TheWishingPotion",
    335: "Q00335_TheSongOfTheHunter",
    336: "Q00336_CoinsOfMagic",
    501: "Q00501_ProofOfClanAlliance",
    503: "Q00503_PursuitOfClanAmbition",
    620: "Q00620_FourGoblets",
}


def q21() -> dict:
    Q, d = 21, html_loader(SCRIPT[21], 21)
    WIZ, TOMB, DUKE, PAGE, SHELF = 31522, 31523, 31524, 31525, 31526
    CROSS, CROSS_NEXT = 7140, 7141
    bishops = (31348, 31349, 31350)
    dialogs = [
        d(WIZ, "talk", "31522-03.htm", when="absent", maxLevel=62),
        d(WIZ, "talk", "31522-01.htm", when="absent", minLevel=63),
        d(WIZ, "event", "31522-02.htm", when="absent", event="31522-02", minLevel=63, ops=START + [cond(1)]),
        d(WIZ, "talk", "31522-05.htm", cond=1),
        d(TOMB, "talk", "31523-01.htm", cond=1),
        d(TOMB, "event", "31523-03.htm", event="31523-03", when="started", cond=1,
          ops=[cond(2), summon_at(DUKE, 51432, -54570, -3136, 300, say="Who awoke me?")]),
        d(TOMB, "talk", "31523-04.htm", cond=2, ops=[summon_at(DUKE, 51432, -54570, -3136, 300, say="Who awoke me?")]),
        d(DUKE, "talk", "31524-01.htm", cond=2),
        d(DUKE, "event", "31524-06.htm", event="31524-06", cond=2,
          ops=[cond(3), summon_at(PAGE, 51608, -54520, -3168, 90,
                               say="My master has instructed me to be your guide.",
                               gone_vars={"end_walk": "0"})]),
        d(DUKE, "talk", "31524-07.htm", cond=3, ops=[setv("end_walk", "1")]),
        d(PAGE, "talk", "31525-01.htm", cond=3, varBelow={"end_walk": 1}),
        d(PAGE, "talk", "31525-02.htm", cond=3, varAtLeast={"end_walk": 1}, ops=[cond(4)]),
        d(PAGE, "talk", "31525-02.htm", cond=4),
        d(SHELF, "talk", "31526-01.htm", cond=4, ops=[cond(5)]),
        d(SHELF, "talk", "31526-01.htm", cond=3, varAtLeast={"end_walk": 1}, ops=[cond(5)]),
        d(SHELF, "talk", "31526-10.htm", cond=5),
        d(SHELF, "event", "31526-14.htm", event="31526-14", cond=5, ops=[cond(6), give(CROSS)]),
        d(SHELF, "event", "31526-08.htm", event="31526-08", cond=5, ops=[cond(5)]),
    ]
    for npc in bishops:
        dialogs += [
            d(npc, "talk", f"{npc}-01.htm", cond=6, hasItem=CROSS),
            d(npc, "event", f"{npc}-01.htm", event=f"{npc}-01", cond=6, hasItem=CROSS, ops=[setv(str(npc), "1")]),
            d(npc, "talk", f"{npc}-01.htm", cond=7, hasItem=CROSS),
        ]
    dialogs += [
        d(bishops[0], "event", "31348-01.htm", event="bishop_trinity", cond=6, hasItem=CROSS,
          varAtLeast={str(bishops[1]): 1, str(bishops[2]): 1}, ops=[cond(7)]),
        d(31328, "talk", "31328-01.htm", cond=7, hasItem=CROSS),
        d(31328, "event", "31328-05.htm", event="31328-05", cond=7, hasItem=CROSS,
          ops=[take(CROSS), give(CROSS_NEXT), exp_sp(131228, 11978), DONE]),
    ]
    return {"id": Q, "name": "Hidden Truth", "repeatable": False, "abortable": True,
            "questItems": [CROSS, CROSS_NEXT], "dialogs": dialogs}


def q23() -> dict:
    Q, d = 23, html_loader(SCRIPT[23], 23)
    INN, SHELF, GHOST, TOMB, VIOLET, BOX = 31328, 31526, 31524, 31523, 31386, 31530
    MAP, KEY, HAIRPIN, DIARY, SPEAR = 7063, 7149, 7148, 7064, 7150
    dialogs = [
        d(INN, "talk", "31328-00.htm", when="absent"),
        d(INN, "talk", "31328-00a.htm", when="absent", maxLevel=63),
        d(INN, "talk", "31328-01.htm", when="absent", minLevel=64),
        d(INN, "event", "31328-02.htm", when="absent", event="31328-02", minLevel=64,
          ops=START + [cond(1), give(MAP), give(KEY)]),
        d(INN, "event", "31328-06.htm", event="31328-06", cond=1, ops=[cond(2)]),
        d(SHELF, "talk", "31526-00.htm", cond=2, ops=[cond(3)]),
        d(SHELF, "event", "31526-05.htm", event="31526-05", cond=3, lacksItems=[HAIRPIN], ops=[give(HAIRPIN)]),
        d(SHELF, "event", "31526-11.htm", event="31526-11", cond=3, lacksItems=[DIARY], ops=[give(DIARY)]),
        d(SHELF, "talk", "31526-13.htm", cond=4),
        d(INN, "event", "31328-11.htm", event="31328-11", cond=4, ops=[cond(5)]),
        d(INN, "event", "31328-19.htm", event="31328-19", cond=5, ops=[cond(6)]),
        d(GHOST, "talk", "31524-01.htm", cond=6),
        d(GHOST, "event", "31524-04.htm", event="31524-04", cond=6, ops=[cond(7), take_all(DIARY)]),
        d(TOMB, "talk", "31523-01.htm", cond=7),
        d(TOMB, "event", "31523-02.htm", event="31523-02", cond=7,
          ops=[summon_at(GHOST, 51432, -54570, -3136, 58, say="Who awoke me?")]),
        d(TOMB, "event", "31523-05.htm", event="31523-05", cond=7),
        d(TOMB, "event", "31523-06.htm", event="31523-06", cond=7, ops=[cond(8), give(KEY)]),
        d(VIOLET, "talk", "31386-01.htm", cond=8, ops=[cond(9)]),
        d(BOX, "talk", "31530-01.htm", cond=9),
        d(BOX, "event", "31530-02.htm", event="31530-02", cond=9, ops=[cond(10), take(KEY), give(SPEAR)]),
        d(VIOLET, "talk", "31386-03.htm", cond=9, hasItem=SPEAR, ops=[cond(10)]),
        d(VIOLET, "event", "31386-03.htm", event="31386-03", cond=10, hasItem=SPEAR,
          ops=[take(SPEAR), adena(100000), DONE]),
    ]
    return {"id": Q, "name": "Lidia's Heart", "repeatable": False, "abortable": True,
            "questItems": [MAP, KEY, HAIRPIN, DIARY, SPEAR], "dialogs": dialogs}


def q24() -> dict:
    Q, d = 24, html_loader(SCRIPT[24], 24)
    DORIAN, WIZ, TOMB, MAID = 31389, 31522, 31531, 31532
    FLOWER, LETTER, HAIRPIN = 7152, 7065, 7148
    CROSS, BROKEN, TOTEM, SUSPICIOUS = 7153, 7154, 7151, 7156
    mobs = [21557, 21558, 21560, 21563, 21564, 21565, 21566, 21567]
    dialogs = [
        d(DORIAN, "talk", "31389-02.htm", when="absent", maxLevel=64),
        d(DORIAN, "talk", "31389-01.htm", when="absent", minLevel=65),
        d(DORIAN, "event", "31389-03.htm", when="absent", event="31389-03", minLevel=65,
          ops=START + [cond(1), give(FLOWER)]),
        d(TOMB, "event", "31531-02.htm", event="31531-02", cond=1, ops=[cond(2), take_all(FLOWER)]),
        d(DORIAN, "event", "31389-08.htm", event="31389-08", cond=2),
        d(DORIAN, "event", "31389-13.htm", event="31389-13", cond=2, ops=[cond(3), give(CROSS)]),
        d(MAID, "event", "31532-04.htm", event="31532-04", cond=3, ops=[cond(4), give(LETTER)]),
        d(MAID, "event", "31532-06.htm", event="31532-06", cond=4, hasItem=HAIRPIN,
          ops=[take_all(LETTER, HAIRPIN)]),
        d(MAID, "event", "31532-06.htm", event="31532-06", cond=4, lacksItems=[HAIRPIN], ops=[cond(5)]),
        d(MAID, "event", "31532-19.htm", event="31532-19", cond=5, ops=[cond(6)]),
        d(WIZ, "event", "31522-08.htm", event="31522-08", cond=6, ops=[cond(7)]),
        d(DORIAN, "talk", "31389-19.htm", cond=3, hasItem=BROKEN, ops=[cond(5), take_all(BROKEN)]),
        d(DORIAN, "event", "31389-19.htm", event="31389-19", cond=4, hasItem=BROKEN, ops=[cond(5), take_all(BROKEN)]),
    ]
    # aggro enter: cross breaks
    dialogs += [
        d(25332, "attack", None, cond=3, hasItem=CROSS,
          ops=[take(CROSS), give(BROKEN)]),
        d(WIZ, "event", "31522-03.htm", event="31522-03", cond=7, ops=[take_all(TOTEM)]),
        d(WIZ, "event", "31522-17.htm", event="31522-17", cond=7, ops=[cond(8)]),
        d(WIZ, "event", "31522-21.htm", event="31522-21", when="started", ops=[give(SUSPICIOUS), DONE]),
    ]
    dialogs += kill_stubs(d, mobs, cond=3, ops=[drop(TOTEM, 0.3, limit=1, at=4)])
    return {"id": Q, "name": "Inhabitants of the Forest of the Dead", "repeatable": False, "abortable": True,
            "questItems": [FLOWER, LETTER, HAIRPIN, CROSS, BROKEN, TOTEM, SUSPICIOUS], "dialogs": dialogs}


def q336() -> dict:
    Q, d = 336, html_loader(SCRIPT[336], 336)
    SORINT, BERNARD = 30232, 30702
    DIAGRAM, KALDIS, M1, M2, M3 = 3811, 3812, 3813, 3814, 3815
    kill_mobs = mobius_kill_ids(SCRIPT[Q])
    talk_npcs = mobius_talk_ids(SCRIPT[Q])
    dialogs = [
        d(SORINT, "talk", "30232-01.htm", when="absent", maxLevel=39, ops=[EXIT]),
        d(SORINT, "talk", "30232-02.htm", when="absent", minLevel=40),
        d(SORINT, "event", "30232-05.htm", when="absent", event="30232-05", minLevel=40,
          ops=START + [cond(1), give(DIAGRAM)]),
        d(SORINT, "talk", "30232-06.htm", cond=1, hasItem=DIAGRAM),
        d(SORINT, "event", "30232-07.htm", event="30232-07", when="started", hasItems=[DIAGRAM, KALDIS],
          ops=[take_all(KALDIS, DIAGRAM), give(M3), setv("grade", "3"), cond(4)]),
        d(BERNARD, "talk", "30702-01.htm", cond=1, hasItem=DIAGRAM, varBelow={"grade": 1}),
        d(BERNARD, "event", "30702-02.htm", event="30702-02", cond=1, ops=[cond(2)]),
        d(BERNARD, "event", "30702-06.htm", event="30702-06", cond=2, ops=[cond(7)]),
        d(SORINT, "event", "30232-10.htm", event="raise", when="started"),
    ]
    for npc in talk_npcs:
        if npc in (SORINT, BERNARD):
            continue
        dialogs.append(d(npc, "talk", f"{npc}-01.htm", when="started"))
    dialogs += kill_stubs(d, [20645, 20644], cond=2, ops=[drop(KALDIS, 0.063, limit=1, at=3)])
    for mob in kill_mobs:
        if mob in (20645, 20644):
            continue
        item = 3472 if mob in (20584, 20585, 20587, 20604, 20678, 20663) else (
            3482 if mob in (20583, 20235, 20146, 20240, 20245) else 3490)
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=[drop(item, 0.5)]))
    return {"id": Q, "name": "Coins of Magic", "repeatable": True, "abortable": True,
            "questItems": [DIAGRAM, KALDIS, M1, M2, M3], "dialogs": dialogs}


def q334() -> dict:
    Q, d = 334, html_loader(SCRIPT[334], 334)
    MATILD, TORAI = 30738, 30557
    (TEXT, BOOK, R1, R2, ORB, WISH) = 3678, 3679, 3680, 3681, 3682, 3467
    parts = (3684, 3685, 3686, 3687, 3688, 3689, 3690, 3691)
    drop_map = {
        27139: BOOK,
        20199: 3684, 20078: 3685, 20250: 3686, 20227: 3687, 20168: 3688,
        20087: 3689, 20088: 3689, 20192: 3690, 20193: 3690, 20248: 3691, 20249: 3691,
    }
    dialogs = [
        d(MATILD, "talk", "30738-21.htm", when="absent", maxLevel=29, ops=[EXIT]),
        d(MATILD, "talk", "30738-01.htm", when="absent", minLevel=30),
        d(MATILD, "event", "30738-03.htm", when="absent", event="30738-03", minLevel=30,
          ops=START + [cond(1), give(TEXT)]),
        d(MATILD, "event", "30738-06.htm", event="30738-06", cond=1, hasItem=BOOK,
          ops=[take_all(TEXT, BOOK), give(R1), give(R2), cond(3)]),
        d(MATILD, "event", "30738-10.htm", event="30738-10", cond=3,
          hasItems=parts,
          ops=[take_all(TEXT, BOOK, R1, R2, *parts), give(ORB, 1), give(WISH, 1), cond(0)]),
        d(TORAI, "talk", "30557-01.htm", when="started", hasItem=WISH),
    ]
    for mob, item in drop_map.items():
        chance = 1.0 if mob == 27139 else 0.5
        limit = 1 if mob == 27139 else 0
        at = 2 if mob == 27139 else None
        dialogs.append(d(mob, "kill", None, cond=1, party=True, raw="",
                         ops=[drop(item, chance, limit=limit, at=at)]))
    for mob in mobius_kill_ids(SCRIPT[Q]):
        if mob in drop_map:
            continue
        dialogs.append(d(mob, "kill", None, cond=3, raw="", ops=[]))
    return {"id": Q, "name": "The Wishing Potion", "repeatable": True, "abortable": True,
            "questItems": [TEXT, BOOK, R1, R2, ORB, WISH, *parts], "dialogs": dialogs}


def q620() -> dict:
    Q, d = 620, html_loader(SCRIPT[620], 620)
    SPIRIT = 31453
    goblets = (7256, 7257, 7258, 7259)
    BROOCH, BOX, PASS = 7262, 7255, 7261
    managers = (31921, 31922, 31923, 31924, 31919, 31920)
    dialogs = [
        d(SPIRIT, "talk", "31453-11.htm", when="absent", maxLevel=73),
        d(SPIRIT, "talk", "31453-12.htm", when="absent", minLevel=74),
        d(SPIRIT, "event", "31453-13.htm", when="absent", event="31453-13", minLevel=74, ops=START + [cond(1)]),
        d(SPIRIT, "event", "31453-16.htm", event="31453-16", when="started", hasItems=goblets,
          ops=[take_all(*goblets), give(BROOCH), cond(2)]),
        d(SPIRIT, "event", "31453-18.htm", event="31453-18", when="started", ops=[DONE]),
        d(31452, "talk", "31452-01.htm", when="started"),
        d(31454, "talk", "31454-01.htm", when="started", hasItem=BOX),
    ]
    for mgr in managers:
        dialogs.append(d(mgr, "talk", f"{mgr}-01.htm", when="started"))
    for mob in mobius_kill_ids(SCRIPT[Q]):
        dialogs.append(d(mob, "kill", None, cond=1, party=True, raw="", ops=[drop(BOX, 0.4)]))
        dialogs.append(d(mob, "kill", None, cond=1, party=True, raw="", ops=[drop(7254, 0.2)]))
        dialogs.append(d(mob, "kill", None, cond=1, party=True, raw="",
                           atLeast={str(g): 1 for g in goblets},
                           ops=[drop(goblets[0], 0.05), drop(goblets[1], 0.05), drop(goblets[2], 0.05), drop(goblets[3], 0.05)]))
    return {"id": Q, "name": "Four Goblets", "repeatable": True, "abortable": True,
            "questItems": [BOX, PASS, *goblets, BROOCH], "dialogs": dialogs}


def q501() -> dict:
    Q, d = 501, html_loader(SCRIPT[501], 501)
    KRISTOF, STATUE, ATHREA, KALIS = 30756, 30757, 30758, 30759
    VOUCHER, PROOF = 3873, 3874
    herbs = (3832, 3833, 3834)
    dialogs = [
        d(KRISTOF, "talk", "30756-01.htm", when="absent"),
        d(KRISTOF, "event", "30756-07.htm", when="absent", event="30756-07", ops=START + [cond(1), setv("state", "1")]),
        d(KALIS, "event", "30759-03.htm", event="30759-03", cond=1, ops=[cond(2), setv("state", "2")]),
        d(KALIS, "event", "30759-07.htm", event="30759-07", cond=2, ops=[cond(3), setv("state", "3"), give(3872)]),
        d(STATUE, "event", "30757-04.htm", event="30757-04", cond=3, ops=[cond(4), give(3837, 1)]),
        d(ATHREA, "talk", "30758-01.htm", cond=4),
        d(ATHREA, "event", "30758-03.htm", event="30758-03", cond=4,
          ops=[cond(5), summon_at(27173, 102273, 103433, -3512, 600, owned=False)]),
        d(KALIS, "event", "30759-09.htm", event="30759-09", cond=5, hasItems=list(herbs),
          ops=[take_all(*herbs), give(3835), cond(6), setv("state", "6"), give(VOUCHER)]),
        d(KRISTOF, "event", "30756-09.htm", event="30756-09", when="started", hasItem=VOUCHER,
          ops=[take_all(VOUCHER), give(PROOF), exp_sp(0, 120000), DONE]),
    ]
    herb_drops = {20685: 3833, 20644: 3832, 20576: 3834}
    for mob, item in herb_drops.items():
        dialogs.append(d(mob, "kill", None, cond=3, party=True, raw="", ops=[drop(item, 1.0, limit=1)]))
    for chest in range(27173, 27178):
        dialogs.append(d(chest, "kill", None, cond=5, raw="", ops=[drop(3836, 1.0)]))
    for npc in mobius_talk_ids(SCRIPT[Q]):
        if npc in (KRISTOF, STATUE, ATHREA, KALIS):
            continue
        dialogs.append(d(npc, "talk", f"{npc}-01.htm", when="started"))
    return {"id": Q, "name": "Proof of Clan Alliance", "repeatable": False, "abortable": True,
            "questItems": list(herbs) + [3835, 3836, 3872, 3873, 3874, 3889], "dialogs": dialogs}


def q503() -> dict:
    Q, d = 503, html_loader(SCRIPT[503], 503)
    SIR = 30760
    dialogs = [
        d(SIR, "talk", "30760-01.htm", when="absent"),
        d(SIR, "event", "30760-08.htm", when="absent", event="30760-08", ops=START + [cond(1)]),
        d(SIR, "event", "30760-20.htm", event="30760-20", when="started", ops=[DONE]),
        d(SIR, "event", "30760-23.htm", event="30760-23", when="started", ops=[DONE]),
    ]
    for npc in mobius_talk_ids(SCRIPT[Q]):
        if npc == SIR:
            continue
        dialogs.append(d(npc, "talk", f"{npc}-01.htm", when="started"))
    for mob in mobius_kill_ids(SCRIPT[Q]):
        dialogs.append(d(mob, "kill", None, cond=1, party=True, raw="", ops=[drop(3865, 0.5)]))
    return {"id": Q, "name": "Pursuit of Clan Ambition!", "repeatable": False, "abortable": True,
            "questItems": [3865, 3866, 3867, 3868, 3869], "dialogs": dialogs}


def q335() -> dict:
    Q, d = 335, html_loader(SCRIPT[335], 335)
    GREY, CYBELLIN, BELTON = 30744, 30745, 30746
    LAUREL, LICENSE1, LICENSE2 = 3696, 3697, 3698
    dialogs = [
        d(GREY, "talk", "30744_01.htm", when="absent"),
        d(GREY, "event", "30744_03.htm", when="absent", event="30744_03", ops=START + [cond(1), give(3693)]),
        d(GREY, "event", "30744_16.htm", event="30744_16", cond=1, atLeast={str(LAUREL): 20},
          ops=[take_all(LAUREL), adena(20000), DONE]),
        d(CYBELLIN, "talk", "30745_03.htm", when="started"),
        d(BELTON, "talk", "30746_03.htm", when="started"),
    ]
    for npc in mobius_talk_ids(SCRIPT[Q]):
        if npc in (GREY, CYBELLIN, BELTON):
            continue
        dialogs.append(d(npc, "talk", f"{npc}-01.htm", when="started"))
    for mob in mobius_kill_ids(SCRIPT[Q]):
        dialogs.append(d(mob, "kill", None, cond=1, party=True, raw="", ops=[drop(LAUREL, 0.5)]))
    return {"id": Q, "name": "Song of the Hunter", "repeatable": True, "abortable": True,
            "questItems": [3693, LAUREL, LICENSE1, LICENSE2], "dialogs": dialogs}


def q120() -> dict:
    Q, d = 120, html_loader(SCRIPT[120], 120)
    YUMI, STONES, WENDY = 32041, 32046, 32047
    NECKLACE, EAR = 8292, 854
    talk = mobius_talk_ids(SCRIPT[Q])
    dialogs = [
        d(STONES, "talk", "32046-00.htm", when="absent"),
        d(STONES, "talk", "32046-01.htm", when="absent", minLevel=50),
        d(STONES, "event", "32046-06.htm", when="absent", event="32046-06", minLevel=50,
          ops=START + [cond(1)]),
        d(YUMI, "talk", "32041-01.htm", cond=1),
        d(YUMI, "event", "32041-03.htm", event="32041-03", cond=1, ops=[cond(3)]),
        d(YUMI, "event", "32041-32.htm", event="32041-32", when="started", hasItem=NECKLACE,
          ops=[take(NECKLACE), give(EAR), DONE]),
        d(WENDY, "talk", "32047-01.htm", cond=1),
    ]
    for npc in talk:
        if npc in (YUMI, STONES, WENDY):
            continue
        dialogs.append(d(npc, "talk", f"{npc}-01.htm", when="started"))
    return {"id": Q, "name": "Pavel's Research", "repeatable": True, "abortable": True,
            "questItems": [8290, 8058, 8059, 8060, 8291, NECKLACE, EAR], "dialogs": dialogs}


def main() -> None:
    questio.ensure_meta(reference="Mobius CT_0 stub pack #809 via scripts/gen_quests_stub_pack_809.py")
    makers = (q21, q23, q24, q120, q334, q335, q336, q501, q503, q620)
    for fn in makers:
        q = fn()
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")
    questio.ensure_meta(reference="Mobius CT_0 stub pack #809 via scripts/gen_quests_stub_pack_809.py")


if __name__ == "__main__":
    main()