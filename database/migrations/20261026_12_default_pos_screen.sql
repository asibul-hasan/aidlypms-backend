-- ============================================================================
-- Migration: 20261026_12_default_pos_screen.sql
-- Description:
--   1. Adds default_pos_screen column to cfg_sale_configs to allow configuring
--      the active POS interface screen (screen1, screen2, screen3) per branch.
--   2. Standard zero-default migration: adds column as nullable, backfills,
--      then applies NOT NULL constraint.
-- ============================================================================

ALTER TABLE cfg_sale_configs ADD COLUMN IF NOT EXISTS default_pos_screen VARCHAR(20);
UPDATE cfg_sale_configs SET default_pos_screen = 'screen1' WHERE default_pos_screen IS NULL;
ALTER TABLE cfg_sale_configs ALTER COLUMN default_pos_screen SET NOT NULL;
