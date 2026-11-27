-- Mobius character_macroses: named macros per character (commands as Mobius semicolon CSV).
CREATE TABLE IF NOT EXISTS character_macros (
    character_id int NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    macro_id int NOT NULL,
    icon int NOT NULL DEFAULT 0,
    name text NOT NULL DEFAULT '',
    descr text NOT NULL DEFAULT '',
    acronym text NOT NULL DEFAULT '',
    commands text NOT NULL DEFAULT '',
    PRIMARY KEY (character_id, macro_id)
);
