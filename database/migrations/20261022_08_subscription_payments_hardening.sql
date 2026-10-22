-- ============================================================================
-- Migration: 20261022_08_subscription_payments_hardening.sql
-- Description:
--   1. Adds billing_cycle and plan_no to sub_subscription_payments to store
--      duration and cycle directly on payment records.
--   2. Adds lookup index on transaction_reference for gateway callbacks.
-- ============================================================================

ALTER TABLE sub_subscription_payments
    ADD COLUMN IF NOT EXISTS billing_cycle SMALLINT,
    ADD COLUMN IF NOT EXISTS plan_no BIGINT;

CREATE INDEX IF NOT EXISTS idx_sub_payments_trx_ref
    ON sub_subscription_payments(transaction_reference);
