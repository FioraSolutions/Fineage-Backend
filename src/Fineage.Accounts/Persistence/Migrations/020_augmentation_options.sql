-- Mobius item variation option1/option2 on character_items.

ALTER TABLE character_items ADD COLUMN IF NOT EXISTS option1 int NOT NULL DEFAULT 0;
ALTER TABLE character_items ADD COLUMN IF NOT EXISTS option2 int NOT NULL DEFAULT 0;