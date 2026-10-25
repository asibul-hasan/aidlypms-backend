-- ============================================================================
-- Aidly PMS Database Maintenance Script
-- File: database/maintenance/20261024_stock_ledger_repair.sql
--
-- PURPOSE:
-- 1. Read-only diagnostic of batches whose total_quantity_pcs differs from
--    the sum of inv_stock_movement.qty_pcs.
-- 2. Repair that sets total_quantity_pcs and box_quantity to the ledger sum
--    for batches that have ledger rows and a non-negative sum.
-- 3. Opening-movement insert for imported batches with no ledger rows.
--
-- SAFETY INSTRUCTIONS:
-- - This script is deliberately placed in database/maintenance/ (NOT in migrations/)
--   so that it NEVER runs automatically during CI or application startup.
-- - Each part is strictly isolated in its own BEGIN block.
-- - The default terminating statement for every block is ROLLBACK.
-- - To apply changes permanently in a production environment, inspect the
--   diagnostic outputs, change 'ROLLBACK;' to 'COMMIT;' at the end of the
--   desired section, and execute under DBA supervision.
-- ============================================================================


-- ============================================================================
-- PART 1: READ-ONLY DIAGNOSTIC
-- Inspect batches where prod_product_batches.total_quantity_pcs differs from
-- the sum of physical ledger movements in inv_stock_movement.
-- ============================================================================
BEGIN;

SELECT 
    b.batch_no,
    b.pharmacy_no,
    b.branch_no,
    b.product_no,
    p.product_name,
    b.batch_number,
    b.is_placeholder,
    b.total_quantity_pcs AS current_batch_qty,
    COALESCE(m.sum_qty, 0) AS ledger_sum_qty,
    (b.total_quantity_pcs - COALESCE(m.sum_qty, 0)) AS discrepancy_pcs,
    COALESCE(m.movement_count, 0) AS movement_count,
    CASE 
        WHEN m.batch_no IS NULL THEN 'NO_MOVEMENTS (Requires Opening Movement)'
        WHEN m.sum_qty < 0 THEN 'NEGATIVE_LEDGER_SUM (Requires Manual Investigation)'
        ELSE 'OUT_OF_SYNC (Repairable via Ledger Sum)'
    END AS diagnostic_status
FROM prod_product_batches b
JOIN prod_products p ON b.product_no = p.product_no
LEFT JOIN (
    SELECT 
        batch_no, 
        SUM(qty_pcs) AS sum_qty,
        COUNT(movement_no) AS movement_count
    FROM inv_stock_movement
    WHERE batch_no IS NOT NULL
    GROUP BY batch_no
) m ON b.batch_no = m.batch_no
WHERE b.total_quantity_pcs != COALESCE(m.sum_qty, 0)
ORDER BY b.batch_no;

-- Part 1 is purely diagnostic. Rollback cleanly closes the read transaction.
ROLLBACK;


-- ============================================================================
-- PART 2: REPAIR BATCHES WITH LEDGER ROWS AND NON-NEGATIVE SUM
-- For batches that have at least one ledger movement row and where the sum of
-- movements is >= 0, update total_quantity_pcs and box_quantity to match
-- the ledger sum.
-- ============================================================================
BEGIN;

UPDATE prod_product_batches b
SET 
    total_quantity_pcs = m.sum_qty,
    box_quantity = m.sum_qty / (CASE WHEN b.quantity_in_box > 0 THEN b.quantity_in_box ELSE 1 END),
    updated_at = NOW()
FROM (
    SELECT 
        batch_no,
        SUM(qty_pcs)::int AS sum_qty
    FROM inv_stock_movement
    WHERE batch_no IS NOT NULL
    GROUP BY batch_no
    HAVING SUM(qty_pcs) >= 0
) m
WHERE b.batch_no = m.batch_no
  AND b.total_quantity_pcs != m.sum_qty
  AND b.batch_number IN (
      '' /* Template: list specific batch numbers here (e.g. 'BATCH-001', 'BATCH-002'). Empty by default so unedited execution affects 0 rows. */
  );

-- Verification query to review updated rows before committing:
-- SELECT b.batch_no, b.batch_number, b.total_quantity_pcs, m.sum_qty
-- FROM prod_product_batches b
-- JOIN (SELECT batch_no, SUM(qty_pcs) AS sum_qty FROM inv_stock_movement GROUP BY batch_no) m ON b.batch_no = m.batch_no
-- WHERE b.total_quantity_pcs != m.sum_qty;

-- Default is ROLLBACK for safety.
-- To apply permanently: comment out 'ROLLBACK;' and uncomment 'COMMIT;'.
ROLLBACK;
-- COMMIT;


-- ============================================================================
-- PART 3: OPENING-MOVEMENT INSERT FOR IMPORT BATCHES WITH NO LEDGER ROWS
-- For batches imported or initialized without corresponding rows in
-- inv_stock_movement, insert an opening stock movement record so physical
-- ledger integrity is established and maintained.
-- ============================================================================
BEGIN;

INSERT INTO inv_stock_movement (
    movement_no,
    pharmacy_no,
    branch_no,
    product_no,
    batch_no,
    movement_type,
    reference_doc_type,
    reference_doc_no,
    reference_doc_id,
    qty_pcs,
    unit_cost,
    balance_after_pcs,
    remarks,
    created_by_user_no,
    created_at
)
SELECT 
    nextval('seq_stock_movement_no'),
    b.pharmacy_no,
    b.branch_no,
    b.product_no,
    b.batch_no,
    1, -- MovementType 1: Opening Stock
    'OPENING_STOCK',
    'OP-IMPORT-' || b.batch_no,
    b.batch_no,
    b.total_quantity_pcs,
    COALESCE(p.purchase_price_per_piece, CASE WHEN b.quantity_in_box > 0 THEN b.cost_per_box / b.quantity_in_box ELSE 0 END, 0),
    b.total_quantity_pcs,
    'Opening stock ledger repair/initialization for batch ' || b.batch_number,
    NULL,
    NOW()
FROM prod_product_batches b
JOIN prod_products p ON b.product_no = p.product_no
WHERE b.is_placeholder = FALSE
  AND NOT EXISTS (
      SELECT 1 FROM inv_stock_movement m
      WHERE m.batch_no = b.batch_no
  );

-- Verification query to verify all non-placeholder batches now have movements:
-- SELECT COUNT(*) AS orphan_batches_without_movements
-- FROM prod_product_batches b
-- WHERE b.is_placeholder = FALSE
--   AND NOT EXISTS (SELECT 1 FROM inv_stock_movement m WHERE m.batch_no = b.batch_no);

-- Default is ROLLBACK for safety.
-- To apply permanently: comment out 'ROLLBACK;' and uncomment 'COMMIT;'.
ROLLBACK;
-- COMMIT;


-- ============================================================================
-- PART 4: DIFFERENTIAL OPENING-MOVEMENT INSERT FOR PARTIALLY TRACKED BATCHES
-- For batches whose ledger sum is lower than the batch quantity (total_quantity_pcs)
-- and that have no opening or purchase movement recorded in inv_stock_movement,
-- insert an opening movement for the differential: (batch quantity - ledger sum).
-- ============================================================================
BEGIN;

INSERT INTO inv_stock_movement (
    movement_no,
    pharmacy_no,
    branch_no,
    product_no,
    batch_no,
    movement_type,
    reference_doc_type,
    reference_doc_no,
    reference_doc_id,
    qty_pcs,
    unit_cost,
    balance_after_pcs,
    remarks,
    created_by_user_no,
    created_at
)
SELECT 
    nextval('seq_stock_movement_no'),
    b.pharmacy_no,
    b.branch_no,
    b.product_no,
    b.batch_no,
    1, -- MovementType 1: Opening Stock
    'OPENING_STOCK',
    'OP-DIFF-' || b.batch_no,
    b.batch_no,
    (b.total_quantity_pcs - COALESCE(m.sum_qty, 0)),
    COALESCE(p.purchase_price_per_piece, CASE WHEN b.quantity_in_box > 0 THEN b.cost_per_box / b.quantity_in_box ELSE 0 END, 0),
    b.total_quantity_pcs,
    'Opening stock differential repair for batch ' || b.batch_number,
    NULL,
    NOW()
FROM prod_product_batches b
JOIN prod_products p ON b.product_no = p.product_no
LEFT JOIN (
    SELECT 
        batch_no,
        SUM(qty_pcs) AS sum_qty
    FROM inv_stock_movement
    WHERE batch_no IS NOT NULL
    GROUP BY batch_no
) m ON b.batch_no = m.batch_no
WHERE b.is_placeholder = FALSE
  AND b.total_quantity_pcs > COALESCE(m.sum_qty, 0)
  AND NOT EXISTS (
      SELECT 1 FROM inv_stock_movement sm
      WHERE sm.batch_no = b.batch_no
        AND (sm.movement_type IN (1, 2) OR sm.reference_doc_type IN ('OPENING_STOCK', 'PUR_INVOICE', 'PURCHASE'))
  );

-- Verification query to verify differential batches:
-- SELECT b.batch_no, b.batch_number, b.total_quantity_pcs, COALESCE(m.sum_qty, 0) AS current_ledger_sum
-- FROM prod_product_batches b
-- LEFT JOIN (SELECT batch_no, SUM(qty_pcs) AS sum_qty FROM inv_stock_movement GROUP BY batch_no) m ON b.batch_no = m.batch_no
-- WHERE b.is_placeholder = FALSE AND b.total_quantity_pcs > COALESCE(m.sum_qty, 0);

-- Default is ROLLBACK for safety.
-- To apply permanently: comment out 'ROLLBACK;' and uncomment 'COMMIT;'.
ROLLBACK;
-- COMMIT;
