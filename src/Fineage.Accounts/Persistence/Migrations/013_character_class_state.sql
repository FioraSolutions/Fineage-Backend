-- Block G1: subclasses and noblesse (Mobius characters.base_class / nobless, character_subclasses with the per
-- class_index skills, hennas and shortcuts). The active class stays on the characters row; this payload parks the rest.
CREATE TABLE IF NOT EXISTS character_class_state (
    character_id int PRIMARY KEY REFERENCES characters (id) ON DELETE CASCADE,
    base_class_id int NOT NULL DEFAULT 0,
    class_index int NOT NULL DEFAULT 0,
    noble boolean NOT NULL DEFAULT false,
    payload jsonb NOT NULL
);
