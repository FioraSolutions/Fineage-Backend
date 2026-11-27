-- #996: value-bearing world subsystems (lottery, monster race, Seven Signs, Olympiad, raid boss points, punishments,
-- fishing championship, boss schedule, PC cafe points) move from JSON sidecars into the database. One row per
-- system holds its versioned snapshot; the zone writes it in the economy commit's transaction, with the ledger rows
-- of the move that changed it. revision only grows, so an older write never replaces a newer one.
CREATE TABLE IF NOT EXISTS world_state (
    system text PRIMARY KEY,
    schema_version int NOT NULL CHECK (schema_version > 0),
    revision bigint NOT NULL CHECK (revision > 0),
    payload jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CHECK ((payload ->> 'schemaVersion')::int = schema_version)
);

-- One-time cutover: which legacy sidecar file was imported into world_state, its checksum, and where it was archived.
-- A sidecar that appears again after its import is a second source of truth and stops the zone.
CREATE TABLE IF NOT EXISTS world_state_imports (
    system text PRIMARY KEY REFERENCES world_state (system),
    source_path text NOT NULL,
    source_sha256 text NOT NULL,
    archived_path text NOT NULL,
    imported_at timestamptz NOT NULL
);
