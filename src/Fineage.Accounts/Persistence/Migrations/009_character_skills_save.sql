-- Mobius character_skills_save: active abnormals restored on enter (remaining_time seconds).
CREATE TABLE IF NOT EXISTS character_skills_save (
    character_id int NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    skill_id int NOT NULL CHECK (skill_id > 0),
    skill_level int NOT NULL CHECK (skill_level > 0),
    remaining_time int NOT NULL CHECK (remaining_time > 0),
    PRIMARY KEY (character_id, skill_id)
);
