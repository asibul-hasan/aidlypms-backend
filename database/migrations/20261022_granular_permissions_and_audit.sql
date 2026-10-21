-- ============================================================================
-- MIGRATION: 20261022_granular_permissions_and_audit.sql
-- Description: Adds granular write permission codes (void, stock.adjust, price.edit,
--              cash.close, user.manage, role.manage), grants them to roles with
--              matching module permissions, and nulls historical audit log entries
--              containing password/token/hash.
-- ============================================================================

BEGIN;

-- 1. Insert new granular permissions
INSERT INTO sys_permissions (permission_no, code, module_group, name, description, created_at) VALUES
(22, 'void', 'Sales', 'Void Sale Invoice', 'Authorize voiding completed POS sale invoices', CURRENT_TIMESTAMP),
(23, 'stock.adjust', 'Products', 'Stock Adjustment', 'Authorize recording inventory quantity and cost adjustments', CURRENT_TIMESTAMP),
(24, 'price.edit', 'Products', 'Price Edit', 'Authorize updating master product purchase and sale prices', CURRENT_TIMESTAMP),
(25, 'cash.close', 'Accounting', 'Cash Shift Close', 'Authorize closing and reconciling daily cash register shifts', CURRENT_TIMESTAMP),
(26, 'user.manage', 'Administration', 'User Management', 'Authorize managing employee and staff user accounts', CURRENT_TIMESTAMP),
(27, 'role.manage', 'Administration', 'Role Management', 'Authorize creating and editing authorization roles and access rights', CURRENT_TIMESTAMP)
ON CONFLICT (permission_no) DO NOTHING;

-- 2. Grant new permissions to every role that has matching module permissions:
-- void -> matching PMS-Sale-Module
-- stock.adjust -> matching PMS-Product-Module
-- price.edit -> matching PMS-Product-Module
-- cash.close -> matching PMS-Sale-Module or Account-Module
-- user.manage -> matching Role-Module or Permission-Module
-- role.manage -> matching Role-Module

INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
SELECT DISTINCT rp.role_no, p_new.permission_no, rp.pharmacy_no, rp.branch_no
FROM sys_role_permissions rp
JOIN sys_permissions p_old ON rp.permission_no = p_old.permission_no
CROSS JOIN sys_permissions p_new
WHERE (
    (p_new.code = 'void' AND p_old.code = 'PMS-Sale-Module') OR
    (p_new.code = 'stock.adjust' AND p_old.code = 'PMS-Product-Module') OR
    (p_new.code = 'price.edit' AND p_old.code = 'PMS-Product-Module') OR
    (p_new.code = 'cash.close' AND p_old.code IN ('PMS-Sale-Module', 'Account-Module')) OR
    (p_new.code = 'user.manage' AND p_old.code IN ('Role-Module', 'Permission-Module')) OR
    (p_new.code = 'role.manage' AND p_old.code = 'Role-Module')
)
ON CONFLICT DO NOTHING;

-- 3. Scrub sensitive values in existing sys_audit_logs
UPDATE sys_audit_logs
SET new_data = NULL
WHERE new_data ILIKE '%password%' OR new_data ILIKE '%token%' OR new_data ILIKE '%hash%';

COMMIT;
