-- Block C clans: crest blobs (Mobius crests) and clan hall ownership / auction (Mobius clanhall + auction + auction_bid).
-- Clan rows keep extended state (reputation, sub-units, privileges, wars, member grades, warehouse) in clans.payload.
-- store_snapshot stays the boot source for clans; these tables are the queryable mirror.
CREATE TABLE IF NOT EXISTS clan_crests (
    id int PRIMARY KEY,
    kind smallint NOT NULL CHECK (kind BETWEEN 0 AND 2),
    data bytea NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS clan_halls (
    id int PRIMARY KEY,
    owner_clan_id int NOT NULL DEFAULT 0,
    paid_until_unix_ms bigint NOT NULL DEFAULT 0,
    auction_end_unix_ms bigint NOT NULL DEFAULT 0,
    payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    updated_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE clans ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();
