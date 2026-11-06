# Early quests (MIN_LEVEL ≤ 21) — all races

Mobius CT0 Interlude SoT: `ref/mobius-ct0-interlude/dist/game/data/scripts/quests`.  
Issue: [#299](https://github.com/FioraSolutions/Fineage-Backend/issues/299).

| Metric | Count |
|---|---|
| Mobius scripts with `MIN_LEVEL ≤ 21` | **114** |
| LIVE in `data/quests/` pack (of those) | **44** |
| Still TODO | **70** |

## LIVE (44)

`1, 2, 4, 5, 6, 7, 8, 9, 10, 102, 105, 151, 152, 153, 155, 157, 159, 160, 161, 162, 163, 164, 165, 166, 168, 169, 170, 257, 258, 260, 263, 264, 265, 266, 267, 271, 272, 273, 274, 313, 316, 406, 407, 408, 409`  
(+ `999` tutorial; `256` also shipped but may sit outside the ≤21 filter)

Generators: `scripts/gen_early_wave1_newbie.py`, `scripts/gen_early_wave2_hunts.py`, prior elven gens.

## TODO (70)

`3, 38, 39, 45, 46, 47, 48, 49, 101, 103, 104, 106, 107, 108, 112, 118, 123, 154, 156, 158, 167, 259, 261, 262, 275, 276, 277, 291, 292, 293, 294, 295, 296, 297, 303, 306, 317, 319, 320, 324, 325, 326, 328, 340, 341, 347, 362, 363, 364, 378, 379, 385, 401, 402, 403, 404, 405, 410, 411, 412, 413, 414, 415, 416, 417, 418, 419, 634, 644, 661`

### Next waves (priority)

1. **Spirit / race tutorials** — Q101, 103, 104, 106, 107, 108  
2. **Remaining village hunts** — Q259, 261, 277, 291–297, 319, 324, 325, …  
3. **Non-elf 1st-class paths** — Q401–405, 410–418  
4. **HARD / deferred** — timers/spawns/party-heavy: Q3, 38, 39, 154, 158, 262, 275, 276, 303, 306, 317, 385, 634, 644, 661, travel Q45–49, …

Elf-village docs are a **subset**. Parity target is this checklist + matrix row 29.
