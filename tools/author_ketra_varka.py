#!/usr/bin/env python3
"""Ketra Orc / Varka Silenos alliance quests 605-616 (Mobius CT_0 behaviour, step tables written by hand).

Both sides mirror each other; `side()` holds the per-side ids. Alliance penalties and passive monsters live in
QuestMachine.Factions.cs. Usage: python3 tools/author_ketra_varka.py
"""
from questkit import DONE, START, attack, cond, event, exp, give, kill, setv, spawn_at, take, take_all, talk, unset, write

KEY, WISDOM_STONE = 1661, 7081
SOLDIER = [100, 200, 300, 300, 400]
OFFICER = [0, 100, 200, 300, 400]
CAPTAIN = [0, 0, 100, 200, 200]

# Enemy mobs: (alliance drop chance per mille, first cond that drops) for the alliance quest; war-quest chance.
VARKA_ALLY = {21350: (500, 1), 21351: (500, 1), 21353: (509, 1), 21354: (521, 1), 21355: (519, 1), 21357: (500, 2),
              21358: (500, 2), 21360: (509, 2), 21361: (518, 2), 21362: (518, 2), 21364: (527, 2), 21365: (500, 3),
              21366: (500, 3), 21368: (508, 3), 21369: (628, 2), 21370: (604, 2), 21371: (627, 3), 21372: (604, 3),
              21373: (649, 3), 21374: (626, 3), 21375: (626, 3)}
KETRA_ALLY = {21324: (500, 1), 21325: (500, 1), 21327: (509, 1), 21328: (521, 1), 21329: (519, 1), 21331: (500, 2),
              21332: (500, 2), 21334: (509, 2), 21335: (518, 2), 21336: (518, 2), 21338: (527, 2), 21339: (500, 3),
              21340: (500, 3), 21342: (508, 3), 21343: (628, 2), 21344: (604, 2), 21345: (627, 3), 21346: (604, 3),
              21347: (649, 3), 21348: (626, 3), 21349: (626, 3)}
VARKA_WAR = {21350: 500, 21353: 510, 21354: 522, 21355: 519, 21357: 529, 21358: 529, 21360: 539, 21362: 539,
             21364: 558, 21365: 568, 21366: 568, 21368: 568, 21369: 664, 21371: 713, 21373: 738}
KETRA_WAR = {21324: 500, 21327: 510, 21328: 522, 21329: 519, 21331: 529, 21332: 529, 21334: 539, 21336: 548,
             21338: 558, 21339: 568, 21340: 568, 21342: 578, 21343: 664, 21345: 713, 21347: 738}

KETRA = dict(
    base=605, folder_side="ketra_orcs", enemy_folder="varka_silenos",
    ally_npc=31371, war_npc=31370, shaman=31372, box=31561, altar=31560,
    ally_mobs=VARKA_ALLY, war_mobs=VARKA_WAR,
    badges=(7216, 7217, 7218), valor=7219, wisdom=7220, marks=[7211, 7212, 7213, 7214, 7215],
    enemy_marks=[7221, 7222, 7223, 7224, 7225], war_item=7233, war_reward=7186,
    hero=25309, hero_head=7235, commander=25312, commander_head=7236,
    stolen=7237, totem=7238, boss=25316, heart=7239, boss_at=(104825, -36926, -1136),
    names=("Alliance with Ketra Orcs", "War with Varka Silenos", "Magical Power of Water - Part 1",
           "Magical Power of Water - Part 2"),
    scripts=("Q00605_AllianceWithKetraOrcs", "Q00606_BattleAgainstVarkaSilenos", "Q00607_ProveYourCourageKetra",
             "Q00608_SlayTheEnemyCommanderKetra", "Q00609_MagicalPowerOfWaterPart1", "Q00610_MagicalPowerOfWaterPart2"),
    folders=("00605_alliance_with_ketra_orcs", "00606_battle_against_varka_silenos", "00607_prove_your_courage_ketra",
             "00608_slay_the_enemy_commander_ketra", "00609_magical_power_of_water_part_1",
             "00610_magical_power_of_water_part_2"),
)
VARKA = dict(
    base=611, ally_npc=31378, war_npc=31377, shaman=31379, box=31559, altar=31558,
    ally_mobs=KETRA_ALLY, war_mobs=KETRA_WAR,
    badges=(7226, 7227, 7228), valor=7229, wisdom=7230, marks=[7221, 7222, 7223, 7224, 7225],
    enemy_marks=[7211, 7212, 7213, 7214, 7215], war_item=7234, war_reward=7187,
    hero=25299, hero_head=7240, commander=25302, commander_head=7241,
    stolen=7242, totem=7243, boss=25306, heart=7244, boss_at=(142528, -82528, -6496),
    names=("Alliance with Varka Silenos", "War with Ketra Orcs", "Magical Power of Fire - Part 1",
           "Magical Power of Fire - Part 2"),
    scripts=("Q00611_AllianceWithVarkaSilenos", "Q00612_BattleAgainstKetraOrcs", "Q00613_ProveYourCourageVarka",
             "Q00614_SlayTheEnemyCommanderVarka", "Q00615_MagicalPowerOfFirePart1", "Q00616_MagicalPowerOfFirePart2"),
    folders=("00611_alliance_with_varka_silenos", "00612_battle_against_ketra_orcs", "00613_prove_your_courage_varka",
             "00614_slay_the_enemy_commander_varka", "00615_magical_power_of_fire_part_1",
             "00616_magical_power_of_fire_part_2"),
)


def alliance(s):
    q, n = s["base"], s["ally_npc"]
    S, O, C = s["badges"]
    M, V, W = s["marks"], s["valor"], s["wisdom"]
    p = lambda x: f"{n}-{x}"

    def need(level):  # badge counts for alliance level `level` (1-based cond)
        i = level - 1
        return {k: v for k, v in ((S, SOLDIER[i]), (O, OFFICER[i]), (C, CAPTAIN[i])) if v}

    d = [
        talk(n, p("01"), when="absent", minLevel=74),
        talk(n, p("02"), when="absent"),
        # Joining while allied with the other side is refused; an existing mark resumes at its level.
        event(n, p("04"), p("03"), when="absent", hasAny=s["enemy_marks"]),
        *[event(n, p("04"), f"{n}-0{i + 5}", ops=START + [cond(i + 2)], when="absent", hasItem=M[i])
          for i in range(5)],
        event(n, p("04"), ops=START + [cond(1)], when="absent"),
        talk(n, p("11"), cond=1, atLeast=need(1)), talk(n, p("10"), cond=1),
        talk(n, p("14"), cond=2, hasItem=M[0], atLeast=need(2)), talk(n, p("13"), cond=2),
        talk(n, p("17"), cond=3, hasItem=M[1], atLeast=need(3)), talk(n, p("16"), cond=3),
        talk(n, p("20"), cond=4, hasItems=[M[2], V], atLeast=need(4)), talk(n, p("19"), cond=4),
        talk(n, p("23"), cond=5, hasItems=[M[3], W], atLeast=need(5),
             ops=[take_all(S, O, C, W, M[3]), give(M[4]), cond(6)]),
        talk(n, p("22"), cond=5),
        talk(n, p("24"), cond=6, hasItem=M[4]),
        event(n, p("12"), ops=[take_all(S), give(M[0]), cond(2)], cond=1, atLeast=need(1)),
        event(n, p("15"), ops=[take_all(S, O, M[0]), give(M[1]), cond(3)], cond=2, atLeast=need(2)),
        event(n, p("18"), ops=[take_all(S, O, C, M[1]), give(M[2]), cond(4)], cond=3, atLeast=need(3)),
        event(n, p("21"), ops=[take_all(S, O, C, V, M[2]), give(M[3]), cond(5)], cond=4, hasItem=V, atLeast=need(4)),
        event(n, p("26"), ops=[take_all(*M, V, W), DONE]),
    ]
    # Badges drop to one random party member; each kind is capped at what the current level needs.
    for mob, (chance, first) in s["ally_mobs"].items():
        item = (S, O, C)[min(first, 3) - 1]
        limits = {S: SOLDIER, O: OFFICER, C: CAPTAIN}[item]
        for c in range(first, 6):
            if limits[c - 1]:
                d.append(kill(mob, [{"op": "drop_item", "itemId": item, "count": 1, "chance": chance / 1000,
                                     "limit": limits[c - 1]}], cond=c, party=True))
    write(q, s["folders"][0], s["scripts"][0], s["names"][0], [S, O, C], d, repeatable=True)


def war(s):
    q, n = s["base"] + 1, s["war_npc"]
    item = s["war_item"]
    d = [
        talk(n, f"{n}-01", when="absent", minLevel=74),
        talk(n, f"{n}-02", when="absent"),
        event(n, f"{n}-03", ops=START, when="absent", minLevel=74),
        talk(n, f"{n}-04", hasItem=item), talk(n, f"{n}-05"),
        event(n, f"{n}-07", ops=[take(item, 100), give(s["war_reward"], 20)], hasItem=item, minCount=100),
        event(n, f"{n}-07", f"{n}-08"),
        event(n, f"{n}-09", ops=[DONE]),
        *[kill(mob, [{"op": "drop_item", "itemId": item, "count": 1, "chance": ch / 1000, "limit": 0}], cond=1,
               party=True) for mob, ch in s["war_mobs"].items()],
    ]
    write(q, s["folders"][1], s["scripts"][1], s["names"][1], [item], d, repeatable=True)


def raid_head(s, idx, boss, head, mark, reward):
    q, n = s["base"] + idx, s["war_npc"]
    d = [
        talk(n, f"{n}-01", when="absent", minLevel=75, hasItem=mark),
        talk(n, f"{n}-02", when="absent", minLevel=75),
        talk(n, f"{n}-03", when="absent"),
        event(n, f"{n}-04", ops=START, when="absent", minLevel=75, hasItem=mark),
        talk(n, f"{n}-05", cond=2, hasItem=head), talk(n, f"{n}-06"),
        event(n, f"{n}-07", ops=[take(head), give(reward), exp(10000), DONE], cond=2, hasItem=head),
        # Every party member near the raid boss gets the head.
        kill(boss, [give(head), cond(2)], cond=1, everyone=True),
    ]
    write(q, s["folders"][idx], s["scripts"][idx], "Prove your courage!" if idx == 2 else "Slay the enemy commander!",
          [head], d, repeatable=True)


def totem_part1(s):
    q, n, sh, box = s["base"] + 4, s["ally_npc"], s["shaman"], s["box"]
    stolen = s["stolen"]
    d = [
        talk(n, f"{n}-01", when="absent", minLevel=74, hasAny=s["marks"]),
        talk(n, f"{n}-00a", when="absent", minLevel=74),
        talk(n, f"{n}-00b", when="absent"),
        event(n, f"{n}-02", ops=START, when="absent", minLevel=74, hasAny=s["marks"]),
        talk(n, f"{n}-03", cond=1),
        talk(sh, f"{sh}-01", cond=1, ops=[cond(2)]),
        talk(sh, f"{sh}-03", cond=2, varAtLeast={"spawned": 1}, ops=[unset("spawned")]),
        talk(sh, f"{sh}-02", cond=2),
        talk(sh, f"{sh}-04", cond=3, ops=[give(s["totem"]), give(WISDOM_STONE), DONE]),
        talk(box, f"{box}-01", cond=2),
        event(box, "open_box", f"{box}-02", lacksItems=[KEY]),
        event(box, "open_box", f"{box}-04", cond=2, varAtLeast={"spawned": 1}, ops=[take(KEY)]),
        event(box, "open_box", f"{box}-03", cond=2, hasItem=KEY, ops=[give(stolen), take(KEY), cond(3)]),
    ]
    # Hitting any enemy soldier while sneaking (cond 2) means the watcher saw you: the box only burns the key now.
    d += [attack(mob, [setv("spawned", 1)], cond=2, varBelow={"spawned": 1}) for mob in s["ally_mobs"]]
    write(q, s["folders"][4], s["scripts"][4], s["names"][2], [stolen], d, repeatable=True)


def totem_part2(s):
    q, sh, altar, boss, heart, totem = s["base"] + 5, s["shaman"], s["altar"], s["boss"], s["heart"], s["totem"]
    x, y, z = s["boss_at"]
    d = [
        talk(sh, f"{sh}-01", when="absent", minLevel=75, hasItem=totem),
        talk(sh, f"{sh}-00a", when="absent", minLevel=75),
        talk(sh, f"{sh}-00b", when="absent"),
        event(sh, f"{sh}-02", ops=START, when="absent", minLevel=75, hasItem=totem),
        talk(sh, f"{sh}-03", cond=1), talk(sh, f"{sh}-04", hasItem=heart), talk(sh, f"{sh}-05"),
        event(sh, "give_heart", f"{sh}-06", hasItem=heart, ops=[exp(10000), DONE]),
        event(sh, "give_heart", f"{sh}-07"),
        # The altar calls the elemental (one at a time; QuestNpc spawn skips a live raid boss).
        talk(altar, f"{altar}-02", cond=1, hasItem=totem, ops=[take(totem), cond(2), spawn_at(boss, x, y, z)]),
        talk(altar, f"{altar}-01", cond=1),
        talk(altar, f"{altar}-02", cond=2, ops=[spawn_at(boss, x, y, z)]),
        talk(altar, f"{altar}-05", cond=3),
        event(altar, "spawn_totem", f"{altar}-02", cond=1, hasItem=totem,
              ops=[take(totem), cond(2), spawn_at(boss, x, y, z)]),
        event(altar, "spawn_totem", f"{altar}-04", lacksItems=[totem]),
        kill(boss, [take(totem), give(heart), cond(3)], cond=1, hasItem=totem, everyone=True),
        kill(boss, [give(heart), cond(3)], cond=1, everyone=True),
        kill(boss, [give(heart), cond(3)], cond=2, lacksItems=[heart], everyone=True),
        kill(boss, [cond(3)], cond=2, everyone=True),
    ]
    write(q, s["folders"][5], s["scripts"][5], s["names"][3], [totem, heart], d, repeatable=True)


if __name__ == "__main__":
    for side in (KETRA, VARKA):
        alliance(side)
        war(side)
        raid_head(side, 2, side["hero"], side["hero_head"], side["marks"][2], side["valor"])
        raid_head(side, 3, side["commander"], side["commander_head"], side["marks"][3], side["wisdom"])
        totem_part1(side)
        totem_part2(side)
