-- ============================================================================
-- Migration: 20261029_17_add_admin_system_role_and_universal_access.sql
-- Description:
--   1. Creates protected system role 'Admin' (role_no: 1) for developer/platform super admin.
--   2. Grants all system permissions and all sys_menus permissions to role 1.
--   3. Revokes developer-only menu creation (MENU_1904, MENU_1905) from store managers (role 9).
--   4. Assigns user 'asibul' (phone: asibul / email: asibul@infoaidtech.net) to role 1.
--   5. Grants user 'asibul' access to all current branches in sys_user_pharmacy_access.
--   6. Re-aligns seq_role_menu_perm_no.
-- ============================================================================

-- 1. Create protected system role 'Admin' (role_no: 1)
INSERT INTO sys_roles (role_no, pharmacy_no, branch_no, role_name, alias, description, is_system, status, created_at, updated_at)
VALUES (1, 1, 1, 'Admin', 'Admin', 'Platform Super Admin & Developer with universal store access', TRUE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (role_no) DO UPDATE SET
    role_name = 'Admin',
    alias = 'Admin',
    description = 'Platform Super Admin & Developer with universal store access',
    is_system = TRUE,
    status = TRUE,
    updated_at = CURRENT_TIMESTAMP;

-- 2. Grant all permissions in sys_permissions to Admin (role 1)
INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
SELECT 1, permission_no, 1, 1 FROM sys_permissions
ON CONFLICT DO NOTHING;

-- 3. Grant full menu permissions for ALL active menus to Admin (role 1)
INSERT INTO sys_role_menu_permissions (role_menu_perm_no, pharmacy_no, role_no, menu_no, can_view, can_create, can_edit, can_delete, created_at, updated_at)
SELECT nextval('seq_role_menu_perm_no'), 1, 1, m.menu_no, TRUE, TRUE, TRUE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_menus m
WHERE NOT EXISTS (
    SELECT 1 FROM sys_role_menu_permissions r WHERE r.role_no = 1 AND r.pharmacy_no = 1 AND r.menu_no = m.menu_no
);

UPDATE sys_role_menu_permissions
SET can_view = TRUE, can_create = TRUE, can_edit = TRUE, can_delete = TRUE, updated_at = CURRENT_TIMESTAMP
WHERE role_no = 1;

-- 4. Revoke developer-only menu creation (MENU_1904, MENU_1905) from store managers (role 9)
UPDATE sys_role_menu_permissions
SET can_view = FALSE, can_create = FALSE, can_edit = FALSE, can_delete = FALSE, updated_at = CURRENT_TIMESTAMP
WHERE role_no = 9 AND menu_no IN (
    SELECT menu_no FROM sys_menus WHERE menu_code IN ('MENU_1904', 'MENU_1905')
);

-- 5. Assign user 'asibul' to role 1 (Admin)
UPDATE sys_users
SET role_no = 1,
    updated_at = CURRENT_TIMESTAMP
WHERE phone = 'asibul' OR email = 'asibul@infoaidtech.net';

-- 6. Ensure user 'asibul' has access to all branches across all pharmacies in sys_user_pharmacy_access
INSERT INTO sys_user_pharmacy_access (user_pharmacy_access_no, user_no, pharmacy_no, branch_no, is_default, created_at)
SELECT 
    COALESCE((SELECT MAX(user_pharmacy_access_no) FROM sys_user_pharmacy_access), 0) + ROW_NUMBER() OVER (),
    u.user_no,
    b.pharmacy_no,
    b.branch_no,
    (b.pharmacy_no = 1 AND b.branch_no = 1),
    CURRENT_TIMESTAMP
FROM sys_users u
CROSS JOIN sys_branches b
WHERE (u.phone = 'asibul' OR u.email = 'asibul@infoaidtech.net')
  AND NOT EXISTS (
      SELECT 1 FROM sys_user_pharmacy_access a
      WHERE a.user_no = u.user_no AND a.pharmacy_no = b.pharmacy_no AND a.branch_no = b.branch_no
  );

-- 7. Re-align seq_role_menu_perm_no
SELECT setval('seq_role_menu_perm_no', GREATEST(COALESCE((SELECT MAX(role_menu_perm_no) FROM sys_role_menu_permissions), 0) + 1, 1), false);
