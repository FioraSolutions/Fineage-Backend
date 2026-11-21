-- Player language. One tag per account (the client sends it at login / enter).
-- Old rows and old clients stay on English.

ALTER TABLE accounts ADD COLUMN IF NOT EXISTS language text NOT NULL DEFAULT 'en';
