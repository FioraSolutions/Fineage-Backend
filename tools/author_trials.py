#!/usr/bin/env python3
"""Block H2: second-profession trials / testimonies (Q211–Q234) as quest-engine packs.

Step tables written for this engine from Mobius CT_0 quest behaviour (talk / event / kill branches, items, conds,
rewards); no Java is copied. Pages come from the Mobius quest folders via tools/questkit.py.
Common deviations: Dimensional Diamond first-time bonus (secondClassChangeNN) is not handed out; onAttack-only
spawns (helper mobs that rush in when a mob is hit) are dropped — the regular kill drops still finish the step.
Usage: python3 tools/author_trials.py [qid ...]
"""
import sys

from questkit import (DONE, START, FIRST_CLASSES, HUMAN, ELF, DARK_ELF, ORC, DWARF, add, adena, attack, collect_all,
                      cond, drop, event, exp, give, kill, mobius_page, setv, social, spawn, spawn_at, take, take_all,
                      talk, unset, write)

FINISH = social(3)


def mark(item, e, sp):
    return [give(item), exp(e, sp), FINISH, DONE]


def _no_page(row):
    """An action event that answers with no page (Mobius returns the event name, which is no file)."""
    row.pop("htmlFile")
    row["html"] = ""
    return row


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
        # A hit on a Harpy (cond 6) / Road Scavenger (cond 10) calls one or two helpers half the time (once per mob):
        # 50% * 30% = two, 50% * 70% = one; the second step rolls only when the first one missed.
        *[r for mob, helper, c in ((20145, 27088, 6), (20551, 27089, 10)) for r in (
            attack(mob, [spawn(helper), spawn(helper)], cond=c, chance=0.15),
            attack(mob, [spawn(helper)], cond=c, chance=round(0.35 / 0.85, 4)))],
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
        kill(20270, [give(RIBS)], cond=14, hasItem=SKULL, lacksItems=[RIBS], chance=0.5),
        kill(20270, [give(SHIN), cond(15)], cond=14, hasItems=[SKULL, RIBS], lacksItems=[SHIN], chance=0.5),
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


@quest
def q213():
    TERRY, DUFNER, BRUNON, VIKTOR, MARINA = 30064, 30106, 30526, 30684, 30715
    (D_LETTER, ORDER1, ORDER2, T_LETTER, V_LETTER, H_LETTER, MYST, OL_MAHUM, TUREK, ANT, TURAK, BOX, V_REQUEST, SCALES,
     SHILEN, A_REQUEST, M_LETTER, TOOLS, RESULT, ORDER3, HOSTS, AB1, AB2, AB3, AB4, REPORT) = range(2647, 2673)
    MARK = 2673
    ok = [7, 22, 35]
    d = [
        talk(DUFNER, "30106-03", when="absent", classes=ok, minLevel=35),
        talk(DUFNER, "30106-02", when="absent", classes=ok),
        talk(DUFNER, "30106-00", when="absent"),
        event(DUFNER, "30106-05", ops=START + [give(D_LETTER)], when="absent", classes=ok, minLevel=35),
        talk(DUFNER, "30106-06", cond=1),
        talk(DUFNER, "30106-08", hasItem=REPORT, ops=[take(REPORT)] + mark(MARK, 72126, 11000)),
        talk(DUFNER, "30106-07"),
        talk(TERRY, "30064-01", cond=1), talk(TERRY, "30064-04", cond=2), talk(TERRY, "30064-05", cond=3),
        talk(TERRY, "30064-08", cond=4), talk(TERRY, "30064-09", cond=5), talk(TERRY, "30064-11", cond=6),
        talk(TERRY, "30064-12", cond=7, ops=[take(V_LETTER), give(H_LETTER), cond(8)]),
        talk(TERRY, "30064-13", cond=8),
        *[talk(TERRY, "30064-14", cond=c) for c in range(9, 14)],
        talk(TERRY, "30064-15", cond=14, lacksItems=[ORDER3]),
        talk(TERRY, "30064-21", cond=14, minLevel=36, ops=[take(ORDER3), give(HOSTS), cond(15)]),
        talk(TERRY, "30064-20", cond=14),
        talk(TERRY, "30064-22", cond=15), talk(TERRY, "30064-22", cond=16),
        talk(TERRY, "30064-23", cond=17, lacksItems=[REPORT],
             ops=[take(HOSTS), take(AB1), take(AB2), take(AB3), take(AB4), give(REPORT)]),
        talk(TERRY, "30064-24", cond=17),
        event(TERRY, "30064-03", ops=[take(D_LETTER), give(ORDER1), cond(2)], cond=1),
        event(TERRY, "30064-06", ops=[take(MYST), take(ORDER1), give(ORDER2), cond(4)], cond=3),
        event(TERRY, "30064-10", ops=[take(ANT), take(OL_MAHUM), take(TURAK), take(TUREK), take(ORDER2), give(BOX),
                                      give(T_LETTER), cond(6)], cond=5),
        event(TERRY, "30064-18", ops=[take(RESULT), give(HOSTS), cond(16)], cond=14, hasItem=RESULT, minLevel=36),
        event(TERRY, "30064-18", "30064-17", ops=[take(RESULT), give(ORDER3)], cond=14, hasItem=RESULT),
        talk(VIKTOR, "30684-01", cond=6), talk(VIKTOR, "30684-05", cond=7), talk(VIKTOR, "30684-12", cond=8),
        talk(VIKTOR, "30684-13", cond=9), talk(VIKTOR, "30684-14", cond=10), talk(VIKTOR, "30684-16", cond=11),
        *[talk(VIKTOR, "30684-17", cond=c) for c in range(12, 18)],
        event(VIKTOR, "30684-05", ops=[take(T_LETTER), give(V_LETTER), cond(7)], cond=6),
        # Terry and Viktor already swapped their letters at cond 7; only the box and Hawkeye's letter are left to hand in.
        event(VIKTOR, "30684-11", ops=[take(BOX), take(H_LETTER), give(V_REQUEST), cond(9)], cond=8),
        event(VIKTOR, "30684-15", ops=[take(V_REQUEST), take(SCALES, 10), give(A_REQUEST), give(SHILEN), cond(11)],
              cond=10),
        talk(MARINA, "30715-01", cond=11), talk(MARINA, "30715-03", cond=12), talk(MARINA, "30715-04", cond=13),
        talk(MARINA, "30715-06", hasItem=RESULT),
        event(MARINA, "30715-02", ops=[take(SHILEN), take(A_REQUEST), give(M_LETTER), cond(12)], cond=11),
        event(MARINA, "30715-05", ops=[take(TOOLS), give(RESULT), cond(14)], cond=13),
        talk(BRUNON, "30526-01", cond=12, ops=[take(M_LETTER), give(TOOLS), cond(13)]),
        talk(BRUNON, "30526-02", cond=13),
        kill(20198, [give(MYST), cond(3)], cond=2, chance=0.1),
        *collect_all({20080: ANT, 20211: OL_MAHUM, 20249: TURAK, 20495: TUREK}, 1, 4, 5, chance=0.25),
        kill(20158, [drop(SCALES, 0.3, 10, 10)], cond=9),
        *[row for c in (15, 16) for row in collect_all({20234: AB1, 20270: AB2, 20088: AB3, 20580: AB4}, 1, c, 17,
                                                        chance=0.25)],
    ]
    write(213, "00213_trial_of_the_seeker", "Q00213_TrialOfTheSeeker", "Trial of the Seeker",
          [D_LETTER, ORDER1, ORDER2, T_LETTER, V_LETTER, H_LETTER, MYST, OL_MAHUM, TUREK, ANT, TURAK, BOX, V_REQUEST,
           SCALES, SHILEN, A_REQUEST, M_LETTER, TOOLS, RESULT, ORDER3, HOSTS, AB1, AB2, AB3, AB4, REPORT], d)


@quest
def q224():
    BERNARD, HAMIL, ARON, VOKIAN, GAUEN = 30702, 30626, 30653, 30514, 30717
    KADESH = 27090
    (INTRO, H_L1, H_L2, H_L3, RUNE1, RUNE2, T_KADESH, T_SNAKE, CLIP, CHITIN, STRING, HORN, BLOOD) = range(3294, 3307)
    BOW, ARROW, MARK = 3028, 17, 3293
    ok = [7, 22, 35]
    lizards = range(20577, 20583)
    d = [
        talk(BERNARD, "30702-03", when="absent", classes=ok, minLevel=39),
        talk(BERNARD, "30702-01", when="absent", classes=ok),
        talk(BERNARD, "30702-02", when="absent"),
        event(BERNARD, "30702-04", ops=START + [give(INTRO)], when="absent", classes=ok, minLevel=39),
        talk(BERNARD, "30702-05"),
        talk(HAMIL, "30626-01", cond=1), talk(HAMIL, "30626-04", cond=2), talk(HAMIL, "30626-04", cond=3),
        talk(HAMIL, "30626-05", cond=4),
        *[talk(HAMIL, "30626-08", cond=c) for c in (5, 6, 7)],
        talk(HAMIL, "30626-09", cond=8, ops=[take(RUNE2, 10), give(H_L3), cond(9)]),
        *[talk(HAMIL, "30626-10", cond=c) for c in (9, 10, 11)],
        talk(HAMIL, "30626-11", cond=12, ops=[cond(13)]),
        talk(HAMIL, "30626-12", cond=13),
        talk(HAMIL, "30626-13", cond=14, ops=[take_all(BLOOD), take(BOW), take(T_KADESH)] + mark(MARK, 54726, 20250)),
        event(HAMIL, "30626-03", ops=[take(INTRO), give(H_L1), cond(2)], cond=1),
        event(HAMIL, "30626-07", ops=[take(RUNE1, 10), give(H_L2), cond(5)], cond=4),
        talk(ARON, "30653-01", cond=2),
        *[talk(ARON, "30653-03", cond=c) for c in range(3, 15)],
        event(ARON, "30653-02", ops=[take(H_L1), cond(3)], cond=2),
        talk(VOKIAN, "30514-01", cond=5), talk(VOKIAN, "30514-03", cond=6),
        talk(VOKIAN, "30514-04", cond=7, ops=[take(T_SNAKE), cond(8)]),
        *[talk(VOKIAN, "30514-05", cond=c) for c in range(8, 15)],
        event(VOKIAN, "30514-02", ops=[take(H_L2), cond(6)], cond=5),
        talk(GAUEN, "30717-01", cond=9, ops=[take(H_L3), cond(10)]),
        talk(GAUEN, "30717-03", cond=10),
        talk(GAUEN, "30717-02", cond=11, ops=[take(HORN), take(CLIP), take(STRING), take(CHITIN), give(BOW),
                                              give(ARROW, 10), cond(12)]),
        *[talk(GAUEN, "30717-04", cond=c) for c in (12, 13, 14)],
        *[kill(m, [drop(RUNE1, 0.5, 10, 4)], cond=3) for m in (20079, 20080, 20081, 20082, 20084, 20086, 20089, 20090)],
    ]
    for m in (20269, 20270):
        d += [kill(m, [give(RUNE2), give(T_SNAKE), cond(7)], cond=6, atLeast={RUNE2: 9}, chance=0.5),
              kill(m, [drop(RUNE2, 0.5, 10)], cond=6)]
    d += collect_all({20230: CHITIN, 20232: CHITIN, 20234: CHITIN, 20233: STRING, 20551: CLIP, 20563: HORN}, 1, 10, 11,
                     chance=0.1)
    # Serpent Demon Kadesh answers the lizardman blood: past 120 vials each kill calls him with (count - 120) * 5 %.
    for m in lizards:
        d.append(kill(m, [take_all(BLOOD), spawn(KADESH)], cond=13, atLeast={BLOOD: 140}))
        for k in range(139, 120, -1):
            d.append(kill(m, [take_all(BLOOD), spawn(KADESH)], cond=13, atLeast={BLOOD: k}, below={BLOOD: k + 1},
                          chance=round((k - 120) * 0.05, 2)))
        d.append(kill(m, [give(BLOOD)], cond=13))
    d += [
        # Kadesh only yields his talisman to the Crescent Moon Bow; otherwise he rises again.
        kill(KADESH, [give(T_KADESH), cond(14)], cond=13, weapons=[BOW]),
        kill(KADESH, [spawn(KADESH)], cond=13),
    ]
    write(224, "00224_test_of_sagittarius", "Q00224_TestOfSagittarius", "Test of Sagittarius",
          [INTRO, H_L1, H_L2, H_L3, RUNE1, RUNE2, T_KADESH, T_SNAKE, CLIP, CHITIN, STRING, HORN, BLOOD, BOW], d)


@quest
def q225():
    ALEX, TYRA, TREE, CHEST, LUTHER, LEIRYNN, BORYS, JAX = 30291, 30420, 30627, 30628, 30690, 30728, 30729, 30730
    (L_LETTER, WARRANT, ORDER1, TOTEM, ORDER2, FANG, REPORT, STRANGE, LAMBERT, A_LETTER, A_ORDER, CATALOG, CONTRACT,
     SPORE, WINE, OLD_ORDER, DIARY, PIECE1, PIECE2, SOLT, MAKEL, COMBINED, KEY, GOLD, RECOMMEND) = range(2784, 2809)
    MARK = 2809
    ok = [7, 22, 35, 54]
    chest = spawn_at(CHEST, 10098, 157287, -2406)
    d = [
        talk(LUTHER, "30690-04", when="absent", classes=[54], minLevel=39),
        talk(LUTHER, "30690-03", when="absent", classes=ok, minLevel=39),
        talk(LUTHER, "30690-02", when="absent", classes=ok),
        talk(LUTHER, "30690-01", when="absent"),
        event(LUTHER, "30690-05", ops=START + [give(L_LETTER)], when="absent", classes=ok, minLevel=39),
        talk(LUTHER, "30690-06", cond=1),
        *[talk(LUTHER, "30690-07", cond=c) for c in range(2, 19)],
        talk(LUTHER, "30690-08", cond=19, ops=[take(RECOMMEND)] + mark(MARK, 37831, 18750)),
        talk(ALEX, "30291-01", cond=1, ops=[take(L_LETTER), give(WARRANT), cond(2)]),
        talk(ALEX, "30291-02", cond=2),
        *[talk(ALEX, "30291-03", cond=c) for c in range(3, 7)],
        talk(ALEX, "30291-04", cond=7),
        *[talk(ALEX, "30291-08", cond=c) for c in range(8, 13)],
        *[talk(ALEX, "30291-09", cond=c) for c in range(13, 16)],
        talk(ALEX, "30291-10", cond=16), talk(ALEX, "30291-10", cond=17),
        talk(ALEX, "30291-11", cond=18, ops=[take(A_ORDER), take(COMBINED), take_all(GOLD), give(RECOMMEND), cond(19)]),
        talk(ALEX, "30291-12", cond=19),
        event(ALEX, "30291-07", ops=[take(REPORT), take(STRANGE), give(A_LETTER), give(A_ORDER), give(LAMBERT),
                                     cond(8)], cond=7),
        talk(LEIRYNN, "30728-01", cond=2, ops=[take(WARRANT), give(ORDER1), cond(3)]),
        talk(LEIRYNN, "30728-02", cond=3),
        talk(LEIRYNN, "30728-03", cond=4, ops=[take_all(TOTEM), take(ORDER1), give(ORDER2), cond(5)]),
        talk(LEIRYNN, "30728-04", cond=5),
        talk(LEIRYNN, "30728-05", cond=6, ops=[take(FANG), take(ORDER2), give(REPORT), cond(7)]),
        talk(LEIRYNN, "30728-06", cond=7),
        *[talk(LEIRYNN, "30728-07", cond=c) for c in range(8, 20)],
        talk(BORYS, "30729-01", cond=8, ops=[take(A_LETTER), give(CATALOG), cond(9)]),
        *[talk(BORYS, "30729-02", cond=c) for c in (9, 10, 11)],
        talk(BORYS, "30729-03", cond=12, ops=[take(WINE), give(OLD_ORDER), cond(13)]),
        talk(BORYS, "30729-04", cond=13),
        *[talk(BORYS, "30729-05", cond=c) for c in range(14, 20)],
        talk(TYRA, "30420-01", cond=9), talk(TYRA, "30420-02", cond=10),
        talk(TYRA, "30420-03", cond=11, ops=[take_all(SPORE), take(CONTRACT), give(WINE), cond(12)]),
        *[talk(TYRA, "30420-04", cond=c) for c in range(12, 20)],
        event(TYRA, "30420-01a", ops=[take(CATALOG), give(CONTRACT), cond(10)], cond=9),
        talk(JAX, "30730-01", cond=13), talk(JAX, "30730-02", cond=14),
        talk(JAX, "30730-03", cond=15, ops=[take(LAMBERT), take(MAKEL), take(DIARY), take(SOLT), give(COMBINED),
                                           cond(16)]),
        *[talk(JAX, "30730-04", cond=c) for c in range(16, 20)],
        event(JAX, "30730-01d", ops=[take(OLD_ORDER), give(DIARY), cond(14)], cond=13),
        talk(TREE, "30627-01", cond=16), talk(TREE, "30627-01", cond=17),
        # The hanged tree raises the strong wooden chest at its Mobius point (again, if it went away unopened).
        event(TREE, "30627-01a", ops=[give(KEY), cond(17), chest], cond=16),
        event(TREE, "30627-01a", ops=[chest], cond=17),
        talk(CHEST, "30628-01", cond=17),
        event(CHEST, "30628-01a", ops=[take_all(KEY), give(GOLD, 20), cond(18)], cond=17, hasItem=KEY),
        event(CHEST, "30628-01a", "30628-02", cond=17),
        kill(20781, [drop(TOTEM, 1.0, 10, 4)], cond=3),
        kill(27093, [give(FANG), give(STRANGE), cond(6)], cond=5),
        kill(20555, [drop(SPORE, 1.0, 10, 11)], cond=10),
    ]
    # Torn map pieces: four of a kind make Solt's / Makel's map; with both maps the trial moves on.
    for mob, piece, own, other in ((20551, PIECE1, SOLT, MAKEL), (20144, PIECE2, MAKEL, SOLT)):
        d += [
            kill(mob, [take_all(piece), give(own), cond(15)], cond=14, lacksItems=[own], hasItem=other,
                 atLeast={piece: 3}, chance=0.5),
            kill(mob, [take_all(piece), give(own)], cond=14, lacksItems=[own, other], atLeast={piece: 3}, chance=0.5),
            kill(mob, [drop(piece, 0.5)], cond=14, lacksItems=[own], below={piece: 3}),
        ]
    write(225, "00225_test_of_the_searcher", "Q00225_TestOfTheSearcher", "Test of the Searcher",
          [L_LETTER, WARRANT, ORDER1, TOTEM, ORDER2, FANG, REPORT, STRANGE, LAMBERT, A_LETTER, A_ORDER, CATALOG,
           CONTRACT, SPORE, WINE, OLD_ORDER, DIARY, PIECE1, PIECE2, SOLT, MAKEL, COMBINED, KEY, GOLD, RECOMMEND], d)


@quest
def q219():
    KAIRA, METHEUS, IXIA, ALDER, ROA, NORMAN, THIFIELL, ARKENIA, PIXY, TREANT = (
        30476, 30614, 30463, 30613, 30114, 30210, 30358, 30419, 31845, 31850)
    (K_LETTER, JAR, REMAINS, TEXTBOOK, I_LIST, ICHOR, FLUIDS, DUNG, BLOOD, ROOT, BELLADONNA, SKULL1, SKULL2, RECEIPT,
     MANUSCRIPT, K_INSTR, K_RECOMMEND, CHARM, T_LETTER, A_NOTE, GARNET, G_SKULL, K_SKULL, B_SKULL, L_SKULL, DUST, SEED,
     LEAF, SAP, A_LETTER) = range(3173, 3203)
    MARK = 3172
    ok = dict(races=[DARK_ELF], classes=FIRST_CLASSES, minLevel=37)
    d = [
        talk(KAIRA, "30476-03", when="absent", **ok),
        talk(KAIRA, "30476-01", when="absent", races=[DARK_ELF]),
        talk(KAIRA, "30476-02", when="absent"),
        event(KAIRA, "30476-05", ops=START + [give(K_LETTER)], when="absent", **ok),
        talk(KAIRA, "30476-06", cond=1), talk(KAIRA, "30476-07", cond=2), talk(KAIRA, "30476-07", cond=3),
        *[talk(KAIRA, "30476-08", cond=c) for c in range(4, 9)],
        # Kaira calls up the Alder spirit next to the player.
        talk(KAIRA, "30476-09", cond=9, ops=[take(SKULL1), spawn(ALDER), cond(10)]),
        *[talk(KAIRA, "30476-10", cond=c) for c in (10, 11, 12)],
        talk(KAIRA, "30476-11", cond=13),
        talk(KAIRA, "30476-12", cond=14, minLevel=38, ops=[take(K_INSTR), take(MANUSCRIPT), give(K_RECOMMEND), cond(15)]),
        talk(KAIRA, "30476-14", cond=14),
        talk(KAIRA, "30476-16", cond=15),
        *[talk(KAIRA, "30476-17", cond=c) for c in (16, 17, 18)],
        event(KAIRA, "30476-12", ops=[take(MANUSCRIPT), give(K_RECOMMEND), cond(15)], cond=13, minLevel=38),
        event(KAIRA, "30476-12", "30476-13", ops=[give(K_INSTR), cond(14)], cond=13),
        talk(METHEUS, "30614-01", cond=1, ops=[take(K_LETTER), give(JAR), cond(2)]),
        talk(METHEUS, "30614-02", cond=2),
        talk(METHEUS, "30614-03", cond=3, ops=[take(REMAINS), give(TEXTBOOK), cond(5)]),
        *[talk(METHEUS, "30614-04", cond=c) for c in range(4, 8)],
        talk(METHEUS, "30614-05", cond=8, ops=[take(BELLADONNA), give(SKULL1), cond(9)]),
        *[talk(METHEUS, "30614-06", cond=c) for c in range(9, 19)],
        talk(IXIA, "30463-01", cond=5, ops=[take(TEXTBOOK), give(I_LIST), cond(6)]),
        talk(IXIA, "30463-02", cond=6),
        talk(IXIA, "30463-03", cond=7, ops=[take(I_LIST), take_all(DUNG, FLUIDS, ICHOR, ROOT, BLOOD), give(BELLADONNA),
                                           cond(8)]),
        talk(IXIA, "30463-04", cond=8),
        *[talk(IXIA, "30463-05", cond=c) for c in range(9, 19)],
        talk(ALDER, "30613-01", cond=10, ops=[give(SKULL2), cond(11)]),
        talk(ROA, "30114-01", cond=11), talk(ROA, "30114-05", cond=12),
        *[talk(ROA, "30114-06", cond=c) for c in range(13, 19)],
        event(ROA, "30114-04", ops=[take(SKULL2), give(RECEIPT), cond(12)], cond=11),
        talk(NORMAN, "30210-01", cond=12, ops=[take(RECEIPT), give(MANUSCRIPT), cond(13)]),
        *[talk(NORMAN, "30210-02", cond=c) for c in range(13, 19)],
        talk(THIFIELL, "30358-01", cond=15, ops=[take(K_RECOMMEND), give(CHARM), give(T_LETTER), cond(16)]),
        talk(THIFIELL, "30358-02", cond=16), talk(THIFIELL, "30358-03", cond=17),
        talk(THIFIELL, "30358-04", cond=18, ops=[take(CHARM), take(A_LETTER)] + mark(MARK, 68183, 1750)),
        talk(ARKENIA, "30419-01", cond=16),
        talk(ARKENIA, "30419-04", cond=17, hasItems=[SAP, DUST]),
        talk(ARKENIA, "30419-03", cond=17),
        talk(ARKENIA, "30419-06", cond=18),
        event(ARKENIA, "30419-02", ops=[take(T_LETTER), give(A_NOTE), cond(17)], cond=16),
        event(ARKENIA, "30419-05", ops=[take(A_NOTE), take(SAP), take(DUST), give(A_LETTER), cond(18)], cond=17,
              hasItems=[SAP, DUST]),
        talk(PIXY, "31845-04", cond=17, hasItem=GARNET, atLeast={G_SKULL: 10, K_SKULL: 10, B_SKULL: 10, L_SKULL: 10},
             ops=[take_all(B_SKULL, G_SKULL, K_SKULL, L_SKULL), take(GARNET), give(DUST)]),
        talk(PIXY, "31845-03", cond=17, hasItem=GARNET),
        talk(PIXY, "31845-05", cond=17, hasItem=DUST),
        talk(PIXY, "31845-01", cond=17),
        talk(PIXY, "31845-05", cond=18),
        event(PIXY, "31845-02", ops=[give(GARNET)], cond=17, lacksItems=[GARNET, DUST]),
        talk(TREANT, "31850-04", cond=17, hasItems=[SEED, LEAF], ops=[take(LEAF), take(SEED), give(SAP)]),
        talk(TREANT, "31850-03", cond=17, hasItem=SEED),
        talk(TREANT, "31850-05", cond=17, hasItem=SAP),
        talk(TREANT, "31850-01", cond=17),
        talk(TREANT, "31850-05", cond=18),
        event(TREANT, "31850-02", ops=[give(SEED)], cond=17, lacksItems=[SEED, SAP]),
        kill(20144, [take(JAR), give(REMAINS), cond(3)], cond=2),
        *collect_all({20202: (DUNG, 0.5), 20192: (BLOOD, 0.5), 20193: (BLOOD, 0.6), 20158: (ICHOR, 0.5),
                      20157: (ROOT, 0.4), 20230: (ROOT, 0.3), 20232: (ROOT, 0.5), 20234: (ROOT, 0.6),
                      20233: (FLUIDS, 0.5)}, 10, 6, 7),
        *[kill(m, [drop(item, 1.0, 10)], hasItem=GARNET)
          for m, item in ((20554, G_SKULL), (20582, L_SKULL), (20270, B_SKULL), (20600, K_SKULL))],
        kill(27079, [drop(LEAF, 1.0, 1)], hasItem=SEED),
    ]
    write(219, "00219_testimony_of_fate", "Q00219_TestimonyOfFate", "Testimony of Fate", list(range(3173, 3203)), d)


@quest
def q215():
    SANTIAGO, TANAPI, MARTANKUS, GAURI, DORF, GERALD, PRIMOS, PETRON, ANDELLIA, URUHA, CASIAN = (
        30648, 30571, 30649, 30550, 30651, 30650, 30117, 30036, 30362, 30652, 30612)
    (SAGE, VOUCHER, SPIRIT, ESSENCE, B_GERALD, BADGE, PICTURE, HAIR, STATUE, DARKNESS, DEBRIS, TAG) = range(2722, 2734)
    MARK = 2721
    ok = [15, 29, 42, 50]
    d = [
        talk(SANTIAGO, "30648-03", when="absent", classes=ok, minLevel=35),
        talk(SANTIAGO, "30648-01", when="absent", classes=ok),
        talk(SANTIAGO, "30648-02", when="absent"),
        event(SANTIAGO, "30648-04", ops=START + [give(VOUCHER)], when="absent", classes=ok, minLevel=35),
        talk(SANTIAGO, "30648-10", cond=17, ops=[take(SAGE)] + mark(MARK, 77382, 16000)),
        talk(SANTIAGO, "30648-09"),
        talk(TANAPI, "30571-01", cond=1, ops=[take(VOUCHER), cond(2)]),
        *[talk(TANAPI, "30571-02", cond=c) for c in (2, 3, 4)],
        talk(TANAPI, "30571-03", cond=5, ops=[cond(6)]),
        *[talk(TANAPI, "30571-03", cond=c) for c in range(6, 18)],
        talk(MARTANKUS, "30649-01", cond=2, ops=[cond(3)]),
        talk(MARTANKUS, "30649-02", cond=3), talk(MARTANKUS, "30649-03", cond=4),
        event(MARTANKUS, "30649-04", ops=[take(ESSENCE), give(SPIRIT), cond(5)], cond=4),
        talk(GAURI, "30550-01", cond=6, ops=[give(TAG), cond(7)]),
        *[talk(GAURI, "30550-02", cond=c) for c in range(7, 18)],
        talk(DORF, "30651-02", cond=7, hasItem=B_GERALD, ops=[take(TAG), give(BADGE), cond(8)]),
        talk(DORF, "30651-01", cond=7, ops=[take(TAG), give(BADGE), cond(8)]),
        *[talk(DORF, "30651-03", cond=c) for c in range(8, 18)],
        talk(GERALD, "30650-01", cond=7, lacksItems=[B_GERALD]),
        talk(GERALD, "30650-04", cond=8, hasItem=B_GERALD, ops=[take(B_GERALD), adena(100000)]),
        event(GERALD, "30650-02", ops=[take(57, 100000), give(B_GERALD)], cond=7, atLeast={57: 100000},
              lacksItems=[B_GERALD]),
        event(GERALD, "30650-02", "30650-03", cond=7),
        talk(PRIMOS, "30117-01", cond=8, ops=[cond(9)]),
        *[talk(PRIMOS, "30117-02", cond=c) for c in range(9, 18)],
        talk(PETRON, "30036-01", cond=9, ops=[give(PICTURE), cond(10)]),
        talk(PETRON, "30036-02", cond=10),
        talk(PETRON, "30036-03", cond=11, ops=[take(HAIR), take(PICTURE), give(STATUE), cond(12)]),
        *[talk(PETRON, "30036-04", cond=c) for c in range(12, 18)],
        talk(ANDELLIA, "30362-01", cond=12, minLevel=36, ops=[cond(13)]),
        talk(ANDELLIA, "30362-01a", cond=12),
        talk(ANDELLIA, "30362-02a", cond=13, chance=0.5),
        talk(ANDELLIA, "30362-02", cond=13),
        talk(ANDELLIA, "30362-07", cond=14), talk(ANDELLIA, "30362-03", cond=15), talk(ANDELLIA, "30362-06", cond=16),
        event(ANDELLIA, "30362-04", ops=[cond(16)], cond=15),
        event(ANDELLIA, "30362-05", ops=[take(DARKNESS), cond(16)], cond=15),
        talk(URUHA, "30652-01", cond=14), talk(URUHA, "30652-03", cond=15),
        event(URUHA, "30652-02", ops=[take(DEBRIS), give(DARKNESS), cond(15)], cond=14),
        # The Book of Darkness is only handed in if Andellia was not given it.
        talk(CASIAN, "30612-01", cond=16, ops=[take_all(DARKNESS), take(BADGE), take(SPIRIT), take(STATUE), give(SAGE),
                                              cond(17)]),
        talk(CASIAN, "30612-02", cond=17),
        kill(27116, [give(ESSENCE), cond(4)], cond=3, chance=0.2),
        kill(27117, [give(HAIR), cond(11)], cond=10, chance=0.2),
        kill(27118, [give(DEBRIS), cond(14)], cond=13, chance=0.2),
    ]
    write(215, "00215_trial_of_the_pilgrim", "Q00215_TrialOfThePilgrim", "Trial of the Pilgrim", list(range(2722, 2734)), d)


@quest
def q227():
    PUPINA, SLA, RAMUS, KATARI, KAKAN, NYAKURI, PILGRIM = 30118, 30666, 30667, 30668, 30669, 30670, 30732
    REVENANT, ARURAUNE, INSPECTOR, BETRAYER, WEREWOLF, KRUDEL = 27099, 27128, 27129, 27130, 27131, 27132
    (BOOK, INTRO, S_LETTER, GREETINGS, MONEY, K_LETTER, N_LETTER, U_LIST, R_LETTER, DIARY, NAIL, B_LETTER, BONE4, BONE5,
     BONE6, BONE7, BONE8) = range(2822, 2839)
    KAKAN_LETTER, MARK = 3037, 2821
    ok = [15, 42]
    pilgrim = spawn_at(PILGRIM, -4015, 40141, -3664)
    inspector = spawn_at(INSPECTOR, -4034, 40201, -3665)
    d = [
        talk(PUPINA, "30118-03", when="absent", classes=ok, minLevel=39),
        talk(PUPINA, "30118-01", when="absent", classes=ok),
        talk(PUPINA, "30118-02", when="absent"),
        event(PUPINA, "30118-04", ops=START + [give(BOOK)], when="absent", classes=ok, minLevel=39),
        talk(PUPINA, "30118-04a", cond=1), talk(PUPINA, "30118-04a", cond=2), talk(PUPINA, "30118-05", cond=3),
        talk(PUPINA, "30118-07"),
        event(PUPINA, "30118-06", ops=[take(BOOK), take(NAIL), give(INTRO), cond(4)], cond=3),
        talk(SLA, "30666-01", cond=4),
        *[talk(SLA, "30666-05", cond=c) for c in range(5, 10)],
        talk(SLA, "30666-06", cond=10, ops=[take(MONEY), give(GREETINGS, 3), cond(11)]),
        *[talk(SLA, "30666-06", cond=c) for c in range(11, 20)],
        talk(SLA, "30666-07", cond=20, ops=[take(K_LETTER), take(KAKAN_LETTER), take(N_LETTER), take(R_LETTER)]
             + mark(MARK, 164032, 17500)),
        event(SLA, "30666-04", ops=[take(INTRO), give(S_LETTER), cond(5)], cond=4),
        # Katari sends the Ol Mahum pilgrim with its inspector, later the betrayer, to their Mobius points.
        talk(KATARI, "30668-01", cond=5, ops=[take(S_LETTER), cond(6), pilgrim, inspector]),
        talk(KATARI, "30668-01", cond=6, ops=[inspector]),
        talk(KATARI, "30668-01", cond=7, ops=[pilgrim]),
        talk(KATARI, "30668-02", cond=8, ops=[spawn_at(BETRAYER, -4106, 40174, -3660)]),
        talk(KATARI, "30668-03", cond=9, ops=[take(B_LETTER), give(K_LETTER), cond(10)]),
        *[talk(KATARI, "30668-04", cond=c) for c in range(10, 21)],
        talk(PILGRIM, "30732-01", cond=7, ops=[give(MONEY), cond(8)]),
        talk(KAKAN, "30669-01", cond=11), talk(KAKAN, "30669-01", cond=12),
        talk(KAKAN, "30669-04", cond=13, ops=[take(GREETINGS), give(KAKAN_LETTER), cond(14)]),
        *[talk(KAKAN, "30669-04", cond=c) for c in range(14, 21)],
        *[event(KAKAN, "30669-03", ops=[cond(12), spawn_at(WEREWOLF, -9382, -89852, -2333)], cond=c) for c in (11, 12)],
        talk(NYAKURI, "30670-01", cond=14), talk(NYAKURI, "30670-01", cond=15),
        talk(NYAKURI, "30670-04", cond=16, ops=[take(GREETINGS), give(N_LETTER), cond(17)]),
        *[talk(NYAKURI, "30670-04", cond=c) for c in range(17, 21)],
        *[event(NYAKURI, "30670-03", ops=[cond(15), spawn_at(KRUDEL, 126019, -179983, -1781)], cond=c) for c in (14, 15)],
        talk(RAMUS, "30667-01", cond=17, ops=[take(GREETINGS), give(U_LIST), cond(18)]),
        talk(RAMUS, "30667-02", cond=18),
        talk(RAMUS, "30667-03", cond=19, ops=[take(BONE4), take(BONE5), take(BONE6), take(BONE7), take(BONE8),
                                             take(U_LIST), give(R_LETTER), cond(20)]),
        talk(RAMUS, "30667-03", cond=20),
        # Ripped diary pages (Mobius also wants Disrupt Undead as the finishing skill; any kill counts here).
        *[kill(REVENANT, [take_all(DIARY), spawn(ARURAUNE), cond(2)], cond=c, atLeast={DIARY: 6}) for c in (1, 2)],
        *[kill(REVENANT, [give(DIARY)], cond=c) for c in (1, 2)],
        kill(ARURAUNE, [give(NAIL), cond(3)], cond=2),
        kill(INSPECTOR, [cond(7)], cond=6),
        kill(BETRAYER, [give(B_LETTER), cond(9)], cond=8),
        kill(WEREWOLF, [cond(13)], cond=12),
        kill(KRUDEL, [cond(16)], cond=15),
        *collect_all({20404: BONE4, 20104: BONE5, 20102: BONE6, 20022: BONE7, 20100: BONE8}, 1, 18, 19),
    ]
    write(227, "00227_test_of_the_reformer", "Q00227_TestOfTheReformer", "Test of the Reformer",
          [BOOK, INTRO, S_LETTER, GREETINGS, MONEY, K_LETTER, N_LETTER, U_LIST, R_LETTER, DIARY, NAIL, B_LETTER, BONE4,
           BONE5, BONE6, BONE7, BONE8, KAKAN_LETTER], d)


@quest
def q228():
    PARINA, SNAKE, SALAMANDER, SYLPH, UNDINE, CASIAN, RUKAL = 30391, 30409, 30411, 30412, 30413, 30612, 30629
    (R_LETTER, P_LETTER, LILAC, SEED1, SEED2, SEED3, SCORE, DROP, CRYSTAL, FEATHER, WINGBONE, MANE, SHELL, POWDER,
     SCRAP, T_WATER, T_FIRE, T_WIND, T_EARTH, C_SALAMANDER, C_SYLPH, C_UNDINE, C_SERPENT) = range(2841, 2864)
    MARK = 2840
    ok = [11, 26, 39]
    tones = [T_WATER, T_FIRE, T_WIND, T_EARTH]
    d = [
        talk(RUKAL, "30629-03", when="absent", classes=ok, minLevel=39),
        talk(RUKAL, "30629-02", when="absent", classes=ok),
        talk(RUKAL, "30629-01", when="absent"),
        event(RUKAL, "30629-04", ops=START + [give(R_LETTER)], when="absent", classes=ok, minLevel=39),
        talk(RUKAL, "30629-05", cond=1), talk(RUKAL, "30629-06", cond=2), talk(RUKAL, "30629-07", cond=3),
        talk(RUKAL, "30629-08", cond=4), talk(RUKAL, "30629-11", cond=5),
        talk(RUKAL, "30629-12", cond=6, ops=[take(SCORE)] + [take(t) for t in tones] + mark(MARK, 139039, 40000)),
        event(RUKAL, "30629-10", ops=[take(SEED1), take(SEED2), take(SEED3), take(LILAC), give(SCORE), cond(5)], cond=4),
        talk(PARINA, "30391-01", cond=1), talk(PARINA, "30391-03", cond=2),
        talk(PARINA, "30391-04", cond=3), talk(PARINA, "30391-04", cond=4),
        talk(PARINA, "30391-05", cond=5), talk(PARINA, "30391-05", cond=6),
        event(PARINA, "30391-02", ops=[take(R_LETTER), give(P_LETTER), cond(2)], cond=1),
        talk(CASIAN, "30612-01", cond=2), talk(CASIAN, "30612-03", cond=3), talk(CASIAN, "30612-04", cond=4),
        talk(CASIAN, "30612-05", cond=5), talk(CASIAN, "30612-05", cond=6),
        event(CASIAN, "30612-02", ops=[take(P_LETTER), give(LILAC), cond(3)], cond=2),
    ]
    # The four elemental spirits: charm -> materials -> tone; the last tone moves the test to cond 6.
    spirits = [
        (UNDINE, "30413", C_UNDINE, T_WATER, {DROP: 20}, True, "04"),
        (SALAMANDER, "30411", C_SALAMANDER, T_FIRE, {CRYSTAL: 5}, True, "04"),
        (SYLPH, "30412", C_SYLPH, T_WIND, {FEATHER: 20, WINGBONE: 10, MANE: 10}, False, "05"),
        (SNAKE, "30409", C_SERPENT, T_EARTH, {SHELL: 10, POWDER: 10, SCRAP: 10}, False, "06"),
    ]
    for npc, pre, charm, tone, need, charm_on_talk, done in spirits:
        others = [t for t in tones if t != tone]
        hand_in = [take(i, n) for i, n in need.items()] + [take(charm), give(tone)]
        ready, waiting, first = {
            SNAKE: ("05", "04", "01"), SYLPH: ("04", "03", "01")}.get(npc, ("03", "02", "01"))
        d += [
            talk(npc, f"{pre}-{ready}", cond=5, hasItems=[charm] + others, atLeast=need, ops=hand_in + [cond(6)]),
            talk(npc, f"{pre}-{ready}", cond=5, hasItem=charm, atLeast=need, ops=hand_in),
            talk(npc, f"{pre}-{waiting}", cond=5, hasItem=charm),
            talk(npc, f"{pre}-{first}", cond=5, lacksItems=[tone], ops=[give(charm)] if charm_on_talk else []),
            talk(npc, f"{pre}-{done}", cond=5),
            talk(npc, f"{pre}-{done}", cond=6),
        ]
    d += [
        event(SYLPH, "30412-02", ops=[give(C_SYLPH)], cond=5, lacksItems=[C_SYLPH, T_WIND]),
        event(SNAKE, "30409-03", ops=[give(C_SERPENT)], cond=5, lacksItems=[C_SERPENT, T_EARTH]),
        *collect_all({27095: SEED1, 27096: SEED2, 27097: SEED3}, 1, 3, 4),
        kill(27098, [drop(CRYSTAL, 0.5, 5)], cond=5, hasItem=C_SALAMANDER),
        *[kill(m, [drop(DROP, p, 20)], cond=5, hasItem=C_UNDINE)
          for m, p in ((20231, 0.3), (20157, 0.3), (20230, 0.3), (20232, 0.4), (20234, 0.5))],
        kill(20145, [drop(FEATHER, 1.0, 20)], cond=5, hasItem=C_SYLPH),
        kill(20176, [drop(WINGBONE, 0.5, 10)], cond=5, hasItem=C_SYLPH),
        kill(20553, [drop(MANE, 0.5, 10)], cond=5, hasItem=C_SYLPH),
        *[kill(m, [drop(item, 1.0, 10)], cond=5, hasItem=C_SERPENT)
          for m, item in ((20564, SHELL), (20565, POWDER), (20566, SCRAP))],
    ]
    write(228, "00228_test_of_magus", "Q00228_TestOfMagus", "Test of Magus", list(range(2841, 2864)), d)


@quest
def q229():
    LARA, ALEXANDRIA, IKER, VADIN, NESTLE, VASPER, LEOPOLD, KAIRA, ORIM, RODERIK, ENDRIGO, EVERT = (
        30063, 30098, 30110, 30188, 30314, 30417, 30435, 30476, 30630, 30631, 30632, 30633)
    REVENANT, MERCENARY, ZERUEL = 27099, 27100, 27101
    (DIAGRAM, A_BOOK, I_LIST, FANG, CHARM, HEART, L_MEMO, N_MEMO, JOURNAL, GEM1, GEM2, GEM3, GEM4, GEM5, GEM6, BRIM1,
     INSTR, O_LETTER1, O_LETTER2, V_LETTER, CRUCIFIX, AMULET, SANCTIONS, I_AMULET, SOULTRAP, KEY, BIND, BRIM2) = range(
        3308, 3336)
    SWORD, MARK = 3029, 3307
    ok = [4, 11, 32]
    GEMS = "gem456"

    def gem(n):  # Mobius st.getInt("gem456") == n
        return dict(varAtLeast={GEMS: n}, varBelow={GEMS: n + 1})

    d = [
        talk(ORIM, "30630-03", when="absent", classes=[11], minLevel=39),
        talk(ORIM, "30630-05", when="absent", classes=ok, minLevel=39),
        talk(ORIM, "30630-02", when="absent", classes=ok),
        talk(ORIM, "30630-01", when="absent"),
        event(ORIM, "30630-08", ops=START + [give(DIAGRAM)], when="absent", classes=ok, minLevel=39),
        talk(ORIM, "30630-09", cond=1), talk(ORIM, "30630-10", cond=2), talk(ORIM, "30630-11", cond=3),
        talk(ORIM, "30630-14", cond=4), talk(ORIM, "30630-15", cond=5), talk(ORIM, "30630-17", cond=6),
        talk(ORIM, "30630-18", cond=7, ops=[cond(8)]), talk(ORIM, "30630-18", cond=8), talk(ORIM, "30630-18", cond=9),
        talk(ORIM, "30630-19", cond=10),
        # Drevanul Prince Zeruel shows himself at his Mobius point when the six gems are handed in.
        event(ORIM, "30630-14", ops=[unset(GEMS)] + [take(g) for g in (GEM1, GEM2, GEM3, GEM4, GEM5, GEM6)]
              + [take(A_BOOK), give(BRIM1), cond(4), spawn_at(ZERUEL, 70381, 109638, -3726)], cond=3),
        event(ORIM, "30630-16", ops=[take(BRIM1), give(INSTR), give(O_LETTER1), give(O_LETTER2), cond(6)], cond=5),
        event(ORIM, "30630-22", ops=[take(I_AMULET), take(INSTR), take(KEY), take(SWORD), take(BIND)]
              + mark(MARK, 139796, 40000), cond=10),
        talk(ALEXANDRIA, "30098-01", cond=1), talk(ALEXANDRIA, "30098-04", cond=2), talk(ALEXANDRIA, "30098-05"),
        event(ALEXANDRIA, "30098-03", ops=[take(DIAGRAM), give(A_BOOK), setv(GEMS, 1), cond(2)], cond=1),
        talk(KAIRA, "30476-03", hasItem=GEM2),
        talk(KAIRA, "30476-01", cond=2),
        *[talk(KAIRA, "30476-04", cond=c) for c in range(4, 11)],
        event(KAIRA, "30476-02", ops=[give(GEM2), cond(3)], cond=2, lacksItems=[GEM2], hasItems=[GEM1, GEM3],
              varAtLeast={GEMS: 6}),
        event(KAIRA, "30476-02", ops=[give(GEM2)], cond=2, lacksItems=[GEM2]),
        talk(IKER, "30110-06", hasItem=GEM1),
        talk(IKER, "30110-05", hasItems=[I_LIST, GEM2, GEM3], atLeast={FANG: 20, CHARM: 20, HEART: 20},
             varAtLeast={GEMS: 6}, ops=[take(I_LIST), take_all(FANG, HEART, CHARM), give(GEM1), cond(3)]),
        talk(IKER, "30110-05", hasItem=I_LIST, atLeast={FANG: 20, CHARM: 20, HEART: 20},
             ops=[take(I_LIST), take_all(FANG, HEART, CHARM), give(GEM1)]),
        talk(IKER, "30110-04", hasItem=I_LIST),
        talk(IKER, "30110-01", cond=2),
        talk(IKER, "30110-07", cond=6, lacksItems=[SOULTRAP]),
        *[talk(IKER, "30110-09", cond=c) for c in range(6, 10)],
        talk(IKER, "30110-10", cond=10),
        event(IKER, "30110-03", ops=[give(I_LIST)], cond=2, lacksItems=[I_LIST, GEM1]),
        event(IKER, "30110-08", ops=[take(O_LETTER2), give(I_AMULET), give(SOULTRAP), cond(7)], cond=6, hasItem=SWORD,
              lacksItems=[SOULTRAP]),
        event(IKER, "30110-08", ops=[take(O_LETTER2), give(I_AMULET), give(SOULTRAP)], cond=6, lacksItems=[SOULTRAP]),
        talk(LARA, "30063-04", hasItem=GEM3), talk(LARA, "30063-03", hasItem=L_MEMO), talk(LARA, "30063-01", cond=2),
        *[talk(LARA, "30063-05", cond=c) for c in range(3, 11)],
        event(LARA, "30063-02", ops=[give(L_MEMO)], cond=2, lacksItems=[L_MEMO, GEM3]),
        talk(RODERIK, "30631-01", hasAny=[L_MEMO, GEM3]), talk(ENDRIGO, "30632-01", hasAny=[L_MEMO, GEM3]),
        talk(NESTLE, "30314-01", **gem(1)), talk(NESTLE, "30314-03", **gem(2)),
        talk(NESTLE, "30314-04", varAtLeast={GEMS: 3}),
        event(NESTLE, "30314-02", ops=[setv(GEMS, 2), give(N_MEMO)], **gem(1)),
        talk(LEOPOLD, "30435-01", **gem(2)),
        talk(LEOPOLD, "30435-03", varAtLeast={GEMS: 3}, varBelow={GEMS: 6}),
        talk(LEOPOLD, "30435-04", **gem(6)),
        *[talk(LEOPOLD, "30435-05", cond=c) for c in range(4, 11)],
        event(LEOPOLD, "30435-02", ops=[setv(GEMS, 3), take(N_MEMO), give(JOURNAL)], **gem(2)),
        talk(VASPER, "30417-04", hasAny=[V_LETTER, CRUCIFIX]),
        talk(VASPER, "30417-05", hasItems=[SANCTIONS, SOULTRAP], ops=[take(SANCTIONS), give(SWORD), cond(7)]),
        talk(VASPER, "30417-05", hasItem=SANCTIONS, ops=[take(SANCTIONS), give(SWORD)]),
        talk(VASPER, "30417-01", cond=6),
        *[talk(VASPER, "30417-06", cond=c) for c in range(7, 11)],
        event(VASPER, "30417-03", ops=[take(O_LETTER1), give(V_LETTER)], cond=6, hasItem=O_LETTER1),
        talk(VADIN, "30188-01", hasItem=V_LETTER, ops=[take(V_LETTER), give(CRUCIFIX)]),
        talk(VADIN, "30188-03", hasItem=CRUCIFIX, atLeast={AMULET: 20},
             ops=[take_all(AMULET, CRUCIFIX), give(SANCTIONS)]),
        talk(VADIN, "30188-02", hasItem=CRUCIFIX),
        talk(VADIN, "30188-04", hasItem=SANCTIONS),
        *[talk(VADIN, "30188-05", cond=c) for c in range(7, 11)],
        talk(EVERT, "30633-01", cond=7), talk(EVERT, "30633-01", cond=8),
        talk(EVERT, "30633-02", cond=9, ops=[spawn_at(ZERUEL, 13395, 169807, -3708)]),
        talk(EVERT, "30633-03", cond=10),
        *[event(EVERT, "30633-02", ops=[give(BRIM2), cond(9), spawn_at(ZERUEL, 13395, 169807, -3708)], cond=c)
          for c in (7, 8)],
        kill(20557, [drop(FANG, 1.0, 20)], hasItem=I_LIST),
        kill(20565, [drop(HEART, 1.0, 20)], hasItem=I_LIST),
        *[kill(m, [drop(CHARM, p, 20)], hasItem=I_LIST)
          for m, p in ((20577, 0.5), (20578, 0.5), (20579, 0.6), (20580, 0.6), (20581, 0.7), (20582, 0.7))],
        kill(REVENANT, [take(L_MEMO), give(GEM3), cond(3)], hasItems=[L_MEMO, GEM1, GEM2], varAtLeast={GEMS: 6}),
        kill(REVENANT, [take(L_MEMO), give(GEM3)], hasItem=L_MEMO),
        kill(MERCENARY, [setv(GEMS, 4), give(GEM4)], **gem(3)),
        kill(MERCENARY, [setv(GEMS, 5), give(GEM5)], **gem(4)),
        kill(MERCENARY, [setv(GEMS, 6), take(JOURNAL), give(GEM6), cond(3)], hasItems=[GEM1, GEM2, GEM3], **gem(5)),
        kill(MERCENARY, [setv(GEMS, 6), take(JOURNAL), give(GEM6)], **gem(5)),
        *[kill(m, [drop(AMULET, 0.5, 20)], hasItem=CRUCIFIX) for m in (20601, 20602)],
        # Zeruel flees from the first blow at cond 4 (Mobius onAttack); here felling him counts the same.
        kill(ZERUEL, [cond(5)], cond=4),
        # At cond 9 only the Sword of Binding in hand binds him.
        kill(ZERUEL, [take(BRIM2), take(SOULTRAP), give(KEY), give(BIND), cond(10)], cond=9, weapons=[SWORD]),
    ]
    write(229, "00229_test_of_witchcraft", "Q00229_TestOfWitchcraft", "Test of Witchcraft",
          list(range(3308, 3336)) + [SWORD], d)


@quest
def q214():
    SYLVAIN, LUCAS, VALKON, DIETER, JUREK, EDROC, RAUT, POITAN, MIRIEN, MARIA, CRETA, CRONOS, TRIFF, CASIAN = (
        30070, 30071, 30103, 30111, 30115, 30230, 30316, 30458, 30461, 30608, 30609, 30610, 30611, 30612)
    (SIGIL1, SIGIL2, SIGIL3, M_INSTR, MA_LETTER1, MA_LETTER2, LU_LETTER, HANDBAG, CR_LETTER1, PAINT1, PAINT2, PAINT3,
     SCRAP, PURITY1, HP_SIGIL, GM_SIGIL, CR_SIGIL, SY_LETTER, SY_SYMBOL, J_LIST, EYE_SKIN, NECKLACE, SCALP, J_SYMBOL,
     CRO_LETTER, D_KEY, CR_LETTER2, D_LETTER, D_DIARY, ENVELOPE, RING, CH1, CH2, CH3, CH4, V_REQUEST, NOTES) = range(
        2675, 2712)
    LIQUOR, PURITY2, C_LIST, G_SKIN, M_BLOOD, ICHOR, NAIL, CRO_SYMBOL = range(2713, 2721)
    MARK = 2674
    ok = [11, 26, 39]
    d = [
        talk(MIRIEN, "30461-03", when="absent", classes=ok, minLevel=35),
        talk(MIRIEN, "30461-02", when="absent", classes=ok),
        talk(MIRIEN, "30461-01", when="absent"),
        event(MIRIEN, "30461-04", ops=START + [give(SIGIL1)], when="absent", classes=ok, minLevel=35),
        *[talk(MIRIEN, "30461-05", cond=c) for c in range(1, 14)],
        talk(MIRIEN, "30461-06", cond=14, ops=[take(SIGIL1), take(SY_SYMBOL), give(SIGIL2), cond(15)]),
        *[talk(MIRIEN, "30461-07", cond=c) for c in (15, 16, 17)],
        talk(MIRIEN, "30461-08", cond=18, lacksItems=[M_INSTR]),
        talk(MIRIEN, "30461-12", cond=18, minLevel=36,
             ops=[take(M_INSTR), take(SIGIL2), take(J_SYMBOL), give(SIGIL3), cond(19)]),
        talk(MIRIEN, "30461-11", cond=18),
        *[talk(MIRIEN, "30461-13", cond=c) for c in range(19, 31)],
        talk(MIRIEN, "30461-14", cond=31, ops=[take(SIGIL3), take(CRO_SYMBOL)] + mark(MARK, 80265, 30000)),
        event(MIRIEN, "30461-09", "30461-10", ops=[take(SIGIL2), take(J_SYMBOL), give(SIGIL3), cond(19)], cond=18,
              lacksItems=[M_INSTR], minLevel=36),
        event(MIRIEN, "30461-09", ops=[give(M_INSTR)], cond=18, lacksItems=[M_INSTR]),
        talk(SYLVAIN, "30070-01", cond=1),
        *[talk(SYLVAIN, "30070-03", cond=c) for c in range(2, 13)],
        talk(SYLVAIN, "30070-04", cond=13, ops=[take(PURITY1), take(HP_SIGIL), give(SY_SYMBOL), cond(14)]),
        talk(SYLVAIN, "30070-05", cond=14),
        *[talk(SYLVAIN, "30070-06", cond=c) for c in range(15, 32)],
        event(SYLVAIN, "30070-02", ops=[give(HP_SIGIL), give(SY_LETTER), cond(2)], cond=1),
        talk(MARIA, "30608-01", cond=2), talk(MARIA, "30608-03", cond=3),
        talk(MARIA, "30608-04", cond=4, ops=[take(LU_LETTER), give(MA_LETTER2), cond(5)]),
        talk(MARIA, "30608-05", cond=5), talk(MARIA, "30608-06", cond=6), talk(MARIA, "30608-09", cond=7),
        talk(MARIA, "30608-10", cond=8, ops=[take(PAINT1), give(PAINT2), cond(9)]),
        talk(MARIA, "30608-11", cond=9),
        talk(MARIA, "30608-12", cond=10, ops=[cond(11)]), talk(MARIA, "30608-12", cond=11),
        talk(MARIA, "30608-13", cond=12), talk(MARIA, "30608-15", cond=13),
        talk(MARIA, "30608-16", hasAny=[SY_SYMBOL, SIGIL2]),
        *[talk(MARIA, "30608-18", cond=c, hasItem=V_REQUEST, ops=[take(V_REQUEST), give(PURITY2)]) for c in range(19, 32)],
        *[talk(MARIA, "30608-17", cond=c) for c in range(19, 32)],
        event(MARIA, "30608-02", ops=[take(SY_LETTER), give(MA_LETTER1), cond(3)], cond=2),
        event(MARIA, "30608-08", ops=[take(CR_LETTER1), give(HANDBAG), cond(7)], cond=6),
        event(MARIA, "30608-14", ops=[take_all(SCRAP), take(PAINT3), give(PURITY1), cond(13)], cond=12),
        talk(LUCAS, "30071-01", cond=3, ops=[take(MA_LETTER1), give(LU_LETTER), cond(4)]),
        *[talk(LUCAS, "30071-02", cond=c) for c in range(4, 9)],
        talk(LUCAS, "30071-03", cond=9),
        talk(LUCAS, "30071-05", cond=10), talk(LUCAS, "30071-05", cond=11), talk(LUCAS, "30071-06", cond=12),
        *[talk(LUCAS, "30071-07", cond=c) for c in range(13, 32)],
        event(LUCAS, "30071-04", ops=[take(PAINT2), give(PAINT3), cond(10)], cond=9),
        talk(CRETA, "30609-01", cond=5), talk(CRETA, "30609-06", cond=6), talk(CRETA, "30609-07", cond=7),
        *[talk(CRETA, "30609-10", cond=c) for c in range(8, 13)],
        *[talk(CRETA, "30609-11", cond=c) for c in range(13, 19)],
        talk(CRETA, "30609-12", cond=21),
        *[talk(CRETA, "30609-15", cond=c) for c in range(22, 32)],
        event(CRETA, "30609-05", ops=[take(MA_LETTER2), give(CR_LETTER1), cond(6)], cond=5),
        event(CRETA, "30609-09", ops=[take(HANDBAG), give(PAINT1), cond(8)], cond=7),
        event(CRETA, "30609-14", ops=[take(D_KEY), give(CR_LETTER2), cond(22)], cond=21),
        talk(JUREK, "30115-01", cond=15), talk(JUREK, "30115-04", cond=16),
        talk(JUREK, "30115-05", cond=17, ops=[take(GM_SIGIL), take(J_LIST), take_all(EYE_SKIN, SCALP, NECKLACE),
                                             give(J_SYMBOL), cond(18)]),
        talk(JUREK, "30115-06", cond=18),
        *[talk(JUREK, "30115-07", cond=c) for c in range(19, 32)],
        event(JUREK, "30115-03", ops=[give(GM_SIGIL), give(J_LIST), cond(16)], cond=15),
        talk(CRONOS, "30610-01", cond=19),
        *[talk(CRONOS, "30610-11", cond=c) for c in range(20, 30)],
        talk(CRONOS, "30610-12", cond=30), talk(CRONOS, "30610-15", cond=31),
        event(CRONOS, "30610-10", ops=[give(CRO_LETTER), give(CR_SIGIL), cond(20)], cond=19),
        event(CRONOS, "30610-14", ops=[take(CR_SIGIL), take(D_DIARY), take(CH1), take(CH2), take(CH3), take(CH4),
                                       take(RING), give(CRO_SYMBOL), cond(31)], cond=30),
        talk(DIETER, "30111-01", cond=20), talk(DIETER, "30111-06", cond=21), talk(DIETER, "30111-07", cond=22),
        talk(DIETER, "30111-10", cond=23), talk(DIETER, "30111-11", cond=24),
        *[talk(DIETER, "30111-13", cond=c, hasItems=[CH1, CH2, CH3, CH4]) for c in range(25, 31)],
        *[talk(DIETER, "30111-12", cond=c) for c in range(25, 31)],
        talk(DIETER, "30111-15", cond=31),
        event(DIETER, "30111-05", ops=[take(CRO_LETTER), give(D_KEY), cond(21)], cond=20),
        event(DIETER, "30111-09", ops=[take(CR_LETTER2), give(D_DIARY), give(D_LETTER), cond(23)], cond=22),
        talk(EDROC, "30230-01", cond=23), talk(EDROC, "30230-03", cond=24),
        *[talk(EDROC, "30230-04", cond=c) for c in range(25, 32)],
        event(EDROC, "30230-02", ops=[take(D_LETTER), give(ENVELOPE), cond(24)], cond=23),
        talk(RAUT, "30316-01", cond=24), talk(RAUT, "30316-04", cond=25),
        *[talk(RAUT, "30316-05", cond=c) for c in range(26, 32)],
        event(RAUT, "30316-02", ops=[take(ENVELOPE), give(CH1), give(LIQUOR), cond(25)], cond=24),
        talk(TRIFF, "30611-01", cond=25),
        *[talk(TRIFF, "30611-05", cond=c) for c in range(26, 32)],
        event(TRIFF, "30611-04", ops=[take(LIQUOR), give(RING), cond(26)], cond=25),
        talk(VALKON, "30103-07", hasItems=[RING, CH2]),
        talk(VALKON, "30103-05", hasItems=[RING, V_REQUEST]),
        talk(VALKON, "30103-06", hasItems=[RING, PURITY2], ops=[take(PURITY2), give(CH2)]),
        talk(VALKON, "30103-01", hasItem=RING),
        event(VALKON, "30103-04", ops=[give(V_REQUEST)], hasItem=RING, lacksItems=[V_REQUEST, PURITY2, CH2]),
        talk(POITAN, "30458-01", cond=26, lacksItems=[NOTES], ops=[give(NOTES)]),
        talk(POITAN, "30458-01", cond=27, lacksItems=[NOTES], ops=[give(NOTES)]),
        talk(POITAN, "30458-02", cond=26), talk(POITAN, "30458-02", cond=27),
        talk(POITAN, "30458-03", cond=28), talk(POITAN, "30458-03", cond=29), talk(POITAN, "30458-04", cond=30),
        *[talk(CASIAN, "30612-02", cond=c, hasItems=[NOTES, CH1, CH2, CH3]) for c in (26, 27)],
        talk(CASIAN, "30612-01", cond=26, hasItem=NOTES, ops=[cond(27)]),
        talk(CASIAN, "30612-01", cond=27, hasItem=NOTES),
        talk(CASIAN, "30612-05", cond=28), talk(CASIAN, "30612-06", cond=29), talk(CASIAN, "30612-08", cond=30),
        *[event(CASIAN, "30612-04", ops=[give(C_LIST), cond(28)], cond=c, hasItems=[NOTES, CH1, CH2, CH3])
          for c in (26, 27)],
        event(CASIAN, "30612-07", ops=[take(C_LIST), take_all(NAIL, ICHOR, G_SKIN, M_BLOOD), take(NOTES), give(CH4),
                                       cond(30)], cond=29),
        kill(20580, [drop(SCRAP, 0.5, 5, 12)], cond=11),
        *collect_all({20235: SCALP, 20279: SCALP, 20068: EYE_SKIN, 20269: NECKLACE}, 5, 16, 17, chance=0.5,
                     limits={SCALP: 2}),
        kill(20554, [drop(CH3, 0.3, 1)], hasItem=RING, lacksItems=[CH3]),
        *collect_all({20201: G_SKIN, 20158: M_BLOOD, 20552: ICHOR, 20567: NAIL}, 5, 28, 29,
                     limits={G_SKIN: 10, M_BLOOD: 12}),
    ]
    write(214, "00214_trial_of_the_scholar", "Q00214_TrialOfTheScholar", "Trial of the Scholar",
          list(range(2675, 2712)) + list(range(2713, 2721)), d)


@quest
def q230():
    LARA, GALATEA = 30063, 30634
    (AMULET, SPORES, K_TOTEM, SHARDS, B_TOTEM, BLOODSTONE, TALONS, WINGS, TUSK, FANGS, LIST1, LIST2, LIST3, LIST4, LIST5,
     G_LETTER, BEGINNER) = range(3337, 3354)
    MARK = 3336
    ok = [11, 26, 39]
    lists = [(LIST1, SPORES, AMULET), (LIST2, K_TOTEM, SHARDS), (LIST3, BLOODSTONE, B_TOTEM), (LIST4, TUSK, TALONS),
             (LIST5, WINGS, FANGS)]
    # (spirit npc, quest var, duel mob, arcana, crystal block base): progress, in-progress, foul, defeat, victory.
    spirits = [(30635, "Almors", 27102, 3354, 3360), (30636, "Camoniell", 27103, 3355, 3365),
               (30637, "Belthus", 27104, 3356, 3370), (30638, "Basilla", 27105, 3357, 3375),
               (30639, "Celestiel", 27106, 3358, 3380), (30640, "Brynthea", 27107, 3359, 3385)]
    arcanas = [a for _, _, _, a, _ in spirits]

    def is_(var, n):
        return dict(varAtLeast={var: n}, varBelow={var: n + 1})

    def lara_pick(ops_head):
        """Mobius getRandom(5): five rows rolled in turn with 1/5, 1/4, 1/3, 1/2, 1 give each list the same odds."""
        rows = []
        for i, (lst, _, _) in enumerate(lists):
            chance = 1.0 / (5 - i)
            ops = ops_head + [give(lst), setv("Lara", i + 1)]
            rows.append((ops, None if i == 4 else round(chance, 4)))
        return rows

    d = [
        talk(GALATEA, "30634-03", when="absent", classes=ok, minLevel=39),
        talk(GALATEA, "30634-02", when="absent", classes=ok),
        talk(GALATEA, "30634-01", when="absent"),
        event(GALATEA, "30634-08", ops=START + [setv(v, 1) for _, v, _, _, _ in spirits] + [give(G_LETTER)],
              when="absent", classes=ok, minLevel=39),
        talk(GALATEA, "30634-09", cond=1),
        *[talk(GALATEA, "30634-11", cond=c, hasItem=BEGINNER) for c in (2, 3)],
        *[talk(GALATEA, "30634-10", cond=c) for c in (2, 3)],
        talk(GALATEA, "30634-12", cond=4, ops=[take_all(BEGINNER, *arcanas, LIST1, LIST2, LIST3, LIST4, LIST5)]
             + mark(MARK, 148409, 30000)),
        talk(LARA, "30063-01", cond=1),
        talk(LARA, "30063-03", varBelow={"Lara": 1}),
    ]
    for ops, chance in lara_pick([take(G_LETTER), cond(2)]):
        d.append(event(LARA, "30063-02", ops=ops, cond=1, **({"chance": chance} if chance else {})))
    for ops, chance in lara_pick([]):
        d.append(event(LARA, "30063-04", ops=ops, varBelow={"Lara": 1}, cond=3, **({"chance": chance} if chance else {})))
    for i, (lst, a, b) in enumerate(lists, start=1):
        d += [
            talk(LARA, "30063-06", **is_("Lara", i), atLeast={a: 30, b: 30},
                 ops=[cond(3), unset("Lara"), take(lst), take_all(a, b), give(BEGINNER, 2)]),
            talk(LARA, "30063-05", **is_("Lara", i)),
        ]
    for npc, var, mob, arcana, base in spirits:
        progress, victory = base, base + 4
        others = [a for a in arcanas if a != arcana]
        d += [
            talk(npc, f"{npc}-01", **is_(var, 1)),
            talk(npc, f"{npc}-08", **is_(var, 2)),
            talk(npc, f"{npc}-09", **is_(var, 3)),
            talk(npc, f"{npc}-05", **is_(var, 4)),
            talk(npc, f"{npc}-06", **is_(var, 5)),
            talk(npc, f"{npc}-07", **is_(var, 6), hasItems=others,
                 ops=[setv(var, 7), take_all(victory), give(arcana), cond(4)]),
            talk(npc, f"{npc}-07", **is_(var, 6), ops=[setv(var, 7), take_all(victory), give(arcana)]),
            talk(npc, f"{npc}-10", **is_(var, 7)),
            event(npc, f"{npc}-02", f"{npc}-03", hasItem=BEGINNER),
            event(npc, f"{npc}-02"),
            # Accept the duel (also after a lost or fouled one): one beginner arcana for a crystal of progress.
            *[event(npc, f"{npc}-04", ops=[setv(var, 2), take_all(base + 2, base + 3), take(BEGINNER), give(progress)],
                    hasItem=BEGINNER, **is_(var, n)) for n in (1, 4, 5)],
            # Mobius wants the summon alone to fight the duel (onAttack fouls); here the kill itself counts.
            *[kill(mob, [setv(var, 6), take_all(progress, base + 1), give(victory)], **is_(var, n)) for n in (2, 3)],
        ]
    d += [
        kill(20555, [drop(SPORES, 0.8, 30)], **is_("Lara", 1)),
        *[kill(m, [drop(AMULET, p, 30)], **is_("Lara", 1))
          for m, p in ((20577, 0.25), (20578, 0.25), (20579, 0.5), (20580, 0.5), (20581, 0.75), (20582, 0.75))],
        kill(20563, [drop(SHARDS, 0.8, 30)], **is_("Lara", 2)),
        kill(20600, [drop(K_TOTEM, 0.8, 30)], **is_("Lara", 2)),
        *[kill(m, [drop(B_TOTEM, p, 30)], **is_("Lara", 3))
          for m, p in ((20267, 0.25), (20268, 0.25), (20271, 0.25), (20269, 0.5), (20270, 0.5))],
        kill(20552, [drop(BLOODSTONE, 0.6, 30)], **is_("Lara", 3)),
        kill(20553, [drop(TUSK, 0.7, 30)], **is_("Lara", 4)),
        *[kill(m, [drop(TALONS, 0.5, 30)], **is_("Lara", 4)) for m in (20192, 20193)],
        *[kill(m, [drop(WINGS, 0.6, 30)], **is_("Lara", 5)) for m in (20089, 20090)],
        kill(20176, [drop(FANGS, 0.5, 30)], **is_("Lara", 5)),
    ]
    write(230, "00230_test_of_the_summoner", "Q00230_TestOfTheSummoner", "Test of the Summoner",
          list(range(3337, 3390)), d)


@quest
def q220():
    KASMAN, VOKIAN, MANAKIA, KAKAI, TANAPI, VOLTAR, KEPRA, BURAI, HARAK, DRIKO, CHIANTA = (
        30501, 30514, 30515, 30565, 30571, 30615, 30616, 30617, 30618, 30619, 30642)
    PASHIKA, VULTUS, ENKU, MAKUM, REVENANT = 27080, 27081, 27082, 27083, 27086
    (V_ORDER1, SHARD, TALON, FANG, V_ORDER2, NECKLACE, C_ORDER1, S_BREKA, S_ENKU, S_VUKU, S_TUREK, S_TUNATH, C_ORDER2,
     C_ORDER3, SKULL, HEAD, BOX, H_PASHIKA, H_VULTUS, G_VOLTAR, H_ENKU, G_KEPRA, H_MAKUM, G_BURAI, M_LETTER1, M_LETTER2,
     K_LETTER1, K_LETTER2, K_LETTER3, CONTRACT, HUSK, T_ORDER, S_TANTOS, RITUAL) = range(3204, 3238)
    MARK = 3203
    scepters = [S_BREKA, S_ENKU, S_VUKU, S_TUREK, S_TUNATH]
    mid = list(range(4, 12))
    sons = [spawn_at(PASHIKA, 80117, 120039, -2259), spawn_at(VULTUS, 80058, 120038, -2259)]
    overlords = [spawn_at(ENKU, x, y, z) for x, y, z in ((19894, 189743, -3074), (19869, 189800, -3059),
                                                        (19818, 189818, -3047), (19753, 189837, -3027))]
    thugs = [spawn_at(MAKUM, -94292, 110781, -3701), spawn_at(MAKUM, -94293, 110861, -3701)]

    def scepter(npc, page, ops, sc, **gates):
        """Hand over a chief's scepter; the fifth one moves the trial on to cond 5."""
        others = [x for x in scepters if x != sc]
        merged = dict(gates)
        held = merged.pop("hasItems", []) + others
        return [talk(npc, page, ops=ops + [give(sc), cond(5)], hasItems=held, **merged),
                talk(npc, page, ops=ops + [give(sc)], **gates)]

    ok = dict(races=[ORC], classes=FIRST_CLASSES, minLevel=37)
    d = [
        talk(VOKIAN, "30514-03", when="absent", **ok),
        talk(VOKIAN, "30514-01", when="absent", races=[0, 1, 2, 4]),
        talk(VOKIAN, "30514-02", when="absent", races=[ORC], maxLevel=36),
        talk(VOKIAN, "30514-01a", when="absent"),
        event(VOKIAN, "30514-05", ops=START + [give(V_ORDER1)], when="absent", **ok),
        talk(VOKIAN, "30514-06", cond=1),
        talk(VOKIAN, "30514-08", cond=2, ops=[take(FANG, 10), take(SHARD, 10), take(TALON, 10), take(V_ORDER1),
                                             give(NECKLACE), give(V_ORDER2), cond(3)]),
        talk(VOKIAN, "30514-09", cond=3), talk(VOKIAN, "30514-10", cond=8),
        talk(CHIANTA, "30642-01", cond=3), talk(CHIANTA, "30642-04", cond=4),
        talk(CHIANTA, "30642-09", cond=5, hasItem=C_ORDER2, minLevel=37, ops=[take(C_ORDER2), give(C_ORDER3), cond(6)]),
        talk(CHIANTA, "30642-08", cond=5, hasItem=C_ORDER2),
        talk(CHIANTA, "30642-05", cond=5),
        talk(CHIANTA, "30642-10", cond=6),
        talk(CHIANTA, "30642-11", cond=7, ops=[take(C_ORDER3), take(NECKLACE), take(SKULL, 20), take(HEAD, 20), give(BOX),
                                              cond(8)]),
        talk(CHIANTA, "30642-12", cond=8),
        *[talk(CHIANTA, "30642-13", cond=c) for c in (9, 10, 11)],
        event(CHIANTA, "30642-03", ops=[take(V_ORDER2), give(C_ORDER1), cond(4)], cond=3),
        event(CHIANTA, "30642-07", ops=[take_all(C_ORDER1, K_LETTER1, M_LETTER1, M_LETTER2, *scepters), give(C_ORDER3),
                                        cond(6)], cond=5, minLevel=37),
        event(CHIANTA, "30642-07", "30642-06", ops=[take_all(C_ORDER1, K_LETTER1, M_LETTER1, M_LETTER2, *scepters),
                                                    give(C_ORDER2)], cond=5),
        talk(KASMAN, "30501-01", hasItem=C_ORDER1),
        *[talk(KASMAN, "30501-11", cond=c) for c in range(5, 12)],
        talk(MANAKIA, "30515-01", hasItem=C_ORDER1),
        *[talk(MANAKIA, "30515-08", cond=c) for c in range(5, 12)],
    ]
    # Kasman and Manakia write the introductions to the five chiefs (a second visit only reminds).
    for npc, ev, letter, sc, extra, have_page, give_page in (
            (KASMAN, "30501-02", K_LETTER1, S_VUKU, [CONTRACT], "30501-04", "30501-03"),
            (KASMAN, "30501-05", K_LETTER2, S_TUREK, [G_BURAI, H_MAKUM], "30501-07", "30501-06"),
            (KASMAN, "30501-08", K_LETTER3, S_TUNATH, [], "30501-10", "30501-09"),
            (MANAKIA, "30515-02", M_LETTER1, S_BREKA, [G_VOLTAR, H_PASHIKA, H_VULTUS], "30515-04", "30515-03"),
            (MANAKIA, "30515-05", M_LETTER2, S_ENKU, [G_KEPRA, H_ENKU], "30515-07", "30515-06")):
        d += [
            event(npc, ev, have_page, cond=4, hasItem=letter),
            event(npc, ev, give_page, ops=[give(letter)], cond=4, lacksItems=[sc] + extra),
            event(npc, ev, cond=4),
        ]
    d += [
        talk(VOLTAR, "30615-02", hasItem=M_LETTER1),
        talk(VOLTAR, "30615-05", hasItem=G_VOLTAR, ops=sons),
        *scepter(VOLTAR, "30615-06", [take(H_PASHIKA), take(H_VULTUS)], S_BREKA, hasItems=[H_PASHIKA, H_VULTUS]),
        talk(VOLTAR, "30615-07", hasItem=S_BREKA),
        talk(VOLTAR, "30615-01", hasItem=C_ORDER1),
        *[talk(VOLTAR, "30615-08", cond=c) for c in range(4, 9)],
        event(VOLTAR, "30615-04", ops=[take(M_LETTER1), give(G_VOLTAR)] + sons, hasItem=M_LETTER1),
        talk(KEPRA, "30616-02", hasItem=M_LETTER2),
        talk(KEPRA, "30616-05", hasItem=G_KEPRA, ops=overlords),
        *scepter(KEPRA, "30616-06", [take_all(H_ENKU)], S_ENKU, atLeast={H_ENKU: 4}),
        talk(KEPRA, "30616-07", hasItem=S_ENKU),
        talk(KEPRA, "30616-01", hasItem=C_ORDER1),
        *[talk(KEPRA, "30616-08", cond=c) for c in range(4, 9)],
        event(KEPRA, "30616-05", ops=[take(M_LETTER2), give(G_KEPRA)] + overlords, hasItem=M_LETTER2),
        talk(BURAI, "30617-02", hasItem=K_LETTER2),
        talk(BURAI, "30617-04", hasItem=G_BURAI, ops=thugs),
        *scepter(BURAI, "30617-05", [take(H_MAKUM, 2)], S_TUREK, atLeast={H_MAKUM: 2}),
        talk(BURAI, "30617-06", hasItem=S_TUREK),
        talk(BURAI, "30617-01", hasItem=C_ORDER1),
        *[talk(BURAI, "30617-07", cond=c) for c in range(4, 8)],
        event(BURAI, "30617-04", ops=[take(K_LETTER2), give(G_BURAI)] + thugs, hasItem=K_LETTER2),
        talk(HARAK, "30618-02", hasItem=K_LETTER3),
        talk(HARAK, "30618-04", hasItem=S_TUNATH),
        talk(HARAK, "30618-01", hasItem=C_ORDER1),
        *[talk(HARAK, "30618-05", cond=c) for c in range(4, 9)],
        *[event(HARAK, "30618-03", ops=row["ops"], hasItem=K_LETTER3, **({"hasItems": row["hasItems"]}
                                                                         if "hasItems" in row else {}))
          for row in scepter(HARAK, "x", [take(K_LETTER3)], S_TUNATH)],
        talk(DRIKO, "30619-02", hasItem=K_LETTER1),
        *scepter(DRIKO, "30619-05", [take(CONTRACT), take(HUSK, 30)], S_VUKU, hasItems=[CONTRACT], atLeast={HUSK: 30}),
        talk(DRIKO, "30619-04", hasItem=CONTRACT),
        talk(DRIKO, "30619-06", hasItem=S_VUKU),
        talk(DRIKO, "30619-01", hasItem=C_ORDER1),
        *[talk(DRIKO, "30619-07", cond=c) for c in range(4, 8)],
        event(DRIKO, "30619-03", ops=[take(K_LETTER1), give(CONTRACT)], hasItem=K_LETTER1),
        talk(TANAPI, "30571-01", cond=8), talk(TANAPI, "30571-04", cond=9),
        talk(TANAPI, "30571-05", cond=10, ops=[take(S_TANTOS), take(T_ORDER), give(RITUAL), cond(11)]),
        talk(TANAPI, "30571-06", cond=11),
        event(TANAPI, "30571-03", ops=[take(BOX), give(T_ORDER), cond(9)], cond=8),
        *[talk(KAKAI, "30565-01", cond=c) for c in (8, 9, 10)],
        talk(KAKAI, "30565-02", cond=11, ops=[take(RITUAL)] + mark(MARK, 91457, 2500)),
        *collect_all({20192: (TALON, 0.5), 20550: (FANG, 0.5), 20563: (SHARD, 0.75)}, 10, 1, 2),
        kill(20234, [drop(HUSK, 0.75, 30)], hasItem=CONTRACT),
        kill(PASHIKA, [give(H_PASHIKA), take(G_VOLTAR)], hasItems=[G_VOLTAR, H_VULTUS], lacksItems=[H_PASHIKA]),
        kill(PASHIKA, [give(H_PASHIKA)], hasItem=G_VOLTAR, lacksItems=[H_PASHIKA]),
        kill(VULTUS, [give(H_VULTUS), take(G_VOLTAR)], hasItems=[G_VOLTAR, H_PASHIKA], lacksItems=[H_VULTUS]),
        kill(VULTUS, [give(H_VULTUS)], hasItem=G_VOLTAR, lacksItems=[H_VULTUS]),
        kill(ENKU, [give(H_ENKU), take(G_KEPRA)], hasItem=G_KEPRA, atLeast={H_ENKU: 3}),
        kill(ENKU, [give(H_ENKU)], hasItem=G_KEPRA),
        kill(MAKUM, [give(H_MAKUM), take(G_BURAI)], hasItem=G_BURAI, atLeast={H_MAKUM: 1}),
        kill(MAKUM, [give(H_MAKUM)], hasItem=G_BURAI),
        *collect_all({**{m: (HEAD, round(0.5 + (m - 20583) * 0.1, 2)) for m in range(20583, 20589)},
                      20601: (SKULL, 0.5), 20602: (SKULL, 0.6)}, 20, 6, 7),
        *[kill(m, [spawn(REVENANT)], cond=9) for m in (20778, 20779)],
        kill(REVENANT, [give(S_TANTOS), cond(10)], cond=9),
    ]
    write(220, "00220_testimony_of_glory", "Q00220_TestimonyOfGlory", "Testimony of Glory", list(range(3204, 3238)), d)


@quest
def q233():
    VIVYAN, SARIEN, RACOY, SOMAK, MANAKIA, ORIM, MARTANKUS, PEKIRON = (
        30030, 30436, 30507, 30510, 30515, 30630, 30649, 30682)
    (VENDETTA, T_HEAD, W_TOTEM, CONTRACT, EYE, SCALE, TALON, BRAKI1, P_TOTEM, TO_SKULL, TO_RIB, TO_SPINE, TO_ARM,
     TO_THIGH, TONAR1, M_TOTEM, HE_SKULL, HE_RIB, HE_SPINE, HE_ARM, HE_THIGH, HERMODT1, R_TOTEM, V_LETTER, BOOK,
     KI_SKULL, KI_RIB, KI_SPINE, KI_ARM, KI_THIGH, KIRUNA1, BRAKI2, TONAR2, HERMODT2, KIRUNA2) = range(2880, 2915)
    MARK = 2879
    remains = [BRAKI1, TONAR1, HERMODT1, KIRUNA1]

    def remains_rows(npc, page, ops, got, **gates):
        """Hand in one set of bones for its remains; the fourth set moves the trial to cond 2."""
        others = [r for r in remains if r != got]
        g = dict(gates)
        held = g.pop("hasItems", []) + others
        return [talk(npc, page, ops=ops + [give(got), cond(2)], cond=1, hasItems=held, **g),
                talk(npc, page, ops=ops + [give(got)], cond=1, **gates)]

    d = [
        talk(SOMAK, "30510-04", when="absent", classes=[50], minLevel=39),
        talk(SOMAK, "30510-03", when="absent", classes=[50]),
        talk(SOMAK, "30510-02", when="absent", races=[ORC]),
        talk(SOMAK, "30510-01", when="absent"),
        event(SOMAK, "30510-05", ops=START, when="absent", classes=[50], minLevel=39),
        talk(SOMAK, "30510-06", cond=1),
        talk(SOMAK, "30510-07", cond=2, ops=[take(r) for r in remains] + [give(VENDETTA), cond(3)]),
        talk(SOMAK, "30510-08", cond=3),
        talk(SOMAK, "30510-09", cond=4, ops=[take(VENDETTA), give(BRAKI2), give(HERMODT2), give(KIRUNA2), give(TONAR2),
                                            give(W_TOTEM), cond(5)]),
        talk(SOMAK, "30510-10", cond=5),
        talk(MARTANKUS, "30649-01", cond=5),
        event(MARTANKUS, "30649-03", ops=[take_all(T_HEAD, W_TOTEM, BRAKI2, HERMODT2, KIRUNA2, TONAR2)]
              + mark(MARK, 63483, 17500), cond=5),
        talk(ORIM, "30630-01", cond=1, lacksItems=[BRAKI1, CONTRACT]),
        *remains_rows(ORIM, "30630-06", [take(SCALE, 10), take(TALON, 10), take(EYE, 10), take(CONTRACT)], BRAKI1,
                      lacksItems=[BRAKI1], atLeast={EYE: 10, SCALE: 10, TALON: 10}),
        talk(ORIM, "30630-05", cond=1, lacksItems=[BRAKI1]),
        talk(ORIM, "30630-07"),
        event(ORIM, "30630-04", ops=[give(CONTRACT)], cond=1, lacksItems=[BRAKI1, CONTRACT]),
        talk(RACOY, "30507-01", cond=1, lacksItems=[KIRUNA1, R_TOTEM]),
        talk(RACOY, "30507-04", cond=1, lacksItems=[KIRUNA1], hasItem=V_LETTER),
        *remains_rows(RACOY, "30507-06", [take(BOOK), take(R_TOTEM), take(KI_ARM), take(KI_RIB), take(KI_SKULL),
                                          take(KI_SPINE), take(KI_THIGH)], KIRUNA1, lacksItems=[KIRUNA1],
                      hasItems=[BOOK, KI_ARM, KI_RIB, KI_SKULL, KI_SPINE, KI_THIGH]),
        talk(RACOY, "30507-05", cond=1, lacksItems=[KIRUNA1], hasItem=BOOK),
        talk(RACOY, "30507-03", cond=1, lacksItems=[KIRUNA1]),
        talk(RACOY, "30507-07"),
        event(RACOY, "30507-02", ops=[give(R_TOTEM)], cond=1, lacksItems=[KIRUNA1, R_TOTEM]),
        talk(VIVYAN, "30030-05", cond=1, hasItems=[R_TOTEM, V_LETTER]),
        talk(VIVYAN, "30030-06", cond=1, hasItems=[R_TOTEM, BOOK]),
        talk(VIVYAN, "30030-01", cond=1, hasItem=R_TOTEM),
        talk(VIVYAN, "30030-07"),
        event(VIVYAN, "30030-04", ops=[give(V_LETTER)], cond=1, hasItem=R_TOTEM, lacksItems=[V_LETTER, BOOK]),
        talk(SARIEN, "30436-01", cond=1, hasItems=[R_TOTEM, V_LETTER], ops=[take(V_LETTER), give(BOOK)]),
        talk(SARIEN, "30436-02", cond=1, hasItems=[R_TOTEM, BOOK]),
        talk(SARIEN, "30436-03", cond=1, hasItem=R_TOTEM),
        talk(PEKIRON, "30682-01", cond=1, lacksItems=[TONAR1, P_TOTEM]),
        *remains_rows(PEKIRON, "30682-04", [take(P_TOTEM), take(TO_ARM), take(TO_RIB), take(TO_SKULL), take(TO_SPINE),
                                            take(TO_THIGH)], TONAR1, lacksItems=[TONAR1],
                      hasItems=[TO_ARM, TO_RIB, TO_SKULL, TO_SPINE, TO_THIGH]),
        talk(PEKIRON, "30682-03", cond=1, lacksItems=[TONAR1]),
        talk(PEKIRON, "30682-05"),
        event(PEKIRON, "30682-02", ops=[give(P_TOTEM)], cond=1, lacksItems=[TONAR1, P_TOTEM]),
        talk(MANAKIA, "30515-01", cond=1, lacksItems=[HERMODT1, M_TOTEM]),
        *remains_rows(MANAKIA, "30515-04", [take(M_TOTEM), take(HE_ARM), take(HE_RIB), take(HE_SKULL), take(HE_SPINE),
                                            take(HE_THIGH)], HERMODT1, lacksItems=[HERMODT1],
                      hasItems=[HE_ARM, HE_RIB, HE_SKULL, HE_SPINE, HE_THIGH]),
        talk(MANAKIA, "30515-03", cond=1, lacksItems=[HERMODT1]),
        talk(MANAKIA, "30515-05"),
        event(MANAKIA, "30515-02", ops=[give(M_TOTEM)], cond=1, lacksItems=[HERMODT1, M_TOTEM]),
        kill(20213, [drop(EYE, 1.0, 10)], hasItem=CONTRACT),
        kill(20214, [drop(SCALE, 1.0, 10)], hasItem=CONTRACT),
        kill(20215, [drop(TALON, 1.0, 10)], hasItem=CONTRACT),
        kill(27108, [give(HE_SKULL)], hasItem=M_TOTEM, lacksItems=[HE_SKULL]),
        *[kill(m, [drop(T_HEAD, 0.5, 13, 4)], cond=3, hasItem=VENDETTA) for m in (20601, 20602)],
    ]
    # Sequential bone hunts: each lucky kill brings the first missing bone of the set.
    for mobs, gate, bones in (((20581, 20582), P_TOTEM, [TO_SKULL, TO_RIB, TO_SPINE, TO_ARM, TO_THIGH]),
                              ((20158,), M_TOTEM, [HE_RIB, HE_SPINE, HE_ARM, HE_THIGH])):
        for m in mobs:
            for i, bone in enumerate(bones):
                d.append(kill(m, [give(bone)], hasItems=[gate] + bones[:i], lacksItems=[bone], chance=0.5))
    # Noble ants (Mobius getRandom(100) bands): 29 % thighbone then armbone, 30 % spine then ribbone, 30 % skull.
    for m in (20089, 20090):
        d += [
            kill(m, [give(KI_THIGH)], hasItem=BOOK, lacksItems=[KI_THIGH], chance=0.29),
            kill(m, [give(KI_ARM)], hasItems=[BOOK, KI_THIGH], lacksItems=[KI_ARM], chance=0.29),
            kill(m, [give(KI_SPINE)], hasItem=BOOK, lacksItems=[KI_SPINE], chance=0.42),
            kill(m, [give(KI_RIB)], hasItems=[BOOK, KI_SPINE], lacksItems=[KI_RIB], chance=0.42),
            kill(m, [give(KI_SKULL)], hasItem=BOOK, lacksItems=[KI_SKULL], chance=0.73),
        ]
    write(233, "00233_test_of_the_war_spirit", "Q00233_TestOfTheWarSpirit", "Test of the War Spirit",
          list(range(2880, 2915)), d)


@quest
def q232():
    SOMAK, MANAKIA, JAKAL, SUMARI, KAKAI, VARKEES, TANTUS, HATOS, TAKUNA, CHIANTA, FIRST_ORC, MARTANKUS = (
        30510, 30515, 30558, 30564, 30565, 30566, 30567, 30568, 30641, 30642, 30643, 30649)
    (NECKLACE, C_VARKEES, C_TANTUS, C_HATOS, C_TAKUNA, C_CHIANTA, ORDERS, B_FANG, AMULET, HUGE_FANG, S_LETTER, URUTU,
     SKULL, SWORD, AXE_BLADE, AXE, FEELER, FEET, BROOCH, CORNEA, CARVING, BEAR_NECKLACE, M_CHARM, R_HEAD,
     R_NOTICE) = range(3391, 3416)
    FLAME, ARROW, MARK = 3416, 1341, 3390
    pieces = [SWORD, AXE, CARVING, BROOCH, HUGE_FANG]

    def piece(npc, page, ops, got, **gates):
        """Hand over one of the five ordeal pieces; with all five (and the necklace) the trial moves to cond 2."""
        g = dict(gates)
        held = g.pop("hasItems", []) + [x for x in pieces if x != got] + [NECKLACE]
        return [talk(npc, page, ops=ops + [give(got), cond(2)], hasItems=held, **g),
                talk(npc, page, ops=ops + [give(got)], **gates)]

    first_orc = spawn_at(FIRST_ORC, 21036, -107690, -3038)
    d = [
        talk(KAKAI, "30565-04", when="absent", classes=[50], minLevel=39),
        talk(KAKAI, "30565-03", when="absent", classes=[50]),
        talk(KAKAI, "30565-02", when="absent", races=[ORC]),
        talk(KAKAI, "30565-01", when="absent"),
        event(KAKAI, "30565-05", ops=START + [give(NECKLACE)], when="absent", classes=[50], minLevel=39),
        talk(KAKAI, "30565-06", cond=1), talk(KAKAI, "30565-07", cond=2), talk(KAKAI, "30565-09", cond=3),
        *[talk(KAKAI, "30565-10", cond=c) for c in (4, 5, 6)],
        talk(KAKAI, "30565-11", cond=7, ops=[take(FLAME)] + mark(MARK, 92955, 16250)),
        event(KAKAI, "30565-08", ops=[take(p) for p in (SWORD, AXE, CARVING, BROOCH, NECKLACE, HUGE_FANG)]
              + [give(BEAR_NECKLACE), cond(3)], cond=2),
        talk(MARTANKUS, "30649-01", cond=3), talk(MARTANKUS, "30649-05", cond=4),
        talk(MARTANKUS, "30649-06", cond=5, ops=[take(M_CHARM), take(R_HEAD), take(R_NOTICE), give(FLAME), cond(6)]),
        talk(MARTANKUS, "30649-07", cond=6), talk(MARTANKUS, "30649-08", cond=7),
        event(MARTANKUS, "30649-04", ops=[take(BEAR_NECKLACE), give(M_CHARM), cond(4)], cond=3),
        # Martankus calls the First Orc to his Mobius point.
        event(MARTANKUS, "30649-07", ops=[first_orc], cond=6),
        talk(FIRST_ORC, "30643-01", cond=6), talk(FIRST_ORC, "30643-03", cond=7),
        event(FIRST_ORC, "30643-02", ops=[cond(7)], cond=6),
        talk(VARKEES, "30566-05", hasItem=HUGE_FANG),
        *piece(VARKEES, "30566-04", [take_all(C_VARKEES, AMULET)], HUGE_FANG, hasItems=[C_VARKEES, AMULET]),
        talk(VARKEES, "30566-03", hasItem=C_VARKEES),
        talk(VARKEES, "30566-01"),
        event(VARKEES, "30566-02", ops=[give(C_VARKEES)], lacksItems=[C_VARKEES, HUGE_FANG]),
        talk(MANAKIA, "30515-05", hasItem=HUGE_FANG),
        talk(MANAKIA, "30515-04", hasItem=AMULET),
        talk(MANAKIA, "30515-03", hasItem=ORDERS, atLeast={B_FANG: 20}, ops=[take_all(ORDERS, B_FANG), give(AMULET)]),
        talk(MANAKIA, "30515-02", hasItem=ORDERS),
        talk(MANAKIA, "30515-01", ops=[give(ORDERS)]),
        talk(TANTUS, "30567-05", hasItem=AXE),
        *piece(TANTUS, "30567-04", [take(ARROW, 1000), take(AXE_BLADE), take(C_TANTUS)], AXE,
               hasItems=[C_TANTUS, AXE_BLADE], atLeast={ARROW: 1000}),
        talk(TANTUS, "30567-03", hasItem=C_TANTUS),
        talk(TANTUS, "30567-01"),
        event(TANTUS, "30567-02", ops=[give(C_TANTUS)], lacksItems=[C_TANTUS, AXE]),
        talk(JAKAL, "30558-05", hasItem=AXE),
        talk(JAKAL, "30558-04", hasItem=AXE_BLADE),
        talk(JAKAL, "30558-01", hasItem=C_TANTUS, atLeast={57: 1000}),
        talk(JAKAL, "30558-03", hasItem=C_TANTUS),
        event(JAKAL, "30558-02", ops=[take(57, 1000), give(AXE_BLADE)], hasItem=C_TANTUS, atLeast={57: 1000},
              lacksItems=[AXE_BLADE, AXE]),
        talk(HATOS, "30568-05", hasItem=SWORD),
        *piece(HATOS, "30568-04", [take(C_HATOS), take(URUTU), take_all(SKULL)], SWORD, hasItems=[C_HATOS, URUTU],
               atLeast={SKULL: 10}),
        talk(HATOS, "30568-03", hasItem=C_HATOS),
        talk(HATOS, "30568-01"),
        event(HATOS, "30568-02", ops=[give(C_HATOS)], lacksItems=[C_HATOS, SWORD]),
        talk(SUMARI, "30564-03", hasItem=URUTU),
        talk(SUMARI, "30564-02", hasItem=S_LETTER),
        talk(SUMARI, "30564-01", hasItem=C_HATOS, ops=[give(S_LETTER)]),
        talk(SOMAK, "30510-03", hasItem=SWORD),
        talk(SOMAK, "30510-02", hasItem=URUTU),
        talk(SOMAK, "30510-01", hasItem=S_LETTER, ops=[take(S_LETTER), give(URUTU)]),
        talk(TAKUNA, "30641-05", hasItem=BROOCH),
        *piece(TAKUNA, "30641-04", [take_all(FEELER, FEET), take(C_TAKUNA)], BROOCH, hasItems=[C_TAKUNA],
               atLeast={FEELER: 10, FEET: 10}),
        talk(TAKUNA, "30641-03", hasItem=C_TAKUNA),
        talk(TAKUNA, "30641-01"),
        event(TAKUNA, "30641-02", ops=[give(C_TAKUNA)], lacksItems=[C_TAKUNA, BROOCH]),
        talk(CHIANTA, "30642-05", hasItem=CARVING),
        *piece(CHIANTA, "30642-04", [take_all(CORNEA), take(C_CHIANTA)], CARVING, hasItems=[C_CHIANTA],
               atLeast={CORNEA: 20}),
        talk(CHIANTA, "30642-03", hasItem=C_CHIANTA),
        talk(CHIANTA, "30642-01"),
        event(CHIANTA, "30642-02", ops=[give(C_CHIANTA)], lacksItems=[C_CHIANTA, CARVING]),
        kill(20564, [drop(CORNEA, 1.0, 20)], hasItem=C_CHIANTA),
        *[kill(m, [drop(SKULL, p, 10)], hasItem=C_HATOS)
          for m, p in ((20583, 0.71), (20584, 0.71), (20585, 0.71), (20586, 0.81), (20587, 1.0), (20588, 1.0))],
        kill(20233, [drop(FEET, 1.0, 10)], hasItem=C_TAKUNA, atLeast={FEELER: 10}),
        kill(20233, [give(FEELER)], hasItem=C_TAKUNA),
        kill(20269, [drop(B_FANG, 0.41, 20)], hasItem=ORDERS),
        kill(20270, [drop(B_FANG, 0.51, 20)], hasItem=ORDERS),
        *[kill(m, [give(R_NOTICE)], hasItem=M_CHARM, lacksItems=[R_NOTICE]) for m in (20778, 20779)],
        *[kill(m, [give(R_HEAD), cond(5)], hasItem=M_CHARM, lacksItems=[R_HEAD]) for m in (20778, 20779)],
    ]
    write(232, "00232_test_of_the_lord", "Q00232_TestOfTheLord", "Test of the Lord",
          list(range(3391, 3417)), d)


@quest
def q216():
    VALKON, NORMAN, ALTRAN, PINTER, DUNING = 30103, 30210, 30283, 30298, 30688
    (R_RING, R_BEAD) = 3024, 3025
    (V_REC, BERRY, A_INSTR, A_REC1, A_REC2, N_INSTR, N_RECEIPT, D_INSTR, D_KEY, N_LIST, POWDER, WHETSTONE, PIGMENT,
     YARN, GEM, P_INSTR, BEAD, LUMP, DECO, RING) = range(3120, 3140)
    MARK = 3119
    ok = [54, 56]
    d = [
        talk(VALKON, "30103-03", when="absent", classes=ok, minLevel=35),
        talk(VALKON, "30103-02", when="absent", classes=ok),
        talk(VALKON, "30103-01", when="absent"),
        # Valkon asks 2000 adena for his recommendation.
        event(VALKON, "30103-06", ops=START + [take(57, 2000), give(V_REC)], when="absent", classes=ok, minLevel=35,
              atLeast={57: 2000}),
        event(VALKON, "30103-06", "30103-05a", when="absent"),
        talk(VALKON, "30103-06c", cond=1),
        *[talk(VALKON, "30103-07", cond=c) for c in (2, 3, 4)],
        talk(VALKON, "30103-08", cond=5),
        talk(VALKON, "30103-09", cond=6, atLeast={RING: 7}, below={RING: 8}),
        talk(VALKON, "30103-08", cond=6),
        *[event(VALKON, ev, ops=[cond(3)], cond=c) for ev in ("30103-06c", "30103-07c") for c in (1, 2)],
        *[event(VALKON, ev, ops=[take(A_INSTR), take_all(RING)] + mark(MARK, 80993, 12250), cond=6,
                atLeast={RING: 7}) for ev in ("30103-09a", "30103-09b")],
        talk(ALTRAN, "30283-01", cond=1, ops=[cond(2)]),
        talk(ALTRAN, "30283-01", cond=2), talk(ALTRAN, "30283-01", cond=3),
        talk(ALTRAN, "30283-02", cond=4),
        talk(ALTRAN, "30283-04", cond=5), talk(ALTRAN, "30283-04", cond=6),
        event(ALTRAN, "30283-03", ops=[take(BERRY), take(V_REC), give(A_INSTR), give(A_REC1), give(A_REC2),
                                       give(R_RING), cond(5)], cond=4),
        # Norman: the receipt for Duning, then 30 keys, then the 4 x 70 materials for 7 Journeyman Gems.
        talk(NORMAN, "30210-01", cond=5, hasItem=A_REC1),
        talk(NORMAN, "30210-05", cond=5, hasItem=N_RECEIPT),
        talk(NORMAN, "30210-06", cond=5, hasItem=D_INSTR),
        talk(NORMAN, "30210-07", cond=5, atLeast={D_KEY: 30}, ops=[take_all(D_KEY)]),
        *[talk(NORMAN, "30210-12", cond=5, hasItem=N_LIST,
               atLeast={POWDER: 70, WHETSTONE: 70, PIGMENT: 70, YARN: 70, **extra},
               ops=[take(N_INSTR), take(N_LIST), take_all(YARN, WHETSTONE, POWDER, PIGMENT), give(GEM, 7)] + more)
          for extra, more in (({DECO: 7}, [cond(6)]), ({}, []))],
        talk(NORMAN, "30210-11", cond=5, hasItem=N_LIST),
        event(NORMAN, "30210-04", ops=[take(A_REC1), give(N_INSTR), give(N_RECEIPT)], cond=5, hasItem=A_REC1),
        event(NORMAN, "30210-10", ops=[give(N_LIST)], cond=5, hasItem=N_INSTR, lacksItems=[N_LIST, D_INSTR, N_RECEIPT]),
        talk(DUNING, "30688-01", cond=5, hasItem=N_RECEIPT),
        talk(DUNING, "30688-03", cond=5, hasItem=D_INSTR, below={D_KEY: 30}),
        talk(DUNING, "30688-04", cond=5, hasItem=D_INSTR, ops=[take(D_INSTR)]),
        talk(DUNING, "30688-05", cond=5),
        event(DUNING, "30688-02", ops=[take(N_RECEIPT), give(D_INSTR)], cond=5, hasItem=N_RECEIPT),
        # Pinter: 70 amber beads for 7 Journeyman Deco Beads; Artisans also get the Amber Bead recipe.
        talk(PINTER, "30298-02", cond=5, hasItem=A_REC2, minLevel=36),
        talk(PINTER, "30298-01", cond=5, hasItem=A_REC2),
        talk(PINTER, "30298-06", cond=5, hasItem=P_INSTR, below={BEAD: 70}),
        *[talk(PINTER, "30298-07", cond=5, hasItem=P_INSTR, atLeast=extra or None,
               ops=[take_all(BEAD), take(P_INSTR), give(DECO, 7)] + more)
          for extra, more in (({GEM: 7}, [cond(6)]), ({}, []))],
        talk(PINTER, "30298-08", cond=6, hasItem=DECO),
        event(PINTER, "30298-04", "30298-05", ops=[take(A_REC2), give(P_INSTR), give(R_BEAD)], cond=5,
              hasItem=A_REC2, classes=[56]),
        event(PINTER, "30298-04", ops=[take(A_REC2), give(P_INSTR)], cond=5, hasItem=A_REC2),
        *[kill(m, [give(BERRY), cond(4)], cond=3) for m in (20154, 20155, 20156)],
        *[kill(m, [drop(D_KEY, 1.0, 30)], hasItem=D_INSTR) for m in (20267, 20268, 20269, 20270, 20271)],
        *[kill(m, [drop(item, 1.0, 70, count=n)], hasItem=N_LIST)
          for m, item, n in ((20201, POWDER, 5), (20200, POWDER, 5), (20083, WHETSTONE, 7), (20202, PIGMENT, 7),
                             (20168, YARN, 10))],
        # Ants: 5 Amber Beads a kill (up to 70); an Artisan also finds an Amber Lump half the time.
        *[kill(m, [give(LUMP), drop(BEAD, 1.0, 70, count=5)], classes=[56], chance=0.5) for m in (20079, 20080)],
        *[kill(m, [drop(BEAD, 1.0, 70, count=5)]) for m in (20079, 20080)],
    ]
    write(216, "00216_trial_of_the_guildsman", "Q00216_TrialOfTheGuildsman", "Trial of the Guildsman",
          [R_RING, R_BEAD] + list(range(3120, 3140)), d)


@quest
def q221():
    (WILFORD, PARMAN, LILITH, BRIGHT, SHARI, MION, LOCKIRIN, SPIRON, BALANKI, KEEF, FILAUR, ARIN, MARYSE, BOLTER,
     TOROCCO, TOMA, PIOTUR, EMILY, NIKOLA, BOX) = (30005, 30104, 30368, 30466, 30517, 30519, 30531, 30532, 30533,
                                                   30534, 30535, 30536, 30553, 30554, 30555, 30556, 30597, 30620,
                                                   30621, 30622)
    SKIN, R_KEY, KEY = 1867, 3023, 3030
    (RING1, RING2, BOOK, SEED, E_RECIPE, WAFER, FRAGMENT, LICENSE, N1, N2, N3, N4, N5, C_SHARI, C_MION, C_MARYSE,
     M_REQUEST, C_TOMA, R_BOLTER, R1, R2, R3, R4, R5, PROCURATION, B_LIST, PETAL, MOSS, BOUQUET, P_INSTR, P_LETTER,
     CLAY, PATTERN, N_LIST, SHELL, SAC, THORN) = range(3239, 3276)
    BROOCH = 3428
    MARK = 3238
    early = (1, 2)
    four = [SEED, BOOK, E_RECIPE, WAFER]

    def piece(npc, page, ops, got, cnd=1, **gates):
        """Hand over one of the four Parman pieces; the last one moves the testimony to cond 2."""
        others = [x for x in four if x != got]
        g = dict(gates)
        held = g.pop("hasItems", []) + others
        return [talk_or_event(npc, page, ops + [give(got), cond(2)], cnd, hasItems=held, **g),
                talk_or_event(npc, page, ops + [give(got)], cnd, **gates)]

    def talk_or_event(npc, page, ops, cnd, **gates):
        if page.startswith("ev:"):
            return event(npc, page[3:], ops=ops, cond=cnd, **gates)
        return talk(npc, page, ops=ops, cond=cnd, **gates)

    def collector(npc, notice, receipt, pages, contrib_rows):
        """Lockirin's five collectors: take the notice, swap the contribution(s) for a receipt."""
        p01, p02, p03, p04 = pages
        return [talk(npc, p01, cond=1, hasItems=[LICENSE, notice], ops=[take(notice)]),
                *contrib_rows,
                talk(npc, p04, cond=1, hasItems=[LICENSE, receipt]),
                talk(npc, p02, cond=1, hasItem=LICENSE)]

    d = [
        talk(PARMAN, "30104-03", when="absent", races=[DWARF], classes=FIRST_CLASSES, minLevel=37),
        talk(PARMAN, "30104-01", when="absent", races=[HUMAN, ELF, DARK_ELF, ORC]),
        talk(PARMAN, "30104-02", when="absent", maxLevel=36),
        talk(PARMAN, "30104-01a", when="absent"),
        event(PARMAN, "30104-04", ops=START + [give(RING1)], when="absent", races=[DWARF], classes=FIRST_CLASSES,
              minLevel=37),
        talk(PARMAN, "30104-05", cond=1), talk(PARMAN, "30104-06", cond=2),
        event(PARMAN, "30104-07", "30104-08", ops=[take(SEED), take(E_RECIPE), take(WAFER), take(BOOK), take(RING1),
                                                   give(P_LETTER), give(RING2), cond(4)], cond=2, minLevel=38),
        event(PARMAN, "30104-07", ops=[take(SEED), take(E_RECIPE), take(WAFER), take(BOOK), take(RING1),
                                       give(P_INSTR), cond(3)], cond=2),
        talk(PARMAN, "30104-10", cond=3, minLevel=38, ops=[take(P_INSTR), give(P_LETTER), give(RING2), cond(4)]),
        talk(PARMAN, "30104-09", cond=3),
        *[talk(PARMAN, "30104-11", cond=c) for c in (4, 5, 6)],
        talk(PARMAN, "30104-12", cond=7), talk(PARMAN, "30104-12", cond=8),
        talk(PARMAN, "30104-13", cond=9, ops=[take(FRAGMENT), take(RING2)] + mark(MARK, 12969, 1000)),
        # Lockirin: collection licence and five notices; the five receipts buy the Old Account Book.
        *[r for c in early for r in (
            piece(LOCKIRIN, "30531-05", [take(LICENSE), take(R1), take(R2), take(R3), take(R4), take(R5)], BOOK, c,
                  hasItems=[LICENSE, R1, R2, R3, R4, R5]))],
        *[talk(LOCKIRIN, "30531-04", cond=c, hasItem=LICENSE) for c in early],
        *[talk(LOCKIRIN, "30531-06", cond=c, hasItem=BOOK) for c in early],
        *[talk(LOCKIRIN, "30531-01", cond=c) for c in early],
        *[talk(LOCKIRIN, "30531-07", cond=c) for c in range(4, 10)],
        event(LOCKIRIN, "30531-02", "30531-04", hasItem=LICENSE),
        *[event(LOCKIRIN, "30531-03", ops=[give(LICENSE), give(N1), give(N2), give(N3), give(N4), give(N5)], cond=c,
                lacksItems=[LICENSE, BOOK]) for c in early],
        *collector(SPIRON, N1, R1, ("30532-01", "30532-02", "30532-03", "30532-04"),
                   [talk(SPIRON, "30532-03", cond=1, hasItems=[LICENSE, C_SHARI], ops=[take(C_SHARI), give(R1)])]),
        *collector(BALANKI, N2, R2, ("30533-01", "30533-02", "30533-03", "30533-04"),
                   [talk(BALANKI, "30533-03", cond=1, hasItems=[LICENSE, C_MARYSE, C_MION],
                         ops=[take(C_MARYSE), take(C_MION), give(R2)])]),
        *collector(KEEF, N3, R3, ("30534-01", "30534-02", "30534-03", "30534-04"),
                   [talk(KEEF, "30534-03", cond=1, hasItems=[LICENSE, PROCURATION])]),
        event(KEEF, "30534-03a", "30534-03b", ops=[take(57, 5000), take(PROCURATION), give(R3)], cond=1,
              hasItems=[LICENSE, PROCURATION], atLeast={57: 5000}),
        event(KEEF, "30534-03a"),
        *collector(FILAUR, N4, R4, ("30535-01", "30535-02", "30535-03", "30535-04"),
                   [talk(FILAUR, "30535-03", cond=1, hasItems=[LICENSE, R_BOLTER], ops=[take(R_BOLTER), give(R4)])]),
        *collector(ARIN, N5, R5, ("30536-01", "30536-02", "30536-03", "30536-04"),
                   [talk(ARIN, "30536-03", cond=1, hasItems=[LICENSE, C_TOMA], ops=[take(C_TOMA), give(R5)])]),
        talk(SHARI, "30517-02", cond=1, hasItems=[LICENSE, C_SHARI]),
        talk(SHARI, "30517-01", cond=1, hasItem=LICENSE, lacksItems=[N1, R1], ops=[give(C_SHARI)]),
        talk(MION, "30519-02", cond=1, hasItems=[LICENSE, C_MION]),
        talk(MION, "30519-01", cond=1, hasItem=LICENSE, lacksItems=[N2, R2], ops=[give(C_MION)]),
        talk(MARYSE, "30553-02", cond=1, hasItems=[LICENSE, M_REQUEST], below={SKIN: 100}),
        talk(MARYSE, "30553-03", cond=1, hasItems=[LICENSE, M_REQUEST],
             ops=[take(SKIN, 100), take(M_REQUEST), give(C_MARYSE)]),
        talk(MARYSE, "30553-04", cond=1, hasItems=[LICENSE, C_MARYSE]),
        talk(MARYSE, "30553-01", cond=1, hasItem=LICENSE, lacksItems=[N2, R2], ops=[give(M_REQUEST)]),
        talk(TOROCCO, "30555-03", cond=1, hasItems=[LICENSE, PROCURATION]),
        talk(TOROCCO, "30555-01", cond=1, hasItem=LICENSE, lacksItems=[N3, R3]),
        event(TOROCCO, "30555-02", ops=[give(PROCURATION)], cond=1, hasItem=LICENSE, lacksItems=[N3, R3, PROCURATION]),
        talk(BOLTER, "30554-02", cond=1, hasItems=[LICENSE, R_BOLTER]),
        talk(BOLTER, "30554-01", cond=1, hasItem=LICENSE, lacksItems=[N4, R4], ops=[give(R_BOLTER)]),
        talk(TOMA, "30556-02", cond=1, hasItems=[LICENSE, C_TOMA]),
        talk(TOMA, "30556-01", cond=1, hasItem=LICENSE, lacksItems=[N5, R5], ops=[give(C_TOMA)]),
        # Piotur's Blessed Seed, Wilford's brooch for Lilith's Elven Wafer, Bright's bouquet for Emily's recipe.
        *[talk(PIOTUR, "30597-03", cond=c, hasItem=SEED) for c in early],
        *[talk(PIOTUR, "30597-01", cond=c) for c in early],
        *[talk(PIOTUR, "30597-04", cond=c) for c in range(4, 10)],
        *[r for c in early for r in piece(PIOTUR, "ev:30597-02", [], SEED, c, lacksItems=[SEED])],
        *[talk(WILFORD, "30005-06", cond=c, hasItem=WAFER) for c in early],
        *[talk(WILFORD, "30005-05", cond=c, hasItem=BROOCH) for c in early],
        *[talk(WILFORD, "30005-01", cond=c) for c in early],
        *[talk(WILFORD, "30005-07", cond=c) for c in range(4, 10)],
        *[event(WILFORD, "30005-04", ops=[give(BROOCH)], cond=c, lacksItems=[BROOCH, WAFER]) for c in early],
        *[talk(LILITH, "30368-01", cond=c, hasItem=BROOCH) for c in early],
        *[talk(LILITH, "30368-04", cond=c, hasItem=WAFER) for c in early],
        *[talk(LILITH, "30368-05", cond=c) for c in range(4, 10)],
        *[r for c in early for r in piece(LILITH, "ev:30368-03", [take(BROOCH)], WAFER, c, hasItems=[BROOCH])],
        *[talk(BRIGHT, "30466-07", cond=c, hasItem=E_RECIPE) for c in early],
        *[talk(BRIGHT, "30466-06", cond=c, hasItem=BOUQUET) for c in early],
        *[talk(BRIGHT, "30466-05", cond=c, hasItem=B_LIST, atLeast={MOSS: 10, PETAL: 20},
               ops=[take(B_LIST), take(MOSS, 10), take(PETAL, 20), give(BOUQUET)]) for c in early],
        *[talk(BRIGHT, "30466-04", cond=c, hasItem=B_LIST) for c in early],
        *[talk(BRIGHT, "30466-01", cond=c) for c in early],
        *[talk(BRIGHT, "30466-08", cond=c) for c in range(4, 10)],
        *[event(BRIGHT, "30466-03", ops=[give(B_LIST)], cond=c, lacksItems=[B_LIST, BOUQUET, E_RECIPE]) for c in early],
        *[talk(EMILY, "30620-04", cond=c, hasItem=E_RECIPE) for c in early],
        *[talk(EMILY, "30620-01", cond=c, hasItem=BOUQUET) for c in early],
        *[talk(EMILY, "30620-05", cond=c) for c in range(4, 10)],
        *[r for c in early for r in piece(EMILY, "ev:30620-03", [take(BOUQUET)], E_RECIPE, c, hasItems=[BOUQUET])],
        # Nikola and the Box of Titan: the keyhole pattern, then a Key of Titan crafted from his recipe.
        talk(NIKOLA, "30621-01", cond=4, ops=[take_all(P_LETTER)]),
        talk(NIKOLA, "30621-05", cond=5),
        talk(NIKOLA, "30621-06", cond=6, ops=[take(PATTERN), give(N_LIST), give(R_KEY), cond(7)]),
        *[talk(NIKOLA, "30621-08", cond=c, hasItem=KEY) for c in (7, 8)],
        *[talk(NIKOLA, "30621-07", cond=c) for c in (7, 8)],
        talk(NIKOLA, "30621-09", cond=9),
        event(NIKOLA, "30621-04", ops=[give(CLAY), cond(5)], cond=4),
        talk(BOX, "30622-01", cond=5),
        talk(BOX, "30622-03", cond=8, hasItem=KEY),
        talk(BOX, "30622-05"),
        event(BOX, "30622-02", ops=[take(CLAY), give(PATTERN), cond(6)], cond=5),
        event(BOX, "30622-04", ops=[take(KEY), take(N_LIST), take_all(R_KEY, SHELL, THORN, SAC), give(FRAGMENT),
                                    cond(9)], cond=8, hasItem=KEY),
        *[kill(m, [drop(PETAL, p, 20)], hasItem=B_LIST)
          for m, p in ((20223, 0.3), (20154, 0.6), (20155, 0.8), (20156, 1.0))],
        kill(20228, [drop(MOSS, 1.0, 10)], hasItem=B_LIST),
        *collect_all({20157: (SHELL, 0.2), 20230: (SHELL, 0.3), 20232: (SHELL, 0.5), 20234: (SHELL, 0.6),
                      20231: (SAC, 0.2), 20233: (THORN, 0.2)}, 10, 7, 8, limits={SHELL: 20}),
    ]
    write(221, "00221_testimony_of_prosperity", "Q00221_TestimonyOfProsperity", "Testimony of Prosperity",
          list(range(3239, 3276)) + [BROOCH], d)


@quest
def q231():
    LOCKIRIN, SPIRON, BALANKI, KEEF, FILAUR, ARIN, TOMA, CROTO, DUBABAH, LORAIN = (
        30531, 30532, 30533, 30534, 30535, 30536, 30556, 30671, 30672, 30673)
    REC_B, REC_F, REC_A = 2864, 2865, 2866
    (LETTER, PAINT_K, NECKLACE, PAINT_T, DEVICE, ARCH, REPORT, ANTIDOTE, NEEDLE, WEB, BLOOD) = range(2868, 2879)
    BROKEN = 2916
    MARK = 2867
    recs = [REC_A, REC_B, REC_F]

    def v(key, n):  # Mobius st.getInt(key) == n
        return dict(varAtLeast={key: n}, varBelow={key: n + 1})

    def rec(npc, page, ops, got, **gates):
        others = [r for r in recs if r != got]
        return [talk(npc, page, ops=ops + [give(got), cond(2)], cond=1, hasItems=others, **gates),
                talk(npc, page, ops=ops + [give(got)], cond=1, **gates)]

    d = [
        talk(LOCKIRIN, "30531-03", when="absent", classes=[56], minLevel=39),
        talk(LOCKIRIN, "30531-02", when="absent", classes=[56]),
        talk(LOCKIRIN, "30531-01", when="absent"),
        event(LOCKIRIN, "30531-04", ops=START, when="absent", classes=[56], minLevel=39),
        talk(LOCKIRIN, "30531-05", cond=1),
        talk(LOCKIRIN, "30531-06", cond=2, ops=[take(REC_A), take(REC_B), take(REC_F)] + mark(MARK, 46000, 5900)),
        talk(SPIRON, "30532-01"), talk(KEEF, "30534-01"), talk(DUBABAH, "30672-01"),
        # Balanki: Croto's soldiers want the Necklace of Kamuru off the Evil Eye Lord.
        talk(BALANKI, "30533-01", **v("bCond", 0)),
        talk(BALANKI, "30533-03", **v("bCond", 1)),
        *rec(BALANKI, "30533-04", [setv("bCond", 3), take(LETTER)], REC_B, **v("bCond", 2)),
        talk(BALANKI, "30533-05", **v("bCond", 3)),
        event(BALANKI, "30533-02", ops=[setv("bCond", 1)], **v("bCond", 0)),
        talk(CROTO, "30671-01", lacksItems=[PAINT_K], **v("bCond", 1)),
        talk(CROTO, "30671-03", lacksItems=[NECKLACE], **v("bCond", 1)),
        talk(CROTO, "30671-04", ops=[setv("bCond", 2), take(NECKLACE), take(PAINT_K), give(LETTER)], **v("bCond", 1)),
        talk(CROTO, "30671-05", varAtLeast={"bCond": 2}),
        event(CROTO, "30671-02", ops=[give(PAINT_K)], lacksItems=[PAINT_K], **v("bCond", 1)),
        # Arin: Toma's teleport device; the trip drops the hero among King Bugbears.
        talk(ARIN, "30536-01", ops=[setv("aCond", 1), give(PAINT_T)], **v("aCond", 0)),
        talk(ARIN, "30536-02", **v("aCond", 1)),
        *rec(ARIN, "30536-03", [setv("aCond", 3), take_all(DEVICE)], REC_A, **v("aCond", 2)),
        talk(ARIN, "30536-04", **v("aCond", 3)),
        talk(TOMA, "30556-01", lacksItems=[BROKEN], **v("aCond", 1)),
        talk(TOMA, "30556-06", lacksItems=[DEVICE], ops=[setv("aCond", 2), take(BROKEN), give(DEVICE, 5)],
             **v("aCond", 1)),
        talk(TOMA, "30556-07", varAtLeast={"aCond": 2}),
        _no_page(event(TOMA, "TELEPORT", ops=[take(PAINT_T), give(BROKEN), {"op": "teleport", "x": 140352, "y": -194133,
                                                                     "z": -3146},
                                        spawn_at(20150, 140333, -194153, -3138), spawn_at(20150, 140395, -194147, -3146),
                                        spawn_at(20150, 140304, -194082, -3157)],
               hasItem=PAINT_T, **v("aCond", 1))),
        # Filaur: Lorain's antidote ingredients for the report on Kruma.
        talk(FILAUR, "30535-01", ops=[setv("fCond", 1), give(ARCH)], **v("fCond", 0)),
        talk(FILAUR, "30535-02", **v("fCond", 1)),
        *rec(FILAUR, "30535-03", [setv("fCond", 3), take(REPORT)], REC_F, **v("fCond", 2)),
        talk(FILAUR, "30535-04", **v("fCond", 3)),
        talk(LORAIN, "30673-01", lacksItems=[REPORT, ANTIDOTE], ops=[take(ARCH), give(ANTIDOTE)], **v("fCond", 1)),
        talk(LORAIN, "30673-03", lacksItems=[REPORT], atLeast={NEEDLE: 10, WEB: 10, BLOOD: 10}, **v("fCond", 1)),
        talk(LORAIN, "30673-02", lacksItems=[REPORT], **v("fCond", 1)),
        talk(LORAIN, "30673-05", varAtLeast={"fCond": 2}),
        event(LORAIN, "30673-04", ops=[setv("fCond", 2), take_all(BLOOD, NEEDLE, WEB), take(ANTIDOTE), give(REPORT)],
              hasItem=ANTIDOTE, atLeast={NEEDLE: 10, WEB: 10, BLOOD: 10}, **v("fCond", 1)),
        *[kill(m, [drop(item, 1.0, 10)], cond=1, hasItem=ANTIDOTE)
          for m, item in ((20225, BLOOD), (20229, NEEDLE), (20233, WEB))],
        kill(27133, [give(NECKLACE)], cond=1, hasItem=PAINT_K, lacksItems=[NECKLACE]),
    ]
    write(231, "00231_test_of_the_maestro", "Q00231_TestOfTheMaestro", "Test of the Maestro",
          [REC_B, REC_F, REC_A] + list(range(2868, 2879)) + [BROKEN], d)


@quest
def q234():
    """Fate's Whisper: Reorin forges an A-grade weapon from a B-grade one (3rd-class prerequisite)."""
    import json
    import pathlib
    REORIN, CLIFF, FERRIS, ZENKIN, KASPAR = 31002, 30182, 30847, 30178, 30833
    PIPETTE, ORB, KERNON, GOLKONDA, HALLATE, HAMMER, MOLD, VARNISH, RED = (
        4665, 4666, 4667, 4668, 4669, 4670, 4671, 4672, 4673)
    CRYSTAL_B, STAR = 1460, 5011
    SCRIPT = "Q00234_FatesWhisper"
    chests = {25035: 31027, 25054: 31028, 25126: 31029, 25220: 31030}
    b_grade = [79, 2626, 287, 97, 175, 210, 234, 268, 171, 7883, 7889, 7901, 7893]
    a_grade = [80, 288, 98, 150, 212, 235, 269, 2504, 5233, 7884, 7894, 7899]
    rows = json.loads((pathlib.Path(__file__).resolve().parent.parent / "data/items.json").read_text())["items"]
    names = {r["id"]: r["name"] for r in rows}

    def v(key, n):
        return dict(varAtLeast={key: n}, varBelow={key: n + 1})

    pages = {}

    def named(page, item):
        """Mobius fills %weaponname% at runtime; here each weapon gets its own copy of the page."""
        out = f"{page}-{item}.htm"
        pages[out] = (REORIN, mobius_page(SCRIPT, page).replace("%weaponname%", names[item]))
        return out

    d = [
        talk(REORIN, "31002-02", when="absent", minLevel=75),
        talk(REORIN, "31002-01", when="absent"),
        event(REORIN, "31002-03", ops=START, when="absent", minLevel=75),
        talk(REORIN, "31002-05", cond=1, hasItem=ORB, ops=[take(ORB), cond(2)]),
        talk(REORIN, "31002-04b", cond=1),
        talk(REORIN, "31002-06", cond=2, hasItems=[KERNON, GOLKONDA, HALLATE],
             ops=[take(GOLKONDA), take(HALLATE), take(KERNON), cond(3)]),
        talk(REORIN, "31002-05c", cond=2),
        talk(REORIN, "31002-07", cond=3, hasItem=VARNISH, ops=[take(VARNISH), cond(4)]),
        talk(REORIN, "31002-06b", cond=3),
        talk(REORIN, "31002-08", cond=4, hasItem=HAMMER, ops=[take(HAMMER), cond(5)]),
        talk(REORIN, "31002-07b", cond=4),
        *[talk(REORIN, "31002-08b", cond=c) for c in (5, 6, 7)],
        talk(REORIN, "31002-09", cond=8, ops=[take_all(MOLD), cond(9)]),
        talk(REORIN, "31002-BGradeList", cond=9, atLeast={CRYSTAL_B: 984}, ops=[take(CRYSTAL_B, 984), cond(10)]),
        talk(REORIN, "31002-09b", cond=9),
        *[talk(REORIN, named("31002-AGradeList", w), cond=10, hasItem=w, varAtLeast={"bypass": 1, "weaponId": w},
               varBelow={"weaponId": w + 1}) for w in b_grade],
        *[talk(REORIN, named("31002-15", w), cond=10, varAtLeast={"bypass": 1, "weaponId": w},
               varBelow={"weaponId": w + 1}) for w in b_grade],
        talk(REORIN, "31002-BGradeList", cond=10),
        # The B-grade choice is locked once confirmed (Mobius "bypass" var).
        *[event(REORIN, f"selectBGrade_{w}", named("31002-13", w), ops=[setv("weaponId", w)], cond=10,
                varBelow={"bypass": 1}) for w in b_grade],
        *[event(REORIN, "confirmWeapon", named("31002-14", w), ops=[setv("bypass", 1)], cond=10, **v("weaponId", w))
          for w in b_grade],
        *[event(REORIN, f"selectAGrade_{a}", named("31002-12", a),
                ops=[take(w), give(a), give(STAR), social(3), {"op": "complete"}], cond=10, hasItem=w,
                varAtLeast={"bypass": 1, "weaponId": w}, varBelow={"weaponId": w + 1})
          for a in a_grade for w in b_grade],
        *[event(REORIN, f"selectAGrade_{a}", named("31002-15", w), cond=10, varAtLeast={"bypass": 1, "weaponId": w},
                varBelow={"weaponId": w + 1}) for a in a_grade for w in b_grade],
        *[event(REORIN, f"selectAGrade_{a}", "31002-16") for a in a_grade],
        talk(CLIFF, "30182-01", cond=3, lacksItems=[VARNISH]),
        talk(CLIFF, "30182-02", cond=3),
        event(CLIFF, "30182-01c", ops=[give(VARNISH)], cond=3, lacksItems=[VARNISH]),
        talk(FERRIS, "30847-01", cond=4, lacksItems=[HAMMER], ops=[give(HAMMER)]),
        *[talk(FERRIS, "30847-02", cond=c, hasItem=HAMMER) for c in range(4, 11)],
        talk(ZENKIN, "30178-01", cond=5),
        *[talk(ZENKIN, "30178-02", cond=c) for c in range(6, 11)],
        event(ZENKIN, "30178-01a", ops=[cond(6)], cond=5),
        talk(KASPAR, "30833-01", cond=6),
        talk(KASPAR, "30833-03", cond=7, hasItem=RED, ops=[take(RED), give(MOLD), cond(8)]),
        talk(KASPAR, "30833-02", cond=7),
        *[talk(KASPAR, "30833-04", cond=c) for c in range(8, 11)],
        event(KASPAR, "30833-01b", ops=[give(PIPETTE), cond(7)], cond=6),
        # Raid bosses leave a chest; Reiria's orb and the three Infernium scepters are inside.
        *[kill(boss, [spawn(chest)], when="any") for boss, chest in chests.items()],
        talk(31027, "31027-01", cond=1, lacksItems=[ORB], ops=[give(ORB)]),
        talk(31027, "31027-02"),
        *[r for chest, item in ((31028, KERNON), (31029, GOLKONDA), (31030, HALLATE)) for r in (
            talk(chest, f"{chest}-01", cond=2, lacksItems=[item], ops=[give(item)]),
            talk(chest, f"{chest}-02"))],
        # Baium's blood: strike him with the Pipette Knife in hand.
        attack(29020, [take(PIPETTE), give(RED)], cond=7, weapons=[PIPETTE], lacksItems=[RED]),
    ]
    write(234, "00234_fateswhisper", SCRIPT, "Fate's Whisper", [PIPETTE, RED], d, pages=pages)


if __name__ == "__main__":
    ids = [int(a) for a in sys.argv[1:]] or sorted(AUTHORS)
    for qid in ids:
        AUTHORS[qid]()
