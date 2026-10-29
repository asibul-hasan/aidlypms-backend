-- Protected capabilities are independent of editable role names.
CREATE TABLE IF NOT EXISTS sys_role_capabilities (
    role_no BIGINT PRIMARY KEY REFERENCES sys_roles(role_no) ON DELETE RESTRICT,
    access_scope VARCHAR(30) NOT NULL CHECK (access_scope IN ('PLATFORM_OWNER', 'STORE_MANAGER'))
);
INSERT INTO sys_role_capabilities (role_no, access_scope)
SELECT role_no, CASE WHEN role_no = 1 THEN 'PLATFORM_OWNER' ELSE 'STORE_MANAGER' END
FROM sys_roles WHERE role_no IN (1, 9) AND is_system = TRUE
ON CONFLICT (role_no) DO NOTHING;

CREATE TABLE IF NOT EXISTS sys_auth_sessions (
    session_id UUID PRIMARY KEY,
    user_no BIGINT NOT NULL REFERENCES sys_users(user_no) ON DELETE CASCADE,
    role_no BIGINT NOT NULL REFERENCES sys_roles(role_no) ON DELETE CASCADE,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE CASCADE,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE CASCADE,
    user_updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    revoked_at TIMESTAMP WITH TIME ZONE
);
CREATE INDEX IF NOT EXISTS idx_auth_sessions_user ON sys_auth_sessions (user_no);
CREATE INDEX IF NOT EXISTS idx_auth_sessions_expiry ON sys_auth_sessions (expires_at);
CREATE INDEX IF NOT EXISTS idx_sale_returns_completed_invoice ON sale_returns (sale_invoice_no) WHERE return_status = 2;
CREATE INDEX IF NOT EXISTS idx_sale_return_items_sale_line ON sale_return_items (sale_invoice_item_no, sale_return_no) INCLUDE (total_qty_pcs);
CREATE INDEX IF NOT EXISTS idx_acc_ledger_tenant_day ON acc_transaction_ledger (pharmacy_no, branch_no, transaction_date, ledger_no);
CREATE INDEX IF NOT EXISTS idx_sys_users_tenant_list ON sys_users (pharmacy_no, branch_no, user_no DESC);
CREATE INDEX IF NOT EXISTS idx_sys_user_access_user_branch ON sys_user_pharmacy_access (user_no, branch_no);
CREATE INDEX IF NOT EXISTS idx_sys_users_email_lookup ON sys_users ((lower(email)));
CREATE INDEX IF NOT EXISTS idx_sys_users_phone_digits_lookup ON sys_users ((regexp_replace(phone, '[^0-9]', '', 'g')));
CREATE INDEX IF NOT EXISTS idx_sys_users_email_localpart_lookup ON sys_users ((lower(split_part(email, '@', 1))));
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE SEQUENCE IF NOT EXISTS seq_sys_user_no;
SELECT setval('seq_sys_user_no', GREATEST(COALESCE((SELECT MAX(user_no) FROM sys_users), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_sys_user_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_sys_role_no;
SELECT setval('seq_sys_role_no', GREATEST(COALESCE((SELECT MAX(role_no) FROM sys_roles), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_sys_role_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_sys_user_access_no;
SELECT setval('seq_sys_user_access_no', GREATEST(COALESCE((SELECT MAX(user_pharmacy_access_no) FROM sys_user_pharmacy_access), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_sys_user_access_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_company_no;
SELECT setval('seq_company_no', GREATEST(COALESCE((SELECT MAX(company_no) FROM prod_companies), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_company_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_generic_no;
SELECT setval('seq_generic_no', GREATEST(COALESCE((SELECT MAX(generic_no) FROM prod_generics), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_generic_no)), false);

CREATE SEQUENCE IF NOT EXISTS seq_sys_pharmacy_no;
SELECT setval('seq_sys_pharmacy_no', GREATEST(COALESCE((SELECT MAX(pharmacy_no) FROM sys_pharmacies), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_sys_pharmacy_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_sys_branch_no;
SELECT setval('seq_sys_branch_no', GREATEST(COALESCE((SELECT MAX(branch_no) FROM sys_branches), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_sys_branch_no)), false);


DO $$
DECLARE extension_schema TEXT;
BEGIN
    SELECT n.nspname INTO extension_schema FROM pg_extension e JOIN pg_namespace n ON n.oid = e.extnamespace WHERE e.extname = 'pg_trgm';
    EXECUTE format('CREATE INDEX IF NOT EXISTS idx_prod_products_active_name_trgm ON %I.prod_products USING gin (product_name %I.gin_trgm_ops) WHERE is_active = TRUE', current_schema(), extension_schema);
    EXECUTE format('CREATE INDEX IF NOT EXISTS idx_prod_generics_name_trgm ON %I.prod_generics USING gin (name %I.gin_trgm_ops)', current_schema(), extension_schema);
    EXECUTE format('CREATE INDEX IF NOT EXISTS idx_prod_products_barcode_trgm ON %I.prod_products USING gin (barcode_value %I.gin_trgm_ops)', current_schema(), extension_schema);
END $$;

CREATE INDEX IF NOT EXISTS idx_sale_invoices_tenant_cursor ON sale_invoices (pharmacy_no, branch_no, sale_timestamp DESC, sale_invoice_no DESC);
-- Remove only proven duplicates of constraints; preserve the constraints and any differently defined deployed index.
DO $$
DECLARE candidate RECORD;
BEGIN
    FOR candidate IN SELECT * FROM (VALUES
        ('idx_prod_products_lookup', 'uq_product_branch_name'),
        ('idx_sale_invoices_sales_no', 'uq_sale_number'),
        ('idx_pur_invoices_inv_no', 'uq_pur_inventory_no'),
        ('idx_supp_suppliers_phone', 'uq_supp_phone'),
        ('idx_cust_customers_phone', 'uq_cust_phone')
    ) AS pairs(index_name, constraint_name)
    LOOP
        IF EXISTS (
            SELECT 1 FROM pg_class ordinary JOIN pg_namespace ns ON ns.oid = ordinary.relnamespace
            JOIN pg_index oi ON oi.indexrelid = ordinary.oid
            JOIN pg_constraint con ON con.connamespace = ns.oid AND con.conname = candidate.constraint_name AND con.contype = 'u'
            JOIN pg_index ui ON ui.indexrelid = con.conindid
            WHERE ns.nspname = current_schema() AND ordinary.relname = candidate.index_name
              AND NOT oi.indisunique AND oi.indrelid = ui.indrelid AND oi.indkey = ui.indkey
              AND oi.indclass = ui.indclass AND oi.indcollation = ui.indcollation AND oi.indoption = ui.indoption
              AND oi.indpred IS NULL AND oi.indexprs IS NULL AND ui.indpred IS NULL AND ui.indexprs IS NULL
        ) THEN EXECUTE format('DROP INDEX %I.%I', current_schema(), candidate.index_name);
        END IF;
    END LOOP;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_class old_index JOIN pg_namespace ns ON ns.oid = old_index.relnamespace
        JOIN pg_index oi ON oi.indexrelid = old_index.oid
        JOIN pg_class new_index ON new_index.relnamespace = ns.oid AND new_index.relname = 'idx_sale_invoices_tenant_cursor'
        JOIN pg_index ni ON ni.indexrelid = new_index.oid
        WHERE ns.nspname = current_schema() AND old_index.relname = 'idx_sale_invoices_lookup'
          AND NOT oi.indisunique AND oi.indrelid = ni.indrelid AND oi.indnatts = 3 AND ni.indnatts = 4
          AND oi.indkey[0] = ni.indkey[0] AND oi.indkey[1] = ni.indkey[1] AND oi.indkey[2] = ni.indkey[2]
          AND oi.indclass[0] = ni.indclass[0] AND oi.indclass[1] = ni.indclass[1] AND oi.indclass[2] = ni.indclass[2]
          AND oi.indcollation[0] = ni.indcollation[0] AND oi.indcollation[1] = ni.indcollation[1] AND oi.indcollation[2] = ni.indcollation[2]
          AND oi.indoption[0] = ni.indoption[0] AND oi.indoption[1] = ni.indoption[1] AND oi.indoption[2] = ni.indoption[2]
          AND oi.indpred IS NULL AND oi.indexprs IS NULL
    ) THEN EXECUTE format('DROP INDEX %I.idx_sale_invoices_lookup', current_schema());
    END IF;
END $$;

-- Sequence allocation for all remaining operational and setup records.
CREATE SEQUENCE IF NOT EXISTS seq_additional_balance_no;
SELECT setval('seq_additional_balance_no', GREATEST(COALESCE((SELECT MAX(additional_balance_no) FROM acc_additional_balances), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_additional_balance_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_bulk_sms_config_no;
SELECT setval('seq_bulk_sms_config_no', GREATEST(COALESCE((SELECT MAX(bulk_sms_config_no) FROM cfg_bulk_sms_configs), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_bulk_sms_config_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_doc_sequence_no;
SELECT setval('seq_doc_sequence_no', GREATEST(COALESCE((SELECT MAX(sequence_no) FROM sys_doc_sequence), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_doc_sequence_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_doctor_no;
SELECT setval('seq_doctor_no', GREATEST(COALESCE((SELECT MAX(doctor_no) FROM doc_doctors), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_doctor_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_expense_category_no;
SELECT setval('seq_expense_category_no', GREATEST(COALESCE((SELECT MAX(expense_category_no) FROM acc_expense_categories), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_expense_category_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_income_category_no;
SELECT setval('seq_income_category_no', GREATEST(COALESCE((SELECT MAX(income_category_no) FROM acc_income_categories), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_income_category_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_invoice_config_no;
SELECT setval('seq_invoice_config_no', GREATEST(COALESCE((SELECT MAX(invoice_config_no) FROM cfg_invoice_configs), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_invoice_config_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_loyalty_config_no;
SELECT setval('seq_loyalty_config_no', GREATEST(COALESCE((SELECT MAX(loyalty_config_no) FROM cfg_loyalty_configs), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_loyalty_config_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_prescription_item_no;
SELECT setval('seq_prescription_item_no', GREATEST(COALESCE((SELECT MAX(prescription_item_no) FROM doc_prescription_items), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_prescription_item_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_prescription_no;
SELECT setval('seq_prescription_no', GREATEST(COALESCE((SELECT MAX(prescription_no) FROM doc_prescriptions), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_prescription_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_prescription_setting_no;
SELECT setval('seq_prescription_setting_no', GREATEST(COALESCE((SELECT MAX(prescription_setting_no) FROM doc_prescription_settings), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_prescription_setting_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_product_config_no;
SELECT setval('seq_product_config_no', GREATEST(COALESCE((SELECT MAX(product_config_no) FROM cfg_product_configs), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_product_config_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_reconciliation_no;
SELECT setval('seq_reconciliation_no', GREATEST(COALESCE((SELECT MAX(reconciliation_no) FROM acc_reconciliations), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_reconciliation_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_requisition_item_no;
SELECT setval('seq_requisition_item_no', GREATEST(COALESCE((SELECT MAX(requisition_item_no) FROM prod_product_requisition_items), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_requisition_item_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_requisition_no;
SELECT setval('seq_requisition_no', GREATEST(COALESCE((SELECT MAX(requisition_no) FROM prod_product_requisitions), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_requisition_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_sale_config_no;
SELECT setval('seq_sale_config_no', GREATEST(COALESCE((SELECT MAX(sale_config_no) FROM cfg_sale_configs), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_sale_config_no)), false);
CREATE SEQUENCE IF NOT EXISTS seq_supplier_no;
SELECT setval('seq_supplier_no', GREATEST(COALESCE((SELECT MAX(supplier_no) FROM supp_suppliers), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_supplier_no)), false);
CREATE INDEX IF NOT EXISTS idx_sys_users_role ON sys_users (role_no);

-- Imported legacy records may be ahead of these already-declared sequences.
SELECT setval('seq_product_no', GREATEST(COALESCE((SELECT MAX(product_no) FROM prod_products), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_product_no)), false);
SELECT setval('seq_prod_batch_no', GREATEST(COALESCE((SELECT MAX(batch_no) FROM prod_product_batches), 0) + 1, (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END FROM seq_prod_batch_no)), false);
