#!/usr/bin/env python3
"""Pet quests (Q419 Get a Pet, Q421 Little Wing's Big Adventure) as quest-engine packs.

Step tables written for this engine from Mobius CT_0 quest behaviour; no Java is copied. Pages come from the Mobius
quest folders via tools/questkit.py.
Engine features used here: `pick_random` + `htmlByVar` (Q419 random quiz page), `pet` / `byPet` gates (summoned pet vs
the collar registered in var summonOid), `sumItems`, `minEnchant` (collar level = pet level), `remember_item` and
`exchange_var_item` (flute -> bugle, enchant kept).
Deviations: Q419 visit flags are three vars instead of a bitmask; the Q421 curse skills (4167 / 4243) are not cast;
the guardian ghosts do not shout on hits.
Usage: python3 tools/author_pets.py
"""
from questkit import DONE, START, add, cond, drop, event, give, kill, setv, spawn, take, take_all, talk, unset, write, attack


def sound(name): return {"op": "sound", "name": name}
def say(text): return {"op": "say", "text": text}


MIDDLE = sound("ItemSound.quest_middle")
ITEMGET = sound("ItemSound.quest_itemget")


def q419():
    martin, bella, metty, ellie = 30731, 30256, 30072, 30091
    lover, wolf_collar = 3417, 2375
    lists = list(range(3418, 3423))
    proofs = list(range(3423, 3428))
    droplist = {20103: 3423, 20106: 3423, 20108: 3423, 20460: 3424, 20308: 3424, 20466: 3424,
                20025: 3425, 20105: 3425, 20034: 3425, 20474: 3426, 20476: 3426, 20478: 3426,
                20403: 3427, 20508: 3427}
    quiz = {"var": "q", "files": {str(n): f"30731-{n}.htm" for n in range(20, 34)}}
    visited = {"bella": 1, "metty": 1, "ellie": 1}
    reset = [unset(k) for k in ("bella", "metty", "ellie", "quiz", "answered", "wrong", "q")]
    pick = {"op": "pick_random", "key": "quiz", "name": "q"}
    rows = [
        talk(martin, "30731-02", when="absent", minLevel=15),
        talk(martin, "30731-01", when="absent"),
        *[event(martin, "task", f"30731-0{4 + race}", ops=START + [give(3418 + race)], when="absent", minLevel=15,
                races=[race]) for race in range(5)],
        talk(martin, "30731-09", hasAny=lists, sumItems=proofs, sumMax=0),
        talk(martin, "30731-10", hasAny=lists, sumItems=proofs, sumMax=49),
        talk(martin, "30731-11", hasAny=lists),
        talk(martin, "30731-13", varAtLeast=visited),
        talk(martin, "30731-16"),
        event(martin, "30731-12", ops=[take_all(*lists, *proofs), give(lover), MIDDLE], hasAny=lists,
              sumItems=proofs, sumMin=50),
        talk(bella, "30256-01"),
        event(bella, "30256-03", ops=[setv("bella", 1), MIDDLE], varAtLeast={"metty": 1, "ellie": 1}),
        event(bella, "30256-03", ops=[setv("bella", 1)]),
        talk(metty, "30072-01"),
        event(metty, "30072-02", ops=[setv("metty", 1), MIDDLE], varAtLeast={"bella": 1, "ellie": 1}),
        event(metty, "30072-02", ops=[setv("metty", 1)]),
        talk(ellie, "30091-01"),
        event(ellie, "30091-02", ops=[setv("ellie", 1), MIDDLE], varAtLeast={"bella": 1, "metty": 1}),
        event(ellie, "30091-02", ops=[setv("ellie", 1)]),
        # Quiz: ten random questions out of 14; one wrong answer sends the player back to the three visits.
        event(martin, "test", "30731-20", ops=[setv("quiz", " ".join(str(n) for n in range(20, 34))),
                                               setv("answered", 0), setv("wrong", 0), pick],
              hasItem=lover, varAtLeast=visited, htmlByVar=quiz),
        event(martin, "right", "30731-20", ops=[add("answered"), pick], varAtLeast={"q": 20},
              varBelow={"answered": 9}, htmlByVar=quiz),
        event(martin, "right", "30731-15", ops=[give(wolf_collar), DONE], varAtLeast={"q": 20, "answered": 9},
              varBelow={"wrong": 1}),
        event(martin, "right", "30731-14", ops=reset, varAtLeast={"q": 20, "answered": 9}),
        event(martin, "wrong", "30731-20", ops=[add("answered"), add("wrong"), pick], varAtLeast={"q": 20},
              varBelow={"answered": 9}, htmlByVar=quiz),
        event(martin, "wrong", "30731-14", ops=reset, varAtLeast={"q": 20, "answered": 9}),
    ]
    for mob, item in droplist.items():
        rows.append(kill(mob, [drop(item, 1.0, 50)], hasItem=item - 5))
    write(419, "00419_get_a_pet", "Q00419_GetAPet", "Get a Pet", [lover, *lists, *proofs], rows, repeatable=True)


def q421():
    cronos, mimyu, leaf = 30610, 30747, 4325
    flutes = [3500, 3501, 3502]
    trees = [27185, 27186, 27187, 27188]
    remember = {"op": "remember_item", "key": "summonOid", "items": flutes, "minEnchant": 55}
    bugle = {"op": "exchange_var_item", "key": "summonOid", "items": flutes, "offset": 922}
    rows = [
        talk(cronos, "30610-01", when="absent", maxLevel=44),
        talk(cronos, "30610-02", when="absent", sumItems=flutes, sumMax=0),
        talk(cronos, "30610-02", when="absent", sumItems=flutes, sumMin=2),
        talk(cronos, "30610-04", when="absent", hasAny=flutes, minEnchant=55),
        talk(cronos, "30610-03", when="absent"),
        event(cronos, "30610-06", "30610-05", ops=START + [remember], when="absent", minLevel=45,
              sumItems=flutes, sumMin=1, sumMax=1, hasAny=flutes, minEnchant=55),
        event(cronos, "30610-06", when="absent"),
        talk(cronos, "30610-07"),
        talk(mimyu, "30747-01", cond=1, varBelow={"met": 1}, ops=[setv("met", 1)]),
        talk(mimyu, "30747-04", cond=1, pet="registered"),
        talk(mimyu, "30747-03", cond=1, pet="other"),
        talk(mimyu, "30747-02", cond=1),
        event(mimyu, "30747-02", "30747-04", cond=1, pet="registered"),
        event(mimyu, "30747-02", "30747-03", cond=1, pet="other"),
        event(mimyu, "30747-02", cond=1),
        event(mimyu, "30747-05", ops=[cond(2), give(leaf, 4)], cond=1, pet="registered"),
        event(mimyu, "30747-05", "30747-06", cond=1),
        talk(mimyu, "30747-07", cond=2, varBelow={"trees": 1}),
        talk(mimyu, "30747-11", cond=2),
        talk(mimyu, "30747-12", cond=3, varBelow={"fairy": 1}, pet="none"),
        talk(mimyu, "30747-14", cond=3, varBelow={"fairy": 1}, pet="other"),
        talk(mimyu, "30747-13", cond=3, varBelow={"fairy": 1}, ops=[setv("fairy", 1)]),
        talk(mimyu, "30747-15", cond=3, pet="registered"),
        talk(mimyu, "30747-17", cond=3, sumItems=flutes, sumMin=2),
        talk(mimyu, "30747-16", cond=3, holdsVarItem="summonOid", ops=[bugle, DONE]),
        talk(mimyu, "30747-18", cond=3),
    ]
    leaf_ops = [take(leaf), say("Give me a Fairy Leaf...!"),
                say("Leave now, before you incur the wrath of the guardian ghost..."), add("trees")]
    for tree in trees:
        done = f"t{tree}"
        gates = dict(cond=2, byPet=True, pet="registered", hasItem=leaf, chance=0.01)
        rows.append(attack(tree, [setv(done, 1), *leaf_ops, cond(3)], varBelow={done: 1},
                           varAtLeast={"trees": 3}, **gates))
        rows.append(attack(tree, [setv(done, 1), *leaf_ops, ITEMGET], varBelow={done: 1, "trees": 3}, **gates))
        rows.append(kill(tree, [spawn(27189)] * 20, when="any"))
    write(421, "00421_little_wing_s_big_adventure", "Q00421_LittleWingsBigAdventure", "Little Wing's Big Adventure",
          [leaf], rows, repeatable=True)


if __name__ == "__main__":
    q419()
    q421()
