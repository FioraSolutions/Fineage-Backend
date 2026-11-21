-- Freight warehouse rows (Mobius ItemLocation.FREIGHT) alongside inventory / private warehouse.
ALTER TABLE character_items DROP CONSTRAINT IF EXISTS character_items_loc_check;
ALTER TABLE character_items ADD CONSTRAINT character_items_loc_check
    CHECK (loc IN ('inventory', 'paperdoll', 'warehouse', 'freight'));