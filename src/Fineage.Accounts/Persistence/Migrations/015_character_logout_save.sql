-- Mobius characters.curCp/maxCp and character_skills_save reuse columns.
-- remaining_time = -1 marks a reuse-only row (restore_type = 1); effects stay > 0.
ALTER TABLE characters ADD COLUMN IF NOT EXISTS max_cp int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS cp int NOT NULL DEFAULT 0;

ALTER TABLE character_skills_save DROP CONSTRAINT IF EXISTS character_skills_save_remaining_time_check;
ALTER TABLE character_skills_save ADD CONSTRAINT character_skills_save_remaining_time_check
    CHECK (remaining_time = -1 OR remaining_time > 0);

ALTER TABLE character_skills_save ADD COLUMN IF NOT EXISTS reuse_delay bigint NOT NULL DEFAULT 0;
ALTER TABLE character_skills_save ADD COLUMN IF NOT EXISTS systime bigint NOT NULL DEFAULT 0;
ALTER TABLE character_skills_save ADD COLUMN IF NOT EXISTS restore_type int NOT NULL DEFAULT 0;
