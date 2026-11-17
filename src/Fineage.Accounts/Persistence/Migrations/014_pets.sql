-- Block P2 pets: Mobius `pets` (item_obj_id, name, level, curHp, curMp, exp, sp, fed, ownerId) keyed by the collar's
-- object id, plus the pet inventory (Mobius items with loc PET) as a jsonb payload. Postgres is the source of truth
-- for pets (FindPet reads this table); the snapshot keeps a copy for memory mode and bootstrap.
CREATE TABLE IF NOT EXISTS pets (
    control_object_id int PRIMARY KEY,
    owner_character_id int NOT NULL DEFAULT 0,
    npc_id int NOT NULL,
    name text NOT NULL DEFAULT '',
    level int NOT NULL DEFAULT 1,
    exp bigint NOT NULL DEFAULT 0,
    cur_hp int NOT NULL DEFAULT 0,
    cur_mp int NOT NULL DEFAULT 0,
    fed int NOT NULL DEFAULT 0,
    items jsonb NOT NULL DEFAULT '[]'::jsonb,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS pets_owner_idx ON pets (owner_character_id);
