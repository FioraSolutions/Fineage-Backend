-- Mobius characters.karma / pkkills / pvpkills (Player.java INSERT/UPDATE_CHARACTER).
-- Written when a PK/PvP kill lands so death-drop karma rates and chaotic nameplates survive logout.
ALTER TABLE characters ADD COLUMN IF NOT EXISTS karma int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS pk_kills int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS pvp_kills int NOT NULL DEFAULT 0;
