-- Mobius mods_wedding (custom Wedding feature).

CREATE TABLE IF NOT EXISTS wedding_couples (
    id int PRIMARY KEY,
    player1_id int NOT NULL,
    player2_id int NOT NULL,
    married boolean NOT NULL DEFAULT false,
    affiance_date_unix_ms bigint NOT NULL DEFAULT 0,
    wedding_date_unix_ms bigint NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS wedding_couples_player1_idx ON wedding_couples (player1_id);
CREATE INDEX IF NOT EXISTS wedding_couples_player2_idx ON wedding_couples (player2_id);

INSERT INTO store_meta (key, value_bigint) VALUES ('next_wedding_couple_id', 1)
ON CONFLICT (key) DO NOTHING;