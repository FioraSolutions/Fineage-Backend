-- #1000: Agent identity, tenant binding and scopes. Integration credentials are not game
-- accounts; the link is an explicit binding row. Only a hashed secret is stored; the
-- plaintext secret is never written. Revocation is a timestamp, not a silent delete.
CREATE TABLE IF NOT EXISTS agent_tenants (
    tenant_id text PRIMARY KEY,
    created_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz NULL
);

CREATE TABLE IF NOT EXISTS agent_principals (
    principal_id text PRIMARY KEY,
    tenant_id text NOT NULL REFERENCES agent_tenants (tenant_id),
    created_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz NULL
);

CREATE INDEX IF NOT EXISTS agent_principals_tenant_idx
    ON agent_principals (tenant_id);

CREATE TABLE IF NOT EXISTS agent_agents (
    agent_id text PRIMARY KEY,
    principal_id text NOT NULL REFERENCES agent_principals (principal_id),
    tenant_id text NOT NULL REFERENCES agent_tenants (tenant_id),
    created_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz NULL
);

CREATE INDEX IF NOT EXISTS agent_agents_principal_idx
    ON agent_agents (principal_id);

CREATE TABLE IF NOT EXISTS agent_scopes (
    agent_id text NOT NULL REFERENCES agent_agents (agent_id),
    scope text NOT NULL,
    PRIMARY KEY (agent_id, scope),
    CHECK (scope IN (
        'world.read',
        'navigation.execute',
        'combat.execute',
        'quest.read',
        'inventory.mutate',
        'billing.read'
    ))
);

CREATE TABLE IF NOT EXISTS agent_character_bindings (
    agent_id text NOT NULL REFERENCES agent_agents (agent_id),
    principal_id text NOT NULL REFERENCES agent_principals (principal_id),
    tenant_id text NOT NULL REFERENCES agent_tenants (tenant_id),
    game_account text NOT NULL,
    character_id text NOT NULL,
    bound_at timestamptz NOT NULL DEFAULT now(),
    unbound_at timestamptz NULL,
    PRIMARY KEY (agent_id, character_id)
);

-- A character may have only one active Agent binding.
CREATE UNIQUE INDEX IF NOT EXISTS agent_character_bindings_active_character_uidx
    ON agent_character_bindings (character_id)
    WHERE unbound_at IS NULL;

CREATE INDEX IF NOT EXISTS agent_character_bindings_agent_idx
    ON agent_character_bindings (agent_id)
    WHERE unbound_at IS NULL;

CREATE TABLE IF NOT EXISTS agent_credentials (
    credential_id text PRIMARY KEY,
    agent_id text NOT NULL REFERENCES agent_agents (agent_id),
    principal_id text NOT NULL REFERENCES agent_principals (principal_id),
    tenant_id text NOT NULL REFERENCES agent_tenants (tenant_id),
    secret_hash text NOT NULL,
    secret_salt text NOT NULL,
    iterations int NOT NULL CHECK (iterations >= 10000 AND iterations <= 200000),
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz NULL
);

CREATE INDEX IF NOT EXISTS agent_credentials_agent_idx
    ON agent_credentials (agent_id);

CREATE TABLE IF NOT EXISTS agent_revocation_log (
    id bigserial PRIMARY KEY,
    at timestamptz NOT NULL DEFAULT now(),
    tenant_id text NOT NULL,
    principal_id text NULL,
    agent_id text NULL,
    credential_id text NULL,
    reason text NOT NULL,
    policy text NOT NULL
);
