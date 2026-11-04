-- Mobius character_shortcuts: hotbar slots per character (slot 0..11 on page 0..9).
-- type: 1 item (shortcut_id = item object id), 2 skill (level = skill level), 3 action, 4 macro, 5 recipe.
CREATE TABLE IF NOT EXISTS character_shortcuts (
    character_id int NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    slot int NOT NULL CHECK (slot >= 0 AND slot < 12),
    page int NOT NULL CHECK (page >= 0 AND page < 10),
    type int NOT NULL CHECK (type BETWEEN 1 AND 5),
    shortcut_id int NOT NULL,
    level int NOT NULL DEFAULT 0,
    PRIMARY KEY (character_id, slot, page)
);
-- No backfill here: PostgresAccountStore.Open re-projects normalized rows from store_snapshot.
