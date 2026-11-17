#!/usr/bin/env python3
"""Block H2: second-profession trials / testimonies (Q211–Q234) as quest-engine packs.

Step tables written for this engine from Mobius CT_0 quest behaviour (talk / event / kill branches, items, conds,
rewards); no Java is copied. Pages come from the Mobius quest folders via tools/questkit.py.
Common deviations: Dimensional Diamond first-time bonus (secondClassChangeNN) is not handed out; onAttack-only
spawns (helper mobs that rush in when a mob is hit) are dropped — the regular kill drops still finish the step.
Usage: python3 tools/author_trials.py [qid ...]
"""
import sys

from questkit import (DONE, START, FIRST_CLASSES, HUMAN, ELF, DARK_ELF, ORC, DWARF, add, adena, collect_all, cond,
                      drop, event, exp, give, kill, setv, social, spawn, spawn_at, take, take_all, talk, unset, write)

FINISH = social(3)


def mark(item, e, sp):
    return [give(item), exp(e, sp), FINISH, DONE]


def gate_start(npc, ok_page, start_page, ops, classes=None, races=None, level=0, refusals=()):
    """Created-state talk: ok page when eligible; refusals = [(page, gates)] tried in order after it.
    start_page: the accept event (gated the same as the ok page)."""
    g = {}
    if classes:
        g["classes"] = classes
    if races:
        g["races"] = races
    rows = [talk(npc, ok_page, when="absent", minLevel=level, **g)]
    rows += [talk(npc, page, when="absent", **gates) for page, gates in refusals]
    rows.append(event(npc, start_page, ops=START + ops, when="absent", minLevel=level, **g))
    return rows


AUTHORS = {}


def quest(fn):
    AUTHORS[int(fn.__name__[1:])] = fn
    return fn


@quest
def q211():
    FILAUR, KASH, MARTIEN, RALDO, CHEST = 30535, 30644, 30645, 30646, 30647
    LETTER, EYE1, EYE2, SCROLL, KEY = 2628, 2629, 2630, 2631, 2632
    MARK = 2627
    ok = [1, 19, 32, 45, 47]
    d = [
        *gate_start(KASH, "30644-03", "30644-05", [], classes=ok, level=35,
                    refusals=[("30644-01", {"classes": ok}), ("30644-02", {})]),
        talk(KASH, "30644-06", cond=1),
        talk(KASH, "30644-07", cond=2, ops=[take(SCROLL), give(LETTER), cond(3)]),
        talk(KASH, "30644-08", cond=3),
        *[talk(KASH, "30644-09", cond=c) for c in range(4, 11)],
        talk(CHEST, "30647-01"),
        # Chest of Shyslassys: the broken key opens it 1 in 5 (recipe / material loot), otherwise some adena.
        event(CHEST, "30647-04", "30647-03", hasItem=KEY, chance=0.02,
              ops=[take(KEY), give(2927), give(1940), give(1943), give(2918), give(1946)]),
        event(CHEST, "30647-04", "30647-03", hasItem=KEY, chance=0.04 / 0.98, ops=[take(KEY), give(1904), give(2030)]),
        event(CHEST, "30647-04", "30647-03", hasItem=KEY, chance=0.06 / 0.94, ops=[take(KEY), give(1936)]),
        event(CHEST, "30647-04", "30647-03", hasItem=KEY, chance=0.08 / 0.88, ops=[take(KEY), give(1940)]),
        event(CHEST, "30647-04", "30647-02", hasItem=KEY, ops=[take(KEY), adena(500)]),
        event(CHEST, "30647-04"),
        talk(MARTIEN, "30645-01", cond=3), talk(MARTIEN, "30645-03", cond=4),
        talk(MARTIEN, "30645-04", cond=5, ops=[take(EYE1), cond(6)]),
        talk(MARTIEN, "30645-05", cond=6), talk(MARTIEN, "30645-07", cond=7),
        *[talk(MARTIEN, "30645-06", cond=c) for c in (8, 9, 10)],
        event(MARTIEN, "30645-02", ops=[take(LETTER), cond(4)], cond=3),
        talk(RALDO, "30646-01", cond=7), talk(RALDO, "30646-06a", cond=8),
        talk(RALDO, "30646-07", cond=10, ops=[take_all(KEY)] + mark(MARK, 72394, 11250)),
        *[event(RALDO, ev, ops=[take(EYE2), cond(8)], cond=7) for ev in ("30646-04", "30646-06")],
        talk(FILAUR, "30535-01", cond=8, minLevel=36, ops=[cond(9)]),
        talk(FILAUR, "30535-03", cond=8),
        talk(FILAUR, "30535-02", cond=9), talk(FILAUR, "30535-04", cond=10),
        kill(27110, [give(KEY), give(SCROLL), cond(2), spawn(CHEST)], cond=1),
        kill(27112, [give(EYE1), cond(5)], cond=4),
        kill(27113, [give(EYE2), cond(7), spawn(RALDO)], cond=6),
        kill(27114, [cond(10), spawn(RALDO)], cond=9),
    ]
    write(211, "00211_trial_of_the_challenger", "Q00211_TrialOfTheChallenger", "Trial of the Challenger",
          [LETTER, EYE1, EYE2, SCROLL, KEY], d)


@quest
def q217():
    HOLLINT, ASTERIOS, THIFIELL, CLAYTON, SERESIN, KAKAI, MANAKIA, LOCKIRIN, NIKOLA, BIOTIN = (
        30191, 30154, 30358, 30464, 30657, 30565, 30515, 30531, 30621, 30031)
    (TO_ELF, TO_DE, TO_DWARF, TO_ORC, TO_SERESIN, DE_TRUST, ELF_TRUST, DWARF_TRUST, ORC_TRUST, RECOMM, ORDER_AST,
     BREATH, SEED, FROM_THIFIELL, BASILIK_BLOOD, APHID, FLUIDS, PLASMA, DEW, ICHOR, ORDER_CLAYTON, PARASITE,
     TO_MANAKIA, OF_MANAKIA, TO_NIKOLA, ORDER_NIKOLA, HEARTSTONE) = range(2735, 2762)
    MARK = 2734
    d = [
        *gate_start(HOLLINT, "30191-03", "30191-04", [give(TO_ELF), give(TO_DE)], classes=FIRST_CLASSES,
                    races=[HUMAN], level=37,
                    refusals=[("30191-01", {"classes": FIRST_CLASSES, "races": [HUMAN]}),
                              ("30191-02", {"classes": FIRST_CLASSES}), ("30191-01a", {})]),
        *[talk(HOLLINT, "30191-08", cond=c) for c in range(1, 9)],
        talk(HOLLINT, "30191-05", cond=9, ops=[take(DE_TRUST), take(ELF_TRUST), give(TO_SERESIN), cond(10)]),
        *[talk(HOLLINT, "30191-09", cond=c) for c in range(10, 22)],
        talk(HOLLINT, "30191-06", cond=22, ops=[take(DWARF_TRUST), take(ORC_TRUST), give(RECOMM), cond(23)]),
        talk(HOLLINT, "30191-07", cond=23),
        talk(ASTERIOS, "30154-01", cond=1), talk(ASTERIOS, "30154-04", cond=2),
        talk(ASTERIOS, "30154-05", cond=3, ops=[take(BREATH), take(SEED), take(ORDER_AST), give(ELF_TRUST), cond(4)]),
        *[talk(ASTERIOS, "30154-06", cond=c) for c in range(4, 24)],
        event(ASTERIOS, "30154-03", ops=[take(TO_ELF), give(ORDER_AST), cond(2)], cond=1),
        talk(THIFIELL, "30358-01", cond=4),
        *[talk(THIFIELL, "30358-05", cond=c) for c in (5, 6, 7)],
        talk(THIFIELL, "30358-03", cond=8, ops=[take(PLASMA), take(DEW), take(ICHOR), give(DE_TRUST), cond(9)]),
        *[talk(THIFIELL, "30358-04", cond=c) for c in range(9, 24)],
        event(THIFIELL, "30358-02", ops=[take(TO_DE), give(FROM_THIFIELL), cond(5)], cond=4),
        talk(CLAYTON, "30464-01", cond=5, ops=[take(FROM_THIFIELL), give(ORDER_CLAYTON), cond(6)]),
        talk(CLAYTON, "30464-02", cond=6),
        talk(CLAYTON, "30464-03", cond=7, ops=[take(ORDER_CLAYTON), cond(8)]),
        *[talk(CLAYTON, "30464-03", cond=c) for c in range(8, 24)],
        talk(SERESIN, "30657-01", cond=10), talk(SERESIN, "30657-01", cond=11),
        *[talk(SERESIN, "30657-04", cond=c) for c in range(12, 22)],
        talk(SERESIN, "30657-05", cond=22),
        event(SERESIN, "30657-03", ops=[take(TO_SERESIN), give(TO_DWARF), give(TO_ORC), cond(12)], cond=10, minLevel=38),
        event(SERESIN, "30657-03", ops=[take(TO_SERESIN), give(TO_DWARF), give(TO_ORC), cond(12)], cond=11, minLevel=38),
        event(SERESIN, "30657-03", "30657-02", ops=[cond(11)], cond=10),
        event(SERESIN, "30657-03", "30657-02", cond=11),
        talk(KAKAI, "30565-01", cond=12),
        *[talk(KAKAI, "30565-03", cond=c) for c in (13, 14, 15)],
        talk(KAKAI, "30565-04", cond=16, ops=[take(OF_MANAKIA), give(ORC_TRUST), cond(17)]),
        *[talk(KAKAI, "30565-05", cond=c) for c in range(17, 24)],
        event(KAKAI, "30565-02", ops=[take(TO_ORC), give(TO_MANAKIA), cond(13)], cond=12),
        talk(MANAKIA, "30515-01", cond=13), talk(MANAKIA, "30515-03", cond=14),
        talk(MANAKIA, "30515-04", cond=15, ops=[take_all(PARASITE), give(OF_MANAKIA), cond(16)]),
        *[talk(MANAKIA, "30515-05", cond=c) for c in range(16, 24)],
        event(MANAKIA, "30515-02", ops=[take(TO_MANAKIA), cond(14)], cond=13),
        talk(LOCKIRIN, "30531-01", cond=17),
        *[talk(LOCKIRIN, "30531-03", cond=c) for c in (18, 19, 20)],
        talk(LOCKIRIN, "30531-04", cond=21, ops=[give(DWARF_TRUST), cond(22)]),
        talk(LOCKIRIN, "30531-05", cond=22),
        event(LOCKIRIN, "30531-02", ops=[take(TO_DWARF), give(TO_NIKOLA), cond(18)], cond=17),
        talk(NIKOLA, "30621-01", cond=18), talk(NIKOLA, "30621-03", cond=19),
        talk(NIKOLA, "30621-04", cond=20, ops=[take_all(HEARTSTONE), take(ORDER_NIKOLA), cond(21)]),
        *[talk(NIKOLA, "30621-05", cond=c) for c in range(21, 24)],
        event(NIKOLA, "30621-02", ops=[take(TO_NIKOLA), give(ORDER_NIKOLA), cond(19)], cond=18),
        talk(BIOTIN, "30031-01", cond=23, ops=[take(RECOMM)] + mark(MARK, 39571, 2500)),
        # Elven trust: dryads / lireins may raise Actea / Luell, whose deaths give seed / breath.
        *[kill(m, [spawn(27121)], cond=2, lacksItems=[SEED], chance=0.33) for m in (20013, 20019)],
        *[kill(m, [spawn(27120)], cond=2, lacksItems=[BREATH], chance=0.33) for m in (20036, 20044)],
        kill(27121, [give(SEED), cond(3)], cond=2, lacksItems=[SEED], hasItem=BREATH),
        kill(27121, [give(SEED)], cond=2, lacksItems=[SEED]),
        kill(27120, [give(BREATH), cond(3)], cond=2, lacksItems=[BREATH], hasItem=SEED),
        kill(27120, [give(BREATH)], cond=2, lacksItems=[BREATH]),
        kill(20553, [drop(PARASITE, 0.5, 10, 15)], cond=14),
        kill(20213, [drop(HEARTSTONE, 1.0, 10, 20)], cond=19),
    ]
    # Dark-elf trust: ten of each part turn into the finished ingredient; all three finished -> cond 7.
    parts = [((20157, 20230, 20232, 20234), FLUIDS, ICHOR), ((20082, 20084, 20086, 20087, 20088), APHID, DEW),
             ((20550,), BASILIK_BLOOD, PLASMA)]
    for mobs, part, done in parts:
        others = [x for _, _, x in parts if x != done]
        convert = [take_all(part), give(done)]
        for m in mobs:
            d += [
                kill(m, convert + [cond(7)], cond=6, lacksItems=[done], hasItems=others, atLeast={part: 9}),
                kill(m, convert, cond=6, lacksItems=[done], atLeast={part: 9}),
                kill(m, [give(part)], cond=6, lacksItems=[done]),
            ]
    write(217, "00217_testimony_of_trust", "Q00217_TestimonyOfTrust", "Testimony of Trust",
          [TO_ELF, TO_DE, TO_DWARF, TO_ORC, TO_SERESIN, DE_TRUST, ELF_TRUST, DWARF_TRUST, ORC_TRUST, RECOMM, ORDER_AST,
           BREATH, SEED, FROM_THIFIELL, BASILIK_BLOOD, APHID, FLUIDS, PLASMA, DEW, ICHOR, ORDER_CLAYTON, PARASITE,
           TO_MANAKIA, OF_MANAKIA, TO_NIKOLA, ORDER_NIKOLA, HEARTSTONE], d)


@quest
def q222():
    KAIEN = 30623
    ORDERS = list(range(2763, 2768))
    (SHARD, FEELER, CHITIN, FANG, NECKLACE, CHAIN, AMULET, MEAT, RING, ARROW, FINAL, EXCURO, KRATOR, GRANDIS, BELT,
     MACE) = range(2768, 2784)
    MARK = 2762
    ok = [1, 47, 19, 32]
    first = {20085: SHARD, 20090: FEELER, 20234: CHITIN, 20202: FANG, 20270: NECKLACE, 20552: CHAIN, 20582: AMULET,
             20564: MEAT, 20601: RING, 20602: ARROW}
    second = {20214: EXCURO, 20217: KRATOR, 20604: MACE, 20554: GRANDIS, 20588: BELT}
    d = [
        talk(KAIEN, "30623-03", when="absent", classes=ok, minLevel=39),
        talk(KAIEN, "30623-01", when="absent", classes=ok),
        talk(KAIEN, "30623-02", when="absent"),
        event(KAIEN, "30623-04", "30623-05", when="absent", races=[ORC]),
        event(KAIEN, "30623-07", ops=START + [cond(2)] + [give(o) for o in ORDERS], when="absent", classes=ok,
              minLevel=39),
        talk(KAIEN, "30623-07a", cond=2), talk(KAIEN, "30623-13", cond=3), talk(KAIEN, "30623-17", cond=4),
        talk(KAIEN, "30623-18", cond=5, ops=[take(FINAL), take_all(*second.values())] + mark(MARK, 47015, 20000)),
        event(KAIEN, "30623-16", ops=[take_all(*ORDERS, *first.values()), give(FINAL), cond(4)], cond=3),
        *collect_all(first, 10, 2, 3),
        *collect_all(second, 3, 4, 5),
    ]
    write(222, "00222_test_of_the_duelist", "Q00222_TestOfTheDuelist", "Test of the Duelist",
          [*ORDERS, *first.values(), FINAL, *second.values()], d)


@quest
def q223():
    ASCALON, GROOT, MOUEN, MASON = 30624, 30093, 30196, 30625
    (L1, MASON_LETTER, ROSE_RING, L2, INSIGNIA, GROOT_LETTER, L3, ORDER1, ORDER2, MOUEN_LETTER, EGG, VENOM, BILE,
     AXE_HEAD, RATMAN_HEAD, FANG) = range(3277, 3293)
    MARK = 3276
    d = [
        talk(ASCALON, "30624-03", when="absent", classes=[1], minLevel=39),
        talk(ASCALON, "30624-04", when="absent", classes=[45], minLevel=39),
        talk(ASCALON, "30624-02", when="absent", classes=[1, 45]),
        talk(ASCALON, "30624-01", when="absent"),
        event(ASCALON, "30624-06", ops=START + [give(L1)], when="absent", classes=[1, 45], minLevel=39),
        talk(ASCALON, "30624-07", cond=1), talk(ASCALON, "30624-08", cond=2), talk(ASCALON, "30624-08", cond=3),
        talk(ASCALON, "30624-09", cond=4), talk(ASCALON, "30624-11", cond=5),
        talk(ASCALON, "30624-12", cond=6), talk(ASCALON, "30624-12", cond=7), talk(ASCALON, "30624-13", cond=8),
        talk(ASCALON, "30624-15", cond=9),
        *[talk(ASCALON, "30624-16", cond=c) for c in range(10, 14)],
        talk(ASCALON, "30624-17", cond=14, ops=[take(MOUEN_LETTER)] + mark(MARK, 117454, 25000)),
        event(ASCALON, "30624-10", ops=[take(MASON_LETTER), give(L2), cond(5)], cond=4),
        event(ASCALON, "30624-14", ops=[take(GROOT_LETTER), give(L3), cond(9)], cond=8),
        talk(MASON, "30625-01", cond=1), talk(MASON, "30625-04", cond=2),
        talk(MASON, "30625-05", cond=3, ops=[take_all(AXE_HEAD), take(ROSE_RING), give(MASON_LETTER), cond(4)]),
        talk(MASON, "30625-06", cond=4),
        *[talk(MASON, "30625-07", cond=c) for c in range(5, 15)],
        event(MASON, "30625-03", ops=[take(L1), give(ROSE_RING), cond(2)], cond=1),
        talk(GROOT, "30093-01", cond=5), talk(GROOT, "30093-03", cond=6),
        talk(GROOT, "30093-04", cond=7, ops=[take(INSIGNIA), take_all(EGG, VENOM, BILE), give(GROOT_LETTER), cond(8)]),
        talk(GROOT, "30093-05", cond=8),
        *[talk(GROOT, "30093-06", cond=c) for c in range(9, 15)],
        event(GROOT, "30093-02", ops=[take(L2), give(INSIGNIA), cond(6)], cond=5),
        talk(MOUEN, "30196-01", cond=9), talk(MOUEN, "30196-04", cond=10), talk(MOUEN, "30196-05", cond=11),
        talk(MOUEN, "30196-07", cond=12),
        talk(MOUEN, "30196-08", cond=13, ops=[take_all(FANG), take(ORDER2), give(MOUEN_LETTER), cond(14)]),
        talk(MOUEN, "30196-09", cond=14),
        event(MOUEN, "30196-03", ops=[take(L3), give(ORDER1), cond(10)], cond=9),
        event(MOUEN, "30196-06", ops=[take(ORDER1), take_all(RATMAN_HEAD), give(ORDER2), cond(12)], cond=11),
        kill(20780, [drop(AXE_HEAD, 1.0, 100, 3)], cond=2),
        *collect_all({20145: EGG, 27088: EGG, 20158: VENOM, 20553: BILE}, 30, 6, 7, chance=0.5),
        *[kill(m, [drop(RATMAN_HEAD, 1.0, 100, 11)], cond=10) for m in (27089, 20551)],
        *[kill(m, [drop(FANG, 0.5 + 0.1 * (m - 20577), 100, 13)], cond=12) for m in range(20577, 20583)],
    ]
    write(223, "00223_test_of_the_champion", "Q00223_TestOfTheChampion", "Test of the Champion",
          [MASON_LETTER, VENOM, BILE, INSIGNIA, EGG, GROOT_LETTER, MOUEN_LETTER, L1, ROSE_RING, AXE_HEAD, L2, L3,
           ORDER1, RATMAN_HEAD, ORDER2, FANG], d)


@quest
def q212():
    HANNAVALT, DUSTIN, COLLIN, ARON, KIEL, SILVERSHADOW, TALIANUS = 30109, 30116, 30311, 30653, 30654, 30655, 30656
    (L_DUSTIN, TEAR, MIRROR, CONFESSION, PIECE1, PIECE2, LOYALTY, ARTICLE, URN, SKULL, RIBS, SHIN,
     L_WINDAWOOD) = range(2634, 2647)
    SWORD, MARK = 3027, 2633
    ok = [4, 19, 32]
    d = [
        talk(HANNAVALT, "30109-03", when="absent", classes=ok, minLevel=35),
        talk(HANNAVALT, "30109-01", when="absent", classes=ok),
        talk(HANNAVALT, "30109-02", when="absent"),
        event(HANNAVALT, "30109-04", ops=START, when="absent", classes=ok, minLevel=35),
        talk(HANNAVALT, "30109-05", cond=18, ops=[take(L_DUSTIN)] + mark(MARK, 79832, 3750)),
        talk(HANNAVALT, "30109-04a"),
        talk(ARON, "30653-01", cond=1, ops=[give(SWORD), cond(2)]),
        talk(ARON, "30653-02", cond=2),
        talk(ARON, "30653-03", cond=3, ops=[take(TEAR), take(SWORD), cond(4)]),
        *[talk(ARON, "30653-04", cond=c) for c in range(4, 19)],
        talk(KIEL, "30654-01", cond=4, ops=[cond(5)]), talk(KIEL, "30654-02", cond=5),
        talk(KIEL, "30654-03", cond=6, ops=[give(MIRROR), cond(7)]), talk(KIEL, "30654-04", cond=7),
        talk(KIEL, "30654-05", cond=9, ops=[take(CONFESSION), cond(10)]),
        *[talk(KIEL, "30654-06", cond=c) for c in range(10, 19)],
        talk(TALIANUS, "30656-01", cond=8, ops=[take(MIRROR), take(PIECE2), give(CONFESSION), cond(9)]),
        talk(SILVERSHADOW, "30655-02", cond=10, minLevel=35, ops=[cond(11)]),
        talk(SILVERSHADOW, "30655-01", cond=10),
        talk(SILVERSHADOW, "30655-03", cond=11),
        talk(SILVERSHADOW, "30655-04", cond=12, ops=[take_all(ARTICLE), give(LOYALTY), cond(13)]),
        talk(SILVERSHADOW, "30655-05", cond=13),
        talk(DUSTIN, "30116-01", cond=13), talk(DUSTIN, "30116-06", cond=14),
        talk(DUSTIN, "30116-07", cond=15, ops=[take(SKULL), take(RIBS), take(SHIN), give(URN), cond(16)]),
        talk(DUSTIN, "30116-09", cond=16),
        talk(DUSTIN, "30116-08", cond=17, ops=[take(L_WINDAWOOD), give(L_DUSTIN), cond(18)]),
        talk(DUSTIN, "30116-10", cond=18),
        event(DUSTIN, "30116-05", ops=[take(LOYALTY), cond(14)], cond=13),
        talk(COLLIN, "30311-01", cond=16, ops=[take(URN), give(L_WINDAWOOD), cond(17)]),
        talk(COLLIN, "30311-02", cond=17), talk(COLLIN, "30311-02", cond=18),
        *[kill(m, [spawn(27119)], cond=2, chance=0.1) for m in (20190, 20191)],
        # Hanged Man Ripper only counts when struck down with the Old Knight Sword.
        kill(27119, [give(TEAR), cond(3)], cond=2, weapons=[SWORD]),
        *[kill(m, [take_all(PIECE1), give(PIECE2), cond(6)], cond=5, atLeast={PIECE1: 9}) for m in (20200, 20201)],
        *[kill(m, [give(PIECE1)], cond=5) for m in (20200, 20201)],
        # Spirit of Sir Talianus rises from a Lesser Undead (once, at cond 7).
        kill(20144, [spawn(TALIANUS), cond(8)], cond=7, chance=0.33),
        *[kill(m, [drop(ARTICLE, 1.0, 20, 12)], cond=11) for m in range(20577, 20583)],
        kill(20270, [give(SKULL)], cond=14, lacksItems=[SKULL], chance=0.5),
        kill(20270, [give(RIBS)], cond=14, lacksItems=[RIBS], chance=0.5),
        kill(20270, [give(SHIN), cond(15)], cond=14, lacksItems=[SHIN], chance=0.5),
    ]
    write(212, "00212_trial_of_duty", "Q00212_TrialOfDuty", "Trial of Duty",
          [L_DUSTIN, TEAR, MIRROR, CONFESSION, PIECE1, PIECE2, LOYALTY, ARTICLE, URN, SKULL, RIBS, SHIN, L_WINDAWOOD,
           SWORD], d)


@quest
def q226():
    BANDELLOS, SORIUS, ALLANA, PERRIN, GUPU, ORPHAN, WINDY, DARKELF, PIPER, SLEIN, KAIN, KRISTINA, DAURIN = (
        30473, 30327, 30424, 30428, 30658, 30659, 30660, 30661, 30662, 30663, 30664, 30665, 30674)
    LEADER, ASSASSIN, SNIPER, WIZARD, LORD, TATOMA = 27123, 27124, 27125, 27126, 27127, 27134
    REPORT, K_LETTER, PICTURE, STATUE, PEBBLES, ORDER, S1, S2, S3, S4 = range(2810, 2820)
    MARK = 2820
    ok = [4, 19, 15, 29]
    tatoma = spawn_at(TATOMA, -93254, 147559, -2679)
    leader = spawn_at(LEADER, -97441, 106585, -3405)
    d = [
        talk(BANDELLOS, "30473-03", when="absent", classes=ok, minLevel=39),
        talk(BANDELLOS, "30473-02", when="absent", classes=ok),
        talk(BANDELLOS, "30473-01", when="absent"),
        event(BANDELLOS, "30473-04", ops=START + [give(REPORT)], when="absent", classes=ok, minLevel=39),
        *[talk(BANDELLOS, "30473-05", cond=c) for c in range(1, 23)],
        talk(BANDELLOS, "30473-07", cond=23, hasItem=STATUE),
        talk(BANDELLOS, "30473-06", cond=23, ops=mark(MARK, 118304, 26250)),
        event(BANDELLOS, "30473-09", ops=[take(STATUE)] + mark(MARK, 134839, 50000), cond=23, hasItem=STATUE),
        talk(PERRIN, "30428-01", cond=1), talk(PERRIN, "30428-01", cond=2),
        talk(PERRIN, "30428-03", cond=3, ops=[take(REPORT), cond(4)]),
        *[talk(PERRIN, "30428-04", cond=c) for c in range(4, 24)],
        # Tatoma waits at his Mobius point; the event can be repeated while he is not slain yet.
        event(PERRIN, "30428-02", ops=[cond(2), tatoma], cond=1),
        event(PERRIN, "30428-02", ops=[tatoma], cond=2),
        talk(ORPHAN, "30659-01"),
        talk(ALLANA, "30424-01", cond=4, ops=[cond(5)]),
        *[talk(ALLANA, "30424-02", cond=c) for c in range(5, 24)],
        *[talk(GUPU, "30658-07", cond=c, varAtLeast={"gupu": 1}, ops=[cond(9)]) for c in (5, 6, 7, 8)],
        talk(GUPU, "30658-01", cond=5, ops=[cond(6)]), talk(GUPU, "30658-01", cond=6),
        talk(GUPU, "30658-04", cond=7),
        talk(GUPU, "30658-06", cond=8, hasItem=PEBBLES, ops=[take(PEBBLES), give(STATUE)]),
        talk(GUPU, "30658-06", cond=8),
        *[talk(GUPU, "30658-07", cond=c) for c in range(9, 24)],
        event(GUPU, "30658-02", ops=[take(57, 100000), give(PICTURE), cond(7)], cond=6, atLeast={57: 100000}),
        event(GUPU, "30658-02", "30658-05", cond=6),
        event(GUPU, "30658-03", ops=[setv("gupu", 1)], cond=6),
        event(GUPU, "30658-07", ops=[cond(9)], cond=8),
        talk(WINDY, "30660-01", cond=7),
        talk(WINDY, "30660-04", hasItem=PEBBLES),
        event(WINDY, "30660-03", ops=[take(PICTURE), give(PEBBLES), cond(8)], cond=7),
        talk(SORIUS, "30327-01", cond=9, ops=[give(ORDER), cond(10)]),
        *[talk(SORIUS, "30327-02", cond=c) for c in range(10, 22)],
        talk(SORIUS, "30327-03", cond=22, ops=[take(K_LETTER), cond(23)]),
        talk(SORIUS, "30327-04", cond=23),
        talk(DAURIN, "30674-01", cond=10),
        talk(DAURIN, "30674-02a", cond=11),
        talk(DAURIN, "30674-03", cond=12, ops=[cond(13)]),
        *[talk(DAURIN, "30674-04", cond=c) for c in range(13, 24)],
        event(DAURIN, "30674-02", ops=[take(ORDER), cond(11), leader], cond=10),
        event(DAURIN, "30674-02", ops=[leader], cond=11),
    ]
    for npc in (PIPER, SLEIN, KAIN):
        d += [talk(npc, f"{npc}-01", cond=c) for c in (13, 14)]
        d += [talk(npc, f"{npc}-02", cond=c) for c in range(15, 19)]
        d += [talk(npc, f"{npc}-03", cond=c, ops=[cond(21)]) for c in (19, 20, 21)]
    d += [
        # The Mysterious Dark Elf calls the Leto ambush around the player; while a wave is alive, another visit
        # brings one more of the wave (Mobius checkSpawn keeps the wave at full size).
        talk(DARKELF, "30661-01", cond=13, ops=[cond(14), spawn(ASSASSIN), spawn(ASSASSIN), spawn(ASSASSIN)]),
        talk(DARKELF, "30661-01", cond=14, ops=[spawn(ASSASSIN)]),
        talk(DARKELF, "30661-02", cond=15, ops=[cond(16), spawn(SNIPER), spawn(SNIPER), spawn(SNIPER)]),
        talk(DARKELF, "30661-02", cond=16, ops=[spawn(SNIPER)]),
        talk(DARKELF, "30661-03", cond=17, ops=[cond(18), spawn(WIZARD), spawn(WIZARD), spawn(LORD)]),
        talk(DARKELF, "30661-03", cond=18, ops=[spawn(LORD)]),
        talk(DARKELF, "30661-04", cond=19, ops=[cond(20)]),
        talk(DARKELF, "30661-04", cond=20), talk(DARKELF, "30661-04", cond=21),
        *[talk(KRISTINA, "30665-01", cond=c) for c in (19, 20, 21)],
        *[talk(KRISTINA, "30665-04", cond=c) for c in (22, 23)],
        talk(KRISTINA, "30665-03"),
        *[event(KRISTINA, "30665-02", ops=[take(S1), take(S2), take(S3), take(S4), give(K_LETTER), cond(22)], cond=c)
          for c in (19, 20, 21)],
        *[kill(TATOMA, [cond(3)], cond=c) for c in (1, 2)],
        *[kill(LEADER, [give(S1), cond(12)], cond=c) for c in (10, 11)],
        *[kill(ASSASSIN, [give(S2), cond(15)], cond=c) for c in (13, 14)],
        *[kill(SNIPER, [give(S3), cond(17)], cond=c) for c in (15, 16)],
        *[kill(LORD, [give(S4), cond(19)], cond=c) for c in (17, 18)],
    ]
    write(226, "00226_test_of_the_healer", "Q00226_TestOfTheHealer", "Test of the Healer",
          [REPORT, K_LETTER, PICTURE, STATUE, PEBBLES, ORDER, S1, S2, S3, S4], d)


@quest
def q218():
    ASTERIOS, PUSHKIN, THALIA, ADONIUS, ARKENIA, CARDIEN, ISAEL = 30154, 30300, 30371, 30375, 30419, 30460, 30655
    SPEAR = 3026
    (C_LETTER, CAMOMILE, H_LETTER, MOONFLOWER, DIAGRAM, T_LETTER1, T_LETTER2, T_INSTR, P_LIST, CUP, CONTRACT, A_INSTR,
     AD_LIST, ANDARIEL, STARDUST, I_INSTR, I_LETTER, GRAIL, TEARS, WATER, ORE, ACID, TALON, ICHOR, DOWN) = range(3141, 3166)
    PIECES = list(range(3166, 3172))
    MARK = 3140
    d = [
        talk(CARDIEN, "30460-03", when="absent", races=[ELF], classes=FIRST_CLASSES, minLevel=37),
        talk(CARDIEN, "30460-02", when="absent", races=[ELF]),
        talk(CARDIEN, "30460-01", when="absent"),
        event(CARDIEN, "30460-04", ops=START + [give(C_LETTER)], when="absent", races=[ELF], classes=FIRST_CLASSES,
              minLevel=37),
        talk(CARDIEN, "30460-05", cond=1),
        *[talk(CARDIEN, "30460-06", cond=c) for c in range(2, 21)],
        talk(CARDIEN, "30460-07", cond=21, ops=[take(CAMOMILE)] + mark(MARK, 104591, 11250)),
        talk(ASTERIOS, "30154-01", cond=1), talk(ASTERIOS, "30154-08", cond=2),
        talk(ASTERIOS, "30154-09", cond=20, ops=[take(MOONFLOWER), take(WATER), give(CAMOMILE), cond(21)]),
        talk(ASTERIOS, "30154-10", cond=21),
        event(ASTERIOS, "30154-07", ops=[take(C_LETTER), give(H_LETTER), give(MOONFLOWER), cond(2)], cond=1),
        talk(PUSHKIN, "30300-01", cond=3), talk(PUSHKIN, "30300-07", cond=4), talk(PUSHKIN, "30300-08", cond=5),
        talk(PUSHKIN, "30300-11", cond=6),
        *[talk(PUSHKIN, "30300-12", cond=c) for c in range(7, 22)],
        event(PUSHKIN, "30300-06", ops=[take(DIAGRAM), give(P_LIST), cond(4)], cond=3),
        event(PUSHKIN, "30300-10", ops=[take(P_LIST), take_all(ACID, ORE, TALON), give(CUP), cond(6)], cond=5),
        talk(THALIA, "30371-01", cond=2), talk(THALIA, "30371-04", cond=3),
        talk(THALIA, "30371-05", cond=4), talk(THALIA, "30371-05", cond=5),
        talk(THALIA, "30371-06", cond=6, ops=[take(CUP), give(T_LETTER1), cond(7)]),
        talk(THALIA, "30371-07", cond=7),
        *[talk(THALIA, "30371-08", cond=c) for c in range(8, 12)],
        talk(THALIA, "30371-09", cond=12),
        talk(THALIA, "30371-13", cond=13, minLevel=38, ops=[take(T_INSTR), give(T_LETTER2), cond(14)]),
        talk(THALIA, "30371-12", cond=13),
        talk(THALIA, "30371-14", cond=14),
        talk(THALIA, "30371-15", cond=15), talk(THALIA, "30371-15", cond=16),
        talk(THALIA, "30371-16", cond=17, ops=[take(I_LETTER), give(GRAIL), cond(18)]),
        talk(THALIA, "30371-17", cond=18),
        talk(THALIA, "30371-18", cond=19, ops=[take(TEARS), give(WATER), cond(20)]),
        talk(THALIA, "30371-19", cond=20), talk(THALIA, "30371-19", cond=21),
        event(THALIA, "30371-03", ops=[take(H_LETTER), give(DIAGRAM), cond(3)], cond=2),
        event(THALIA, "30371-11", ops=[take(STARDUST), give(T_LETTER2), cond(14)], cond=12, minLevel=38),
        event(THALIA, "30371-11", "30371-10", ops=[take(STARDUST), give(T_INSTR), cond(13)], cond=12),
        talk(ADONIUS, "30375-01", cond=8), talk(ADONIUS, "30375-03", cond=9),
        talk(ADONIUS, "30375-04", cond=10, ops=[take(AD_LIST), take_all(DOWN, ICHOR), give(ANDARIEL), cond(11)]),
        talk(ADONIUS, "30375-05", cond=11),
        *[talk(ADONIUS, "30375-06", cond=c) for c in range(12, 22)],
        event(ADONIUS, "30375-02", ops=[take(A_INSTR), give(AD_LIST), cond(9)], cond=8),
        talk(ARKENIA, "30419-01", cond=7),
        *[talk(ARKENIA, "30419-05", cond=c) for c in (8, 9, 10)],
        talk(ARKENIA, "30419-06", cond=11, ops=[take(ANDARIEL), take(CONTRACT), give(STARDUST), cond(12)]),
        talk(ARKENIA, "30419-07", cond=12),
        *[talk(ARKENIA, "30419-08", cond=c) for c in range(13, 22)],
        event(ARKENIA, "30419-04", ops=[take(T_LETTER1), give(CONTRACT), give(A_INSTR), cond(8)], cond=7),
        talk(ISAEL, "30655-01", cond=14), talk(ISAEL, "30655-03", cond=15),
        talk(ISAEL, "30655-04", cond=16, hasItems=PIECES,
             ops=[take_all(*PIECES), take(I_INSTR), give(I_LETTER), give(SPEAR), cond(17)]),
        talk(ISAEL, "30655-03", cond=16),
        talk(ISAEL, "30655-05", cond=17),
        *[talk(ISAEL, "30655-06", cond=c) for c in range(18, 22)],
        event(ISAEL, "30655-02", ops=[take(T_LETTER2), give(I_INSTR), cond(15)], cond=14),
        *collect_all({20550: (ORE, 0.5), 20176: (TALON, 0.5), **{m: (ACID, 0.8) for m in (20082, 20084, 20086, 20087, 20088)}},
                     20, 4, 5, limits={ORE: 10}),
        *collect_all({20233: ICHOR, 20145: DOWN}, 20, 9, 10, chance=0.5),
        # The unicorn's tears only come to one wielding Talin's spear.
        kill(27077, [take(GRAIL), take(SPEAR), give(TEARS), cond(19)], cond=18, weapons=[SPEAR]),
    ]
    # Talin's spear pieces: each lucky kill gives the first missing piece; with all six, the next one sets cond 16.
    for m in (20581, 20582):
        for i, piece in enumerate(PIECES):
            d.append(kill(m, [give(piece)], cond=15, chance=0.5, lacksItems=[piece],
                          **({"hasItems": PIECES[:i]} if i else {})))
        d.append(kill(m, [cond(16)], cond=15, chance=0.5, hasItems=PIECES))
    write(218, "00218_testimony_of_life", "Q00218_TestimonyOfLife", "Testimony of Life",
          [SPEAR, C_LETTER, CAMOMILE, H_LETTER, MOONFLOWER, DIAGRAM, T_LETTER1, T_LETTER2, T_INSTR, P_LIST, CUP,
           CONTRACT, A_INSTR, AD_LIST, ANDARIEL, STARDUST, I_INSTR, I_LETTER, GRAIL, TEARS, WATER, ORE, ACID, TALON,
           ICHOR, DOWN, *PIECES], d)


if __name__ == "__main__":
    ids = [int(a) for a in sys.argv[1:]] or sorted(AUTHORS)
    for qid in ids:
        AUTHORS[qid]()
