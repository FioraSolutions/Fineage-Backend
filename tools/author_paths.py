#!/usr/bin/env python3
"""Block H1: first-profession path quests the structural auto-port left unstartable or uncompletable (401–405, 410–418).

Step tables written for this engine from Mobius CT_0 quest behaviour (talk / event / kill branches, items, conds,
rewards); no Java is copied. Pages come from the Mobius quest folders via tools/questkit.py.
Usage: python3 tools/author_paths.py
"""
from questkit import (START, DONE, add, cond, drop, event, exp, give, kill, setv, social, spawn, take, take_all,
                      talk, unset, write)

FINISH = social(3)


def created(npc, start_page, ok_ops, base, done_class, gate_item, pages):
    """Mobius path start: wrong class (already the target class) / low level / already has the token / start."""
    low, already, wrong_done, wrong = pages
    return [
        event(npc, start_page, ops=START + ok_ops, when="absent", classes=[base], minLevel=19, lacksItems=[gate_item]),
        event(npc, start_page, already, when="absent", classes=[base], minLevel=19),
        event(npc, start_page, low, when="absent", classes=[base]),
        event(npc, start_page, wrong_done, when="absent", classes=[done_class]),
        event(npc, start_page, wrong, when="absent"),
    ]


def q404():
    PARINA, SNAKE, LIZARD, SALAMANDER, SYLPH, UNDINE = 30391, 30409, 30410, 30411, 30412, 30413
    MAP, KEY, EARING, MIRROR, FEATHER, BANGLE, DIARY, PEBBLE, NECKLACE, COIN, SOIL, RING, BEAD = range(1280, 1293)
    d = [
        talk(PARINA, "30391-04", when="absent", classes=[10], minLevel=19, lacksItems=[BEAD]),
        talk(PARINA, "30391-03", when="absent", classes=[10], minLevel=19),
        talk(PARINA, "30391-02", when="absent", classes=[10]),
        talk(PARINA, "30391-02a", when="absent", classes=[11]),
        talk(PARINA, "30391-01", when="absent"),
        event(PARINA, "30391-08", ops=START, when="absent", classes=[10], minLevel=19, lacksItems=[BEAD]),
        talk(PARINA, "30391-06", cond=13, ops=[take(RING), take(EARING), take(NECKLACE), take(BANGLE), give(BEAD),
                                               exp(3200, 2020), FINISH, DONE]),
        talk(PARINA, "30391-05"),
        talk(SALAMANDER, "30411-01", cond=1, ops=[give(MAP), cond(2)]),
        talk(SALAMANDER, "30411-02", cond=2),
        talk(SALAMANDER, "30411-03", cond=3, ops=[take(KEY), take(MAP), give(EARING), cond(4)]),
        *[talk(SALAMANDER, "30411-04", cond=c) for c in range(4, 14)],
        talk(SYLPH, "30412-01", cond=4, ops=[give(MIRROR), cond(5)]),
        talk(SYLPH, "30412-02", cond=5),
        talk(SYLPH, "30412-03", cond=6, ops=[take(FEATHER), give(BANGLE), cond(7)]),
        *[talk(SYLPH, "30412-04", cond=c) for c in range(7, 14)],
        talk(LIZARD, "30410-01", cond=5),
        *[talk(LIZARD, "30410-04", cond=c) for c in range(6, 14)],
        event(LIZARD, "30410-03", ops=[take(MIRROR), give(FEATHER), cond(6)], cond=5),
        talk(UNDINE, "30413-01", cond=7, ops=[give(DIARY), cond(8)]),
        talk(UNDINE, "30413-02", cond=8),
        talk(UNDINE, "30413-03", cond=9, ops=[take(DIARY), take_all(PEBBLE), give(NECKLACE), cond(10)]),
        *[talk(UNDINE, "30413-04", cond=c) for c in range(10, 14)],
        talk(SNAKE, "30409-01", cond=10, ops=[give(COIN), cond(11)]),
        talk(SNAKE, "30409-02", cond=11),
        talk(SNAKE, "30409-03", cond=12, ops=[take(SOIL), take(COIN), give(RING), cond(13)]),
        talk(SNAKE, "30409-04", cond=13),
        kill(20359, [drop(KEY, 0.8, 1, 3)], cond=2),       # Ratman Warrior
        kill(27030, [drop(PEBBLE, 0.8, 2, 9)], cond=8),    # Water Seer
        kill(20021, [drop(SOIL, 0.2, 1, 12)], cond=11),    # Red Bear
    ]
    items = [MAP, KEY, EARING, MIRROR, FEATHER, BANGLE, DIARY, PEBBLE, NECKLACE, COIN, SOIL, RING]
    write(404, "00404_path_of_the_human_wizard", "Q00404_PathOfTheHumanWizard", "Path to a Human Wizard", items, d,
          repeatable=True)


def q405():
    GALLINT, ZIGAUNT, VIVYAN, PRAGA, SIMPLON, LIONEL = 30017, 30022, 30030, 30333, 30253, 30408
    L1, L2, LBOOK, BVIVYAN, BSIMPLON, BPRAGA, CERT, PENDANT, NECKLACE, COVENANT, MARK = range(1191, 1202)
    d = [
        talk(ZIGAUNT, "30022-01", when="absent", classes=[10], minLevel=19, lacksItems=[MARK]),
        talk(ZIGAUNT, "30022-04", when="absent", classes=[10], minLevel=19),
        talk(ZIGAUNT, "30022-03", when="absent", classes=[10]),
        talk(ZIGAUNT, "30022-02a", when="absent", classes=[15]),
        talk(ZIGAUNT, "30022-02", when="absent"),
        event(ZIGAUNT, "30022-05", ops=START + [give(L1)], when="absent", classes=[10], minLevel=19, lacksItems=[MARK]),
        talk(ZIGAUNT, "30022-06", cond=1),
        talk(ZIGAUNT, "30022-08", cond=2, ops=[take(BPRAGA), take(BVIVYAN), take(BSIMPLON, 3), take(L1), give(L2), cond(3)]),
        *[talk(ZIGAUNT, "30022-07", cond=c) for c in (3, 4, 5)],
        talk(ZIGAUNT, "30022-09", cond=6, ops=[take(L2), take(COVENANT), give(MARK), exp(3200, 5610), FINISH, DONE]),
        talk(SIMPLON, "30253-01", cond=1, lacksItems=[BSIMPLON], ops=[give(BSIMPLON, 3)]),
        *[talk(SIMPLON, "30253-02", cond=c) for c in range(1, 7)],
        talk(PRAGA, "30333-01", cond=1, hasItem=BSIMPLON, lacksItems=[BPRAGA, NECKLACE], ops=[give(NECKLACE)]),
        talk(PRAGA, "30333-02", cond=1, lacksItems=[PENDANT]),
        talk(PRAGA, "30333-03", cond=1, hasItems=[PENDANT, BVIVYAN],
             ops=[take(NECKLACE), take(PENDANT), give(BPRAGA), cond(2)]),
        talk(PRAGA, "30333-03", cond=1, ops=[take(NECKLACE), take(PENDANT), give(BPRAGA)]),
        *[talk(PRAGA, "30333-04", cond=c) for c in range(2, 7)],
        talk(VIVYAN, "30030-01", cond=1, hasItems=[BSIMPLON, BPRAGA], lacksItems=[BVIVYAN], ops=[give(BVIVYAN), cond(2)]),
        talk(VIVYAN, "30030-01", cond=1, hasItem=BSIMPLON, lacksItems=[BVIVYAN], ops=[give(BVIVYAN)]),
        *[talk(VIVYAN, "30030-02", cond=c, **({"hasItem": BVIVYAN} if c == 1 else {})) for c in range(1, 7)],
        *[talk(LIONEL, "30408-02", cond=c) for c in (1, 2)],
        talk(LIONEL, "30408-01", cond=3, ops=[give(LBOOK), cond(4)]),
        talk(LIONEL, "30408-03", cond=4),
        talk(LIONEL, "30408-04", cond=5, ops=[take(CERT), give(COVENANT), cond(6)]),
        talk(LIONEL, "30408-05", cond=6),
        talk(GALLINT, "30017-01", cond=4, ops=[take(LBOOK), give(CERT), cond(5)]),
        *[talk(GALLINT, "30017-02", cond=c) for c in (5, 6)],
        *[kill(m, [drop(PENDANT, 1.0, 1)], cond=1, hasItem=NECKLACE, lacksItems=[PENDANT]) for m in (20029, 20026)],
    ]
    items = [L1, BSIMPLON, BPRAGA, BVIVYAN, NECKLACE, PENDANT, L2, LBOOK, CERT, COVENANT]
    write(405, "00405_path_of_the_cleric", "Q00405_PathOfTheCleric", "Path to a Cleric", items, d, repeatable=True)


def q411():
    TRISKEL, ARKENIA, LEIKAN = 30416, 30419, 30382
    CALL, ALETTER, NOTE, MOLAR, TEARS, RECOMM, HEART = 1245, 1246, 1247, 1248, 1250, 1251, 1252
    d = [
        talk(TRISKEL, "30416-01", when="absent"),
        *created(TRISKEL, "30416-05", [give(CALL)], 31, 35, HEART, ("30416-03", "30416-04", "30416-02a", "30416-02")),
        talk(TRISKEL, "30416-11", cond=1), talk(TRISKEL, "30416-07", cond=2),
        talk(TRISKEL, "30416-08", cond=3), talk(TRISKEL, "30416-08", cond=4),
        talk(TRISKEL, "30416-09", cond=5), talk(TRISKEL, "30416-10", cond=6),
        talk(TRISKEL, "30416-06", cond=7, ops=[take(RECOMM), give(HEART), exp(3200, 3930), FINISH, DONE]),
        talk(ARKENIA, "30419-01", cond=1), talk(ARKENIA, "30419-07", cond=2),
        talk(ARKENIA, "30419-10", cond=3), talk(ARKENIA, "30419-10", cond=4), talk(ARKENIA, "30419-11", cond=5),
        talk(ARKENIA, "30419-08", cond=6, ops=[take_all(TEARS), give(RECOMM), cond(7)]),
        talk(ARKENIA, "30419-09", cond=7),
        event(ARKENIA, "30419-05", ops=[take(CALL), give(ALETTER), cond(2)], cond=1),
        talk(LEIKAN, "30382-01", cond=2),
        talk(LEIKAN, "30382-06", cond=3, hasItem=MOLAR), talk(LEIKAN, "30382-05", cond=3),
        talk(LEIKAN, "30382-07", cond=4, ops=[take_all(MOLAR, NOTE), cond(5)]),
        talk(LEIKAN, "30382-09", cond=5), talk(LEIKAN, "30382-08", cond=6), talk(LEIKAN, "30382-08", cond=7),
        event(LEIKAN, "30382-03", ops=[take(ALETTER), give(NOTE), cond(3)], cond=2),
        kill(20369, [drop(MOLAR, 1.0, 10, 4)], cond=3),          # Moonstone Beast
        kill(27036, [give(TEARS), cond(6)], cond=5),           # Calpico
    ]
    write(411, "00411_path_of_the_assassin", "Q00411_PathOfTheAssassin", "Path to an Assassin",
          [CALL, ALETTER, NOTE, MOLAR, TEARS, RECOMM], d, repeatable=True)


def q412():
    VARIKA, CHARKEREN, ANNIKA, ARKENIA = 30421, 30415, 30418, 30419
    ANGER, DESPAIR, HORROR, LUNACY, REMAINS, LIQUOR, KNEE, HEART, JEWEL = range(1253, 1262)
    KEY, CANDLE, SCENT = 1277, 1278, 1279
    d = [
        talk(VARIKA, "30421-01", when="absent"),
        *created(VARIKA, "30421-05", [give(DESPAIR)], 38, 39, JEWEL, ("30421-02", "30421-04", "30421-02a", "30421-03")),
        event(VARIKA, "30421-07", "30421-06", hasItem=ANGER),
        event(VARIKA, "30421-07", "30421-08", hasItem=KEY),
        event(VARIKA, "30421-07", "30421-18", atLeast={REMAINS: 3}),
        event(VARIKA, "30421-07"),
        event(VARIKA, "30421-10", "30421-09", hasItem=HORROR),
        event(VARIKA, "30421-10", "30421-19", atLeast={KNEE: 2}),
        event(VARIKA, "30421-10"),
        event(VARIKA, "30421-13", "30421-12", hasItem=LUNACY),
        event(VARIKA, "30421-13"),
        talk(VARIKA, "30421-16", hasItems=[ANGER, HORROR, LUNACY],
             ops=[take(ANGER), take(DESPAIR), take(HORROR), take(LUNACY), give(JEWEL), exp(3200, 1650), FINISH, DONE]),
        talk(VARIKA, "30421-17"),
        event(CHARKEREN, "30415-03", ops=[give(KEY)], lacksItems=[KEY, ANGER]),
        talk(CHARKEREN, "30415-06", hasItem=ANGER),
        talk(CHARKEREN, "30415-01", lacksItems=[KEY]),
        talk(CHARKEREN, "30415-05", atLeast={REMAINS: 3}, ops=[take_all(REMAINS), take(KEY), give(ANGER)]),
        talk(CHARKEREN, "30415-04"),
        event(ANNIKA, "30418-02", ops=[give(CANDLE)], lacksItems=[CANDLE, HORROR]),
        talk(ANNIKA, "30418-04", hasItem=HORROR),
        talk(ANNIKA, "30418-01", lacksItems=[CANDLE]),
        talk(ANNIKA, "30418-04", atLeast={KNEE: 2}, ops=[take(CANDLE), take_all(KNEE), give(HORROR)]),
        talk(ANNIKA, "30418-03"),
        talk(ARKENIA, "30419-03", hasItem=LUNACY),
        talk(ARKENIA, "30419-01", lacksItems=[SCENT], ops=[give(SCENT)]),
        talk(ARKENIA, "30419-03", atLeast={HEART: 3}, ops=[take_all(HEART), take(SCENT), give(LUNACY)]),
        talk(ARKENIA, "30419-02"),
        kill(20015, [drop(REMAINS, 0.5, 3)], hasItem=KEY),                  # Lesser Dark Horror? (Mobius id 20015)
        *[kill(m, [drop(KNEE, 0.5, 2)], hasItem=CANDLE) for m in (20022, 20517, 20518)],
        kill(20045, [drop(HEART, 0.5, 3)], hasItem=SCENT),
    ]
    write(412, "00412_path_of_the_dark_wizard", "Q00412_PathOfTheDarkWizard", "Path to a Dark Wizard",
          [ANGER, DESPAIR, HORROR, LUNACY, REMAINS, KNEE, HEART, KEY, CANDLE, SCENT], d, repeatable=True)


def q413():
    SIDRA, ADONIUS, TALBOT = 30330, 30375, 30377
    SLETTER, SHEET, RUNE, GARMIEL, PRAYER, PENITENT, BONES, ANDARIEL, ORB = range(1262, 1271)
    d = [
        talk(SIDRA, "30330-01", when="absent"),
        event(SIDRA, "30330-05", when="absent", classes=[38], minLevel=19, lacksItems=[ORB]),
        *created(SIDRA, "30330-05", [], 38, 42, ORB, ("30330-02", "30330-04", "30330-02a", "30330-03"))[1:],
        event(SIDRA, "30330-06", ops=START + [give(SLETTER)], when="absent", classes=[38], minLevel=19, lacksItems=[ORB]),
        talk(SIDRA, "30330-07", cond=1),
        *[talk(SIDRA, "30330-08", cond=c) for c in (2, 3)],
        *[talk(SIDRA, "30330-09", cond=c) for c in (4, 5, 6)],
        talk(SIDRA, "30330-10", cond=7, ops=[take(ANDARIEL), take(GARMIEL), give(ORB), exp(3200, 3120), FINISH, DONE]),
        talk(TALBOT, "30377-01", cond=1),
        talk(TALBOT, "30377-04", cond=2, hasItem=RUNE), talk(TALBOT, "30377-03", cond=2),
        talk(TALBOT, "30377-05", cond=3, ops=[take_all(RUNE), give(GARMIEL), give(PRAYER), cond(4)]),
        *[talk(TALBOT, "30377-06", cond=c) for c in (4, 5, 6)],
        talk(TALBOT, "30377-07", cond=7),
        event(TALBOT, "30377-02", ops=[take(SLETTER), give(SHEET, 5), cond(2)], cond=1),
        talk(ADONIUS, "30375-01", cond=4),
        talk(ADONIUS, "30375-05", cond=5, hasItem=BONES), talk(ADONIUS, "30375-06", cond=5),
        talk(ADONIUS, "30375-07", cond=6, ops=[take_all(BONES, PENITENT), give(ANDARIEL), cond(7)]),
        talk(ADONIUS, "30375-08", cond=7),
        event(ADONIUS, "30375-04", ops=[take(PRAYER), give(PENITENT), cond(5)], cond=4),
        kill(20776, [take(SHEET), drop(RUNE, 1.0, 5, 3)], cond=2, hasItem=SHEET),   # Dark Succubus
        *[kill(m, [drop(BONES, 1.0, 10, 6)], cond=5) for m in (20457, 20458, 20514, 20515)],
    ]
    write(413, "00413_path_of_the_shillien_oracle", "Q00413_PathOfTheShillienOracle", "Path to a Shillien Oracle",
          [SLETTER, SHEET, RUNE, GARMIEL, PRAYER, PENITENT, BONES, ANDARIEL], d, repeatable=True)


def q414():
    KARUKIA, KASMAN, TAZEER = 30570, 30501, 31978
    BLOOD, MAP, TOOTH, REPORT1, REPORT2, HEAD, MARK, TIMORA_HEAD = 1578, 1579, 1580, 1589, 1590, 1591, 1592, 8544
    reward = [give(MARK), exp(3200, 2360), FINISH, DONE]
    d = [
        talk(KARUKIA, "30570-01", when="absent"),
        *created(KARUKIA, "30570-05", [give(MAP)], 44, 45, MARK, ("30570-02", "30570-04", "30570-02a", "30570-03")),
        talk(KARUKIA, "30570-06", cond=1), talk(KARUKIA, "30570-07", cond=2),
        talk(KARUKIA, "30570-08", cond=3), talk(KARUKIA, "30570-08", cond=4), talk(KARUKIA, "30570-07b", cond=5),
        event(KARUKIA, "30570-07a", ops=[take(MAP), take_all(TOOTH), give(REPORT1), give(REPORT2), cond(3)], cond=2),
        event(KARUKIA, "30570-07b", ops=[take(MAP), take_all(TOOTH), cond(5)], cond=2),
        talk(KASMAN, "30501-02", cond=3, hasItem=HEAD), talk(KASMAN, "30501-01", cond=3),
        talk(KASMAN, "30501-03", cond=4, ops=[take(REPORT1), take(REPORT2), take_all(HEAD)] + reward),
        talk(TAZEER, "31978-01", cond=5), talk(TAZEER, "31978-04", cond=6),
        talk(TAZEER, "31978-05", cond=7, ops=[take(TIMORA_HEAD)] + reward),
        event(TAZEER, "31978-03", ops=[cond(6)], cond=5),
        # Goblin Tomb Raider Leader: each Green Blood held adds 5 % to the Kuruka Ratman Leader spawn chance; a failed
        # roll collects one more blood (the give row right after each chance row catches the miss).
        *[row for n in range(20, 0, -1) for row in (
            kill(20320, [take_all(BLOOD), spawn(27045)], cond=1, atLeast={BLOOD: n}, chance=n / 20),
            kill(20320, [give(BLOOD)], cond=1, atLeast={BLOOD: n}))],
        kill(20320, [give(BLOOD)], cond=1),
        kill(27045, [drop(TOOTH, 1.0, 10, 2)], cond=1),
        kill(27054, [drop(HEAD, 0.2, 2, 4)], cond=3),
        kill(27320, [drop(TIMORA_HEAD, 0.6, 1, 7)], cond=6),
    ]
    write(414, "00414_path_of_the_orc_raider", "Q00414_PathOfTheOrcRaider", "Path to an Orc Raider",
          [BLOOD, MAP, TOOTH, REPORT1, REPORT2, HEAD, TIMORA_HEAD], d, repeatable=True)


def q417():
    RAUT, SHARI, MION, PIPPI, BRONK, ZIMENF, TOMA, TORAI, YASHENI = 30316, 30517, 30519, 30524, 30525, 30538, 30556, 30557, 31958
    RING, PLETTER, SCROLL, UNDIES, MLETTER, INGOT, AXE, POTION, BPAY, SPAY, ZPAY = range(1642, 1653)
    BEAR_PIC, TARANTULA_PIC, HONEY, BEAD, PARCEL1, PARCEL2 = 1653, 1654, 1655, 1656, 1657, 8543
    PAYS = (BPAY, SPAY, ZPAY)
    errand = [("30519-02", POTION), ("30519-03", AXE), ("30519-04", INGOT)]  # Mobius ZIMENF_POTION - random(3)
    reward = [give(RING), exp(3200, 7080), FINISH, DONE]

    def mion_pick(name, pre):
        rows = []
        for i, (page, item) in enumerate(errand):
            chance = [1 / 3, 1 / 2, None][i]
            rows.append(event(MION, name, page, ops=pre + [give(item)], chance=chance))
        return rows

    def deliver(npc, carry, pay, pages):
        first, second, after = pages
        return [
            talk(npc, second, hasItem=carry, varAtLeast={"id": 20}, ops=[cond(3), add("id", 10), take(carry), give(pay)]),
            talk(npc, first, hasItem=carry, ops=[add("id", 10), take(carry), give(pay)]),
            talk(npc, after, hasItem=pay),
        ]

    d = [
        talk(PIPPI, "30524-01", when="absent"),
        *created(PIPPI, "30524-05", [give(PLETTER)], 53, 54, RING, ("30524-02", "30524-04", "30524-02a", "30524-08")),
        talk(PIPPI, "30524-06", cond=1),
        *[talk(PIPPI, "30524-07", cond=c) for c in range(2, 13)],
        *mion_pick("30519_1", [cond(2), take_all(PLETTER)]),
        *mion_pick("30519_2", [take_all(*PAYS)]),
        event(MION, "30519-07", ops=[add("id")]),
        event(MION, "30519-09", "30519-07", ops=[add("id")], varBelow={"id": 20}),
        event(MION, "30519-09", ops=[add("id")], varBelow={"id": 30}),
        event(MION, "30519-09", "30519-10", ops=[cond(4), take_all(AXE, POTION, INGOT), give(MLETTER)]),
        event(MION, "30519-11", "30519-06", chance=0.5),
        event(MION, "30519-11"),
        talk(MION, "30519-01", hasItem=PLETTER),
        talk(MION, "30519-05", hasAny=[INGOT, AXE, POTION], varBelow={"id": 10}),
        talk(MION, "30519-08", hasAny=[INGOT, AXE, POTION]),
        talk(MION, "30519-12", hasAny=list(PAYS), varBelow={"id": 50}),
        talk(MION, "30519-15", hasAny=list(PAYS), ops=[cond(4), take_all(*PAYS), give(MLETTER)]),
        talk(MION, "30519-13", cond=4),
        *[talk(MION, "30519-14", cond=c) for c in range(5, 13)],
        *deliver(SHARI, AXE, SPAY, ("30517-01", "30517-02", "30517-03")),
        *deliver(BRONK, INGOT, BPAY, ("30525-01", "30525-02", "30525-03")),
        *deliver(ZIMENF, POTION, ZPAY, ("30538-01", "30538-02", "30538-03")),
        talk(TOMA, "30556-01", cond=4, ops=[take(MLETTER), give(BEAR_PIC), cond(5)]),
        talk(TOMA, "30556-02", cond=5),
        talk(TOMA, "30556-03", cond=6, ops=[take_all(HONEY), take(BEAR_PIC), give(TARANTULA_PIC), cond(7)]),
        talk(TOMA, "30556-04", cond=7), talk(TOMA, "30556-05a", cond=8), talk(TOMA, "30556-06a", cond=9),
        talk(TOMA, "30556-07", cond=10), talk(TOMA, "30556-07", cond=11), talk(TOMA, "30556-06c", cond=12),
        event(TOMA, "30556-05b", ops=[take_all(BEAD, TARANTULA_PIC), give(PARCEL1), cond(9)], cond=8),
        *[event(TOMA, "30556-06b", ops=[take_all(BEAD, TARANTULA_PIC), give(PARCEL2), cond(12)], cond=c) for c in (8, 9)],
        talk(RAUT, "30316-01", cond=9), talk(RAUT, "30316-04", cond=10),
        talk(RAUT, "30316-05", cond=11, ops=[take(UNDIES)] + reward),
        *[event(RAUT, ev, ops=[take(PARCEL1), give(SCROLL), cond(10)], cond=9) for ev in ("30316-02", "30316-03")],
        talk(TORAI, "30557-01", cond=10),
        event(TORAI, "30557-03", ops=[take(SCROLL), give(UNDIES), cond(11)], cond=10),
        talk(YASHENI, "31958-01", cond=12),
        event(YASHENI, "31958-02", ops=[take(PARCEL2)] + reward, cond=12),
        # Hunter Bear: past 20 kills each kill may raise a Honey Bear, the odds growing 10 % per extra kill.
        *[kill(20777, [spawn(27058), unset("step")], cond=5, varAtLeast={"step": s}, varBelow={"step": s + 1},
               chance=min(1.0, (s - 20) / 10)) for s in range(21, 30)],
        kill(20777, [spawn(27058), unset("step")], cond=5, varAtLeast={"step": 30}),
        kill(20777, [add("step")], cond=5),
        # Honey Bear / tarantulas: Mobius also needs the killer to be the spoiler; the zone does not report the
        # spoiler to quests, so any kill by the quest owner counts.
        kill(27058, [drop(HONEY, 1.0, 5, 6)], cond=5),
        kill(20403, [drop(BEAD, 0.33, 20, 8)], cond=7),
        kill(20508, [drop(BEAD, 0.6, 20, 8)], cond=7),
    ]
    items = [PLETTER, SCROLL, UNDIES, MLETTER, INGOT, AXE, POTION, BPAY, SPAY, ZPAY, BEAR_PIC, TARANTULA_PIC, HONEY,
             BEAD, PARCEL1, PARCEL2]
    write(417, "00417_path_of_the_scavenger", "Q00417_PathOfTheScavenger", "Path to become a Scavenger", items, d,
          repeatable=True)


def info_start(npc, info, start, ops, base, done_class, mark, pages):
    """Mobius two-step start: the info page (event `info`) checks class / level / token, the accept link starts."""
    low, already, wrong_done, wrong = pages
    return [
        event(npc, info, when="absent", classes=[base], minLevel=19, lacksItems=[mark]),
        event(npc, info, already, when="absent", classes=[base], minLevel=19),
        event(npc, info, low, when="absent", classes=[base]),
        event(npc, info, wrong_done, when="absent", classes=[done_class]),
        event(npc, info, wrong, when="absent"),
        event(npc, start, ops=START + ops, when="absent", classes=[base], minLevel=19, lacksItems=[mark]),
    ]


def q401():
    AURON, SIMPLON = 30010, 30253
    LETTER, GUILD, SWORD1, SWORD2, SWORD3, SLETTER, LEG, MEDAL = range(1138, 1146)
    d = [
        talk(AURON, "30010-01", when="absent"),
        *info_start(AURON, "30010-05", "30010-06", [give(LETTER)], 0, 1, MEDAL,
                    ("30010-02", "30010-04", "30010-03", "30010-02b")),
        talk(AURON, "30010-07", cond=1), talk(AURON, "30010-08", cond=2), talk(AURON, "30010-08", cond=3),
        talk(AURON, "30010-09", cond=4), talk(AURON, "30010-12", cond=5),
        talk(AURON, "30010-13", cond=6, ops=[take_all(LEG), take(SWORD3), give(MEDAL), exp(3200, 1500), FINISH, DONE]),
        event(AURON, "30010-11", ops=[take(SWORD2), take(SLETTER), give(SWORD3), cond(5)], cond=4),
        talk(SIMPLON, "30253-01", cond=1),
        talk(SIMPLON, "30253-03b", cond=2, hasItem=SWORD1), talk(SIMPLON, "30253-03", cond=2),
        talk(SIMPLON, "30253-04", cond=3, ops=[take(SWORD1, 10), take(GUILD), give(SWORD2), give(SLETTER), cond(4)]),
        talk(SIMPLON, "30253-05", cond=4),
        event(SIMPLON, "30253-02", ops=[take(LETTER), give(GUILD), cond(2)], cond=1),
        *[kill(m, [drop(SWORD1, 0.4, 10, 3)], cond=2) for m in (20035, 20042)],
        # Poison spider legs only count while the Rusted Bronze Sword (trial sword) is wielded.
        *[kill(m, [drop(LEG, 1.0, 20, 6)], cond=5, weapons=[SWORD3]) for m in (20038, 20043)],
    ]
    write(401, "00401_path_of_the_warrior", "Q00401_PathOfTheWarrior", "Path to a Warrior",
          [LETTER, GUILD, SWORD1, SWORD2, SWORD3, SLETTER, LEG], d, repeatable=True)


def q402():
    KLAUS, BATHIS, RAYMOND, BEZIQUE, LEVIAN, GILBERT, BIOTIN, AARON = 30417, 30332, 30289, 30379, 30037, 30039, 30031, 30653
    SWORD = 1161
    COINS = list(range(1162, 1168))
    GM1, NECKLACE, CM1, CRUCIFIX, GM2, LEG, CM2, TOTEM, GM3, HUSK, CM3, SKULL = range(1168, 1180)
    ESQUIRE = 1271
    finish = [take_all(*COINS), take(ESQUIRE), give(SWORD)]
    d = [
        talk(KLAUS, "30417-01", when="absent"),
        *info_start(KLAUS, "30417-05", "30417-08", [give(ESQUIRE)], 0, 4, SWORD,
                    ("30417-02", "30417-04", "30417-02a", "30417-03")),
        # Coins of Lords are counted in the "coins" journal var (Mobius sums the six coin items).
        talk(KLAUS, "30417-09", varBelow={"coins": 3}),
        talk(KLAUS, "30417-10", varBelow={"coins": 4}),
        talk(KLAUS, "30417-11", varBelow={"coins": 6}),
        talk(KLAUS, "30417-12", ops=finish + [exp(3200, 7260), FINISH, DONE]),
    ]
    for ev in ("30417-13", "30417-14"):
        for c in (3, 4, 5):
            d.append(event(KLAUS, ev, ops=finish + [exp(3200, 1500 + 1920 * (c - 3)), FINISH, DONE],
                           varAtLeast={"coins": c}, varBelow={"coins": c + 1}))
    for npc, mark, loot, need, coin, pages in (
            (BATHIS, GM1, NECKLACE, 10, COINS[0], "30332-01 30332-02 30332-03 30332-04 30332-05"),
            (RAYMOND, CM1, CRUCIFIX, 12, COINS[1], "30289-01 30289-03 30289-04 30289-05 30289-06"),
            (BEZIQUE, GM2, LEG, 20, COINS[2], "30379-01 30379-02 30379-03 30379-04 30379-05"),
            (LEVIAN, CM2, TOTEM, 20, COINS[3], "30037-01 30037-02 30037-03 30037-04 30037-05"),
            (GILBERT, GM3, HUSK, 20, COINS[4], "30039-01 30039-02 30039-03 30039-04 30039-05"),
            (BIOTIN, CM3, SKULL, 10, COINS[5], "30031-01 30031-02 30031-03 30031-04 30031-05")):
        intro, accept, wait, done_page, after = pages.split()
        d += [
            talk(npc, after, hasItem=coin),
            talk(npc, done_page, hasItem=mark, atLeast={loot: need},
                 ops=[take_all(loot), take(mark), give(coin), add("coins")]),
            talk(npc, wait, hasItem=mark),
            talk(npc, intro),
            event(npc, accept, ops=[give(mark)], lacksItems=[mark, coin]),
        ]
    d.append(talk(AARON, "30653-01"))
    d += [
        kill(20775, [drop(NECKLACE, 1.0, 10)], hasItem=GM1),
        kill(27024, [drop(CRUCIFIX, 0.5, 12)], hasItem=CM1),
        *[kill(m, [drop(LEG, 1.0, 20)], hasItem=GM2) for m in (20038, 20043, 20050)],
        *[kill(m, [drop(TOTEM, 0.5, 20)], hasItem=CM2) for m in (20030, 20027, 20024)],
        *[kill(m, [drop(HUSK, 0.4, 20)], hasItem=GM3) for m in (20103, 20106, 20108)],
        kill(20404, [drop(SKULL, 0.4, 10)], hasItem=CM3),
    ]
    write(402, "00402_path_of_the_human_knight", "Q00402_PathOfTheHumanKnight", "Path to a Human Knight",
          [ESQUIRE, *COINS, GM1, NECKLACE, CM1, CRUCIFIX, GM2, LEG, CM2, TOTEM, GM3, HUSK, CM3, SKULL], d,
          repeatable=True)


def q403():
    BEZIQUE, NETI = 30379, 30425
    LETTER, BOW, DAGGER, BONES, HORSESHOE, WANTED, JEWELRY, TOMES, RING, NECKLACE, RECOMM = range(1180, 1191)
    STOLEN = [JEWELRY, TOMES, RING, NECKLACE]
    d = [
        talk(BEZIQUE, "30379-01", when="absent"),
        *info_start(BEZIQUE, "30379-05", "30379-06", [give(LETTER)], 0, 7, RECOMM,
                    ("30379-02", "30379-04", "30379-02a", "30379-02")),
        talk(BEZIQUE, "30379-07", cond=1), talk(BEZIQUE, "30379-10", cond=2), talk(BEZIQUE, "30379-10", cond=3),
        talk(BEZIQUE, "30379-08", cond=4, ops=[take(HORSESHOE), give(WANTED), cond(5)]),
        talk(BEZIQUE, "30379-11", cond=5),
        talk(BEZIQUE, "30379-09", cond=6, ops=[take(BOW), take(DAGGER), take_all(*STOLEN, WANTED), give(RECOMM),
                                               exp(3200, 1500), FINISH, DONE]),
        talk(NETI, "30425-01", cond=1), talk(NETI, "30425-06", cond=2),
        talk(NETI, "30425-07", cond=3, ops=[take(BONES, 10), give(HORSESHOE), cond(4)]),
        *[talk(NETI, "30425-08", cond=c) for c in (4, 5, 6)],
        event(NETI, "30425-05", ops=[give(BOW), give(DAGGER), cond(2)], cond=1),
    ]
    netis = [BOW, DAGGER]
    for mobs, chance in (((20035, 20045, 20051), 0.2), ((20042,), 0.3), ((20054, 20060), 0.8)):
        d += [kill(m, [drop(BONES, chance, 10, 3)], cond=2, weapons=netis) for m in mobs]
    # Catsy's thief (27038) drops one of the four stolen items at random; a duplicate is wasted. The three rows
    # per pick are mutually exclusive, so each pick rolls once (1/4, then 1/3 of the rest, ...).
    for i, item in enumerate(STOLEN):
        chance = 1 / (len(STOLEN) - i) if i < len(STOLEN) - 1 else None
        others = [x for x in STOLEN if x != item]
        d += [
            kill(27038, [give(item), cond(6)], cond=5, weapons=netis, lacksItems=[item], hasItems=others, chance=chance),
            kill(27038, [give(item)], cond=5, weapons=netis, lacksItems=[item], chance=chance),
            kill(27038, [], cond=5, weapons=netis, hasItem=item, chance=chance),
        ]
    write(403, "00403_path_of_the_rogue", "Q00403_PathOfTheRogue", "Path to a Rogue",
          [LETTER, BOW, DAGGER, BONES, HORSESHOE, WANTED, *STOLEN], d, repeatable=True)


def q410():
    KALINTA, VIRGIL = 30422, 30329
    TALISMAN, SKULL, VLETTER, MORTE, CARAPACE, SILK, COFFIN, GAZE = range(1237, 1245)
    d = [
        talk(VIRGIL, "30329-01", when="absent"),
        *info_start(VIRGIL, "30329-05", "30329-06", [give(TALISMAN)], 31, 32, GAZE,
                    ("30329-02", "30329-04", "30329-02a", "30329-03")),
        talk(VIRGIL, "30329-08", cond=1, hasItem=SKULL), talk(VIRGIL, "30329-07", cond=1),
        talk(VIRGIL, "30329-09", cond=2),
        *[talk(VIRGIL, "30329-12", cond=c) for c in (3, 4, 5)],
        talk(VIRGIL, "30329-11", cond=6, ops=[take(COFFIN), give(GAZE), exp(3200, 1500), FINISH, DONE]),
        event(VIRGIL, "30329-10", ops=[take_all(SKULL), take(TALISMAN), give(VLETTER), cond(3)], cond=2),
        talk(KALINTA, "30422-01", cond=3),
        talk(KALINTA, "30422-04", cond=4, hasItems=[SILK, CARAPACE]), talk(KALINTA, "30422-03", cond=4),
        talk(KALINTA, "30422-05", cond=5), talk(KALINTA, "30422-06", cond=6),
        event(KALINTA, "30422-02", ops=[take(VLETTER), give(MORTE), cond(4)], cond=3),
        event(KALINTA, "30422-06", ops=[take_all(SILK, CARAPACE), take(MORTE), give(COFFIN), cond(6)], cond=5),
        kill(20049, [drop(SKULL, 1.0, 13, 2)], cond=1),
        kill(20043, [drop(SILK, 1.0, 5, 5)], cond=4, hasItem=CARAPACE),
        kill(20043, [drop(SILK, 1.0, 5)], cond=4),
        kill(20038, [give(CARAPACE), cond(5)], cond=4, lacksItems=[CARAPACE], atLeast={SILK: 5}),
        kill(20038, [give(CARAPACE)], cond=4, lacksItems=[CARAPACE]),
    ]
    write(410, "00410_path_of_the_palus_knight", "Q00410_PathOfThePalusKnight", "Path to a Palus Knight",
          [TALISMAN, SKULL, VLETTER, MORTE, CARAPACE, SILK, COFFIN], d, repeatable=True)


def q415():
    GANTAKI, ROSHEEK, KASMAN, TORUKU, AREN, MOIRA = 30587, 30590, 30501, 30591, 32056, 31979
    (POMEGRANATE, POUCH1, POUCH2, POUCH3, FULL1, FULL2, FULL3, CLAW, TALON, SCALE, FIERY, RLETTER, GLETTER, FIG,
     POUCH4, FULL4, TUSK, FANG, LANG, FELIM, IRON, TLETTER, TOTEM) = range(1593, 1616)
    SPIDER_TOOTH, HORN = 8545, 8546
    d = [
        talk(GANTAKI, "30587-01", when="absent"),
        *info_start(GANTAKI, "30587-05", "30587-06", [give(POMEGRANATE)], 44, 47, TOTEM,
                    ("30587-03", "30587-04", "30587-02a", "30587-02")),
        talk(GANTAKI, "30587-07", cond=1),
        *[talk(GANTAKI, "30587-08", cond=c) for c in range(2, 8)],
        talk(GANTAKI, "30587-09", cond=8), talk(GANTAKI, "30587-10", cond=9),
        *[talk(GANTAKI, "30587-11", cond=c) for c in range(10, 21)],
        event(GANTAKI, "30587-09a", ops=[take(RLETTER), give(GLETTER), cond(9)], cond=8),
        event(GANTAKI, "30587-09b", ops=[take(RLETTER), cond(14)], cond=8),
        talk(ROSHEEK, "30590-01", cond=1, ops=[take(POMEGRANATE), give(POUCH1), cond(2)]),
        talk(ROSHEEK, "30590-02", cond=2),
        talk(ROSHEEK, "30590-03", cond=3, ops=[take(FULL1), give(POUCH2), cond(4)]),
        talk(ROSHEEK, "30590-04", cond=4),
        talk(ROSHEEK, "30590-05", cond=5, ops=[take(FULL2), give(POUCH3), cond(6)]),
        talk(ROSHEEK, "30590-06", cond=6),
        talk(ROSHEEK, "30590-07", cond=7, ops=[take(FULL3), give(FIERY), give(RLETTER), cond(8)]),
        talk(ROSHEEK, "30590-08", cond=8),
        *[talk(ROSHEEK, "30590-09", cond=c) for c in range(9, 21)],
        talk(KASMAN, "30501-01", cond=9, ops=[take(GLETTER), give(FIG), cond(10)]),
        talk(KASMAN, "30501-02", cond=10), talk(KASMAN, "30501-03", cond=11), talk(KASMAN, "30501-03", cond=12),
        talk(KASMAN, "30501-04", cond=13, ops=[take(FIERY), take(IRON), take(TLETTER), give(TOTEM), exp(3200, 1500),
                                               FINISH, DONE]),
        talk(TORUKU, "30591-01", cond=10, ops=[take(FIG), give(POUCH4), cond(11)]),
        talk(TORUKU, "30591-02", cond=11),
        talk(TORUKU, "30591-03", cond=12, ops=[take(FULL4), give(IRON), give(TLETTER), cond(13)]),
        talk(TORUKU, "30591-04", cond=13),
        talk(AREN, "32056-01", cond=14), talk(AREN, "32056-04", cond=15),
        talk(AREN, "32056-05", cond=16, ops=[take_all(SPIDER_TOOTH), cond(17)]),
        talk(AREN, "32056-06", cond=17),
        talk(AREN, "32056-07", cond=18, ops=[take_all(HORN), cond(19)]),
        talk(AREN, "32056-09", cond=20),
        event(AREN, "32056-03", ops=[cond(15)], cond=14),
        event(AREN, "32056-08", ops=[cond(20)], cond=19),
        talk(MOIRA, "31979-01", cond=20),
        event(MOIRA, "31979-03", ops=[take(FIERY), give(TOTEM), exp(3200, 4230), FINISH, DONE], cond=20),
        # Mobius drops the quest when a non-fist weapon kills; here the kill simply does not count (deviation).
    ]
    for mob, at, item, pouch, full, nxt in ((20479, 2, CLAW, POUCH1, FULL1, 3), (20478, 4, TALON, POUCH2, FULL2, 5),
                                            (20415, 6, SCALE, POUCH3, FULL3, 7)):
        d += [
            kill(mob, [take_all(item), take(pouch), give(full), cond(nxt)], cond=at, atLeast={item: 4}),
            kill(mob, [give(item)], cond=at),
        ]
    d += [kill(m, [drop(SPIDER_TOOTH, 0.5, 6, 16)], cond=15) for m in (20478, 20476)]
    four = {20014: FELIM, 20017: TUSK, 20024: LANG, 20359: FANG}
    for mob, item in four.items():
        need = {x: 3 for x in four.values() if x != item}
        need[item] = 2
        d += [
            kill(mob, [take_all(*four.values()), take(POUCH4), give(FULL4), cond(12)], cond=11, atLeast=need),
            kill(mob, [drop(item, 1.0, 3)], cond=11),
        ]
    d.append(kill(21118, [give(HORN), cond(18)], cond=17))
    write(415, "00415_path_of_the_orc_monk", "Q00415_PathOfTheOrcMonk", "Path to a Monk",
          [POMEGRANATE, POUCH1, POUCH2, POUCH3, FULL1, FULL2, FULL3, CLAW, TALON, SCALE, FIERY, RLETTER, GLETTER, FIG,
           POUCH4, FULL4, TUSK, FANG, LANG, FELIM, IRON, TLETTER, SPIDER_TOOTH, HORN], d, repeatable=True)


def q416():
    TATARU, UMOS, HESTUI, DUDA, MOIRA, GANDI, CARCASS = 30585, 30502, 30592, 30593, 31979, 32057, 32090
    (CHARM, PELT, HUSK, EGG1, MASK, EGG2, CLAW, TLETTER, FLAME, BLOOD, CAULDRON, NET, BOUND, PARASITE,
     SPIRIT_BLOOD, MEDIUM) = range(1616, 1632)
    d = [
        talk(TATARU, "30585-01", when="absent"),
        *info_start(TATARU, "30585-05", "30585-06", [give(CHARM)], 49, 50, MEDIUM,
                    ("30585-03", "30585-04", "30585-02a", "30585-02")),
        talk(TATARU, "30585-07", cond=1),
        talk(TATARU, "30585-08", cond=2, ops=[take(EGG1), take(CHARM), take(PELT), take(HUSK), give(EGG2), give(MASK),
                                              cond(3)]),
        talk(TATARU, "30585-09", cond=3), talk(TATARU, "30585-10", cond=4), talk(TATARU, "30585-12", cond=5),
        *[talk(TATARU, "30585-13", cond=c) for c in range(6, 12)],
        talk(TATARU, "30585-11c", cond=12),
        event(TATARU, "30585-11b", ops=[take(CLAW), give(TLETTER), cond(5)], cond=4),
        event(TATARU, "30585-11c", ops=[take(CLAW), cond(12)], cond=4),
        talk(HESTUI, "30592-01", cond=3), talk(HESTUI, "30592-04", cond=4),
        *[talk(HESTUI, "30592-05", cond=c) for c in range(5, 12)],
        event(HESTUI, "30592-03", ops=[take(MASK), take(EGG2), give(CLAW), cond(4)], cond=3),
        talk(UMOS, "30502-01", cond=5, ops=[take(TLETTER), give(FLAME), cond(6)]),
        talk(UMOS, "30502-02", cond=6),
        talk(UMOS, "30502-03", cond=7, ops=[take(FLAME), take(BLOOD, 3), give(CAULDRON), cond(8)]),
        talk(UMOS, "30502-04", cond=8), talk(UMOS, "30502-05", cond=9), talk(UMOS, "30502-05", cond=10),
        talk(UMOS, "30502-06", cond=11),
        event(UMOS, "30502-07", ops=[take_all(SPIRIT_BLOOD), give(MEDIUM), exp(3200, 2600), FINISH, DONE], cond=11),
        talk(DUDA, "30593-01", cond=8), talk(DUDA, "30593-04", cond=9),
        talk(DUDA, "30593-05", cond=10, ops=[take(BOUND), give(SPIRIT_BLOOD), cond(11)]),
        talk(DUDA, "30593-06", cond=11),
        event(DUDA, "30593-03", ops=[take(CAULDRON), give(NET), cond(9)], cond=8),
        talk(MOIRA, "31979-01", cond=12, ops=[cond(13)]),
        *[talk(MOIRA, "31979-02", cond=c) for c in range(13, 21)],
        talk(MOIRA, "31979-03", cond=21, ops=[give(MEDIUM), exp(3200, 3250), FINISH, DONE]),
        talk(GANDI, "32057-01", cond=13),
        *[talk(GANDI, "32057-03", cond=c) for c in range(14, 20)],
        talk(GANDI, "32057-04", cond=20),
        event(GANDI, "32057-02", ops=[cond(14)], cond=13),
        event(GANDI, "32057-05", ops=[cond(21)], cond=20),
        talk(CARCASS, "32090-01a", cond=14),
        talk(CARCASS, "32090-01", cond=15, ops=[cond(16)]),
        talk(CARCASS, "32090-01b", cond=16), talk(CARCASS, "32090-02", cond=17),
        talk(CARCASS, "32090-05", cond=18),
        talk(CARCASS, "32090-06", cond=19, ops=[cond(20)]),
        event(CARCASS, "32090-04", ops=[cond(18)], cond=17),
    ]
    trio = {20479: PELT, 20478: HUSK, 20415: EGG1}
    for mob, item in trio.items():
        others = [x for x in trio.values() if x != item]
        d += [
            kill(mob, [give(item), cond(2)], cond=1, lacksItems=[item], hasItems=others),
            kill(mob, [give(item)], cond=1, lacksItems=[item]),
        ]
    d.append(kill(20335, [drop(BLOOD, 1.0, 3, 7)], cond=6))
    # Durka parasites: from five held, each spider kill may raise the Durka Spirit (10 % at 5, 20 % at 6-7,
    # always at 8); a miss adds another parasite (the plain give row after each chance row).
    raise_spirit = [take_all(PARASITE), spawn(27056)]
    for m in (20038, 20043):
        d += [
            kill(m, raise_spirit, cond=9, atLeast={PARASITE: 8}),
            kill(m, raise_spirit, cond=9, atLeast={PARASITE: 6}, chance=0.2),
            kill(m, [give(PARASITE)], cond=9, atLeast={PARASITE: 6}),
            kill(m, raise_spirit, cond=9, atLeast={PARASITE: 5}, chance=0.1),
            kill(m, [give(PARASITE)], cond=9),
        ]
    d += [
        kill(27056, [take_all(PARASITE), take(NET), give(BOUND), cond(10)], cond=9),
        kill(27319, [cond(15)], cond=14, varAtLeast={"leopard": 1}),
        kill(27319, [setv("leopard", 1)], cond=14),
        kill(27319, [cond(17)], cond=16),
        kill(27319, [cond(19)], cond=18),
    ]
    write(416, "00416_path_of_the_orc_shaman", "Q00416_PathOfTheOrcShaman", "Path to an Orc Shaman",
          [CHARM, PELT, HUSK, EGG1, MASK, EGG2, CLAW, TLETTER, FLAME, BLOOD, CAULDRON, NET, BOUND, PARASITE,
           SPIRIT_BLOOD], d, repeatable=True)


def q418():
    SILVERA, KLUTO, PINTER, OBI, HITCHI, LOCKIRIN, RYDEL = 30527, 30317, 30298, 32052, 31963, 30531, 31956
    RING, PASS1, PASS2, FINAL, TOOTH, LEADER, KLETTER, FOOTPRINT, STOLEN_BOX, BOX = range(1632, 1642)
    big = [take_all(PASS1, PASS2, BOX), give(FINAL), exp(3200, 6980), FINISH, DONE]
    small = [take_all(PASS1, PASS2, BOX), give(FINAL), exp(3200, 3490), FINISH, DONE]
    d = [
        talk(SILVERA, "30527-01", when="absent"),
        *info_start(SILVERA, "30527-05", "30527-06", [give(RING)], 53, 56, FINAL,
                    ("30527-03", "30527-04", "30527-02a", "30527-02")),
        talk(SILVERA, "30527-07", cond=1), talk(SILVERA, "30527-08", cond=2), talk(SILVERA, "30527-09", cond=3),
        talk(SILVERA, "30527-09a", cond=8),
        event(SILVERA, "30527-08a", ops=[take_all(LEADER, TOOTH), take(RING), give(PASS1), cond(3)], cond=2),
        event(SILVERA, "30527-08b", ops=[take_all(LEADER, TOOTH), take(RING), cond(8)], cond=2),
        talk(KLUTO, "30317-01", cond=3), talk(KLUTO, "30317-08", cond=4), talk(KLUTO, "30317-09", cond=7),
        *[event(KLUTO, ev, ops=[give(KLETTER), cond(4)], cond=3) for ev in ("30317-04", "30317-07")],
        event(KLUTO, "30317-10", ops=big, cond=7),
        event(KLUTO, "30317-12", ops=small, cond=7),
        talk(PINTER, "30298-01", cond=4), talk(PINTER, "30298-04", cond=5), talk(PINTER, "30298-05", cond=6),
        talk(PINTER, "30298-07", cond=7),
        event(PINTER, "30298-03", ops=[take_all(KLETTER), give(FOOTPRINT), cond(5)], cond=4),
        event(PINTER, "30298-06", ops=[take_all(FOOTPRINT, STOLEN_BOX), give(PASS2), give(BOX), cond(7)], cond=6),
        talk(OBI, "32052-01", cond=8), talk(OBI, "32052-06a", cond=9), talk(OBI, "32052-07", cond=11),
        event(OBI, "32052-06", ops=[cond(9)], cond=8),
        event(OBI, "32052-11", ops=small, cond=11),
        talk(HITCHI, "31963-01", cond=9), talk(HITCHI, "31963-04", cond=10), talk(HITCHI, "31963-06a", cond=11),
        talk(HITCHI, "31963-08", cond=12),
        event(HITCHI, "31963-04", ops=[cond(10)], cond=9),
        event(HITCHI, "31963-05", ops=[cond(11)], cond=10),
        event(HITCHI, "31963-07", ops=[cond(12)], cond=11),
        event(HITCHI, "31963-10", ops=small, cond=12),
        talk(LOCKIRIN, "30531-01", cond=10),
        event(LOCKIRIN, "30531-05", ops=small, cond=10),
        talk(RYDEL, "31956-01", cond=12),
        event(RYDEL, "31956-04", ops=small, cond=12),
        kill(20389, [drop(TOOTH, 0.7, 10, 2)], cond=1, atLeast={LEADER: 2}),
        kill(20389, [drop(TOOTH, 0.7, 10)], cond=1),
        kill(20390, [drop(LEADER, 0.5, 2, 2)], cond=1, atLeast={TOOTH: 10}),
        kill(20390, [drop(LEADER, 0.5, 2)], cond=1),
        kill(20017, [drop(STOLEN_BOX, 0.2, 1, 6)], cond=5),
    ]
    write(418, "00418_path_of_the_artisan", "Q00418_PathOfTheArtisan", "Path to an Artisan",
          [RING, PASS1, PASS2, TOOTH, LEADER, KLETTER, FOOTPRINT, STOLEN_BOX, BOX], d, repeatable=True)


if __name__ == "__main__":
    for f in (q401, q402, q403, q404, q405, q410, q411, q412, q413, q414, q415, q416, q417, q418):
        f()
