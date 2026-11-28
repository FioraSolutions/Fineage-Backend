# `data/skill-transfer-trees.json` provenance

**Status: not Interlude data.** Mobius `L2J_Mobius_CT_0_Interlude` (pinned `e4b90cdf`, see
`tools/import_npc_templates.py`) has no `stats/players/skillTrees/transferSkillTree.xml`, and no Holy Pomander
items 15307–15309 in `stats/items`. It does ship the code and scripts that would use one:
`SkillTreeData` (`case "transferSkillTree"`), `HealerTrainer` (`SkillTransfer*`), `PlayerConfig.FeeDeleteTransferSkills`
and `skill_sharing_item` params on the healer trainers in `stats/npcs/30000-30099.xml`. With CT 0's data the
transfer trees load empty, so the feature is inert there.

The committed JSON (73 rows: class 16 = 19, class 30 = 22, class 43 = 32) was imported from the CT2.6 HighFive
`transferSkillTree.xml` (rows tagged "Confirmed CT2.5") and trimmed by hand; `docs/data/interlude-purge.md` records
the removal of 1531 / 1539. Raw HighFive has 24 / 24 / 32 rows for the same classes.

`scripts/gen_transfer_skill_trees.py` now takes the XML path explicitly and refuses anything outside the pinned
CT 0 checkout, so a re-run can't re-import HighFive rows. CT 0 gives it nothing to import, so the file is frozen.

Code that reads it: `Fineage.Game` `TransferSkillTreeCatalog` / `TransferSkills`, `GameData`,
`ZoneWorldTransferSkills`, `ZoneWorldSkills` (learn transfer skill), `ZoneWorldGear` (use pomander),
`TransferSkillCertificateTests`.
