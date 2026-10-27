-- ============================================================================
-- Migration: 20261027_15_seed_missing_card_rocket_accounts.sql
-- Description:
--   1. Re-aligns existing Card POS settlement account name to 'Card POS Machine'.
--   2. Seeds missing Card POS Machine (type 7) and Rocket (type 6) accounts
--      strictly for existing pharmacies/branches that already have accounts.
--   3. Guarantees sequence alignment for seq_account_no.
--   4. Preserves idempotent migration history for renumbered migrations 13 & 14.
-- ============================================================================

-- Standardize naming for existing Card POS accounts
UPDATE acc_transaction_accounts
SET account_name = 'Card POS Machine',
    updated_at = CURRENT_TIMESTAMP
WHERE account_code = '10104' AND account_name = 'Card POS Settlement Account';

-- Seed missing Card POS Machine (type 7) for existing pharmacy branches that already have accounts
INSERT INTO acc_transaction_accounts (
    account_no, pharmacy_no, branch_no, account_category, account_code,
    account_name, phone_number, account_type, bank_account_number, bank_name,
    current_balance, is_active, created_at, updated_at
)
SELECT
    nextval('seq_account_no'),
    b.pharmacy_no,
    b.branch_no,
    1,
    '10104',
    'Card POS Machine',
    NULL,
    7,
    NULL,
    'Card POS Settlement',
    0.0000,
    TRUE,
    CURRENT_TIMESTAMP,
    CURRENT_TIMESTAMP
FROM (
    SELECT DISTINCT pharmacy_no, branch_no
    FROM acc_transaction_accounts
) b
WHERE NOT EXISTS (
    SELECT 1 FROM acc_transaction_accounts a
    WHERE a.pharmacy_no = b.pharmacy_no
      AND a.branch_no = b.branch_no
      AND a.account_type = 7
);

-- Seed missing Rocket (type 6) for existing pharmacy branches that already have accounts
INSERT INTO acc_transaction_accounts (
    account_no, pharmacy_no, branch_no, account_category, account_code,
    account_name, phone_number, account_type, bank_account_number, bank_name,
    current_balance, is_active, created_at, updated_at
)
SELECT
    nextval('seq_account_no'),
    b.pharmacy_no,
    b.branch_no,
    1,
    '10105',
    'Rocket',
    '01900000002',
    6,
    NULL,
    'Rocket Merchant',
    0.0000,
    TRUE,
    CURRENT_TIMESTAMP,
    CURRENT_TIMESTAMP
FROM (
    SELECT DISTINCT pharmacy_no, branch_no
    FROM acc_transaction_accounts
) b
WHERE NOT EXISTS (
    SELECT 1 FROM acc_transaction_accounts a
    WHERE a.pharmacy_no = b.pharmacy_no
      AND a.branch_no = b.branch_no
      AND a.account_type = 6
);

-- Re-align sequence seq_account_no
SELECT setval('seq_account_no', GREATEST(COALESCE((SELECT MAX(account_no) FROM acc_transaction_accounts), 0) + 1, 1), false);

-- Sync __schema_migrations table for renumbered migration files if older IDs exist
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables 
        WHERE table_schema = current_schema() AND table_name = '__schema_migrations'
    ) THEN
        INSERT INTO __schema_migrations (migration_id, applied_at)
        SELECT '20261026_13_global_sys_menus.sql', applied_at
        FROM __schema_migrations
        WHERE migration_id = '20261026_12_global_sys_menus.sql'
        ON CONFLICT (migration_id) DO NOTHING;

        INSERT INTO __schema_migrations (migration_id, applied_at)
        SELECT '20261026_14_legacy_role_menu_permissions.sql', applied_at
        FROM __schema_migrations
        WHERE migration_id = '20261026_13_legacy_role_menu_permissions.sql'
        ON CONFLICT (migration_id) DO NOTHING;
    END IF;
END $$;
