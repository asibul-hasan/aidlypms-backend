-- ============================================================================
-- Migration: Menu & Role Permissions Module
-- Date: 2026-10-22
-- Description:
-- 1. Create sys_menus table (global menu catalog, no company_no/org fields)
-- 2. Enhance sys_roles with role_name and status (pharmacy_no scoped, no branch_no needed)
-- 3. Create sys_role_menu_permissions table (maps role_no and menu_no with
--    granular flags: can_view, can_create, can_edit, can_delete, pharmacy_no scoped)
-- 4. Sequences: seq_sys_menu_no, seq_role_menu_perm_no
-- ============================================================================

-- 1. Gap-safe sequences
CREATE SEQUENCE IF NOT EXISTS seq_sys_menu_no START WITH 100 INCREMENT BY 1;
CREATE SEQUENCE IF NOT EXISTS seq_role_menu_perm_no START WITH 1 INCREMENT BY 1;

-- 2. Application System Menus Table (Global Definition)
CREATE TABLE IF NOT EXISTS sys_menus (
    menu_no BIGINT PRIMARY KEY,
    title VARCHAR(150) NOT NULL,
    path VARCHAR(255),
    icon VARCHAR(100),
    parent_menu_no BIGINT REFERENCES sys_menus(menu_no) ON DELETE SET NULL,
    order_no INT NOT NULL,
    status BOOLEAN NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL
);

-- 3. Ensure role_name and status exist on sys_roles
ALTER TABLE sys_roles ADD COLUMN IF NOT EXISTS role_name VARCHAR(150);
ALTER TABLE sys_roles ADD COLUMN IF NOT EXISTS status BOOLEAN DEFAULT TRUE;

UPDATE sys_roles SET role_name = alias WHERE role_name IS NULL;
UPDATE sys_roles SET status = TRUE WHERE status IS NULL;
ALTER TABLE sys_roles ALTER COLUMN status SET NOT NULL;
ALTER TABLE sys_roles ALTER COLUMN status SET DEFAULT TRUE;

-- 4. Role Menu Permissions Join Table (Pharmacy Scoped, No Branch Required)
CREATE TABLE IF NOT EXISTS sys_role_menu_permissions (
    role_menu_perm_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    role_no BIGINT NOT NULL REFERENCES sys_roles(role_no) ON DELETE CASCADE,
    menu_no BIGINT NOT NULL REFERENCES sys_menus(menu_no) ON DELETE CASCADE,
    can_view BOOLEAN NOT NULL,
    can_create BOOLEAN NOT NULL,
    can_edit BOOLEAN NOT NULL,
    can_delete BOOLEAN NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_role_menu UNIQUE (pharmacy_no, role_no, menu_no)
);

-- 5. Pre-seed Default Aidly PMS Menus Hierarchy
-- Root Modules (Level 1)
INSERT INTO sys_menus (menu_no, title, path, icon, parent_menu_no, order_no, status, created_at, updated_at)
VALUES
(1, 'Sales & Customers', '/customer/sale', 'shopping-cart', NULL, 1, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(2, 'Purchases & Suppliers', '/supplier/purchase', 'shop', NULL, 2, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(3, 'Medicines & Stock', '/products/list', 'medicine-box', NULL, 3, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(4, 'Accounts & Cash', '/cash/closers', 'calculator', NULL, 4, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(5, 'Settings & Admin', '/settings/configs', 'setting', NULL, 5, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_no) DO UPDATE SET
    title = EXCLUDED.title,
    path = EXCLUDED.path,
    icon = EXCLUDED.icon,
    order_no = EXCLUDED.order_no,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;

-- Sub-Menus (Level 2)
INSERT INTO sys_menus (menu_no, title, path, icon, parent_menu_no, order_no, status, created_at, updated_at)
VALUES
-- Sales Sub-Menus
(11, 'POS Sale Terminal', '/customer/sale', 'thunderbolt', 1, 1, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(12, 'Sales Invoice Register', '/customer/sales-register', 'file-text', 1, 2, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(13, 'Customer Directory', '/customer/list', 'team', 1, 3, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(14, 'Customer Due Collection', '/customer/due', 'credit-card', 1, 4, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(15, 'Sales Returns & Exchanges', '/customer/returns', 'rollback', 1, 5, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),

-- Purchase Sub-Menus
(21, 'Purchase Entry', '/supplier/purchase', 'import', 2, 1, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(22, 'Purchase Invoices', '/supplier/invoices', 'file-done', 2, 2, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(23, 'Supplier Directory', '/supplier/list', 'contacts', 2, 3, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(24, 'Supplier Payments', '/supplier/payment', 'dollar', 2, 4, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(25, 'Purchase Returns', '/supplier/returns', 'swap', 2, 5, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),

-- Inventory Sub-Menus
(31, 'Master Medicine Catalog', '/products/list', 'database', 3, 1, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(32, 'Add Medicine', '/products/add', 'plus-circle', 3, 2, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(33, 'Batch Expiry & Stock Ledger', '/products/batches', 'history', 3, 3, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(34, 'Stock Adjustment', '/products/adjust', 'sliders', 3, 4, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(35, 'Inter-Branch Stock Transfer', '/products/transfers', 'deployment-unit', 3, 5, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),

-- Accounting Sub-Menus
(41, 'Daily Cash Shift Register', '/cash/closers', 'audit', 4, 1, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(42, 'Day-End Reconciliation', '/reconciliation', 'file-protect', 4, 2, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(43, 'Expense Management', '/accounting/expenses', 'red-envelope', 4, 3, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(44, 'Chart of Accounts', '/accounting/accounts', 'apartment', 4, 4, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(45, 'Financial Statement Reports', '/reports', 'line-chart', 4, 5, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),

-- Settings & Admin Sub-Menus
(51, 'Role Management', '/roles', 'safety', 5, 1, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(52, 'Menu Management', '/menus', 'menu', 5, 2, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(53, 'Role Menu Permissions', '/roles/permissions', 'key', 5, 3, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(54, 'Pharmacy Configurations', '/settings/configs', 'tool', 5, 4, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(55, 'Doctors Directory', '/doctors', 'idcard', 5, 5, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_no) DO UPDATE SET
    title = EXCLUDED.title,
    path = EXCLUDED.path,
    icon = EXCLUDED.icon,
    parent_menu_no = EXCLUDED.parent_menu_no,
    order_no = EXCLUDED.order_no,
    status = EXCLUDED.status,
    updated_at = CURRENT_TIMESTAMP;

-- 6. Seed full menu permissions for existing Manager / Admin roles
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
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;
