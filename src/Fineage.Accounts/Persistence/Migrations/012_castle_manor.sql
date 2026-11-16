-- Block E2 manor: Mobius castle_manor_production / castle_manor_procure mirrors.
-- store_snapshot stays the boot source (manorProduction / manorProcure); these rows mirror it on every write.
CREATE TABLE IF NOT EXISTS castle_manor_production (
    castle_id int NOT NULL,
    seed_id int NOT NULL,
    amount bigint NOT NULL DEFAULT 0,
    start_amount bigint NOT NULL DEFAULT 0,
    price bigint NOT NULL DEFAULT 0,
    next_period boolean NOT NULL DEFAULT false,
    PRIMARY KEY (castle_id, seed_id, next_period)
);

CREATE TABLE IF NOT EXISTS castle_manor_procure (
    castle_id int NOT NULL,
    crop_id int NOT NULL,
    amount bigint NOT NULL DEFAULT 0,
    start_amount bigint NOT NULL DEFAULT 0,
    price bigint NOT NULL DEFAULT 0,
    reward_type smallint NOT NULL DEFAULT 0,
    next_period boolean NOT NULL DEFAULT false,
    PRIMARY KEY (castle_id, crop_id, next_period)
);
