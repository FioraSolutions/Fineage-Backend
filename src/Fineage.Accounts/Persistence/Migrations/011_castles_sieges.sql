-- Block E castles: Mobius castle (owner, tax, treasury, siege date) and siege_clans mirrors.
-- store_snapshot stays the boot source; door upgrades and the schedule flags ride in castles.payload.
CREATE TABLE IF NOT EXISTS castles (
    id int PRIMARY KEY,
    owner_clan_id int NOT NULL DEFAULT 0,
    tax_percent int NOT NULL DEFAULT 0,
    treasury bigint NOT NULL DEFAULT 0,
    siege_unix_ms bigint NOT NULL DEFAULT 0,
    payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS siege_clans (
    castle_id int NOT NULL,
    clan_id int NOT NULL,
    side smallint NOT NULL,
    PRIMARY KEY (castle_id, clan_id)
);
