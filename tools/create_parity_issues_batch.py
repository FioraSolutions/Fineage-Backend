#!/usr/bin/env python3
"""Create deduped Mobius parity issues (Step 2). Idempotent: skips if title exists open."""
from __future__ import annotations

import json
import subprocess
import sys

REPO = "FioraSolutions/Fineage-Backend"
# Repo integration cannot mint custom labels (403); use existing server + ref:mobius.
LABELS = "server,ref:mobius"

ISSUES = [
    {
        "title": "Combat: signet EffectPoint AOE damage parity",
        "body": """## Mobius reference
`handler/skillhandlers/Sign.java` — signet ground points apply periodic damage in range.

## Fineage today
`ZoneWorldSignets.cs` — EffectPoint path incomplete (TODO).

## Acceptance criteria
- Signet skills place effect points and tick damage per Mobius tables from imported data.
- No silent stub: unknown signet skill id logs concrete catalog miss.
- Unit test covers at least one imported signet row.
""",
    },
    {
        "title": "Combat: Spoil Festival (302) and sweeper combat catalog",
        "body": """## Mobius reference
Combat handlers for AoE spoil / sweep (skills 302, 444) in `skill-combat.xml`.

## Fineage today
Skill 302 appears in `skills.json` but missing from `data/skill-combat.json` — refuse/stub path.

## Acceptance criteria
- Import 302 (+ related sweep combat rows) into `skill-combat.json` via existing Mobius import pipeline.
- Zone applies spoil/sweep using ledger-backed drops (no silent fallback).
- Test asserts spoil credit + sweep loot path for skill 302.
""",
    },
    {
        "title": "Combat: fold weapon enchant level into UserInfo/CharInfo stats",
        "body": """## Mobius reference
`PlayerStat` enchant funcs add +enchant to P.Atk/M.Atk etc. visible on user sheet.

## Fineage today
Item enchant roll works; stat fold into broadcast vitals partial.

## Acceptance criteria
- +N weapon/armor enchant changes combat stats on `UserInfo` after enchant.
- Regression test compares +0 vs +4 row from catalog.
""",
    },
    {
        "title": "Skills: compound usingKinds import (Mobius AND/OR)",
        "body": """## Mobius reference
`Skill.java` weapon kind conditions with AND/OR groups.

## Fineage today
Runtime evaluator OK for `castCondition` in `skills.json`; `gen_skill_conditions.py` still skips compound **usingKinds**.

## Acceptance criteria
- Generator emits compound usingKinds JSON for all Interlude combat skills needing it.
- Cast refuses with logged skill id when kind gate fails (existing pattern).
""",
    },
    {
        "title": "Party: command channel creation and loot rules",
        "body": """## Mobius reference
`CommandChannel` — multi-party leader, creation rules, related chat types 14–16.

## Fineage today
`ZoneWorldChat.cs` ignores party room / CC chat; no CC state machine.

## Acceptance criteria
- Party leaders can form CC per Mobius level/size gates.
- CC member list visible to participants; dissolve on leader logout rules per data.
- Tests cover create + invite second party + leave.
""",
    },
    {
        "title": "Party: exp/SP share level-gap and vitality nuances",
        "body": """## Mobius reference
`Party.distributeXpAndSp` level difference thresholds.

## Fineage today
Core party share ✅ (`ZoneWorldDeathRules`); edge penalties not verified.

## Acceptance criteria
- Share table matches Mobius for out-of-range members (no XP / reduced XP cases).
- Automated test with fabricated party levels.
""",
    },
    {
        "title": "Clan hall: AgitDecoInfo and item-creation function depth",
        "body": """## Mobius reference
`ClanHallManager` — decorative functions, item creation multisells.

## Fineage today
#746 partial `ZoneWorldPledge.HallFunctions`.

## Acceptance criteria
- Activated hall functions broadcast deco state to members entering hall.
- Item creation uses auditable ledger + multisell catalog (no ad-hoc grants).
""",
    },
    {
        "title": "Siege: guard hire and tower-linked respawn data",
        "body": """## Mobius reference
`SiegeGuardManager` hire tickets + spawn linkage to control towers.

## Fineage today
Towers/flame ✅; hire multisell / spawn disable flags data gaps.

## Acceptance criteria
- Owner can hire guards during siege prep using imported hire data.
- Killed hired guards respect respawn/tower disable rules from JSON.
""",
    },
    {
        "title": "Olympiad: hero periodic weapon grant nuances",
        "body": """## Mobius reference
`Hero.java` — weekly weapon selection constraints.

## Fineage today
Core hero + gates ✅; grant timing edge cases open.

## Acceptance criteria
- Hero receives eligible weapon once per period; duplicate grant refused with system message.
- Ledger records grant; weapon removed on hero loss if Mobius does.
""",
    },
    {
        "title": "Social: noblesse village master dialog (RequestNobless)",
        "body": """## Mobius reference
Village master scripts — noblesse certification flow.

## Fineage today
`NpcServiceBypass` noblesse stub.

## Acceptance criteria
- Noblesse gate quest complete → master grants noblesse with SP/adena costs via ledger.
- Tests via bypass on imported master NPC id.
""",
    },
    {
        "title": "Seven Signs: catacomb/necropolis gate NPC validation kick",
        "body": """## Mobius reference
Dimensional gate NPCs validate cabal registration; kick unauthorized entrants.

## Fineage today
Teleport + cabal checks; full gate NPC script parity partial.

## Acceptance criteria
- Wrong-cabal players bounced from catacomb regions with Mobius system messages.
- Net test enters with wrong seal and expects reject.
""",
    },
    {
        "title": "Cursed weapons: raid drop acquisition and disappearance polish",
        "body": """## Mobius reference
`CursedWeaponsManager.java` full cycle: random drop, pick-up activation, disappearance timer.

## Fineage today
`CursedWeaponsManager` + `ZoneWorldCursedWeapons` + tests for activate/fanout; verify mob drop rates live.

## Acceptance criteria
- Configured raid/mob can drop blade per imported chances; pickup runs activation pipeline.
- Disappearance broadcasts + persist snapshot; economy ledger not used for blades (bind rules).
""",
    },
    {
        "title": "Skills: RequestExEnchantSkill trainer enchant flow",
        "body": """## Mobius reference
`RequestExEnchantSkill` / `ExEnchantSkillInfo` packets and SP cost tables.

## Fineage today
`SkillEnchantRules` gated by `FINEAGE_ALLOW_SKILL_ENCHANT` default false.

## Acceptance criteria
- Trainer enchant increases +skill level within imported SP/enchant route tables.
- All SP/adena costs via auditable ledgers; refuse logs skill id when route missing.
""",
    },
    {
        "title": "Skills: complete class skill learn edges (skill-trees.json)",
        "body": """## Mobius reference
`SkillTreeData` all class parents for Interlude.

## Fineage today
Partial `data/skill-trees.json` — some classes missing edges.

## Acceptance criteria
- Import script regen from Mobius leaves zero orphan trainer rows for playable classes.
- `AcquireSkillList` test per race archetype at 20/40/76.
""",
    },
    {
        "title": "Skills: certificate transfer skills",
        "body": """## Mobius reference
Certificate handlers transfer skills between subclasses / dual paths.

## Fineage today
Not found in `src/`.

## Acceptance criteria
- Transfer items consumed via ledger; target skill book gains row per imported certificate.
- Log + system message when certificate id unknown in catalog.
""",
    },
    {
        "title": "Summons: offensive cubic M.Atk success term",
        "body": """## Mobius reference
`Cubic.java` offensive success uses master M.Atk.

## Fineage today
`ZoneWorldCubics.cs` M.Atk TODO.

## Acceptance criteria
- Cubic damage/heal formulas include master M.Atk term from data.
- Test fixtures assert different INT/M.Atk changes cubic outcome.
""",
    },
    {
        "title": "Zones: Mother Tree HP/MP bonus region",
        "body": """## Mobius reference
`MotherTreeZone` — elven town regen bonus.

## Fineage today
Zone type not loaded from `data/zones`.

## Acceptance criteria
- Mother tree polygons from imported zone data apply regen bonus func.
- Player inside elven village receives bonus in regen tick test.
""",
    },
    {
        "title": "World: geodata pathfinding full-world mode",
        "body": """## Mobius reference
`GeoEngine` LOS + path on full geo.

## Fineage today
Geo optional; stub mode on some hosts.

## Acceptance criteria
- Production config loads geo assets; `FindPath` matches Mobius for blocked fixture map.
- Startup logs concrete missing geo file names (no silent straight-line fallback in production mode).
""",
    },
    {
        "title": "World: enable remaining spawn JSON map groups",
        "body": """## Mobius reference
`SpawnData.xml` world coverage.

## Fineage today
189 spawn JSON files enabled; most regions still off default `FINEAGE_ZONE_MAPS`.

## Acceptance criteria
- Document + config profile enables Interlude starter continents without manual edits.
- Spawn tick loads day/night groups for enabled files.
""",
    },
    {
        "title": "World: raid boss live spawns all catalog regions",
        "body": """## Mobius reference
`RaidBossSpawnManager` per-region schedules.

## Fineage today
216/223 bosses `enabled: true`; regional spawn wiring gaps.

## Acceptance criteria
- Each enabled raid row spawns at imported loc with respawn window.
- Admin boss schedule + player `/boss` list matches catalog ids.
""",
    },
    {
        "title": "World: raid boss points ranking NPC",
        "body": """## Mobius reference
`RaidBossPointsManager` + NPC HTML ranking.

## Fineage today
Not present.

## Acceptance criteria
- Points accrue on qualifying kill; NPC shows top N from persisted store.
""",
    },
    {
        "title": "World: boat vehicle client packet wire",
        "body": """## Mobius reference
`BoatManager` + packets `VehicleInfo`/`VehicleDeparture`/in-vehicle move family.

## Fineage today
`ZoneWorldBoats` simulation; `docs/client-packet-contracts.md` lists gaps.

## Acceptance criteria
- Passengers receive vehicle packet family; movement while onboard matches Mobius order.
- Economy unchanged (boats free or ticket via ledger if data requires).
""",
    },
    {
        "title": "Chat: hero voice and remaining Say2 channel types",
        "body": """## Mobius reference
`Say2.java` — hero (17), petition placeholders, level gates.

## Fineage today
Core channels in `ZoneWorldChat`; hero throttle + friend chat (#289) open.

## Acceptance criteria
- Hero channel only for heroes, 10s throttle; trade/shout level 20 gates enforced.
- Tests per channel type table in `net-protocol.md`.
""",
    },
    {
        "title": "Admin: Mobius admin handler long tail",
        "body": """## Mobius reference
`handlers/admin/*.java` ~80 verbs (spawn, teleport, ban, manipulate, etc.)

## Fineage today
`ZoneWorldAdmin` + punishment subset (#737).

## Acceptance criteria
- Priority verbs for live ops (spawn, tele, item create via ledger, kick) implemented with audit log.
- Unknown admin command logs verb + rejects (no silent no-op).
""",
    },
    {
        "title": "Punishment: account login ban type",
        "body": """## Mobius reference
`PunishmentManager` login ban prevents auth.

## Fineage today
Jail/chat/char ban wired; login ban partial on Login service.

## Acceptance criteria
- Login ban flag checked in `NetLoginServer`; handoff refused with message.
- Test covers ban + unban cycle.
""",
    },
    {
        "title": "Fishing: weekly championship top board",
        "body": """## Mobius reference
`FishingChampionshipManager` — weekly scores + rewards.

## Fineage today
Core fishing minigame ✅; championship absent.

## Acceptance criteria
- Weekly reset persists top anglers; reward via grant ledger.
""",
    },
    {
        "title": "Events: EventDropManager and TvT-style drops",
        "body": """## Mobius reference
`EventDropManager` + event scripts.

## Fineage today
Not present.

## Acceptance criteria
- Config-driven event windows add temporary drop rows; no hardcoded item ids in code paths.
""",
    },
    {
        "title": "Drops: rare NPC droplist catalog holes",
        "body": """## Mobius reference
Per-NPC `droplist` XML groups.

## Fineage today
1295 JSON files; audit finds rare mob gaps.

## Acceptance criteria
- Import diff report lists NPC ids with no Fineage droplist; zero ids in default world spawn set missing drops.
""",
    },
    {
        "title": "AI: raid minion clan buff on follow",
        "body": """## Mobius reference
`AttackableAI` 1/30 roll applies clan buff when minion follows raid boss.

## Fineage today
Follow movement ✅; buff roll not implemented.

## Acceptance criteria
- Minion within follow range of leader may cast imported buff skill on interval table.
""",
    },
    {
        "title": "AI: event and quest scripted NPC behaviors (framework)",
        "body": """## Mobius reference
Hundreds of `dist/.../ai/**` scripts.

## Fineage today
Quest JSON packs + limited C# scripts; no generic script host.

## Acceptance criteria
- Document priority set (town event NPCs, raid gates) migrated to data-driven ops or small actors.
- Unhandled script id logs once; no silent skip on player talk.
""",
    },
    {
        "title": "Character: race-specific starting equipment on create",
        "body": """## Mobius reference
`CharCreateTable` / `initialEquipment.xml` race kits.

## Fineage today
Newbie helpers Q255/998; not full race kits.

## Acceptance criteria
- Each race receives Mobius starter adena/items on create via grant ledger.
- Create-char integration test per race id.
""",
    },
    {
        "title": "Wire: MagicSkillCanceled and RelationChanged polish",
        "body": """## Mobius reference
Cast cancel fanout; relation flags (siege war, mutual war).

## Fineage today
Listed in `combat-parity.md` / `client-packet-contracts.md` as residual.

## Acceptance criteria
- Cast interrupt sends `MagicSkillCanceled` to observers; relation updates on war state transitions.
""",
    },
    {
        "title": "Quests: complete 94 partial Interlude quest packs",
        "body": """## Mobius reference
344 quest scripts under `dist/game/data/scripts/quests`.

## Fineage today
`docs/QUEST_STATUS.md` — **94 partial**, **240 implemented**.

## Acceptance criteria
- Each partial quest moves to implemented or documents explicit defer with engine dependency issue link.
- `tools/quest_status.py` reports ≤10 partial (stretch) with CI check in quest wave PRs.
- Spawns + talk NPCs wired; completions use ledger for rewards.
""",
    },
    {
        "title": "Quests: enable 10 stub quest packs (start + completion)",
        "body": """## Mobius reference
Stub quests have dialogs but no start/complete ops (e.g. Q21, Q23, Q24).

## Fineage today
**10 stub** rows in `QUEST_STATUS.md`.

## Acceptance criteria
- Each stub gains `create`/`start` and completion ops matching Mobius flow.
- At least one timer/spawn quest uses existing quest spawn engine.
""",
    },
    {
        "title": "Quests: Ketra/Varka alliance 11–12 talk NPC wiring",
        "body": """## Mobius reference
Q011/Q012 secret meeting — NPC ids 31256+ etc.

## Fineage today
partial — missing talk NPC bindings per quest_status.

## Acceptance criteria
- Alliance quests startable; faction choice persists; rewards via ledger.
""",
    },
]


def open_titles() -> set[str]:
    out = subprocess.check_output(
        ["gh", "issue", "list", "-R", REPO, "--state", "open", "--limit", "500", "--json", "title"],
        text=True,
    )
    return {row["title"] for row in json.loads(out)}


def main() -> int:
    existing = open_titles()
    created: list[int] = []
    skipped: list[str] = []
    for spec in ISSUES:
        title = spec["title"]
        if title in existing:
            skipped.append(title)
            continue
        cmd = [
            "gh",
            "issue",
            "create",
            "-R",
            REPO,
            "--title",
            title,
            "--body",
            spec["body"],
            "--label",
            LABELS,
        ]
        result = subprocess.run(cmd, capture_output=True, text=True, check=True)
        num = result.stdout.strip().split("/")[-1]
        created.append(int(num))
        print(f"created #{num}: {title}")
    print(f"created={len(created)} skipped={len(skipped)}")
    if created:
        print("nums:", " ".join(str(n) for n in created))
    return 0


if __name__ == "__main__":
    sys.exit(main())