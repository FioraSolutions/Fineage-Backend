-- Monster Derby tickets and other items that carry Mobius customType1/customType2.

ALTER TABLE character_items ADD COLUMN IF NOT EXISTS custom_type1 int NOT NULL DEFAULT 0;
ALTER TABLE character_items ADD COLUMN IF NOT EXISTS custom_type2 int NOT NULL DEFAULT 0;