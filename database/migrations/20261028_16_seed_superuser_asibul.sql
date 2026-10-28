-- ============================================================================
-- Migration: 20261028_16_seed_superuser_asibul.sql
-- Description:
--   Seeds superuser account 'asibul' with PMS Manager (Role 9 - Full Access)
--   and credentials (username: asibul, password: infoaidtech@pms#123).
-- ============================================================================

DO $$
DECLARE
    v_user_no BIGINT;
    v_access_no BIGINT;
    v_password_hash VARCHAR(255) := '$2a$11$GR7mRUuMjOYmm40oalSsguNyq8eedHHsrL84aPiqrLi1drj2f0PWK';
BEGIN
    -- Check if user already exists by phone or email
    SELECT user_no INTO v_user_no
    FROM sys_users
    WHERE phone = 'asibul' OR email = 'asibul@infoaidtech.net'
    LIMIT 1;

    IF v_user_no IS NOT NULL THEN
        UPDATE sys_users
        SET phone = 'asibul',
            email = 'asibul@infoaidtech.net',
            password_hash = v_password_hash,
            role_no = 9,
            is_active = TRUE,
            updated_at = CURRENT_TIMESTAMP
        WHERE user_no = v_user_no;
    ELSE
        -- Choose user_no: 3 if available, otherwise MAX + 1
        IF NOT EXISTS (SELECT 1 FROM sys_users WHERE user_no = 3) THEN
            v_user_no := 3;
        ELSE
            SELECT COALESCE(MAX(user_no), 0) + 1 INTO v_user_no FROM sys_users;
        END IF;

        INSERT INTO sys_users (
            user_no, uuid, pharmacy_no, branch_no, role_no,
            first_name, last_name, phone, email, password_hash,
            gender, is_active, created_at, updated_at
        ) VALUES (
            v_user_no, 'd3eebc99-9c0b-4ef8-bb6d-6bb9bd380d44', 1, 1, 9,
            'Asibul', 'Hasan', 'asibul', 'asibul@infoaidtech.net', v_password_hash,
            1, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
        );
    END IF;

    -- Ensure pharmacy access for the user
    IF NOT EXISTS (
        SELECT 1 FROM sys_user_pharmacy_access
        WHERE user_no = v_user_no AND pharmacy_no = 1 AND branch_no = 1
    ) THEN
        SELECT COALESCE(MAX(user_pharmacy_access_no), 0) + 1 INTO v_access_no FROM sys_user_pharmacy_access;
        INSERT INTO sys_user_pharmacy_access (
            user_pharmacy_access_no, user_no, pharmacy_no, branch_no, is_default, created_at
        ) VALUES (
            v_access_no, v_user_no, 1, 1, TRUE, CURRENT_TIMESTAMP
        );
    END IF;
END $$;
