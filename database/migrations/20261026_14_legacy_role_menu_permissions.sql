-- ============================================================================
-- Migration: 20261026_14_legacy_role_menu_permissions.sql
-- Description:
--   1. Migrates existing role permissions: for every role holding a legacy
--      module code in sys_role_permissions, grants granular menu permissions
--      (can_view, can_create, can_edit, can_delete) on every menu mapped to that module.
--   2. Ensures parent submodules and modules are granted can_view = TRUE
--      whenever any child screen is visible.
--   3. Guarantees PMS Manager (Role 9 / alias 'PMS Manager') has full access.
--   4. Idempotent: ON CONFLICT DO NOTHING, running multiple times causes no change.
-- ============================================================================

-- Step 1: Ensure seq_role_menu_perm_no is aligned
CREATE SEQUENCE IF NOT EXISTS seq_role_menu_perm_no START WITH 1 INCREMENT BY 1;

-- Step 2: Grant screen permissions to roles that hold the corresponding legacy module permission

-- Legacy Permission: PMS-Report-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('DASH_2301', 'DASH_2302', 'DASH_2303', 'SALE_1411')
WHERE p.code = 'PMS-Report-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: PMS-Product-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('PROD_1601', 'PROD_1602', 'PROD_1610', 'PROD_1611', 'PROD_1612', 'PROD_1613')
WHERE p.code = 'PMS-Product-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: POS-Shop-Company-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('PROD_1603', 'PROD_1604')
WHERE p.code = 'POS-Shop-Company-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: POS-Shop-Generic-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('PROD_1605', 'PROD_1606')
WHERE p.code = 'POS-Shop-Generic-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: PMS-Requisition-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('PROD_1607', 'PROD_1608', 'PROD_1609')
WHERE p.code = 'PMS-Requisition-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: PMS-Vendor-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('PUR_1503', 'SUPP_1501', 'SUPP_1502', 'PUR_1504', 'PUR_1509')
WHERE p.code = 'PMS-Vendor-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: PMS-Sale-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('SALE_1401', 'SALE_1402', 'SALE_1403', 'SALE_1409', 'SALE_1410', 'CASH_1201')
WHERE p.code = 'PMS-Sale-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: PMS-Customer-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('CUST_1407', 'CUST_1408', 'DUE_1301', 'DUE_1302')
WHERE p.code = 'PMS-Customer-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: PMS-Reconciliation-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('CASH_1203', 'CASH_1204')
WHERE p.code = 'PMS-Reconciliation-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: PMS-Chart-Of-Accounting-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('ACC_1101', 'ACC_1102')
WHERE p.code = 'PMS-Chart-Of-Accounting-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: PMS-Expense-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('ACC_1103', 'ACC_1104')
WHERE p.code = 'PMS-Expense-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: PMS-Income-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('ACC_1106')
WHERE p.code = 'PMS-Income-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: Account-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('ACC_1109')
WHERE p.code = 'Account-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: Pharmacy-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('DOC_1701', 'DOC_1702', 'DOC_1703', 'CFG_2001', 'CFG_2002', 'CFG_2003', 'CFG_2004', 'CFG_2005', 'SUB_2101', 'PHAR_2201', 'SETT_2006')
WHERE p.code = 'Pharmacy-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Legacy Permission: Role-Module
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rp.pharmacy_no,
    rp.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_permissions rp
JOIN sys_permissions p ON rp.permission_no = p.permission_no
JOIN sys_menus m ON m.menu_code IN ('REP_1801', 'REP_1802', 'ROLE_1901', 'ROLE_1902', 'ROLE_1903', 'MENU_1904', 'MENU_1905')
WHERE p.code = 'Role-Module'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Step 3: Ensure parent submodules have can_view = TRUE for roles with child permissions
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rmp.pharmacy_no,
    rmp.role_no,
    parent.menu_no,
    TRUE, FALSE, FALSE, FALSE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_menu_permissions rmp
JOIN sys_menus child ON rmp.menu_no = child.menu_no
JOIN sys_menus parent ON child.parent_menu_no = parent.menu_no
WHERE rmp.can_view = TRUE AND parent.menu_type = 'SUBMODULE'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Step 4: Ensure parent root modules have can_view = TRUE for roles with submodule permissions
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    rmp.pharmacy_no,
    rmp.role_no,
    mod.menu_no,
    TRUE, FALSE, FALSE, FALSE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_role_menu_permissions rmp
JOIN sys_menus sub ON rmp.menu_no = sub.menu_no
JOIN sys_menus mod ON sub.parent_menu_no = mod.menu_no
WHERE rmp.can_view = TRUE AND mod.menu_type = 'MODULE'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Step 5: Guarantee PMS Manager (role_no 9 or alias 'PMS Manager') has full access across all pharmacies
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT 
    nextval('seq_role_menu_perm_no'),
    r.pharmacy_no,
    r.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_roles r
CROSS JOIN sys_menus m
WHERE r.role_no = 9 OR r.alias = 'PMS Manager'
ON CONFLICT (pharmacy_no, role_no, menu_no) DO UPDATE SET
    can_view = TRUE,
    can_create = TRUE,
    can_edit = TRUE,
    can_delete = TRUE,
    updated_at = CURRENT_TIMESTAMP;

-- Step 6: Keep seq_role_menu_perm_no in sync
SELECT setval('seq_role_menu_perm_no', (SELECT GREATEST(MAX(role_menu_perm_no), 1) + 1 FROM sys_role_menu_permissions), false);
