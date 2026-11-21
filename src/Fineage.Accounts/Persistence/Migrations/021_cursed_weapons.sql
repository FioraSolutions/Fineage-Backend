CREATE TABLE IF NOT EXISTS cursed_weapons (
    item_id INT PRIMARY KEY,
    character_id INT NOT NULL DEFAULT 0,
    player_reputation INT NOT NULL DEFAULT 0,
    player_pk_kills INT NOT NULL DEFAULT 0,
    nb_kills INT NOT NULL DEFAULT 0,
    end_time_unix_ms BIGINT NOT NULL DEFAULT 0,
    activated BOOLEAN NOT NULL DEFAULT FALSE,
    dropped BOOLEAN NOT NULL DEFAULT FALSE,
    ground_object_id INT NOT NULL DEFAULT 0
);