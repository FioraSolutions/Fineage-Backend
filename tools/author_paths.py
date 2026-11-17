#!/usr/bin/env python3
"""Block H1: first-profession path quests that the structural auto-port left unstartable (404, 405, 411–414, 417).

Step tables written for this engine from Mobius CT_0 quest behaviour (talk / event / kill branches, items, conds,
rewards); no Java is copied. Pages come from the Mobius quest folders via tools/questkit.py.
Usage: python3 tools/author_paths.py
"""
from questkit import (START, DONE, add, cond, drop, event, exp, give, kill, social, spawn, take, take_all, talk,
                      unset, write)

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


if __name__ == "__main__":
    for f in (q404, q405, q411, q412, q413, q414, q417):
        f()
