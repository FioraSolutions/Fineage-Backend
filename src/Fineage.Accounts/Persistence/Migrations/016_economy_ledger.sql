-- Auditable economy ledger journal (docs/architecture/zone-simulation.md, section 3.8). Rows are only appended:
-- position is the journal order, (tick, sequence) the transaction id, and hash chains each row to the one before it.
CREATE TABLE IF NOT EXISTS economy_ledger (
    position bigint PRIMARY KEY,
    tick bigint NOT NULL,
    sequence int NOT NULL,
    kind text NOT NULL,
    caused_by int NOT NULL,
    reference int NOT NULL,
    wall_unix_ms bigint NOT NULL,
    postings jsonb NOT NULL,
    hash char(64) NOT NULL,
    UNIQUE (tick, sequence)
);
