-- Mobius characters.death_penalty_level (Player.java:337 UPDATE_CHARACTER, :6578 setDeathPenaltyBuffLevel).
-- Set by Player.increaseDeathPenaltyBuffLevel on a successful death penalty roll and lowered by the recovery rules,
-- so it has to survive logout. Default 0 == no penalty, exactly like a fresh Mobius character.
ALTER TABLE characters ADD COLUMN IF NOT EXISTS death_penalty_level int NOT NULL DEFAULT 0;
