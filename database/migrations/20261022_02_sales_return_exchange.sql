-- ============================================================================
-- MIGRATION: 20261021_sales_return_exchange.sql
-- Description: Sales Return & Exchange server-authoritative schema, clearing account,
--              return window configuration, and permissions.
-- ============================================================================

BEGIN;

-- 1. Add columns to sale_returns
ALTER TABLE sale_returns
    ADD COLUMN IF NOT EXISTS refund_method SMALLINT,
    ADD COLUMN IF NOT EXISTS exchange_invoice_no BIGINT REFERENCES sale_invoices(sale_invoice_no) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS idempotency_key UUID;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE table_schema = current_schema()
          AND table_name = 'sale_returns'
          AND constraint_name = 'uq_sale_returns_idempotency_key'
    ) THEN
        ALTER TABLE sale_returns ADD CONSTRAINT uq_sale_returns_idempotency_key UNIQUE (idempotency_key);
    END IF;
END $$;

-- 2. Add columns to sale_invoices
ALTER TABLE sale_invoices
    ADD COLUMN IF NOT EXISTS exchange_of_return_no BIGINT REFERENCES sale_returns(sale_return_no) ON DELETE SET NULL;

-- 3. Add return_window_days to cfg_sale_configs
ALTER TABLE cfg_sale_configs
    ADD COLUMN IF NOT EXISTS return_window_days INT NOT NULL DEFAULT 7;

-- 4. Seed Return Credit Clearing Account (Account Code 10901) dynamically per pharmacy & branch
INSERT INTO acc_transaction_accounts (
    account_no,
    pharmacy_no,
    branch_no,
    account_category,
    account_code,
    account_name,
    account_type,
    bank_account_number,
    bank_name,
    current_balance,
    is_active,
    created_at,
    updated_at
)
SELECT
    (COALESCE(m.max_acc, 0) + ROW_NUMBER() OVER (ORDER BY b.pharmacy_no, b.branch_no)),
    b.pharmacy_no,
    b.branch_no,
    1, -- Asset / Clearing
    '10901',
    'Return Credit Clearing',
    1, -- Cash / Clearing type
    NULL,
    NULL,
    0.0000,
    TRUE,
    CURRENT_TIMESTAMP,
    CURRENT_TIMESTAMP
FROM sys_branches b
CROSS JOIN (SELECT COALESCE(MAX(account_no), 0) AS max_acc FROM acc_transaction_accounts) m
WHERE NOT EXISTS (
    SELECT 1 FROM acc_transaction_accounts a
    WHERE a.pharmacy_no = b.pharmacy_no
      AND a.branch_no = b.branch_no
      AND (a.account_code = '10901' OR a.account_name = 'Return Credit Clearing')
);

-- 5. Seed 'sale.return' Permission Node in sys_permissions
INSERT INTO sys_permissions (permission_no, code, module_group, name, description, created_at)
VALUES (
    (SELECT COALESCE(MAX(permission_no), 0) + 1 FROM sys_permissions),
    'sale.return',
    'Sales',
    'Sales Return & Exchange',
    'Process customer sales returns and item exchanges',
    CURRENT_TIMESTAMP
)
ON CONFLICT (code) DO NOTHING;

-- 6. Grant 'sale.return' permission to EVERY role across all tenants that currently has any sale permission
INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
SELECT DISTINCT
    rp.role_no,
    ret_perm.permission_no,
    rp.pharmacy_no,
    rp.branch_no
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
CROSS JOIN (
    SELECT permission_no FROM sys_permissions WHERE code = 'sale.return' LIMIT 1
) ret_perm
WHERE (p.code ILIKE '%sale%' OR p.module_group ILIKE '%sale%')
  AND NOT EXISTS (
      SELECT 1 FROM sys_role_permissions existing
      WHERE existing.role_no = rp.role_no
        AND existing.permission_no = ret_perm.permission_no
        AND existing.pharmacy_no = rp.pharmacy_no
        AND existing.branch_no = rp.branch_no
  )
ON CONFLICT DO NOTHING;

-- 7. Backfill historical sale_invoice_payments for existing invoices (net of change)
--    Ensures day-end reconciliation reports tie out accurately for historical shifts.
INSERT INTO sale_invoice_payments (
    sale_payment_no,
    pharmacy_no,
    branch_no,
    sale_invoice_no,
    account_no,
    payment_method,
    amount,
    gateway_reference,
    created_at
)
SELECT
    (COALESCE(m.max_pay, 0) + ROW_NUMBER() OVER (ORDER BY s.sale_invoice_no)),
    s.pharmacy_no,
    s.branch_no,
    s.sale_invoice_no,
    COALESCE(acc.account_no, def_acc.account_no, 1),
    COALESCE(s.payment_method, 1),
    GREATEST(0.01, COALESCE(s.customer_will_pay - s.change_amount, s.final_price - s.due_amount, 0)),
    NULL, -- gateway_reference (s.mfs_trx_id does not exist on sale_invoices)
    s.sale_timestamp
FROM sale_invoices s
CROSS JOIN (SELECT COALESCE(MAX(sale_payment_no), 0) AS max_pay FROM sale_invoice_payments) m
LEFT JOIN acc_transaction_accounts acc ON acc.pharmacy_no = s.pharmacy_no
                                       AND acc.branch_no = s.branch_no
                                       AND acc.account_code = CASE s.payment_method
                                           WHEN 2 THEN '10201'
                                           WHEN 3 THEN '10202'
                                           WHEN 4 THEN '10203'
                                           WHEN 5 THEN '10204'
                                           WHEN 6 THEN '10205'
                                           ELSE '10101'
                                       END
LEFT JOIN acc_transaction_accounts def_acc ON def_acc.pharmacy_no = s.pharmacy_no
                                           AND def_acc.branch_no = s.branch_no
                                           AND def_acc.account_code = '10101'
WHERE s.document_status = 2
  AND GREATEST(COALESCE(s.customer_will_pay - s.change_amount, 0), COALESCE(s.final_price - s.due_amount, 0)) > 0
  AND NOT EXISTS (
      SELECT 1 FROM sale_invoice_payments p WHERE p.sale_invoice_no = s.sale_invoice_no
  );

COMMIT;
