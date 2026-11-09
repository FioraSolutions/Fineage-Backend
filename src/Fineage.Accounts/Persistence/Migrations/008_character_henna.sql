-- Mobius character_hennas / player henna dye slots (three tattoos).
-- Dye ids reference data/henna.json (hennaList.xml). 0 = empty slot.
ALTER TABLE characters ADD COLUMN IF NOT EXISTS henna_dye1 int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS henna_dye2 int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS henna_dye3 int NOT NULL DEFAULT 0;
