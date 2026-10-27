-- ============================================================================
-- Migration: 20261026_13_global_sys_menus.sql
-- Description:
--   1. Upgrades sys_menus to a truly DB-driven global menu catalog.
--   2. Adds menu_code (UNIQUE), title_bn, menu_type (MODULE, SUBMODULE, SCREEN), route.
--   3. Replaces legacy sample rows with complete production hierarchy
--      (5 modules, 28 sub-modules, and 58 screens).
--   4. Standard zero-default migration: adds columns nullable, backfills,
--      then applies NOT NULL and UNIQUE constraints.
-- ============================================================================

-- Step 1: Add new columns if not exist (nullable, zero-default standard)
ALTER TABLE sys_menus ADD COLUMN IF NOT EXISTS menu_code VARCHAR(50);
ALTER TABLE sys_menus ADD COLUMN IF NOT EXISTS title_bn VARCHAR(255);
ALTER TABLE sys_menus ADD COLUMN IF NOT EXISTS menu_type VARCHAR(20);
ALTER TABLE sys_menus ADD COLUMN IF NOT EXISTS route VARCHAR(255);

-- Step 2: Remove legacy sample rows that lacked menu_code
DELETE FROM sys_menus WHERE menu_code IS NULL;

-- Step 3: Ensure unique index on menu_code
CREATE UNIQUE INDEX IF NOT EXISTS uq_sys_menus_code ON sys_menus(menu_code);

-- Step 4: Upsert Root Modules & Home Dashboard
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1, 'DASH_2301', 'Executive Financial Dashboard', 'এক্সিকিউটিভ ড্যাশবোর্ড', '/dashboard', '/dashboard', 'dashboard', NULL, 0, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (10, 'MOD_SAL', 'Sales & Customers', 'বিক্রয় ও কাস্টমার', '/customer/sale', '/customer/sale', 'shopping-cart', NULL, 1, 'MODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (20, 'MOD_PUR', 'Purchase & Suppliers', 'ক্রয় ও সাপ্লায়ার', '/supplier/purchase', '/supplier/purchase', 'shop', NULL, 2, 'MODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (30, 'MOD_INV', 'Medicines & Stock', 'ঔষধ ও স্টক', '/products/list', '/products/list', 'medicine-box', NULL, 3, 'MODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (40, 'MOD_ACC', 'Accounts & Cash', 'হিসাব ও ক্যাশ', '/accounting/expenses', '/accounting/expenses', 'account-book', NULL, 4, 'MODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (50, 'MOD_SYS', 'Settings & Admin', 'সেটিংস ও অ্যাডমিন', '/settings/sale-config', '/settings/sale-config', 'setting', NULL, 5, 'MODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;

-- Step 5: Upsert Submodules
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (101, 'SUB_SAL_POS', 'Point of Sale', 'বিক্রয় কাউন্টার', NULL, NULL, 'shopping-cart', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_SAL'), 1, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (102, 'SUB_SAL_SALES_HISTORY', 'Sales History', 'বিক্রয়ের হিসাব', NULL, NULL, 'file-search', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_SAL'), 2, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (103, 'SUB_SAL_CUSTOMERS', 'Customers', 'কাস্টমার', NULL, NULL, 'team', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_SAL'), 3, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (104, 'SUB_SAL_DUE', 'Due Collection', 'বাকি আদায়', NULL, NULL, 'file-text', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_SAL'), 4, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (105, 'SUB_SAL_DOCTORS', 'Doctors & Prescriptions', 'ডাক্তার ও প্রেসক্রিপশন', NULL, NULL, 'user', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_SAL'), 5, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (201, 'SUB_PUR_PURCHASE_ENTRY', 'Purchase Entry', 'ক্রয় এন্ট্রি', NULL, NULL, 'shop', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_PUR'), 1, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (202, 'SUB_PUR_PURCHASE_HISTORY', 'Purchase History', 'ক্রয়ের হিসাব', NULL, NULL, 'file-search', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_PUR'), 2, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (203, 'SUB_PUR_SUPPLIERS', 'Suppliers', 'সাপ্লায়ার', NULL, NULL, 'contacts', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_PUR'), 3, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (301, 'SUB_INV_MEDICINES', 'Medicine List', 'ঔষধের তালিকা', NULL, NULL, 'medicine-box', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_INV'), 1, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (302, 'SUB_INV_COMPANIES', 'Companies', 'কোম্পানি', NULL, NULL, 'bank', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_INV'), 2, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (303, 'SUB_INV_GENERICS', 'Generic Names', 'জেনেরিক নাম', NULL, NULL, 'experiment', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_INV'), 3, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (304, 'SUB_INV_REORDER', 'Reorder List', 'অর্ডার তালিকা', NULL, NULL, 'unordered-list', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_INV'), 4, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (305, 'SUB_INV_EXPIRY', 'Expiry Alerts', 'মেয়াদ সতর্কতা', NULL, NULL, 'alert', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_INV'), 5, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (306, 'SUB_INV_STOCK_REPORTS', 'Stock Reports', 'স্টক রিপোর্ট', NULL, NULL, 'bar-chart', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_INV'), 6, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (307, 'SUB_INV_STOCKTAKES', 'Stock-take (Cycle Count)', 'স্টক নিরীক্ষা / গণনা', NULL, NULL, 'audit', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_INV'), 7, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (308, 'SUB_INV_STOCK_INTEGRITY', 'Stock Ledger Integrity', 'স্টক লেজার সততা', NULL, NULL, 'safety-certificate', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_INV'), 8, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (401, 'SUB_ACC_CASH_COUNTER', 'Cash Counter', 'ক্যাশ কাউন্টার', NULL, NULL, 'dollar', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_ACC'), 1, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (402, 'SUB_ACC_ACCOUNTS', 'Accounts & Transfers', 'অ্যাকাউন্ট ও ট্রান্সফার', NULL, NULL, 'bank', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_ACC'), 2, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (403, 'SUB_ACC_EXPENSES', 'Expenses', 'খরচ', NULL, NULL, 'wallet', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_ACC'), 3, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (404, 'SUB_ACC_INCOME', 'Other Income', 'অন্যান্য আয়', NULL, NULL, 'rise', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_ACC'), 4, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (405, 'SUB_ACC_CAPITAL', 'Owner Capital', 'মূলধন', NULL, NULL, 'safety', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_ACC'), 5, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (406, 'SUB_ACC_REPORTS', 'Reports', 'রিপোর্ট', NULL, NULL, 'bar-chart', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_ACC'), 6, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (501, 'SUB_SYS_ACCESS', 'Users & Roles', 'ইউজার ও রোল', NULL, NULL, 'safety-certificate', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_SYS'), 1, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (502, 'SUB_SYS_COMPANY_REPS', 'Company Representatives', 'কোম্পানি প্রতিনিধি', NULL, NULL, 'team', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_SYS'), 2, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (503, 'SUB_SYS_SHOP_RULES', 'Shop Rules', 'দোকানের নিয়ম', NULL, NULL, 'setting', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_SYS'), 3, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (504, 'SUB_SYS_PRINT_SMS', 'Printing & SMS', 'প্রিন্ট ও এসএমএস', NULL, NULL, 'printer', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_SYS'), 4, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (505, 'SUB_SYS_PLAN_BRANCH', 'Plan & Branch', 'প্ল্যান ও শাখা', NULL, NULL, 'cluster', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_SYS'), 5, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (506, 'SUB_SYS_AUDIT_LOGS', 'Audit Logs', 'সিস্টেম অডিট লগ', NULL, NULL, 'file-protect', (SELECT menu_no FROM sys_menus WHERE menu_code = 'MOD_SYS'), 6, 'SUBMODULE', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;

-- Step 6: Upsert Screens
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1001, 'SALE_1401', 'Point of Sale (POS Terminal)', 'রিটেইল পিওএস কাউন্টার', '/customer/sale', '/customer/sale', 'shopping-cart', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SAL_POS'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1002, 'SALE_1402', 'Wholesale POS Counter', 'পাইকারি পিওএস কাউন্টার', '/customer/sale-wholesale', '/customer/sale-wholesale', 'shopping-cart', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SAL_POS'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1003, 'SALE_1403', 'POS Sales Return & Refund', 'পিওএস বিক্রয় ফেরত ও রিফান্ড', '/customer/sale/return', '/customer/sale/return', 'shopping-cart', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SAL_POS'), 3, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1004, 'SALE_1410', 'Sales Invoices Register', 'বিক্রয় চালান রেজিস্টার', '/customer/sales', '/customer/sales', 'file-search', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SAL_SALES_HISTORY'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1005, 'SALE_1409', 'Customer Return Invoices', 'কাস্টমার রিটার্ন ইনভয়েস তালিকা', '/customer/returns', '/customer/returns', 'file-search', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SAL_SALES_HISTORY'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1006, 'SALE_1411', 'Offline Sales Review', 'অফলাইন সেলস রিভিউ ও ট্র্যাকিং', '/customer/offline-review', '/customer/offline-review', 'file-search', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SAL_SALES_HISTORY'), 3, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1007, 'CUST_1407', 'Customer Directory & Ledger', 'কাস্টমার ডিরেক্টরি ও লেজার', '/customer/list', '/customer/list', 'team', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SAL_CUSTOMERS'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1008, 'CUST_1408', 'Add / Edit Customer Profile', 'কাস্টমার যুক্ত / সম্পাদনা', '/customer/add', '/customer/add', 'team', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SAL_CUSTOMERS'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1009, 'DUE_1301', 'Due Collection Workbench', 'বাকি আদায় ওয়ার্কবেঞ্চ', '/due-collect', '/due-collect', 'file-text', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SAL_DUE'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1010, 'DUE_1302', 'Due Paid Collections Register', 'বাকি আদায় অডিট ও পেমেন্ট হিস্টোরি', '/pms-due-paid-payments', '/pms-due-paid-payments', 'file-text', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SAL_DUE'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1011, 'DOC_1701', 'Medical Practitioners Directory', 'ডাক্তার ডিরেক্টরি ও তালিকা', '/doctors-portal/list', '/doctors-portal/list', 'user', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SAL_DOCTORS'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1012, 'DOC_1702', 'Add / Edit Doctor', 'ডাক্তার যুক্ত / সম্পাদনা', '/doctors-portal/add', '/doctors-portal/add', 'user', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SAL_DOCTORS'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1013, 'PUR_1503', 'Purchase (Invoice Entry)', 'ক্রয় চালান এন্ট্রি (GRN)', '/supplier/purchase', '/supplier/purchase', 'shop', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_PUR_PURCHASE_ENTRY'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1014, 'PUR_1504', 'Purchase Calculation Selector', 'ক্রয় হিসাব মোড নির্বাচক', '/supplier/purchase/mode', '/supplier/purchase/mode', 'shop', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_PUR_PURCHASE_ENTRY'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1015, 'PUR_1509', 'Purchase Invoices Register', 'ক্রয় চালান রেজিস্টার', '/supplier/records', '/supplier/records', 'file-search', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_PUR_PURCHASE_HISTORY'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1016, 'SUPP_1501', 'Supplier Directory & Ledger', 'সাপ্লায়ার ডিরেক্টরি ও লেজার', '/supplier/list', '/supplier/list', 'contacts', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_PUR_SUPPLIERS'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1017, 'SUPP_1502', 'Add / Edit Supplier', 'সাপ্লায়ার যুক্ত / সম্পাদনা', '/supplier/add', '/supplier/add', 'contacts', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_PUR_SUPPLIERS'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1018, 'PROD_1601', 'Master Medicine Catalog', 'মাস্টার মেডিসিন ক্যাটালগ', '/products/list', '/products/list', 'medicine-box', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_INV_MEDICINES'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1019, 'PROD_1602', 'Add Medicine Master', 'নতুন মেডিসিন যুক্ত করুন', '/products/add', '/products/add', 'medicine-box', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_INV_MEDICINES'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1020, 'PROD_1603', 'Pharmaceutical Manufacturers', 'ফার্মাসিউটিক্যাল কোম্পানি সমূহ', '/products/company', '/products/company', 'bank', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_INV_COMPANIES'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1021, 'PROD_1604', 'Add Pharmaceutical Company', 'কোম্পানি যুক্ত করুন', '/products/company/add', '/products/company/add', 'bank', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_INV_COMPANIES'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1022, 'PROD_1605', 'Generic Drug Formulations', 'জেনেরিক ফর্মুলেশন তালিকা', '/products/generic', '/products/generic', 'experiment', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_INV_GENERICS'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1023, 'PROD_1606', 'Add Generic Formulation', 'জেনেরিক যুক্ত করুন', '/products/generic/add', '/products/generic/add', 'experiment', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_INV_GENERICS'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1024, 'PROD_1607', 'Inventory Requisition Planner', 'মেডিসিন রিকুইজিশন প্ল্যানার', '/products/requisition', '/products/requisition', 'unordered-list', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_INV_REORDER'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1025, 'PROD_1608', 'Requisition Item Selector', 'রিকুইজিশন আইটেম নির্বাচক', '/products/requisition/add-item', '/products/requisition/add-item', 'unordered-list', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_INV_REORDER'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1026, 'PROD_1609', 'Requisitions Register', 'রিকুইজিশন রেজিস্টার ও হিস্টোরি', '/products/requisition/records', '/products/requisition/records', 'unordered-list', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_INV_REORDER'), 3, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1027, 'PROD_1610', 'Expired & Near-Expiry Alerts', 'মেয়াদোত্তীর্ণ ও মেয়াদ সতর্কতা', '/products/expired', '/products/expired', 'alert', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_INV_EXPIRY'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1028, 'PROD_1611', 'Stock Valuation & Inventory Report', 'স্টক মূল্যায়ন ও ইনভেন্টরি রিপোর্ট', '/products/reports', '/products/reports', 'bar-chart', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_INV_STOCK_REPORTS'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1029, 'PROD_1612', 'Stock-take (Cycle Count)', 'স্টক অডিট ও সাইকেল কাউন্ট', '/products/stocktakes', '/products/stocktakes', 'audit', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_INV_STOCKTAKES'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1030, 'PROD_1613', 'Stock Ledger Integrity', 'স্টক লেজার অখণ্ডতা যাচাই', '/products/integrity', '/products/integrity', 'safety-certificate', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_INV_STOCK_INTEGRITY'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1031, 'CASH_1201', 'Received Cash Register', 'রিসিভড ক্যাশ রেজিস্টার ও হ্যান্ডওভার', '/cash/receive', '/cash/receive', 'dollar', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_ACC_CASH_COUNTER'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1032, 'ACC_1101', 'Chart of Accounts Register', 'চার্ট অব অ্যাকাউন্টস রেজিস্টার', '/accounting/transaction', '/accounting/transaction', 'bank', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_ACC_ACCOUNTS'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1033, 'ACC_1102', 'Create Transaction Account', 'নতুন অ্যাকাউন্ট তৈরি', '/accounting/transaction/add', '/accounting/transaction/add', 'bank', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_ACC_ACCOUNTS'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1034, 'CASH_1203', 'Inter-Account Fund Transfers', 'আন্তঃহিসাব ফান্ড ট্রান্সফার', '/reconciliation', '/reconciliation', 'bank', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_ACC_ACCOUNTS'), 3, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1035, 'CASH_1204', 'Add Fund Transfer', 'নতুন ফান্ড ট্রান্সফার', '/reconciliation/add', '/reconciliation/add', 'bank', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_ACC_ACCOUNTS'), 4, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1036, 'ACC_1103', 'Expense Vouchers Register', 'খরচ ভাউচার রেজিস্টার', '/accounting/expenses', '/accounting/expenses', 'wallet', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_ACC_EXPENSES'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1037, 'ACC_1104', 'Add Expense Voucher', 'নতুন খরচ ভাউচার', '/accounting/expenses/add', '/accounting/expenses/add', 'wallet', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_ACC_EXPENSES'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1038, 'ACC_1106', 'Miscellaneous Income Register', 'অন্যান্য আয় রেজিস্টার', '/accounting/income', '/accounting/income', 'rise', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_ACC_INCOME'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1039, 'ACC_1109', 'Capital / Equity Balance Log', 'মালিকের মূলধন ব্যালেন্স লগ', '/accounting/additional-balance', '/accounting/additional-balance', 'safety', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_ACC_CAPITAL'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1040, 'DASH_2302', 'Sales Performance & Fast Movers', 'বিক্রয় পারফরম্যান্স ও দ্রুত বিক্রয়', '/dashboard/analytics', '/dashboard/analytics', 'bar-chart', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_ACC_REPORTS'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1041, 'DASH_2303', 'Cashbook & Buy/Sale Register', 'ক্যাশ রেজিস্টার ও ক্রয়/বিক্রয় খাতা', '/dashboard/cashbook', '/dashboard/cashbook', 'bar-chart', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_ACC_REPORTS'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1042, 'ROLE_1901', 'Role Authorization Matrix', 'রোল অথরাইজেশন ও স্পেশাল পারমিশন', '/roles', '/roles', 'safety-certificate', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_ACCESS'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1043, 'ROLE_1902', 'Add / Edit Role & ACL', 'রোল তৈরি ও সম্পাদনা', '/roles/add', '/roles/add', 'safety-certificate', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_ACCESS'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1044, 'ROLE_1903', 'Role Menu Permissions Matrix', 'রোল মেনু পারমিশন ম্যাট্রিক্স', '/roles/permissions', '/roles/permissions', 'safety-certificate', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_ACCESS'), 3, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1045, 'MENU_1904', 'Menu Management', 'সিস্টেম মেনু ম্যানেজমেন্ট', '/menus', '/menus', 'safety-certificate', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_ACCESS'), 4, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1046, 'MENU_1905', 'Create System Menu', 'নতুন মেনু তৈরি', '/menus/add', '/menus/add', 'safety-certificate', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_ACCESS'), 5, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1047, 'REP_1801', 'Representative User List', 'কোম্পানি প্রতিনিধি ও স্টাফ তালিকা', '/representative', '/representative', 'team', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_COMPANY_REPS'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1048, 'REP_1802', 'Add / Edit Representative', 'প্রতিনিধি যুক্ত / সম্পাদনা', '/representative/add', '/representative/add', 'team', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_COMPANY_REPS'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1049, 'CFG_2001', 'Sale Business Rules Config', 'বিক্রয় ব্যবসায়িক নিয়ম কনফিগারেশন', '/settings/sale-config', '/settings/sale-config', 'setting', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_SHOP_RULES'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1050, 'CFG_2003', 'Product Alert Thresholds', 'স্টক থ্রেশহোল্ড ও অ্যালার্ট সেটিংস', '/settings/product-config', '/settings/product-config', 'setting', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_SHOP_RULES'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1051, 'CFG_2004', 'Customer Loyalty Rules', 'কাস্টমার লয়ালটি ও পয়েন্ট রুলস', '/settings/loyalty-config', '/settings/loyalty-config', 'setting', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_SHOP_RULES'), 3, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1052, 'CFG_2002', 'Thermal Receipt / POS Invoice', 'থার্মাল রিসিট ও পিওএস প্রিন্ট ফরম্যাট', '/settings/invoice-config', '/settings/invoice-config', 'printer', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_PRINT_SMS'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1053, 'DOC_1703', 'Prescription Pad Settings', 'প্রেসক্রিপশন প্যাড সেটিংস', '/doctors-portal/settings', '/doctors-portal/settings', 'printer', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_PRINT_SMS'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1054, 'CFG_2005', 'Bulk SMS Packages & Gateway', 'বাল্ক এসএমএস প্যাকেজ ও গেটওয়ে', '/settings/bulk-sms-config', '/settings/bulk-sms-config', 'printer', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_PRINT_SMS'), 3, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1055, 'SUB_2101', 'Subscription Status & History', 'সাবস্ক্রিপশন স্ট্যাটাস ও হিস্টোরি', '/subscription-report', '/subscription-report', 'cluster', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_PLAN_BRANCH'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1056, 'PHAR_2201', 'Store / Branch Switcher', 'স্টোর ও শাখা নির্বাচক', '/change-pharmacy', '/change-pharmacy', 'cluster', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_PLAN_BRANCH'), 2, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;
INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
VALUES (1057, 'SETT_2006', 'System Audit Logs', 'সিস্টেম অডিট ও অ্যাক্টিভিটি লগ', '/settings/audit-logs', '/settings/audit-logs', 'file-protect', (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_SYS_AUDIT_LOGS'), 1, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_code) DO UPDATE SET
    title = EXCLUDED.title,
    title_bn = EXCLUDED.title_bn,
    path = EXCLUDED.path,
    route = EXCLUDED.route,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    menu_type = EXCLUDED.menu_type,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;

-- Step 7: Apply NOT NULL constraints after successful backfill (Zero-default standard)
ALTER TABLE sys_menus ALTER COLUMN menu_code SET NOT NULL;
ALTER TABLE sys_menus ALTER COLUMN title_bn SET NOT NULL;
ALTER TABLE sys_menus ALTER COLUMN menu_type SET NOT NULL;

-- Step 8: Keep sequence in sync with seeded menu_no IDs
SELECT setval('seq_sys_menu_no', (SELECT GREATEST(MAX(menu_no), 1500) + 1 FROM sys_menus), false);
