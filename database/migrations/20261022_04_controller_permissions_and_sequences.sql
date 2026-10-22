-- ============================================================================
-- Migration: 20261022_controller_permissions_and_sequences.sql
-- Description:
--   1. Create Postgres sequences for monotonic, concurrency-safe IDs:
--      - seq_sale_invoice_no
--      - seq_sale_invoice_item_no
--      - seq_sale_return_no
--      - seq_sale_return_item_no
--      - seq_sale_invoice_payment_no
--      - seq_purchase_invoice_no
--      - seq_purchase_invoice_item_no
--      - seq_ledger_no
--      - seq_journal_no
--      - seq_stock_movement_no
--   2. Grant module permissions to roles holding finer permissions so existing
--      users lose nothing.
-- ============================================================================

-- 1. Sequences initialization
CREATE SEQUENCE IF NOT EXISTS seq_sale_invoice_no;
SELECT setval('seq_sale_invoice_no', GREATEST(COALESCE((SELECT MAX(sale_invoice_no) FROM sale_invoices), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_sale_invoice_item_no;
SELECT setval('seq_sale_invoice_item_no', GREATEST(COALESCE((SELECT MAX(sale_invoice_item_no) FROM sale_invoice_items), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_sale_return_no;
SELECT setval('seq_sale_return_no', GREATEST(COALESCE((SELECT MAX(sale_return_no) FROM sale_returns), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_sale_return_item_no;
SELECT setval('seq_sale_return_item_no', GREATEST(COALESCE((SELECT MAX(sale_return_item_no) FROM sale_return_items), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_sale_invoice_payment_no;
SELECT setval('seq_sale_invoice_payment_no', GREATEST(COALESCE((SELECT MAX(sale_payment_no) FROM sale_invoice_payments), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_purchase_invoice_no;
SELECT setval('seq_purchase_invoice_no', GREATEST(COALESCE((SELECT MAX(purchase_invoice_no) FROM pur_purchase_invoices), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_purchase_invoice_item_no;
SELECT setval('seq_purchase_invoice_item_no', GREATEST(COALESCE((SELECT MAX(purchase_item_no) FROM pur_purchase_invoice_items), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_ledger_no;
SELECT setval('seq_ledger_no', GREATEST(COALESCE((SELECT MAX(ledger_no) FROM acc_transaction_ledger), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_journal_no;
SELECT setval('seq_journal_no', GREATEST(COALESCE((SELECT MAX(journal_no) FROM acc_transaction_ledger), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_stock_movement_no;
SELECT setval('seq_stock_movement_no', GREATEST(COALESCE((SELECT MAX(movement_no) FROM inv_stock_movement), 0) + 1, 1), false);

-- 2. Grant module permissions to roles holding finer permissions
-- Ensure Role 9 (Manager) has all permissions across all existing pharmacies
INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
SELECT r.role_no, p.permission_no, r.pharmacy_no, 1
FROM sys_roles r
CROSS JOIN sys_permissions p
WHERE (r.role_no = 9 OR r.alias ILIKE '%Manager%')
ON CONFLICT DO NOTHING;

-- If a role has 'void' or 'sale.return', ensure it holds 'PMS-Sale-Module'
INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
SELECT DISTINCT rp.role_no, p_target.permission_no, rp.pharmacy_no, rp.branch_no
FROM sys_role_permissions rp
JOIN sys_permissions p_src ON rp.permission_no = p_src.permission_no
CROSS JOIN sys_permissions p_target
WHERE p_src.code IN ('void', 'sale.return') AND p_target.code = 'PMS-Sale-Module'
ON CONFLICT DO NOTHING;

-- If a role has 'stock.adjust' or 'price.edit', ensure it holds 'PMS-Product-Module'
INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
SELECT DISTINCT rp.role_no, p_target.permission_no, rp.pharmacy_no, rp.branch_no
FROM sys_role_permissions rp
JOIN sys_permissions p_src ON rp.permission_no = p_src.permission_no
CROSS JOIN sys_permissions p_target
WHERE p_src.code IN ('stock.adjust', 'price.edit') AND p_target.code = 'PMS-Product-Module'
ON CONFLICT DO NOTHING;

-- If a role has 'cash.close', ensure it holds 'Account-Module'
INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
SELECT DISTINCT rp.role_no, p_target.permission_no, rp.pharmacy_no, rp.branch_no
FROM sys_role_permissions rp
JOIN sys_permissions p_src ON rp.permission_no = p_src.permission_no
CROSS JOIN sys_permissions p_target
WHERE p_src.code = 'cash.close' AND p_target.code = 'Account-Module'
ON CONFLICT DO NOTHING;

-- If a role has 'user.manage' or 'role.manage', ensure it holds 'Role-Module'
INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
SELECT DISTINCT rp.role_no, p_target.permission_no, rp.pharmacy_no, rp.branch_no
FROM sys_role_permissions rp
JOIN sys_permissions p_src ON rp.permission_no = p_src.permission_no
CROSS JOIN sys_permissions p_target
WHERE p_src.code IN ('user.manage', 'role.manage') AND p_target.code = 'Role-Module'
ON CONFLICT DO NOTHING;
