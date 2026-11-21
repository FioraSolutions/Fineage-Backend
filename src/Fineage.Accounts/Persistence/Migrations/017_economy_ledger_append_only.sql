-- Append-only enforcement for economy_ledger (docs/architecture/zone-simulation.md §3.8).
-- Idempotent: safe when 016_economy_ledger already created the table.
CREATE OR REPLACE FUNCTION economy_ledger_append_only()
RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'economy_ledger is append-only (position=%)', OLD.position;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS economy_ledger_no_mutate ON economy_ledger;
CREATE TRIGGER economy_ledger_no_mutate
    BEFORE UPDATE OR DELETE ON economy_ledger
    FOR EACH ROW EXECUTE FUNCTION economy_ledger_append_only();