-- ============================================================================
-- Migration: 20261024_09_unreceived_stock_and_stocktake.sql
-- Description:
--   1. Adds is_placeholder to prod_product_batches (backfilling existing
--      batches to false, renaming legacy PRE-PURCHASE to UNRECEIVED, NOT NULL).
--   2. Adds sequences and tables for cycle counting (inv_stocktakes,
--      inv_stocktake_items) with parent-child integrity and lookup indexes.
-- ============================================================================

-- 1. Unreceived Stock Placeholder on prod_product_batches
ALTER TABLE prod_product_batches ADD COLUMN IF NOT EXISTS is_placeholder BOOLEAN;
UPDATE prod_product_batches SET is_placeholder = FALSE WHERE is_placeholder IS NULL;
UPDATE prod_product_batches SET is_placeholder = TRUE, batch_number = 'UNRECEIVED' WHERE batch_number ILIKE '%PRE-PURCHASE%' OR expiry_date >= (CURRENT_DATE + INTERVAL '50 years');
ALTER TABLE prod_product_batches ALTER COLUMN is_placeholder SET NOT NULL;

-- 2. Sequences for Stock-take (Cycle Count)
CREATE SEQUENCE IF NOT EXISTS seq_stocktake_no START WITH 1001 INCREMENT BY 1;
CREATE SEQUENCE IF NOT EXISTS seq_stocktake_item_no START WITH 1001 INCREMENT BY 1;

-- 3. Stock-take Header Table
CREATE TABLE IF NOT EXISTS inv_stocktakes (
    stocktake_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    status VARCHAR(50) NOT NULL,
    note TEXT,
    started_at TIMESTAMP WITH TIME ZONE NOT NULL,
    started_by BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    posted_at TIMESTAMP WITH TIME ZONE,
    posted_by BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL
);

-- 4. Stock-take Item / Snapshot Lines Table
CREATE TABLE IF NOT EXISTS inv_stocktake_items (
    stocktake_item_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    stocktake_no BIGINT NOT NULL REFERENCES inv_stocktakes(stocktake_no) ON DELETE CASCADE,
    product_no BIGINT NOT NULL REFERENCES prod_products(product_no) ON DELETE RESTRICT,
    batch_no BIGINT REFERENCES prod_product_batches(batch_no) ON DELETE RESTRICT,
    system_qty_pcs INT NOT NULL,
    counted_qty_pcs INT,
    variance_pcs INT,
    reason TEXT
);

-- 5. Indexes for Performance & Scoped Lookups
CREATE INDEX IF NOT EXISTS idx_stocktakes_pharmacy_branch ON inv_stocktakes(pharmacy_no, branch_no);
CREATE INDEX IF NOT EXISTS idx_stocktake_items_parent ON inv_stocktake_items(stocktake_no);
CREATE INDEX IF NOT EXISTS idx_stocktake_items_batch ON inv_stocktake_items(batch_no);
CREATE INDEX IF NOT EXISTS idx_prod_batches_placeholder ON prod_product_batches(pharmacy_no, branch_no, product_no, is_placeholder);
