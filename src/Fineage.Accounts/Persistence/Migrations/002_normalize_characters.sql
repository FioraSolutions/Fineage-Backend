-- Normalized character / item / quest tables (Mobius-shaped).
-- After backfill, characters columns + character_items / character_quests are authoritative for the character domain.
-- store_snapshot.payload remains an export/backup document.

ALTER TABLE characters ADD COLUMN IF NOT EXISTS hair_style int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS hair_color int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS face int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS has_position boolean NOT NULL DEFAULT false;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS x int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS y int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS z int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS heading int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS exp bigint NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS sp int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS title text NOT NULL DEFAULT '';
ALTER TABLE characters ADD COLUMN IF NOT EXISTS newbie boolean NOT NULL DEFAULT true;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS max_hp int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS hp int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS max_mp int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS mp int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS clan_id int NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS clan_join_expiry_unix_ms bigint NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS clan_create_expiry_unix_ms bigint NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE characters ADD COLUMN IF NOT EXISTS last_access timestamptz NULL;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS delete_at timestamptz NULL;

CREATE INDEX IF NOT EXISTS characters_account_idx ON characters (account);
CREATE INDEX IF NOT EXISTS characters_clan_id_idx ON characters (clan_id) WHERE clan_id <> 0;

CREATE SEQUENCE IF NOT EXISTS character_id_seq AS int START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE IF NOT EXISTS clan_id_seq AS int START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE IF NOT EXISTS item_object_id_seq AS bigint START WITH 100000 INCREMENT BY 1;

CREATE TABLE IF NOT EXISTS character_items (
    object_id bigint PRIMARY KEY,
    character_id int NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    item_id int NOT NULL,
    count int NOT NULL CHECK (count >= 1),
    enchant_level int NOT NULL DEFAULT 0 CHECK (enchant_level >= 0),
    loc text NOT NULL CHECK (loc IN ('inventory', 'paperdoll', 'warehouse')),
    loc_data int NOT NULL DEFAULT 0,
    mana_left int NULL,
    time_ms bigint NULL
);

CREATE INDEX IF NOT EXISTS character_items_character_loc_idx
    ON character_items (character_id, loc);

CREATE UNIQUE INDEX IF NOT EXISTS character_items_paperdoll_uidx
    ON character_items (character_id, loc_data)
    WHERE loc = 'paperdoll' AND loc_data >= 0;

CREATE TABLE IF NOT EXISTS character_quests (
    character_id int NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    quest_id int NOT NULL,
    state int NOT NULL,
    PRIMARY KEY (character_id, quest_id)
);

CREATE TABLE IF NOT EXISTS character_quest_vars (
    character_id int NOT NULL,
    quest_id int NOT NULL,
    var_key text NOT NULL,
    var_value text NOT NULL DEFAULT '',
    PRIMARY KEY (character_id, quest_id, var_key),
    FOREIGN KEY (character_id, quest_id)
        REFERENCES character_quests (character_id, quest_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS store_meta (
    key text PRIMARY KEY,
    value_bigint bigint NOT NULL
);

INSERT INTO store_meta (key, value_bigint) VALUES ('next_character_id', 1)
ON CONFLICT (key) DO NOTHING;
INSERT INTO store_meta (key, value_bigint) VALUES ('next_clan_id', 1)
ON CONFLICT (key) DO NOTHING;
INSERT INTO store_meta (key, value_bigint) VALUES ('next_item_object_id', 100000)
ON CONFLICT (key) DO NOTHING;

-- Backfill character columns from legacy payload jsonb when present.
UPDATE characters SET
    hair_style = COALESCE((payload->>'hairStyle')::int, hair_style),
    hair_color = COALESCE((payload->>'hairColor')::int, hair_color),
    face = COALESCE((payload->>'face')::int, face),
    has_position = COALESCE((payload->>'hasPosition')::boolean, has_position),
    x = COALESCE((payload->>'x')::int, x),
    y = COALESCE((payload->>'y')::int, y),
    z = COALESCE((payload->>'z')::int, z),
    level = COALESCE((payload->>'level')::int, level),
    exp = COALESCE((payload->>'exp')::bigint, exp),
    sp = COALESCE((payload->>'sp')::int, sp),
    title = COALESCE(payload->>'title', title),
    newbie = COALESCE((payload->>'newbie')::boolean, newbie),
    max_hp = COALESCE((payload->>'maxHp')::int, max_hp),
    hp = COALESCE((payload->>'hp')::int, hp),
    max_mp = COALESCE((payload->>'maxMp')::int, max_mp),
    mp = COALESCE((payload->>'mp')::int, mp),
    clan_id = COALESCE((payload->>'clanId')::int, clan_id),
    clan_join_expiry_unix_ms = COALESCE((payload->>'clanJoinExpiryUnixMs')::bigint, clan_join_expiry_unix_ms),
    clan_create_expiry_unix_ms = COALESCE((payload->>'clanCreateExpiryUnixMs')::bigint, clan_create_expiry_unix_ms),
    created_at = COALESCE((payload->>'createdAt')::timestamptz, created_at),
    last_access = COALESCE((payload->>'lastAccess')::timestamptz, last_access),
    delete_at = COALESCE((payload->>'deleteAt')::timestamptz, delete_at)
WHERE payload IS NOT NULL AND payload <> '{}'::jsonb;

-- Inventory / paperdoll from payload.items (skip if character_items already populated).
INSERT INTO character_items (object_id, character_id, item_id, count, enchant_level, loc, loc_data)
SELECT
    100000 + (ROW_NUMBER() OVER (ORDER BY c.id, ord))::bigint,
    c.id,
    COALESCE((item->>'itemId')::int, 0),
    GREATEST(COALESCE((item->>'count')::int, 1), 1),
    COALESCE((item->>'enchantLevel')::int, 0),
    CASE
        WHEN COALESCE((item->>'equippedSlot')::int, -1) >= 0 THEN 'paperdoll'
        ELSE 'inventory'
    END,
    CASE
        WHEN COALESCE((item->>'equippedSlot')::int, -1) >= 0 THEN (item->>'equippedSlot')::int
        ELSE 0
    END
FROM characters c
CROSS JOIN LATERAL jsonb_array_elements(COALESCE(c.payload->'items', '[]'::jsonb))
    WITH ORDINALITY AS t(item, ord)
WHERE NOT EXISTS (SELECT 1 FROM character_items ci WHERE ci.character_id = c.id)
  AND COALESCE((item->>'itemId')::int, 0) > 0;

-- Warehouse from payload.warehouse.
INSERT INTO character_items (object_id, character_id, item_id, count, enchant_level, loc, loc_data)
SELECT
    2000000 + (ROW_NUMBER() OVER (ORDER BY c.id, ord))::bigint,
    c.id,
    COALESCE((item->>'itemId')::int, 0),
    GREATEST(COALESCE((item->>'count')::int, 1), 1),
    COALESCE((item->>'enchantLevel')::int, 0),
    'warehouse',
    0
FROM characters c
CROSS JOIN LATERAL jsonb_array_elements(COALESCE(c.payload->'warehouse', '[]'::jsonb))
    WITH ORDINALITY AS t(item, ord)
WHERE NOT EXISTS (
        SELECT 1 FROM character_items ci
        WHERE ci.character_id = c.id AND ci.loc = 'warehouse')
  AND COALESCE((item->>'itemId')::int, 0) > 0;

-- Quests from payload.quests.
INSERT INTO character_quests (character_id, quest_id, state)
SELECT
    c.id,
    COALESCE((q->>'questId')::int, 0),
    COALESCE((q->>'state')::int, 0)
FROM characters c
CROSS JOIN LATERAL jsonb_array_elements(COALESCE(c.payload->'quests', '[]'::jsonb)) AS q
WHERE NOT EXISTS (SELECT 1 FROM character_quests cq WHERE cq.character_id = c.id)
  AND COALESCE((q->>'questId')::int, 0) > 0
ON CONFLICT DO NOTHING;

INSERT INTO character_quest_vars (character_id, quest_id, var_key, var_value)
SELECT
    c.id,
    COALESCE((q->>'questId')::int, 0),
    COALESCE(v->>'key', ''),
    COALESCE(v->>'value', '')
FROM characters c
CROSS JOIN LATERAL jsonb_array_elements(COALESCE(c.payload->'quests', '[]'::jsonb)) AS q
CROSS JOIN LATERAL jsonb_array_elements(COALESCE(q->'vars', '[]'::jsonb)) AS v
WHERE COALESCE((q->>'questId')::int, 0) > 0
  AND COALESCE(v->>'key', '') <> ''
ON CONFLICT DO NOTHING;

SELECT setval(
    'character_id_seq',
    GREATEST(1, (SELECT COALESCE(MAX(id), 0) FROM characters)));

SELECT setval(
    'clan_id_seq',
    GREATEST(1, (SELECT COALESCE(MAX(id), 0) FROM clans)));

SELECT setval(
    'item_object_id_seq',
    GREATEST(100000, (SELECT COALESCE(MAX(object_id), 99999) FROM character_items)));

UPDATE store_meta SET value_bigint = (SELECT last_value FROM character_id_seq) + 1
WHERE key = 'next_character_id';
UPDATE store_meta SET value_bigint = (SELECT last_value FROM clan_id_seq) + 1
WHERE key = 'next_clan_id';
UPDATE store_meta SET value_bigint = (SELECT last_value FROM item_object_id_seq) + 1
WHERE key = 'next_item_object_id';
