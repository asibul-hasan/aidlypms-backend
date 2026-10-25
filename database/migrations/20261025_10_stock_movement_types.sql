-- ============================================================================
-- Migration: 20261025_10_stock_movement_types.sql
-- Description:
--   Standardizes universal stock movement types in inv_stock_movement to canonical
--   discrete codes (1..11) mapped from reference_doc_type:
--     1: Opening (OPENING_STOCK, or purchase invoice with opening remarks)
--     2: Purchase Receipt (PUR_INVOICE, PURCHASE)
--     3: POS Sale (SALE_INVOICE)
--     4: Customer Return (SALE_RETURN)
--     5: Void Restock (SALE_VOID)
--     6: Supplier Return (PUR_RETURN)
--     7: Adjustment (INV_ADJUSTMENT, STOCK_ADJUSTMENT, ADJUSTMENT)
--     8: Stock-Take (STOCKTAKE)
--     9: Transfer Out (INV_TRANSFER)
--    10: Transfer In (INV_TRANSFER_RCV)
--    11: Settlement (SETTLEMENT)
-- ============================================================================

-- 1. Opening Stock (reference_doc_type = 'OPENING_STOCK' or initial/opening purchases)
UPDATE inv_stock_movement
SET movement_type = 1
WHERE reference_doc_type = 'OPENING_STOCK'
   OR (reference_doc_type IN ('PUR_INVOICE', 'PURCHASE') AND (remarks ILIKE '%opening%' OR remarks ILIKE '%initial%'));

-- 2. Purchase Receipt (standard purchase invoices)
UPDATE inv_stock_movement
SET movement_type = 2
WHERE reference_doc_type IN ('PUR_INVOICE', 'PURCHASE')
  AND NOT (remarks ILIKE '%opening%' OR remarks ILIKE '%initial%');

-- 3. POS Sale
UPDATE inv_stock_movement
SET movement_type = 3
WHERE reference_doc_type = 'SALE_INVOICE';

-- 4. Customer Return
UPDATE inv_stock_movement
SET movement_type = 4
WHERE reference_doc_type = 'SALE_RETURN';

-- 5. Void Restock
UPDATE inv_stock_movement
SET movement_type = 5
WHERE reference_doc_type = 'SALE_VOID';

-- 6. Supplier Return
UPDATE inv_stock_movement
SET movement_type = 6
WHERE reference_doc_type = 'PUR_RETURN';

-- 7. Inventory Adjustment
UPDATE inv_stock_movement
SET movement_type = 7
WHERE reference_doc_type IN ('INV_ADJUSTMENT', 'STOCK_ADJUSTMENT', 'ADJUSTMENT');

-- 8. Stock-Take (Cycle Count Variance)
UPDATE inv_stock_movement
SET movement_type = 8
WHERE reference_doc_type = 'STOCKTAKE';

-- 9. Inter-Branch Transfer Out
UPDATE inv_stock_movement
SET movement_type = 9
WHERE reference_doc_type = 'INV_TRANSFER';

-- 10. Inter-Branch Transfer In
UPDATE inv_stock_movement
SET movement_type = 10
WHERE reference_doc_type = 'INV_TRANSFER_RCV';

-- 11. Unreceived Stock Placeholder Settlement
UPDATE inv_stock_movement
SET movement_type = 11
WHERE reference_doc_type = 'SETTLEMENT';
