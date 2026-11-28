-- #1003: Durable Agent operation registry. Keyed by IdempotencyKey per principal.
-- Same key and payload reuse the same receipt; a different payload is an explicit conflict.
-- Status and deadline survive a Runtime or Zone restart so a retry cannot double-apply
-- an already committed economy change.
CREATE TABLE IF NOT EXISTS agent_operations (
    operation_id text PRIMARY KEY,
    tenant_id text NOT NULL,
    principal_id text NOT NULL,
    agent_id text NOT NULL,
    character_id text NULL,
    idempotency_key text NOT NULL,
    command_id text NOT NULL,
    correlation_id text NOT NULL,
    tool_name text NOT NULL,
    payload_fingerprint text NOT NULL,
    request_json jsonb NOT NULL,
    status text NOT NULL,
    refusal_code text NULL,
    detail text NULL,
    ledger_commit_id text NULL,
    deadline timestamptz NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    completed_at timestamptz NULL,
    CONSTRAINT agent_operations_principal_idempotency_uidx
        UNIQUE (tenant_id, principal_id, idempotency_key),
    CONSTRAINT agent_operations_status_chk CHECK (status IN (
        'Accepted',
        'Running',
        'Succeeded',
        'Refused',
        'Failed',
        'Expired',
        'Cancelled',
        'Unknown'
    ))
);

CREATE INDEX IF NOT EXISTS agent_operations_recover_idx
    ON agent_operations (status, deadline);
