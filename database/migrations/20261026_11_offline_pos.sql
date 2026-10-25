-- ============================================================================
-- Migration: 20261026_11_offline_pos.sql
-- Description:
--   1. Adds offline sale tracking, idempotency key and price variance columns
--      to sale_invoices.
--   2. Adds unique index on (pharmacy_no, branch_no, idempotency_key).
--   3. Adds offline POS toggle and max age configuration to cfg_sale_configs.
--   4. Standard zero-default migration: adds columns as nullable, backfills,
--      then applies NOT NULL constraints where mandated.
-- ============================================================================

-- 1. Sale Invoices: Offline POS & Idempotency Columns
ALTER TABLE sale_invoices ADD COLUMN IF NOT EXISTS is_offline BOOLEAN;
UPDATE sale_invoices SET is_offline = FALSE WHERE is_offline IS NULL;
ALTER TABLE sale_invoices ALTER COLUMN is_offline SET NOT NULL;

ALTER TABLE sale_invoices ADD COLUMN IF NOT EXISTS client_created_at TIMESTAMP WITH TIME ZONE;
ALTER TABLE sale_invoices ADD COLUMN IF NOT EXISTS offline_receipt_no VARCHAR(100);
ALTER TABLE sale_invoices ADD COLUMN IF NOT EXISTS offline_client_total NUMERIC(18, 4);
ALTER TABLE sale_invoices ADD COLUMN IF NOT EXISTS price_variance NUMERIC(18, 4);

ALTER TABLE sale_invoices ADD COLUMN IF NOT EXISTS late_sync BOOLEAN;
UPDATE sale_invoices SET late_sync = FALSE WHERE late_sync IS NULL;
ALTER TABLE sale_invoices ALTER COLUMN late_sync SET NOT NULL;

ALTER TABLE sale_invoices ADD COLUMN IF NOT EXISTS idempotency_key UUID;
ALTER TABLE sale_invoices ADD COLUMN IF NOT EXISTS idempotency_payload_hash VARCHAR(64);

-- Unique index for idempotent sale deduplication
CREATE UNIQUE INDEX IF NOT EXISTS uq_sale_invoices_idempotency
ON sale_invoices (pharmacy_no, branch_no, idempotency_key)
WHERE idempotency_key IS NOT NULL;

-- Filter index for offline sales review reporting
CREATE INDEX IF NOT EXISTS idx_sale_invoices_offline_review
ON sale_invoices (pharmacy_no, branch_no, is_offline, sale_timestamp)
WHERE is_offline = TRUE;


-- 2. POS Sales Configuration: Offline Mode Switches
ALTER TABLE cfg_sale_configs ADD COLUMN IF NOT EXISTS offline_pos_enabled BOOLEAN;
UPDATE cfg_sale_configs SET offline_pos_enabled = FALSE WHERE offline_pos_enabled IS NULL;
ALTER TABLE cfg_sale_configs ALTER COLUMN offline_pos_enabled SET NOT NULL;

ALTER TABLE cfg_sale_configs ADD COLUMN IF NOT EXISTS offline_max_age_hours INT;
UPDATE cfg_sale_configs SET offline_max_age_hours = 72 WHERE offline_max_age_hours IS NULL;
ALTER TABLE cfg_sale_configs ALTER COLUMN offline_max_age_hours SET NOT NULL;
