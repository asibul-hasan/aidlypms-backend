-- ============================================================================
-- Migration: 20261022_07_soft_delete_and_subscription.sql
-- Description:
--   1. Adds soft-delete tracking columns (deleted_at, deleted_by, deletion_reason)
--      to prod_products, cust_customers, prod_companies, prod_generics, supp_suppliers.
--   2. Adds grace_period_days to sub_pharmacy_subscriptions.
--   3. Creates sequences for subscription identifiers: seq_subscription_payment_no,
--      seq_subscription_no.
-- ============================================================================

-- 1. Soft-Delete Columns
ALTER TABLE prod_companies
    ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMP WITH TIME ZONE,
    ADD COLUMN IF NOT EXISTS deleted_by BIGINT,
    ADD COLUMN IF NOT EXISTS deletion_reason TEXT;

ALTER TABLE prod_generics
    ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMP WITH TIME ZONE,
    ADD COLUMN IF NOT EXISTS deleted_by BIGINT,
    ADD COLUMN IF NOT EXISTS deletion_reason TEXT;

ALTER TABLE prod_products
    ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMP WITH TIME ZONE,
    ADD COLUMN IF NOT EXISTS deleted_by BIGINT,
    ADD COLUMN IF NOT EXISTS deletion_reason TEXT;

ALTER TABLE cust_customers
    ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMP WITH TIME ZONE,
    ADD COLUMN IF NOT EXISTS deleted_by BIGINT,
    ADD COLUMN IF NOT EXISTS deletion_reason TEXT;

ALTER TABLE supp_suppliers
    ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMP WITH TIME ZONE,
    ADD COLUMN IF NOT EXISTS deleted_by BIGINT,
    ADD COLUMN IF NOT EXISTS deletion_reason TEXT;

-- 2. Subscription Grace Period
ALTER TABLE sub_pharmacy_subscriptions
    ADD COLUMN IF NOT EXISTS grace_period_days INT;

-- 3. Subscription Sequences
CREATE SEQUENCE IF NOT EXISTS seq_subscription_payment_no;
SELECT setval('seq_subscription_payment_no', GREATEST(COALESCE((SELECT MAX(subscription_payment_no) FROM sub_subscription_payments), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_subscription_no;
SELECT setval('seq_subscription_no', GREATEST(COALESCE((SELECT MAX(subscription_no) FROM sub_pharmacy_subscriptions), 0) + 1, 1), false);
