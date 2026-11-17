#!/usr/bin/env python3
"""Quest 624 The Finest Ingredients - Part 1 (Mobius CT_0 behaviour, step table written by hand).

Jeremy pays an Ice Crystal (the saga hand-in at cond 3) and a Soy Sauce Jar for 50 of each ingredient.
Usage: python3 tools/author_finest_ingredients.py
"""
from questkit import DONE, START, collect_all, cond, event, give, take_all, talk, write

SCRIPT = "Q00624_TheFinestIngredientsPart1"
JEREMY = 31521
NEPENTHES, ATROX, ATROXSPAWN, BANDERSNATCH = 21319, 21321, 21317, 21314
TRUNK, FOOT, SPICE = 7202, 7203, 7204
ICE_CRYSTAL, SOY_SAUCE = 7080, 7205
FULL = {str(TRUNK): 50, str(FOOT): 50, str(SPICE): 50}


def main():
    d = [
        talk(JEREMY, "31521-01", when="absent", minLevel=73),
        talk(JEREMY, "31521-03", when="absent"),
        event(JEREMY, "31521-02", ops=START, when="absent", minLevel=73),
        talk(JEREMY, "31521-06", cond=1),
        talk(JEREMY, "31521-04", cond=2, atLeast=FULL),
        talk(JEREMY, "31521-07", cond=2),
        event(JEREMY, "31521-05", ops=[take_all(TRUNK, FOOT, SPICE), give(ICE_CRYSTAL), give(SOY_SAUCE), DONE], atLeast=FULL),
        event(JEREMY, "31521-05", "31521-07", ops=[cond(1)]),
        *collect_all({NEPENTHES: TRUNK, ATROX: SPICE, ATROXSPAWN: SPICE, BANDERSNATCH: FOOT}, 50, 1, 2, party=True),
    ]
    write(624, "00624_thefinestingredientspart1", SCRIPT, "The Finest Ingredients - Part 1", [TRUNK, FOOT, SPICE], d,
          repeatable=True)


if __name__ == "__main__":
    main()
