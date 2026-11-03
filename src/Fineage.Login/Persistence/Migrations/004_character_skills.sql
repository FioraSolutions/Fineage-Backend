-- Mobius character_skills: skills learned from a trainer / quest (auto-get tree skills are re-derived, not stored).
CREATE TABLE IF NOT EXISTS character_skills (
    character_id int NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    skill_id int NOT NULL CHECK (skill_id > 0),
    skill_level int NOT NULL CHECK (skill_level > 0),
    PRIMARY KEY (character_id, skill_id)
);
-- No backfill here: PostgresAccountStore.Open re-projects normalized rows from store_snapshot.
