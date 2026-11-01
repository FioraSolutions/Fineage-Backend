-- Fineage durable account store (foundation).
-- store_snapshot.payload is the authoritative versioned document (same shape as accounts.json).
-- Projection tables are updated on each save for SQL inspection / future normalize splits.

CREATE TABLE IF NOT EXISTS schema_migrations (
    id text PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS store_snapshot (
    id int PRIMARY KEY CHECK (id = 1),
    version int NOT NULL DEFAULT 1,
    payload jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS accounts (
    name text PRIMARY KEY,
    email text NULL,
    password_hash text NOT NULL,
    password_salt text NOT NULL,
    iterations int NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS accounts_email_lower_uidx
    ON accounts (lower(email))
    WHERE email IS NOT NULL;

CREATE TABLE IF NOT EXISTS characters (
    id int PRIMARY KEY,
    account text NOT NULL REFERENCES accounts (name) ON DELETE CASCADE,
    name text NOT NULL,
    class_id int NOT NULL,
    race int NOT NULL,
    sex int NOT NULL,
    level int NOT NULL DEFAULT 1,
    payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS characters_name_lower_uidx ON characters (lower(name));

CREATE TABLE IF NOT EXISTS clans (
    id int PRIMARY KEY,
    name text NOT NULL,
    payload jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE UNIQUE INDEX IF NOT EXISTS clans_name_lower_uidx ON clans (lower(name));
