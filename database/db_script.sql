-- ============================================================================
-- PHARMACY MANAGEMENT & POS SYSTEM (PMS)
-- Target Database: PostgreSQL 14+ / 15+ / 16+
-- Architecture:
--   1. Module Code Prefix for every table (sys_, prod_, pur_, sale_, acc_, etc.)
--   2. Conventional PK/FK naming (customer_no, user_no, product_no, etc. instead of id)
--   3. Universal Multi-Tenant Multi-Branch Standard (Option A - Pure Pharmacy Mode):
--      pharmacy_no BIGINT NOT NULL (The Pharmacy Business / Parent Tenant Entity)
--      branch_no BIGINT NOT NULL (Physical Pharmacy Store / Branch Outlet)
--   4. NO ENUM types (Frontend handles label/dropdown lists; DB stores numeric SMALLINT values)
--   5. NO DEFAULT values (All timestamps, booleans, status numbers supplied by app)
--   6. NO DEFAULT number system (No SERIAL/BIGSERIAL/Sequences; explicit BIGINT IDs)
--   7. Arbitrary precision NUMERIC (NO 2-digit decimal limitation; whatever frontend sends is stored)
--   8. NO DB-level calculations (No triggers, no generated columns doing c = a + b; all handled by frontend/middleware)
--   9. Strict relational integrity, foreign keys, and partitioned B-Tree indexes
--  10. Strict financial preservation: ON DELETE RESTRICT on all financial and audit tables
-- ============================================================================

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "citext";

-- ============================================================================
-- 1. FRONTEND LIST / LOOKUP CONSTANT VALUES DICTIONARY
-- (Stored as numeric SMALLINT in DB; labels, codes, and dropdowns handled by frontend)
-- ============================================================================
-- Business Mode:
--   1: Retail, 2: Wholesale
-- Discount Type:
--   1: Percentage, 2: Flat
-- Payment Method:
--   1: Cash, 2: Bank, 3: bKash, 4: Nagad, 5: Rocket, 6: Card, 7: Due, 8: Split
-- Purchase Calculation Mode:
--   1: Default, 2: Unit TP/VAT, 3: Net Value, 4: Opening Stock
-- Inventory Transaction Type (inv_stock_movement):
--   1: Purchase (+), 2: Opening Stock (+), 3: Customer Return (+), 4: POS Sale (-),
--   5: Supplier Return (-), 6: Transfer In (+), 7: Transfer Out (-), 8: Adjustment (+/-)
-- Financial Transaction Type (acc_transaction_ledger):
--   1: POS Sale, 2: Sale Return, 3: Purchase Bill, 4: Supplier Return, 5: Operating Expense,
--   6: Miscellaneous Income, 7: Customer Due Collection, 8: Supplier Payment, 9: Inter-Account Transfer,
--   10: Capital Balance Infusion, 11: COGS & Inventory Relief, 12: Stock Adjustment Write-Off
-- Account Category (acc_transaction_accounts):
--   1: ASSET, 2: LIABILITY, 3: EQUITY, 4: REVENUE, 5: EXPENSE
-- Account Type:
--   1: Cash In Hand, 2: Bank Account, 3: bKash Personal, 4: bKash Merchant, 5: Nagad, 6: Rocket, 7: Card POS Machine, 99: Other
-- Document Status (sale_invoices, pur_purchase_invoices, sale_returns, pur_purchase_returns):
--   1: Draft/Held, 2: Confirmed/Posted, 3: Voided/Cancelled, 4: Partially Returned, 5: Fully Returned
-- Transfer Status (inv_transfers):
--   1: Draft, 2: In Transit, 3: Received, 4: Rejected
-- Adjustment Status (inv_adjustments):
--   1: Draft, 2: Approved/Posted, 3: Rejected
-- Return Disposition (sale_return_items):
--   1: Restock to Batch, 2: Quarantine, 3: Destroy/Waste
-- Cash Received Status:
--   1: Pending, 2: Received
-- Customer Tier:
--   1: Regular, 2: Wholesale Client, 3: VIP, 4: Staff, 5: Doctor Referral
-- Gender:
--   1: Male, 2: Female, 3: Other
-- Medicine Form:
--   1: Tablet, 2: Capsule, 3: Syrup, 4: Suspension, 5: Drop, 6: Injection, 7: Ointment, 8: Gel, 9: Cream, 10: Inhaler, 11: Suppository, 12: Powder, 99: Other
-- Unit Type:
--   1: PIECE, 2: STRIP, 3: BOX, 4: BOTTLE, 5: TUBE, 6: VIAL, 7: AMPOULE, 8: PACKET
-- Paper Type:
--   1: 80mm, 2: 58mm, 3: A4
-- Subscription Status:
--   1: Trial, 2: Active, 3: Overdue, 4: Suspended, 5: Cancelled
-- Billing Cycle:
--   1: Monthly, 2: Yearly
-- Outbox Status (sys_event_outbox):
--   1: Pending, 2: In Process, 3: Completed, 4: Failed Dead Letter
-- ============================================================================

-- ============================================================================
-- 2. MODULE: [sys_] CORE SYSTEM, PHARMACY TENANCY, RBAC & INFRASTRUCTURE
-- ============================================================================

-- Pharmacy Business Organization / Parent Tenant Entity
CREATE TABLE IF NOT EXISTS sys_pharmacies (
    pharmacy_no BIGINT PRIMARY KEY,
    uuid UUID UNIQUE NOT NULL,
    name VARCHAR(200) NOT NULL,
    trade_license_no VARCHAR(100),
    tin_no VARCHAR(100),
    bin_no VARCHAR(100),
    drug_license_no VARCHAR(100),
    contact_phone VARCHAR(20) NOT NULL,
    contact_email VARCHAR(100),
    address TEXT NOT NULL,
    area VARCHAR(100),
    district VARCHAR(100) NOT NULL,
    postal_code VARCHAR(20),
    is_active BOOLEAN NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL
);

-- Physical Store Branches / Outlets
CREATE TABLE IF NOT EXISTS sys_branches (
    branch_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    uuid UUID UNIQUE NOT NULL,
    name VARCHAR(200) NOT NULL,
    drug_license_no VARCHAR(100),
    contact_phone VARCHAR(20) NOT NULL,
    contact_email VARCHAR(100),
    address TEXT NOT NULL,
    area VARCHAR(100),
    district VARCHAR(100) NOT NULL,
    postal_code VARCHAR(20),
    is_active BOOLEAN NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_sys_branch UNIQUE (pharmacy_no, name)
);

-- Application System Menus (Global Definition, No Company/Org Fields)
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

-- Roles
CREATE TABLE IF NOT EXISTS sys_roles (
    role_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    role_name VARCHAR(150),
    alias VARCHAR(100) NOT NULL,
    description TEXT,
    is_system BOOLEAN NOT NULL,
    status BOOLEAN NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_role_per_branch UNIQUE (pharmacy_no, branch_no, alias)
);

-- Granular Permissions Nodes (Global Definition)
CREATE TABLE IF NOT EXISTS sys_permissions (
    permission_no BIGINT PRIMARY KEY,
    code VARCHAR(100) UNIQUE NOT NULL,
    module_group VARCHAR(100) NOT NULL,
    name VARCHAR(150) NOT NULL,
    description TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL
);

-- Role Permissions Join Table
CREATE TABLE IF NOT EXISTS sys_role_permissions (
    role_no BIGINT NOT NULL REFERENCES sys_roles(role_no) ON DELETE CASCADE,
    permission_no BIGINT NOT NULL REFERENCES sys_permissions(permission_no) ON DELETE CASCADE,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    PRIMARY KEY (role_no, permission_no)
);

-- Role Menu Permissions Join Table (Pharmacy Scoped, No Branch Required)
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

-- Users / Representatives / Pharmacists
CREATE TABLE IF NOT EXISTS sys_users (
    user_no BIGINT PRIMARY KEY,
    uuid UUID UNIQUE NOT NULL,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    role_no BIGINT NOT NULL REFERENCES sys_roles(role_no) ON DELETE RESTRICT,
    first_name VARCHAR(100) NOT NULL,
    last_name VARCHAR(100) NOT NULL,
    phone VARCHAR(20) NOT NULL,
    email VARCHAR(150),
    password_hash VARCHAR(255) NOT NULL,
    gender SMALLINT,
    is_active BOOLEAN NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_sys_user_phone UNIQUE (phone)
);

-- User Multi-Branch Access
CREATE TABLE IF NOT EXISTS sys_user_pharmacy_access (
    user_pharmacy_access_no BIGINT PRIMARY KEY,
    user_no BIGINT NOT NULL REFERENCES sys_users(user_no) ON DELETE CASCADE,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    is_default BOOLEAN NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_sys_user_pharmacy UNIQUE (pharmacy_no, branch_no, user_no)
);

-- Gap-Safe Document Sequence Generator
CREATE TABLE IF NOT EXISTS sys_doc_sequence (
    sequence_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    doc_type VARCHAR(50) NOT NULL,
    fin_year VARCHAR(10) NOT NULL,
    last_seq_number BIGINT NOT NULL,
    prefix_format VARCHAR(50) NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_doc_sequence UNIQUE (pharmacy_no, branch_no, doc_type, fin_year)
);

-- Event Outbox for Asynchronous Side-Effects (SMS, Notifications, Third-Party Sync)
CREATE TABLE IF NOT EXISTS sys_event_outbox (
    event_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    aggregate_type VARCHAR(50) NOT NULL,
    aggregate_id BIGINT NOT NULL,
    event_type VARCHAR(100) NOT NULL,
    payload TEXT NOT NULL,
    status SMALLINT NOT NULL,
    retry_count INT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    published_at TIMESTAMP WITH TIME ZONE
);

-- API Idempotency Cache Log
CREATE TABLE IF NOT EXISTS sys_idempotency_log (
    idempotency_no BIGINT PRIMARY KEY,
    idempotency_key VARCHAR(128) NOT NULL,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    endpoint VARCHAR(255) NOT NULL,
    request_hash VARCHAR(64) NOT NULL,
    response_status INT,
    response_payload TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_idempotency_key UNIQUE (pharmacy_no, idempotency_key)
);

-- Enterprise Audit Trail
CREATE TABLE IF NOT EXISTS sys_audit_logs (
    audit_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    table_name VARCHAR(100) NOT NULL,
    record_id BIGINT NOT NULL,
    action VARCHAR(20) NOT NULL,
    old_data TEXT,
    new_data TEXT,
    ip_address VARCHAR(45),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL
);

-- ============================================================================
-- 3. MODULE: [prod_] & [inv_] PRODUCTS & INVENTORY OPERATIONS
-- ============================================================================

-- Pharmaceutical Manufacturers (e.g. Beximco, Square, Incepta, Renata)
CREATE TABLE IF NOT EXISTS prod_companies (
    company_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    name VARCHAR(200) NOT NULL,
    is_active BOOLEAN NOT NULL,
    deleted_at TIMESTAMP WITH TIME ZONE,
    deleted_by BIGINT,
    deletion_reason TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_pharmacy_branch_mfr UNIQUE (pharmacy_no, branch_no, name)
);

-- Drug Chemical Formulations
CREATE TABLE IF NOT EXISTS prod_generics (
    generic_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    name VARCHAR(200) NOT NULL,
    is_active BOOLEAN NOT NULL,
    deleted_at TIMESTAMP WITH TIME ZONE,
    deleted_by BIGINT,
    deletion_reason TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_generic_branch_name UNIQUE (pharmacy_no, branch_no, name)
);

-- Product Master Catalog
CREATE TABLE IF NOT EXISTS prod_products (
    product_no BIGINT PRIMARY KEY,
    uuid UUID UNIQUE NOT NULL,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    company_no BIGINT NOT NULL REFERENCES prod_companies(company_no) ON DELETE RESTRICT,
    generic_no BIGINT REFERENCES prod_generics(generic_no) ON DELETE SET NULL,
    product_name VARCHAR(255) NOT NULL,
    strength VARCHAR(100),
    volume VARCHAR(100),
    medicine_type SMALLINT NOT NULL,
    unit_type SMALLINT NOT NULL,
    quantity_per_box INT NOT NULL,
    cost_per_box NUMERIC NOT NULL,
    sale_price_per_box NUMERIC NOT NULL,
    avg_purchase_price_per_piece NUMERIC NOT NULL,
    purchase_price_per_piece NUMERIC NOT NULL,
    sale_price_per_piece NUMERIC NOT NULL,
    wholesale_price_per_box NUMERIC NOT NULL,
    wholesale_price_per_piece NUMERIC NOT NULL,
    country_of_origin VARCHAR(100),
    barcode_value VARCHAR(100),
    is_medicine BOOLEAN NOT NULL,
    is_prescription_required BOOLEAN NOT NULL,
    is_narcotic BOOLEAN NOT NULL,
    min_stock_qty_pcs INT NOT NULL,
    rak_number VARCHAR(50),
    dar_number VARCHAR(50),
    drug_schedule VARCHAR(50),
    is_active BOOLEAN NOT NULL,
    deleted_at TIMESTAMP WITH TIME ZONE,
    deleted_by BIGINT,
    deletion_reason TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_product_branch_name UNIQUE (pharmacy_no, branch_no, product_name)
);

-- Product Batches & Stock Ledger
CREATE TABLE IF NOT EXISTS prod_product_batches (
    batch_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    product_no BIGINT NOT NULL REFERENCES prod_products(product_no) ON DELETE RESTRICT,
    batch_number VARCHAR(100) NOT NULL,
    expiry_date DATE NOT NULL,
    box_quantity INT NOT NULL,
    quantity_in_box INT NOT NULL,
    total_quantity_pcs INT NOT NULL,
    cost_per_box NUMERIC NOT NULL,
    sale_price_per_box NUMERIC NOT NULL,
    vat_percent NUMERIC NOT NULL,
    barcode_value VARCHAR(100),
    is_expired BOOLEAN NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_product_batch UNIQUE (pharmacy_no, branch_no, product_no, batch_number)
);

-- Universal Inventory Movement Ledger (Single Table Core for ALL Physical Stock Changes)
CREATE TABLE IF NOT EXISTS inv_stock_movement (
    movement_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    product_no BIGINT NOT NULL REFERENCES prod_products(product_no) ON DELETE RESTRICT,
    batch_no BIGINT REFERENCES prod_product_batches(batch_no) ON DELETE SET NULL,
    movement_type SMALLINT NOT NULL,
    reference_doc_type VARCHAR(50) NOT NULL,
    reference_doc_no VARCHAR(100) NOT NULL,
    reference_doc_id BIGINT NOT NULL,
    qty_pcs INT NOT NULL,
    unit_cost NUMERIC(20, 6) NOT NULL,
    balance_after_pcs INT NOT NULL,
    remarks TEXT,
    created_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL
);

-- Inter-Branch Stock Transfers (Header)
CREATE TABLE IF NOT EXISTS inv_transfers (
    transfer_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    transfer_number VARCHAR(100) NOT NULL,
    from_branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    to_branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    total_items INT NOT NULL,
    total_cost_amount NUMERIC(20, 4) NOT NULL,
    transfer_status SMALLINT NOT NULL,
    note TEXT,
    sent_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    received_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    sent_at TIMESTAMP WITH TIME ZONE NOT NULL,
    received_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_inv_transfer_number UNIQUE (pharmacy_no, transfer_number),
    CONSTRAINT chk_inv_diff_branch CHECK (from_branch_no <> to_branch_no)
);

-- Inter-Branch Stock Transfer Items (Lines)
CREATE TABLE IF NOT EXISTS inv_transfer_items (
    transfer_item_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    transfer_no BIGINT NOT NULL REFERENCES inv_transfers(transfer_no) ON DELETE CASCADE,
    product_no BIGINT NOT NULL REFERENCES prod_products(product_no) ON DELETE RESTRICT,
    batch_no BIGINT REFERENCES prod_product_batches(batch_no) ON DELETE RESTRICT,
    batch_number VARCHAR(100),
    qty_pcs INT NOT NULL,
    unit_cost NUMERIC(20, 4) NOT NULL,
    total_cost NUMERIC(20, 4) NOT NULL,
    expiry_date DATE,
    CONSTRAINT chk_transfer_qty CHECK (qty_pcs > 0)
);

-- Inventory Stock Adjustments & Physical Stocktake (Header)
CREATE TABLE IF NOT EXISTS inv_adjustments (
    adjustment_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    adjustment_number VARCHAR(100) NOT NULL,
    adjustment_type SMALLINT NOT NULL,
    total_items INT NOT NULL,
    net_cost_adjustment NUMERIC(20, 4) NOT NULL,
    adjustment_status SMALLINT NOT NULL,
    approved_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    note TEXT,
    adjustment_date TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_inv_adjustment_number UNIQUE (pharmacy_no, branch_no, adjustment_number)
);

-- Inventory Stock Adjustment Items (Lines)
CREATE TABLE IF NOT EXISTS inv_adjustment_items (
    adjustment_item_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    adjustment_no BIGINT NOT NULL REFERENCES inv_adjustments(adjustment_no) ON DELETE CASCADE,
    product_no BIGINT NOT NULL REFERENCES prod_products(product_no) ON DELETE RESTRICT,
    batch_no BIGINT REFERENCES prod_product_batches(batch_no) ON DELETE RESTRICT,
    batch_number VARCHAR(100),
    counted_qty_pcs INT NOT NULL,
    system_qty_pcs INT NOT NULL,
    difference_qty_pcs INT NOT NULL,
    unit_cost NUMERIC(20, 4) NOT NULL,
    total_cost_impact NUMERIC(20, 4) NOT NULL,
    reason_code SMALLINT
);

-- Product Requisitions
CREATE TABLE IF NOT EXISTS prod_product_requisitions (
    requisition_no BIGINT PRIMARY KEY,
    requisition_code VARCHAR(100) NOT NULL,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    company_no BIGINT REFERENCES prod_companies(company_no) ON DELETE SET NULL,
    supplier_no BIGINT,
    total_selected_items INT NOT NULL,
    total_cost NUMERIC NOT NULL,
    is_as_per_sale BOOLEAN NOT NULL,
    sale_velocity_start_date DATE,
    sale_velocity_end_date DATE,
    status VARCHAR(50) NOT NULL,
    created_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_branch_req_no UNIQUE (pharmacy_no, branch_no, requisition_code)
);

-- Product Requisition Items
CREATE TABLE IF NOT EXISTS prod_product_requisition_items (
    requisition_item_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    requisition_no BIGINT NOT NULL REFERENCES prod_product_requisitions(requisition_no) ON DELETE CASCADE,
    product_no BIGINT NOT NULL REFERENCES prod_products(product_no) ON DELETE RESTRICT,
    box_qty INT NOT NULL,
    min_qty_pcs INT NOT NULL,
    sale_qty_6mth_pcs INT NOT NULL,
    quantity_in_box INT NOT NULL,
    btp NUMERIC NOT NULL,
    vat NUMERIC NOT NULL,
    btp_vat NUMERIC NOT NULL,
    total_price NUMERIC NOT NULL,
    is_selected BOOLEAN NOT NULL
);

-- ============================================================================
-- 4. MODULE: [supp_] & [pur_] SUPPLIERS & PURCHASES
-- ============================================================================

-- Suppliers
CREATE TABLE IF NOT EXISTS supp_suppliers (
    supplier_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    name VARCHAR(200) NOT NULL,
    phone VARCHAR(20) NOT NULL,
    note TEXT,
    current_due_balance NUMERIC NOT NULL,
    is_active BOOLEAN NOT NULL,
    deleted_at TIMESTAMP WITH TIME ZONE,
    deleted_by BIGINT,
    deletion_reason TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_supp_phone UNIQUE (pharmacy_no, branch_no, phone)
);

ALTER TABLE prod_product_requisitions DROP CONSTRAINT IF EXISTS fk_req_supplier;
ALTER TABLE prod_product_requisitions 
ADD CONSTRAINT fk_req_supplier FOREIGN KEY (supplier_no) REFERENCES supp_suppliers(supplier_no) ON DELETE SET NULL;

-- Purchase Invoices (Receiving Goods Header)
CREATE TABLE IF NOT EXISTS pur_purchase_invoices (
    purchase_invoice_no BIGINT PRIMARY KEY,
    uuid UUID UNIQUE NOT NULL,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    supplier_no BIGINT NOT NULL REFERENCES supp_suppliers(supplier_no) ON DELETE RESTRICT,
    inventory_number VARCHAR(100) NOT NULL,
    calculation_mode SMALLINT NOT NULL,
    inventory_type SMALLINT NOT NULL,
    supplier_invoice_no VARCHAR(100),
    total_items INT NOT NULL,
    gross_total_price NUMERIC NOT NULL,
    discount_type SMALLINT NOT NULL,
    discount_value NUMERIC NOT NULL,
    net_amount NUMERIC(20, 4) NOT NULL,
    vat_amount NUMERIC(20, 4) NOT NULL,
    round_off_amount NUMERIC(20, 4) NOT NULL,
    final_price NUMERIC NOT NULL,
    paid_amount NUMERIC NOT NULL,
    due_amount NUMERIC NOT NULL,
    document_status SMALLINT NOT NULL,
    created_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    purchase_date TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_pur_inventory_no UNIQUE (pharmacy_no, branch_no, inventory_number)
);

-- Purchase Invoice Items
CREATE TABLE IF NOT EXISTS pur_purchase_invoice_items (
    purchase_item_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    purchase_invoice_no BIGINT NOT NULL REFERENCES pur_purchase_invoices(purchase_invoice_no) ON DELETE RESTRICT,
    product_no BIGINT NOT NULL REFERENCES prod_products(product_no) ON DELETE RESTRICT,
    batch_number VARCHAR(100),
    box_qty INT NOT NULL,
    qty_in_box INT NOT NULL,
    total_qty_pcs INT NOT NULL,
    free_qty_pcs INT NOT NULL,
    btp NUMERIC NOT NULL,
    vat_percent NUMERIC NOT NULL,
    vat_amount NUMERIC NOT NULL,
    btp_vat NUMERIC NOT NULL,
    unit_cost NUMERIC NOT NULL,
    sale_price NUMERIC NOT NULL,
    total_price NUMERIC NOT NULL,
    expiry_date DATE,
    min_qty_pcs INT,
    barcode_value VARCHAR(100)
);

-- Supplier Direct Payments
CREATE TABLE IF NOT EXISTS pur_supplier_payments (
    supplier_payment_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    supplier_no BIGINT NOT NULL REFERENCES supp_suppliers(supplier_no) ON DELETE RESTRICT,
    account_no BIGINT NOT NULL,
    payment_number VARCHAR(100) NOT NULL,
    payment_method SMALLINT NOT NULL,
    amount NUMERIC(20, 4) NOT NULL,
    reference_no VARCHAR(100),
    note TEXT,
    paid_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    payment_date TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_pur_supp_payment_no UNIQUE (pharmacy_no, branch_no, payment_number),
    CONSTRAINT chk_pur_supp_pay_amount CHECK (amount > 0)
);

-- Purchase Returns to Suppliers (Header)
CREATE TABLE IF NOT EXISTS pur_purchase_returns (
    purchase_return_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    supplier_no BIGINT NOT NULL REFERENCES supp_suppliers(supplier_no) ON DELETE RESTRICT,
    purchase_invoice_no BIGINT REFERENCES pur_purchase_invoices(purchase_invoice_no) ON DELETE RESTRICT,
    return_number VARCHAR(100) NOT NULL,
    total_items INT NOT NULL,
    gross_return_amount NUMERIC(20, 4) NOT NULL,
    refund_received_amount NUMERIC(20, 4) NOT NULL,
    due_adjustment_amount NUMERIC(20, 4) NOT NULL,
    return_status SMALLINT NOT NULL,
    note TEXT,
    created_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    return_date TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_pur_return_number UNIQUE (pharmacy_no, branch_no, return_number)
);

-- Purchase Return Items to Suppliers (Lines)
CREATE TABLE IF NOT EXISTS pur_purchase_return_items (
    purchase_return_item_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    purchase_return_no BIGINT NOT NULL REFERENCES pur_purchase_returns(purchase_return_no) ON DELETE RESTRICT,
    purchase_item_no BIGINT REFERENCES pur_purchase_invoice_items(purchase_item_no) ON DELETE RESTRICT,
    product_no BIGINT NOT NULL REFERENCES prod_products(product_no) ON DELETE RESTRICT,
    batch_no BIGINT REFERENCES prod_product_batches(batch_no) ON DELETE RESTRICT,
    batch_number VARCHAR(100),
    return_qty_pcs INT NOT NULL,
    unit_purchase_price NUMERIC(20, 4) NOT NULL,
    total_return_amount NUMERIC(20, 4) NOT NULL,
    reason_code SMALLINT,
    CONSTRAINT chk_pur_return_qty CHECK (return_qty_pcs > 0)
);

-- ============================================================================
-- 5. MODULE: [cust_] & [sale_] CUSTOMERS & POINT OF SALE (POS)
-- ============================================================================

-- Customers
CREATE TABLE IF NOT EXISTS cust_customers (
    customer_no BIGINT PRIMARY KEY,
    uuid UUID UNIQUE NOT NULL,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    name VARCHAR(200) NOT NULL,
    phone VARCHAR(20) NOT NULL,
    gender SMALLINT,
    customer_type SMALLINT NOT NULL,
    discount_percent NUMERIC NOT NULL,
    email VARCHAR(150),
    address TEXT,
    note TEXT,
    has_due BOOLEAN NOT NULL,
    current_due NUMERIC NOT NULL,
    advance_balance NUMERIC NOT NULL,
    promised_payment_date DATE,
    is_loyalty_member BOOLEAN NOT NULL,
    loyalty_points NUMERIC NOT NULL,
    is_active BOOLEAN NOT NULL,
    deleted_at TIMESTAMP WITH TIME ZONE,
    deleted_by BIGINT,
    deletion_reason TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_cust_phone UNIQUE (pharmacy_no, branch_no, phone)
);

-- POS Sale Invoices (Header)
CREATE TABLE IF NOT EXISTS sale_invoices (
    sale_invoice_no BIGINT PRIMARY KEY,
    uuid UUID UNIQUE NOT NULL,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    customer_no BIGINT REFERENCES cust_customers(customer_no) ON DELETE SET NULL,
    prescription_no BIGINT,
    prescription_image_url TEXT,
    sales_number VARCHAR(100) NOT NULL,
    sale_mode SMALLINT NOT NULL,
    total_items INT NOT NULL,
    gross_total_price NUMERIC NOT NULL,
    discount_type SMALLINT NOT NULL,
    discount_value NUMERIC NOT NULL,
    card_commission_percent NUMERIC NOT NULL,
    card_commission_amount NUMERIC NOT NULL,
    net_amount NUMERIC(20, 4) NOT NULL,
    vat_amount NUMERIC(20, 4) NOT NULL,
    round_off_amount NUMERIC(20, 4) NOT NULL,
    final_price NUMERIC NOT NULL,
    approximate_profit NUMERIC NOT NULL,
    customer_will_pay NUMERIC NOT NULL,
    change_amount NUMERIC NOT NULL,
    due_amount NUMERIC NOT NULL,
    payment_method SMALLINT NOT NULL,
    document_status SMALLINT NOT NULL,
    cash_received_status SMALLINT NOT NULL,
    received_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    salesman_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    is_quick_printed BOOLEAN NOT NULL,
    exchange_of_return_no BIGINT,
    sale_timestamp TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_sale_number UNIQUE (pharmacy_no, branch_no, sales_number)
);

-- POS Sale Items (Lines)
CREATE TABLE IF NOT EXISTS sale_invoice_items (
    sale_invoice_item_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    sale_invoice_no BIGINT NOT NULL REFERENCES sale_invoices(sale_invoice_no) ON DELETE RESTRICT,
    product_no BIGINT NOT NULL REFERENCES prod_products(product_no) ON DELETE RESTRICT,
    batch_no BIGINT REFERENCES prod_product_batches(batch_no) ON DELETE RESTRICT,
    sale_mode SMALLINT NOT NULL,
    sale_qty INT NOT NULL,
    box_qty INT NOT NULL,
    pcs_per_box INT NOT NULL,
    unit_cost_price NUMERIC NOT NULL,
    unit_sale_price NUMERIC NOT NULL,
    discount_amount NUMERIC NOT NULL,
    vat_percent NUMERIC(20, 4) NOT NULL,
    vat_amount NUMERIC(20, 4) NOT NULL,
    net_amount NUMERIC(20, 4) NOT NULL,
    total_price NUMERIC NOT NULL
);

-- Split Tender & Multi-Payment Details for POS Sales
CREATE TABLE IF NOT EXISTS sale_invoice_payments (
    sale_payment_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    sale_invoice_no BIGINT NOT NULL REFERENCES sale_invoices(sale_invoice_no) ON DELETE RESTRICT,
    account_no BIGINT NOT NULL,
    payment_method SMALLINT NOT NULL,
    amount NUMERIC(20, 4) NOT NULL,
    gateway_reference VARCHAR(100),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT chk_sale_payment_amount CHECK (amount > 0)
);

-- Sales Returns (Header)
CREATE TABLE IF NOT EXISTS sale_returns (
    sale_return_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    sale_invoice_no BIGINT REFERENCES sale_invoices(sale_invoice_no) ON DELETE SET NULL,
    customer_no BIGINT REFERENCES cust_customers(customer_no) ON DELETE SET NULL,
    return_invoice_no VARCHAR(100) NOT NULL,
    original_invoice_no VARCHAR(100),
    total_items INT NOT NULL,
    gross_refund_price NUMERIC NOT NULL,
    discount_deduction NUMERIC NOT NULL,
    net_amount NUMERIC(20, 4) NOT NULL,
    vat_amount NUMERIC(20, 4) NOT NULL,
    round_off_amount NUMERIC(20, 4) NOT NULL,
    final_refund_price NUMERIC NOT NULL,
    cash_paid_refund NUMERIC NOT NULL,
    inventory_type SMALLINT NOT NULL,
    return_status SMALLINT NOT NULL,
    refund_method SMALLINT,
    exchange_invoice_no BIGINT REFERENCES sale_invoices(sale_invoice_no) ON DELETE SET NULL,
    idempotency_key UUID UNIQUE,
    created_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    return_date TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_sale_return_no UNIQUE (pharmacy_no, branch_no, return_invoice_no)
);

-- Sales Return Items (Lines with Over-Return Protection & Disposition)
CREATE TABLE IF NOT EXISTS sale_return_items (
    sale_return_item_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    sale_return_no BIGINT NOT NULL REFERENCES sale_returns(sale_return_no) ON DELETE RESTRICT,
    sale_invoice_item_no BIGINT REFERENCES sale_invoice_items(sale_invoice_item_no) ON DELETE RESTRICT,
    product_no BIGINT NOT NULL REFERENCES prod_products(product_no) ON DELETE RESTRICT,
    batch_number VARCHAR(100),
    box_qty INT NOT NULL,
    qty_in_box INT NOT NULL,
    total_qty_pcs INT NOT NULL,
    btp_vat NUMERIC NOT NULL,
    sale_price NUMERIC NOT NULL,
    vat_percent NUMERIC(20, 4) NOT NULL,
    vat_amount NUMERIC(20, 4) NOT NULL,
    net_amount NUMERIC(20, 4) NOT NULL,
    total_price NUMERIC NOT NULL,
    free_qty_pcs INT NOT NULL,
    expiry_date DATE,
    reason_code SMALLINT NOT NULL,
    disposition SMALLINT NOT NULL
);

-- ============================================================================
-- 6. MODULE: [cash_] & [due_] CASH & DUE MANAGEMENT
-- ============================================================================

-- Daily Register Cash Closing with Blind Count
CREATE TABLE IF NOT EXISTS cash_closers (
    cash_closer_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    closed_by_user_no BIGINT NOT NULL REFERENCES sys_users(user_no) ON DELETE RESTRICT,
    register_id VARCHAR(50),
    shift_no INT NOT NULL,
    opening_balance NUMERIC NOT NULL,
    cash_sales_collected NUMERIC NOT NULL,
    card_collected NUMERIC(20, 4) NOT NULL,
    bkash_collected NUMERIC(20, 4) NOT NULL,
    nagad_collected NUMERIC(20, 4) NOT NULL,
    rocket_collected NUMERIC(20, 4) NOT NULL,
    split_collected NUMERIC(20, 4) NOT NULL,
    due_collections NUMERIC NOT NULL,
    cash_returns_paid NUMERIC NOT NULL,
    expenses_paid NUMERIC NOT NULL,
    calculated_closing_balance NUMERIC NOT NULL,
    actual_counted_balance NUMERIC NOT NULL,
    discrepancy_amount NUMERIC NOT NULL,
    note TEXT,
    closing_date DATE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_cash_close_shift UNIQUE (pharmacy_no, branch_no, closing_date, shift_no)
);

-- Due Payment Collections
CREATE TABLE IF NOT EXISTS due_collections (
    due_collection_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    customer_no BIGINT NOT NULL REFERENCES cust_customers(customer_no) ON DELETE RESTRICT,
    account_no BIGINT NOT NULL,
    sale_invoice_no BIGINT REFERENCES sale_invoices(sale_invoice_no) ON DELETE SET NULL,
    payment_number VARCHAR(100) NOT NULL,
    payment_method SMALLINT NOT NULL,
    amount NUMERIC NOT NULL,
    note TEXT,
    received_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    payment_date TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_due_payment_no UNIQUE (pharmacy_no, branch_no, payment_number)
);

-- ============================================================================
-- 7. MODULE: [acc_] ACCOUNTING & FINANCIALS (CHART OF ACCOUNTS & GENERAL LEDGER)
-- ============================================================================

-- Chart of Accounts (Real General Ledger Accounts)
CREATE TABLE IF NOT EXISTS acc_transaction_accounts (
    account_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    account_category SMALLINT NOT NULL,
    account_code VARCHAR(30),
    account_name VARCHAR(150) NOT NULL,
    phone_number VARCHAR(20),
    account_type SMALLINT NOT NULL,
    bank_account_number VARCHAR(100),
    bank_name VARCHAR(150),
    current_balance NUMERIC NOT NULL,
    is_active BOOLEAN NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_acc_name UNIQUE (pharmacy_no, branch_no, account_name)
);

-- Foreign Keys to acc_transaction_accounts
ALTER TABLE pur_supplier_payments DROP CONSTRAINT IF EXISTS fk_supp_pay_account;
ALTER TABLE pur_supplier_payments
ADD CONSTRAINT fk_supp_pay_account FOREIGN KEY (account_no) REFERENCES acc_transaction_accounts(account_no) ON DELETE RESTRICT;

ALTER TABLE sale_invoice_payments DROP CONSTRAINT IF EXISTS fk_sale_pay_account;
ALTER TABLE sale_invoice_payments
ADD CONSTRAINT fk_sale_pay_account FOREIGN KEY (account_no) REFERENCES acc_transaction_accounts(account_no) ON DELETE RESTRICT;

ALTER TABLE due_collections DROP CONSTRAINT IF EXISTS fk_due_coll_account;
ALTER TABLE due_collections
ADD CONSTRAINT fk_due_coll_account FOREIGN KEY (account_no) REFERENCES acc_transaction_accounts(account_no) ON DELETE RESTRICT;

-- Expense Categories
CREATE TABLE IF NOT EXISTS acc_expense_categories (
    expense_category_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    name VARCHAR(150) NOT NULL,
    is_active BOOLEAN NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_acc_expense_cat UNIQUE (pharmacy_no, branch_no, name)
);

-- Expenses
CREATE TABLE IF NOT EXISTS acc_expenses (
    expense_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    voucher_no VARCHAR(100) NOT NULL,
    expense_category_no BIGINT NOT NULL REFERENCES acc_expense_categories(expense_category_no) ON DELETE RESTRICT,
    account_no BIGINT NOT NULL REFERENCES acc_transaction_accounts(account_no) ON DELETE RESTRICT,
    amount NUMERIC NOT NULL,
    payment_type VARCHAR(50) NOT NULL,
    note TEXT,
    created_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    expense_date TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_acc_expense_voucher UNIQUE (pharmacy_no, branch_no, voucher_no)
);

-- Income Categories
CREATE TABLE IF NOT EXISTS acc_income_categories (
    income_category_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    name VARCHAR(150) NOT NULL,
    is_active BOOLEAN NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_acc_income_cat UNIQUE (pharmacy_no, branch_no, name)
);

-- Income Entries
CREATE TABLE IF NOT EXISTS acc_incomes (
    income_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    receipt_no VARCHAR(100) NOT NULL,
    income_category_no BIGINT NOT NULL REFERENCES acc_income_categories(income_category_no) ON DELETE RESTRICT,
    account_no BIGINT NOT NULL REFERENCES acc_transaction_accounts(account_no) ON DELETE RESTRICT,
    amount NUMERIC NOT NULL,
    payment_type VARCHAR(50) NOT NULL,
    received_from VARCHAR(200),
    note TEXT,
    created_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    income_date TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_acc_income_receipt UNIQUE (pharmacy_no, branch_no, receipt_no)
);

-- Additional Capital Balances / Owner Equity Injections
CREATE TABLE IF NOT EXISTS acc_additional_balances (
    additional_balance_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    account_no BIGINT REFERENCES acc_transaction_accounts(account_no) ON DELETE RESTRICT,
    amount NUMERIC NOT NULL,
    entry_date DATE NOT NULL,
    note TEXT,
    created_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL
);

-- Inter-Account Reconciliations (Fund Transfers)
CREATE TABLE IF NOT EXISTS acc_reconciliations (
    reconciliation_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    voucher_no VARCHAR(100) NOT NULL,
    payment_from_account_no BIGINT NOT NULL REFERENCES acc_transaction_accounts(account_no) ON DELETE RESTRICT,
    payment_to_account_no BIGINT NOT NULL REFERENCES acc_transaction_accounts(account_no) ON DELETE RESTRICT,
    amount NUMERIC NOT NULL,
    note TEXT,
    created_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    transfer_date TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_acc_rec_voucher UNIQUE (pharmacy_no, branch_no, voucher_no),
    CONSTRAINT chk_diff_accounts CHECK (payment_from_account_no <> payment_to_account_no)
);

-- Universal Financial Transaction Ledger (Single Table Core for ALL Double-Entry Transactions)
CREATE TABLE IF NOT EXISTS acc_transaction_ledger (
    ledger_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    journal_no BIGINT NOT NULL,
    reversal_of_ledger_no BIGINT REFERENCES acc_transaction_ledger(ledger_no) ON DELETE RESTRICT,
    account_no BIGINT NOT NULL REFERENCES acc_transaction_accounts(account_no) ON DELETE RESTRICT,
    transaction_date TIMESTAMP WITH TIME ZONE NOT NULL,
    transaction_type SMALLINT NOT NULL,
    debit_amount NUMERIC(20, 4) NOT NULL,
    credit_amount NUMERIC(20, 4) NOT NULL,
    balance_after NUMERIC(20, 4) NOT NULL,
    source_document_type VARCHAR(50) NOT NULL,
    source_document_no VARCHAR(100) NOT NULL,
    source_document_id BIGINT NOT NULL,
    party_type SMALLINT,
    party_no BIGINT,
    narration TEXT,
    created_by_user_no BIGINT REFERENCES sys_users(user_no) ON DELETE SET NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT chk_ledger_one_side CHECK (
        debit_amount >= 0 AND credit_amount >= 0
        AND (debit_amount = 0 OR credit_amount = 0)
        AND (debit_amount + credit_amount) > 0
    )
);

-- ============================================================================
-- 8. MODULE: [doc_] DOCTORS PORTAL (BILINGUAL EN/BN)
-- ============================================================================

-- Medical Practitioners
CREATE TABLE IF NOT EXISTS doc_doctors (
    doctor_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    first_name_en VARCHAR(100) NOT NULL,
    last_name_en VARCHAR(100),
    first_name_bn VARCHAR(100),
    last_name_bn VARCHAR(100),
    phone VARCHAR(20) NOT NULL,
    gender SMALLINT,
    description_en TEXT,
    description_bn TEXT,
    password_hash VARCHAR(255) NOT NULL,
    signature_image_url TEXT,
    bmdc_reg_no VARCHAR(50),
    is_active BOOLEAN NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_doc_phone UNIQUE (pharmacy_no, branch_no, phone)
);

-- Prescription Configuration & Templates
CREATE TABLE IF NOT EXISTS doc_prescription_settings (
    prescription_setting_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    clinic_logo_url TEXT,
    prescription_bottom_row_text TEXT,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_doc_setting_branch UNIQUE (pharmacy_no, branch_no)
);

-- Patient Prescriptions
CREATE TABLE IF NOT EXISTS doc_prescriptions (
    prescription_no BIGINT PRIMARY KEY,
    prescription_code VARCHAR(100) NOT NULL,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    doctor_no BIGINT NOT NULL REFERENCES doc_doctors(doctor_no) ON DELETE RESTRICT,
    customer_no BIGINT REFERENCES cust_customers(customer_no) ON DELETE SET NULL,
    patient_name VARCHAR(150) NOT NULL,
    patient_age VARCHAR(20),
    patient_gender SMALLINT,
    clinical_notes TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_doc_presc_no UNIQUE (pharmacy_no, branch_no, prescription_code)
);

-- Add Prescription Link to sale_invoices
ALTER TABLE sale_invoices DROP CONSTRAINT IF EXISTS fk_sale_prescription;
ALTER TABLE sale_invoices
ADD CONSTRAINT fk_sale_prescription FOREIGN KEY (prescription_no) REFERENCES doc_prescriptions(prescription_no) ON DELETE SET NULL;

-- Prescription Medicine Lines
CREATE TABLE IF NOT EXISTS doc_prescription_items (
    prescription_item_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    prescription_no BIGINT NOT NULL REFERENCES doc_prescriptions(prescription_no) ON DELETE CASCADE,
    product_no BIGINT REFERENCES prod_products(product_no) ON DELETE SET NULL,
    medicine_name VARCHAR(255) NOT NULL,
    dosage VARCHAR(100) NOT NULL,
    instructions VARCHAR(255),
    duration VARCHAR(100)
);

-- ============================================================================
-- 9. MODULE: [cfg_] SYSTEM & BUSINESS SETTINGS
-- ============================================================================

-- POS Sales Business Configuration
CREATE TABLE IF NOT EXISTS cfg_sale_configs (
    sale_config_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    cash_received BOOLEAN NOT NULL,
    out_of_stock_sale BOOLEAN NOT NULL,
    allow_loss_sale BOOLEAN NOT NULL,
    product_purchase_cost BOOLEAN NOT NULL,
    stock_qty BOOLEAN NOT NULL,
    company_name BOOLEAN NOT NULL,
    profit_margin BOOLEAN NOT NULL,
    rak_number BOOLEAN NOT NULL,
    opening_stock_percent NUMERIC NOT NULL,
    max_sale_discount_percent NUMERIC NOT NULL,
    is_max_discount_enabled BOOLEAN NOT NULL,
    business_type SMALLINT NOT NULL,
    return_window_days INT NOT NULL DEFAULT 7,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_sale_config_branch UNIQUE (pharmacy_no, branch_no)
);

-- 80mm Thermal Receipt / Invoice Configuration
CREATE TABLE IF NOT EXISTS cfg_invoice_configs (
    invoice_config_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    paper_type SMALLINT NOT NULL,
    top_row_text TEXT NOT NULL,
    show_served_by_row BOOLEAN NOT NULL,
    show_discount_less_row BOOLEAN NOT NULL,
    show_card_fee_row BOOLEAN NOT NULL,
    show_customer_name BOOLEAN NOT NULL,
    show_customer_phone BOOLEAN NOT NULL,
    simple_print BOOLEAN NOT NULL,
    show_customer_vendor_balance BOOLEAN NOT NULL,
    show_shop_logo BOOLEAN NOT NULL,
    shop_logo_url TEXT,
    bottom_row_text TEXT,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_invoice_config_branch UNIQUE (pharmacy_no, branch_no)
);

-- Product Stock Alerts Configuration
CREATE TABLE IF NOT EXISTS cfg_product_configs (
    product_config_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    expired_notification_days INT NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_prod_config_branch UNIQUE (pharmacy_no, branch_no)
);

-- Customer Loyalty Reward Configuration
CREATE TABLE IF NOT EXISTS cfg_loyalty_configs (
    loyalty_config_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    is_enabled BOOLEAN NOT NULL,
    earning_spend_amount_per_point NUMERIC NOT NULL,
    redemption_points_per_taka NUMERIC NOT NULL,
    min_redeem_points NUMERIC NOT NULL,
    max_redeem_limit_per_tx NUMERIC NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_loyalty_config_branch UNIQUE (pharmacy_no, branch_no)
);

-- Bulk SMS Package Configuration & Balances
CREATE TABLE IF NOT EXISTS cfg_bulk_sms_configs (
    bulk_sms_config_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    current_sms_count INT NOT NULL,
    sms_pack_unit_size INT NOT NULL,
    sms_pack_unit_price NUMERIC NOT NULL,
    sender_id VARCHAR(50),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_sms_config_branch UNIQUE (pharmacy_no, branch_no)
);

-- SMS Logs
CREATE TABLE IF NOT EXISTS cfg_sms_logs (
    sms_log_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    customer_no BIGINT REFERENCES cust_customers(customer_no) ON DELETE SET NULL,
    recipient_phone VARCHAR(20) NOT NULL,
    message_body TEXT NOT NULL,
    sms_parts_count INT NOT NULL,
    dispatch_status VARCHAR(50) NOT NULL,
    sent_at TIMESTAMP WITH TIME ZONE NOT NULL
);

-- ============================================================================
-- 10. MODULE: [sub_] SAAS SUBSCRIPTION & BILLING
-- ============================================================================

-- Subscription Tier Definitions (Global Definition)
CREATE TABLE IF NOT EXISTS sub_subscription_plans (
    plan_no BIGINT PRIMARY KEY,
    plan_name VARCHAR(100) NOT NULL,
    monthly_fee NUMERIC NOT NULL,
    yearly_fee NUMERIC NOT NULL,
    trial_days INT NOT NULL,
    is_active BOOLEAN NOT NULL
);

-- Pharmacy Tenant Subscriptions
CREATE TABLE IF NOT EXISTS sub_pharmacy_subscriptions (
    subscription_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    plan_no BIGINT NOT NULL REFERENCES sub_subscription_plans(plan_no) ON DELETE RESTRICT,
    billing_cycle SMALLINT NOT NULL,
    subscription_status SMALLINT NOT NULL,
    trial_end_date DATE,
    subscription_start_date DATE NOT NULL,
    subscription_end_date DATE NOT NULL,
    next_payment_date DATE NOT NULL,
    monthly_rate NUMERIC NOT NULL,
    grace_period_days INT,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT uq_subscription_branch UNIQUE (pharmacy_no, branch_no)
);

-- Subscription Payments
CREATE TABLE IF NOT EXISTS sub_subscription_payments (
    subscription_payment_no BIGINT PRIMARY KEY,
    pharmacy_no BIGINT NOT NULL REFERENCES sys_pharmacies(pharmacy_no) ON DELETE RESTRICT,
    branch_no BIGINT NOT NULL REFERENCES sys_branches(branch_no) ON DELETE RESTRICT,
    subscription_no BIGINT NOT NULL REFERENCES sub_pharmacy_subscriptions(subscription_no) ON DELETE RESTRICT,
    transaction_reference VARCHAR(100) NOT NULL,
    payment_gateway VARCHAR(50) NOT NULL,
    subtotal NUMERIC NOT NULL,
    discount_code VARCHAR(50),
    discount_amount NUMERIC NOT NULL,
    final_amount NUMERIC NOT NULL,
    payment_status VARCHAR(50) NOT NULL,
    payment_timestamp TIMESTAMP WITH TIME ZONE NOT NULL,
    download_invoice_url TEXT,
    billing_cycle SMALLINT,
    plan_no BIGINT
);

-- ============================================================================
-- 11. PERFORMANCE B-TREE INDEXES (Multi-Tenant & Multi-Branch Partitioned)
-- ============================================================================

-- Products & Batches
CREATE INDEX IF NOT EXISTS idx_prod_products_lookup ON prod_products(pharmacy_no, branch_no, product_name);
CREATE INDEX IF NOT EXISTS idx_prod_products_barcode ON prod_products(pharmacy_no, branch_no, barcode_value);
CREATE INDEX IF NOT EXISTS idx_prod_products_company_no ON prod_products(pharmacy_no, branch_no, company_no);
CREATE INDEX IF NOT EXISTS idx_prod_products_generic_no ON prod_products(pharmacy_no, branch_no, generic_no);
CREATE INDEX IF NOT EXISTS idx_prod_batches_expiry ON prod_product_batches(pharmacy_no, branch_no, expiry_date);
CREATE INDEX IF NOT EXISTS idx_prod_batches_lookup ON prod_product_batches(pharmacy_no, branch_no, product_no, expiry_date);

-- POS Sales & Payment Lines
CREATE INDEX IF NOT EXISTS idx_sale_invoices_lookup ON sale_invoices(pharmacy_no, branch_no, sale_timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_sale_invoices_sales_no ON sale_invoices(pharmacy_no, branch_no, sales_number);
CREATE INDEX IF NOT EXISTS idx_sale_invoices_customer_no ON sale_invoices(pharmacy_no, branch_no, customer_no);
CREATE INDEX IF NOT EXISTS idx_sale_invoices_cash_status ON sale_invoices(pharmacy_no, branch_no, cash_received_status);
CREATE INDEX IF NOT EXISTS idx_sale_invoice_items_invoice ON sale_invoice_items(sale_invoice_no);
CREATE INDEX IF NOT EXISTS idx_sale_invoice_items_product ON sale_invoice_items(product_no);
CREATE INDEX IF NOT EXISTS idx_sale_invoice_payments_invoice ON sale_invoice_payments(sale_invoice_no);
CREATE INDEX IF NOT EXISTS idx_sale_invoice_payments_account ON sale_invoice_payments(pharmacy_no, branch_no, account_no);

-- Sales Returns
CREATE INDEX IF NOT EXISTS idx_sale_returns_lookup ON sale_returns(pharmacy_no, branch_no, return_date DESC);
CREATE INDEX IF NOT EXISTS idx_sale_return_items_parent ON sale_return_items(sale_return_no);
CREATE INDEX IF NOT EXISTS idx_sale_return_items_product ON sale_return_items(product_no);

-- Suppliers, Purchases & Returns
CREATE INDEX IF NOT EXISTS idx_pur_invoices_lookup ON pur_purchase_invoices(pharmacy_no, branch_no, purchase_date DESC);
CREATE INDEX IF NOT EXISTS idx_pur_invoices_inv_no ON pur_purchase_invoices(pharmacy_no, branch_no, inventory_number);
CREATE INDEX IF NOT EXISTS idx_pur_invoice_items_invoice ON pur_purchase_invoice_items(purchase_invoice_no);
CREATE INDEX IF NOT EXISTS idx_pur_invoice_items_product ON pur_purchase_invoice_items(product_no);
CREATE INDEX IF NOT EXISTS idx_supp_suppliers_phone ON supp_suppliers(pharmacy_no, branch_no, phone);
CREATE INDEX IF NOT EXISTS idx_pur_supplier_payments_lookup ON pur_supplier_payments(pharmacy_no, branch_no, supplier_no, payment_date DESC);
CREATE INDEX IF NOT EXISTS idx_pur_supplier_payments_account ON pur_supplier_payments(pharmacy_no, branch_no, account_no);
CREATE INDEX IF NOT EXISTS idx_pur_purchase_returns_lookup ON pur_purchase_returns(pharmacy_no, branch_no, supplier_no, return_date DESC);
CREATE INDEX IF NOT EXISTS idx_pur_purchase_return_items_parent ON pur_purchase_return_items(purchase_return_no);
CREATE INDEX IF NOT EXISTS idx_pur_purchase_return_items_product ON pur_purchase_return_items(product_no);

-- Inventory Transfers & Adjustments
CREATE INDEX IF NOT EXISTS idx_inv_transfers_from ON inv_transfers(pharmacy_no, from_branch_no, sent_at DESC);
CREATE INDEX IF NOT EXISTS idx_inv_transfers_to ON inv_transfers(pharmacy_no, to_branch_no, sent_at DESC);
CREATE INDEX IF NOT EXISTS idx_inv_transfer_items_parent ON inv_transfer_items(transfer_no);
CREATE INDEX IF NOT EXISTS idx_inv_transfer_items_product ON inv_transfer_items(product_no);
CREATE INDEX IF NOT EXISTS idx_inv_adjustments_lookup ON inv_adjustments(pharmacy_no, branch_no, adjustment_date DESC);
CREATE INDEX IF NOT EXISTS idx_inv_adjustment_items_parent ON inv_adjustment_items(adjustment_no);
CREATE INDEX IF NOT EXISTS idx_inv_adjustment_items_product ON inv_adjustment_items(product_no);

-- Universal Stock Movement Indexes (Single Stock Ledger)
CREATE INDEX IF NOT EXISTS idx_inv_movement_lookup ON inv_stock_movement(pharmacy_no, branch_no, product_no, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_inv_movement_batch ON inv_stock_movement(pharmacy_no, branch_no, batch_no);
CREATE INDEX IF NOT EXISTS idx_inv_movement_doc ON inv_stock_movement(pharmacy_no, branch_no, reference_doc_type, reference_doc_id);

-- Customers & Dues
CREATE INDEX IF NOT EXISTS idx_cust_customers_phone ON cust_customers(pharmacy_no, branch_no, phone);
CREATE INDEX IF NOT EXISTS idx_cust_customers_dues ON cust_customers(pharmacy_no, branch_no, has_due, current_due);
CREATE INDEX IF NOT EXISTS idx_due_collections_lookup ON due_collections(pharmacy_no, branch_no, customer_no, payment_date DESC);
CREATE INDEX IF NOT EXISTS idx_due_collections_account ON due_collections(pharmacy_no, branch_no, account_no);

-- Financials
CREATE INDEX IF NOT EXISTS idx_acc_expenses_date ON acc_expenses(pharmacy_no, branch_no, expense_date);
CREATE INDEX IF NOT EXISTS idx_acc_incomes_date ON acc_incomes(pharmacy_no, branch_no, income_date);
CREATE INDEX IF NOT EXISTS idx_acc_reconciliations_date ON acc_reconciliations(pharmacy_no, branch_no, transfer_date);

-- Universal Financial Transaction Ledger Indexes (Single Financial Ledger)
CREATE INDEX IF NOT EXISTS idx_acc_ledger_lookup ON acc_transaction_ledger(pharmacy_no, branch_no, account_no, transaction_date DESC);
CREATE INDEX IF NOT EXISTS idx_acc_ledger_journal ON acc_transaction_ledger(pharmacy_no, branch_no, journal_no);
CREATE INDEX IF NOT EXISTS idx_acc_ledger_doc ON acc_transaction_ledger(pharmacy_no, branch_no, source_document_type, source_document_id);
CREATE INDEX IF NOT EXISTS idx_acc_ledger_party ON acc_transaction_ledger(pharmacy_no, branch_no, party_type, party_no);
CREATE INDEX IF NOT EXISTS idx_acc_ledger_type ON acc_transaction_ledger(pharmacy_no, branch_no, transaction_type, transaction_date DESC);

-- System Infrastructure Indexes
CREATE INDEX IF NOT EXISTS idx_sys_event_outbox_pending ON sys_event_outbox(status, created_at);
CREATE INDEX IF NOT EXISTS idx_sys_idempotency_log_expiry ON sys_idempotency_log(expires_at);
CREATE INDEX IF NOT EXISTS idx_sys_audit_logs_lookup ON sys_audit_logs(pharmacy_no, branch_no, table_name, record_id);
CREATE INDEX IF NOT EXISTS idx_sys_audit_logs_user ON sys_audit_logs(pharmacy_no, user_no, created_at DESC);

-- Clinical & Prescriptions
CREATE INDEX IF NOT EXISTS idx_doc_prescription_items_parent ON doc_prescription_items(prescription_no);
CREATE INDEX IF NOT EXISTS idx_prod_requisition_items_parent ON prod_product_requisition_items(requisition_no);

-- Subscriptions & Payments
CREATE INDEX IF NOT EXISTS idx_sub_payments_trx_ref ON sub_subscription_payments(transaction_reference);

-- ============================================================================
-- 12. SEED DATA (EXPLICIT APPLICATION-MANAGED IDENTIFIERS)
-- ============================================================================

-- Seed Default Pharmacy Business & Branch
INSERT INTO sys_pharmacies (pharmacy_no, uuid, name, drug_license_no, contact_phone, contact_email, address, district, is_active, created_at, updated_at)
VALUES (1, 'a1000000-0000-0000-0000-000000000001', 'Aidly Model Pharmacy', 'DL-2024-DH-8841', '+8801700000000', 'contact@aidlypms.com', 'Dhaka, Bangladesh', 'Dhaka', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (pharmacy_no) DO NOTHING;

INSERT INTO sys_branches (branch_no, pharmacy_no, uuid, name, contact_phone, address, district, is_active, created_at, updated_at)
VALUES (1, 1, 'b1000000-0000-0000-0000-000000000001', 'Main Branch (Banani)', '+8801700000001', 'Banani, Dhaka', 'Dhaka', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (branch_no) DO NOTHING;

-- Seed Standard Chart of Accounts (COA)
INSERT INTO acc_transaction_accounts (account_no, pharmacy_no, branch_no, account_category, account_code, account_name, phone_number, account_type, bank_account_number, bank_name, current_balance, is_active, created_at, updated_at) VALUES
(1, 1, 1, 1, '10101', 'Main Cash Drawer (Cash in Hand)', NULL, 1, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(2, 1, 1, 1, '10201', 'Bank Current Account (Main)', NULL, 2, '2050123456789', 'Islami Bank Bangladesh Ltd.', 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(3, 1, 1, 1, '10301', 'Accounts Receivable (Customer Due)', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(4, 1, 1, 1, '10401', 'Merchandise Inventory Asset', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(5, 1, 1, 1, '10501', 'Input VAT Asset (Tax Advance)', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(6, 1, 1, 2, '20101', 'Accounts Payable (Supplier Due)', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(7, 1, 1, 2, '20201', 'Output VAT Payable (Tax Due)', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(8, 1, 1, 3, '30101', 'Owner Capital & Retained Earnings', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(9, 1, 1, 4, '40101', 'Sales Revenue', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(10, 1, 1, 4, '40102', 'Sales Returns and Allowances', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(11, 1, 1, 5, '50101', 'Cost of Goods Sold (COGS)', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(12, 1, 1, 5, '50201', 'General & Operating Expenses', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(13, 1, 1, 1, '10102', 'bKash Merchant Wallet', '01700000002', 4, NULL, 'bKash Merchant', 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(14, 1, 1, 1, '10103', 'Nagad Merchant Wallet', '01800000002', 5, NULL, 'Nagad Merchant', 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(15, 1, 1, 1, '10104', 'Card POS Settlement Account', NULL, 7, NULL, 'Card POS Settlement', 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(16, 1, 1, 4, '40202', 'Rounding Adjustment Gain', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(17, 1, 1, 5, '50203', 'Rounding Adjustment Loss', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(18, 1, 1, 5, '50204', 'Cash Drawer Variance & Shortage', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(19, 1, 1, 4, '40203', 'Cash Drawer Variance & Overage', NULL, 99, NULL, NULL, 0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (account_no) DO NOTHING;

-- Seed Sample Pharmaceutical Manufacturers (prod_companies)
INSERT INTO prod_companies (company_no, pharmacy_no, branch_no, name, is_active, created_at, updated_at) VALUES
(1, 1, 1, 'Beximco Pharmaceuticals Ltd.', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(2, 1, 1, 'Square Pharmaceuticals PLC', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(3, 1, 1, 'ACI Limited', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(4, 1, 1, 'Incepta Pharmaceuticals Ltd.', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(5, 1, 1, 'Renata Limited', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (company_no) DO NOTHING;

-- Seed 20 Core Permission Modules (permission_no 1-20)
INSERT INTO sys_permissions (permission_no, code, module_group, name, description, created_at) VALUES
(1, 'Account-Module', 'Accounting', 'Account Management', 'Manage financial accounts and statements', CURRENT_TIMESTAMP),
(2, 'Global-Account-Module', 'Accounting', 'Global Accounts', 'Access global organization chart of accounts', CURRENT_TIMESTAMP),
(3, 'PMS-Barcode-Module', 'Products', 'Barcode Generation', 'Print and scan barcode stickers', CURRENT_TIMESTAMP),
(4, 'PMS-Chart-Of-Accounting-Module', 'Accounting', 'Chart of Accounts', 'Manage ledgers, transfers, and asset balances', CURRENT_TIMESTAMP),
(5, 'PMS-Customer-Module', 'Customers', 'Customer Management', 'View, create, and manage customers', CURRENT_TIMESTAMP),
(6, 'PMS-Expense-Category-Module', 'Accounting', 'Expense Categories', 'Manage expense classification types', CURRENT_TIMESTAMP),
(7, 'PMS-Expense-Module', 'Accounting', 'Expense Management', 'Log and view operational expenses', CURRENT_TIMESTAMP),
(8, 'PMS-Income-Category-Module', 'Accounting', 'Income Categories', 'Manage income classification types', CURRENT_TIMESTAMP),
(9, 'PMS-Income-Module', 'Accounting', 'Income Management', 'Log and view non-sales income', CURRENT_TIMESTAMP),
(10, 'PMS-Product-Module', 'Products', 'Product Management', 'Create, edit, and view medicine stock', CURRENT_TIMESTAMP),
(11, 'PMS-Reconciliation-Module', 'Accounting', 'Account Reconciliation', 'Transfer funds between accounts', CURRENT_TIMESTAMP),
(12, 'PMS-Report-Module', 'Reports', 'Reporting & Exports', 'Download financial and inventory audit reports', CURRENT_TIMESTAMP),
(13, 'PMS-Requisition-Module', 'Products', 'Product Requisitions', 'Create and manage purchase order requisitions', CURRENT_TIMESTAMP),
(14, 'PMS-Sale-Module', 'Sales', 'POS Sales Management', 'Process retail and wholesale sales', CURRENT_TIMESTAMP),
(15, 'PMS-Vendor-Module', 'Suppliers', 'Supplier Management', 'Manage suppliers and purchase receiving', CURRENT_TIMESTAMP),
(16, 'POS-Shop-Company-Module', 'Products', 'Company Directory', 'Manage pharmaceutical manufacturers', CURRENT_TIMESTAMP),
(17, 'POS-Shop-Generic-Module', 'Products', 'Generic Directory', 'Manage drug active generic ingredients', CURRENT_TIMESTAMP),
(18, 'Permission-Module', 'Administration', 'Permissions ACL', 'Configure system access rights', CURRENT_TIMESTAMP),
(19, 'Pharmacy-Module', 'Administration', 'Pharmacy Settings', 'Configure store preferences and print settings', CURRENT_TIMESTAMP),
(20, 'Role-Module', 'Administration', 'Role Management', 'Create and modify employee authorization roles', CURRENT_TIMESTAMP),
(21, 'sale.return', 'Sales', 'Sales Return & Exchange', 'Process customer sales returns and item exchanges', CURRENT_TIMESTAMP),
(22, 'void', 'Sales', 'Void Sale Invoice', 'Authorize voiding completed POS sale invoices', CURRENT_TIMESTAMP),
(23, 'stock.adjust', 'Products', 'Stock Adjustment', 'Authorize recording inventory quantity and cost adjustments', CURRENT_TIMESTAMP),
(24, 'price.edit', 'Products', 'Price Edit', 'Authorize updating master product purchase and sale prices', CURRENT_TIMESTAMP),
(25, 'cash.close', 'Accounting', 'Cash Shift Close', 'Authorize closing and reconciling daily cash register shifts', CURRENT_TIMESTAMP),
(26, 'user.manage', 'Administration', 'User Management', 'Authorize managing employee and staff user accounts', CURRENT_TIMESTAMP),
(27, 'role.manage', 'Administration', 'Role Management', 'Authorize creating and editing authorization roles and access rights', CURRENT_TIMESTAMP)
ON CONFLICT (permission_no) DO NOTHING;

-- Default Subscription Plan
INSERT INTO sub_subscription_plans (plan_no, plan_name, monthly_fee, yearly_fee, trial_days, is_active)
VALUES (1, 'Standard Pharmacy Cloud', 500, 5000, 14, TRUE)
ON CONFLICT (plan_no) DO NOTHING;

-- Default System Roles (PMS Manager = 9, PMS Salesman = 10 as shown in UI)
INSERT INTO sys_roles (role_no, pharmacy_no, branch_no, role_name, alias, description, is_system, status, created_at, updated_at) VALUES
(9, 1, 1, 'PMS Manager', 'PMS Manager', 'Full operational control over store inventory, accounting, settings, and staff', TRUE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(10, 1, 1, 'PMS Salesman', 'PMS Salesman', 'Restricted access for counter sales, POS billing, and customer balance lookup', TRUE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (role_no) DO NOTHING;

-- Assign all permissions to PMS Manager (Role 9)
INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
SELECT 9, permission_no, 1, 1 FROM sys_permissions
ON CONFLICT DO NOTHING;

-- Assign POS and Customer permissions to PMS Salesman (Role 10)
INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
SELECT 10, permission_no, 1, 1 FROM sys_permissions
WHERE code IN ('PMS-Sale-Module', 'PMS-Customer-Module', 'PMS-Product-Module', 'PMS-Barcode-Module', 'sale.return', 'void', 'stock.adjust')
ON CONFLICT DO NOTHING;

-- Pre-seed Default Menus (Level 1 Root Modules)
INSERT INTO sys_menus (menu_no, title, path, icon, parent_menu_no, order_no, status, created_at, updated_at)
VALUES
(1, 'Sales & Customers', '/customer/sale', 'shopping-cart', NULL, 1, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(2, 'Purchases & Suppliers', '/supplier/purchase', 'shop', NULL, 2, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(3, 'Medicines & Stock', '/products/list', 'medicine-box', NULL, 3, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(4, 'Accounts & Cash', '/cash/closers', 'calculator', NULL, 4, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
(5, 'Settings & Admin', '/settings/configs', 'setting', NULL, 5, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON CONFLICT (menu_no) DO NOTHING;

-- Pre-seed Default Sub-Menus (Level 2)
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
ON CONFLICT (menu_no) DO NOTHING;

-- Seed full menu permissions for initial roles
INSERT INTO sys_role_menu_permissions (
    role_menu_perm_no, pharmacy_no, role_no, menu_no,
    can_view, can_create, can_edit, can_delete,
    created_at, updated_at
)
SELECT
    ROW_NUMBER() OVER () as role_menu_perm_no,
    r.pharmacy_no,
    r.role_no,
    m.menu_no,
    TRUE, TRUE, TRUE, TRUE,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM sys_roles r
CROSS JOIN sys_menus m
ON CONFLICT (pharmacy_no, role_no, menu_no) DO NOTHING;

-- Exchange Return Circular FK (Added after both tables exist)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE table_schema = current_schema()
          AND table_name = 'sale_invoices'
          AND constraint_name = 'fk_si_exchange_return'
    ) THEN
        ALTER TABLE sale_invoices
        ADD CONSTRAINT fk_si_exchange_return
        FOREIGN KEY (exchange_of_return_no) REFERENCES sale_returns(sale_return_no)
        ON DELETE SET NULL;
    END IF;
END $$;

-- Sequences for Monotonic Concurrency-Safe ID Generation
CREATE SEQUENCE IF NOT EXISTS seq_sale_invoice_no;
CREATE SEQUENCE IF NOT EXISTS seq_sale_invoice_item_no;
CREATE SEQUENCE IF NOT EXISTS seq_sale_return_no;
CREATE SEQUENCE IF NOT EXISTS seq_sale_return_item_no;
CREATE SEQUENCE IF NOT EXISTS seq_sale_invoice_payment_no;
CREATE SEQUENCE IF NOT EXISTS seq_purchase_invoice_no;
CREATE SEQUENCE IF NOT EXISTS seq_purchase_invoice_item_no;
CREATE SEQUENCE IF NOT EXISTS seq_ledger_no;
CREATE SEQUENCE IF NOT EXISTS seq_journal_no;
CREATE SEQUENCE IF NOT EXISTS seq_stock_movement_no;
CREATE SEQUENCE IF NOT EXISTS seq_sys_menu_no START WITH 100 INCREMENT BY 1;
CREATE SEQUENCE IF NOT EXISTS seq_role_menu_perm_no START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE IF NOT EXISTS seq_prod_batch_no;
CREATE SEQUENCE IF NOT EXISTS seq_sys_audit_no;
CREATE SEQUENCE IF NOT EXISTS seq_cash_closer_no;
CREATE SEQUENCE IF NOT EXISTS seq_shift_no;
CREATE SEQUENCE IF NOT EXISTS seq_due_collection_no;
CREATE SEQUENCE IF NOT EXISTS seq_supplier_payment_no;
CREATE SEQUENCE IF NOT EXISTS seq_purchase_return_no;
CREATE SEQUENCE IF NOT EXISTS seq_purchase_return_item_no;
CREATE SEQUENCE IF NOT EXISTS seq_customer_no;
CREATE SEQUENCE IF NOT EXISTS seq_product_no;
CREATE SEQUENCE IF NOT EXISTS seq_expense_no;
CREATE SEQUENCE IF NOT EXISTS seq_income_no;
CREATE SEQUENCE IF NOT EXISTS seq_account_no;
CREATE SEQUENCE IF NOT EXISTS seq_adjustment_no;
CREATE SEQUENCE IF NOT EXISTS seq_adjustment_item_no;
CREATE SEQUENCE IF NOT EXISTS seq_transfer_no;
CREATE SEQUENCE IF NOT EXISTS seq_transfer_item_no;
CREATE SEQUENCE IF NOT EXISTS seq_subscription_payment_no;
CREATE SEQUENCE IF NOT EXISTS seq_subscription_no;

SELECT setval('seq_sale_invoice_no', GREATEST(COALESCE((SELECT MAX(sale_invoice_no) FROM sale_invoices), 0) + 1, 1), false);
SELECT setval('seq_sale_invoice_item_no', GREATEST(COALESCE((SELECT MAX(sale_invoice_item_no) FROM sale_invoice_items), 0) + 1, 1), false);
SELECT setval('seq_sale_return_no', GREATEST(COALESCE((SELECT MAX(sale_return_no) FROM sale_returns), 0) + 1, 1), false);
SELECT setval('seq_sale_return_item_no', GREATEST(COALESCE((SELECT MAX(sale_return_item_no) FROM sale_return_items), 0) + 1, 1), false);
SELECT setval('seq_sale_invoice_payment_no', GREATEST(COALESCE((SELECT MAX(sale_payment_no) FROM sale_invoice_payments), 0) + 1, 1), false);
SELECT setval('seq_purchase_invoice_no', GREATEST(COALESCE((SELECT MAX(purchase_invoice_no) FROM pur_purchase_invoices), 0) + 1, 1), false);
SELECT setval('seq_purchase_invoice_item_no', GREATEST(COALESCE((SELECT MAX(purchase_item_no) FROM pur_purchase_invoice_items), 0) + 1, 1), false);
SELECT setval('seq_ledger_no', GREATEST(COALESCE((SELECT MAX(ledger_no) FROM acc_transaction_ledger), 0) + 1, 1), false);
SELECT setval('seq_journal_no', GREATEST(COALESCE((SELECT MAX(journal_no) FROM acc_transaction_ledger), 0) + 1, 1), false);
SELECT setval('seq_stock_movement_no', GREATEST(COALESCE((SELECT MAX(movement_no) FROM inv_stock_movement), 0) + 1, 1), false);
SELECT setval('seq_sys_menu_no', GREATEST(COALESCE((SELECT MAX(menu_no) FROM sys_menus), 0) + 1, 100), false);
SELECT setval('seq_role_menu_perm_no', GREATEST(COALESCE((SELECT MAX(role_menu_perm_no) FROM sys_role_menu_permissions), 0) + 1, 1), false);
SELECT setval('seq_prod_batch_no', GREATEST(COALESCE((SELECT MAX(batch_no) FROM prod_product_batches), 0) + 1, 1), false);
SELECT setval('seq_sys_audit_no', GREATEST(COALESCE((SELECT MAX(audit_no) FROM sys_audit_logs), 0) + 1, 1), false);
SELECT setval('seq_cash_closer_no', GREATEST(COALESCE((SELECT MAX(cash_closer_no) FROM cash_closers), 0) + 1, 1), false);
SELECT setval('seq_shift_no', GREATEST(COALESCE((SELECT MAX(shift_no) FROM cash_closers), 0) + 1, 1), false);
SELECT setval('seq_due_collection_no', GREATEST(COALESCE((SELECT MAX(due_collection_no) FROM due_collections), 0) + 1, 1), false);
SELECT setval('seq_supplier_payment_no', GREATEST(COALESCE((SELECT MAX(supplier_payment_no) FROM pur_supplier_payments), 0) + 1, 1), false);
SELECT setval('seq_purchase_return_no', GREATEST(COALESCE((SELECT MAX(purchase_return_no) FROM pur_purchase_returns), 0) + 1, 1), false);
SELECT setval('seq_purchase_return_item_no', GREATEST(COALESCE((SELECT MAX(purchase_return_item_no) FROM pur_purchase_return_items), 0) + 1, 1), false);
SELECT setval('seq_customer_no', GREATEST(COALESCE((SELECT MAX(customer_no) FROM cust_customers), 0) + 1, 1), false);
SELECT setval('seq_product_no', GREATEST(COALESCE((SELECT MAX(product_no) FROM prod_products), 0) + 1, 1), false);
SELECT setval('seq_expense_no', GREATEST(COALESCE((SELECT MAX(expense_no) FROM acc_expenses), 0) + 1, 1), false);
SELECT setval('seq_income_no', GREATEST(COALESCE((SELECT MAX(income_no) FROM acc_incomes), 0) + 1, 1), false);
SELECT setval('seq_account_no', GREATEST(COALESCE((SELECT MAX(account_no) FROM acc_transaction_accounts), 0) + 1, 1), false);
SELECT setval('seq_adjustment_no', GREATEST(COALESCE((SELECT MAX(adjustment_no) FROM inv_adjustments), 0) + 1, 1), false);
SELECT setval('seq_adjustment_item_no', GREATEST(COALESCE((SELECT MAX(adjustment_item_no) FROM inv_adjustment_items), 0) + 1, 1), false);
SELECT setval('seq_transfer_no', GREATEST(COALESCE((SELECT MAX(transfer_no) FROM inv_transfers), 0) + 1, 1), false);
SELECT setval('seq_transfer_item_no', GREATEST(COALESCE((SELECT MAX(transfer_item_no) FROM inv_transfer_items), 0) + 1, 1), false);
SELECT setval('seq_subscription_payment_no', GREATEST(COALESCE((SELECT MAX(subscription_payment_no) FROM sub_subscription_payments), 0) + 1, 1), false);
SELECT setval('seq_subscription_no', GREATEST(COALESCE((SELECT MAX(subscription_no) FROM sub_pharmacy_subscriptions), 0) + 1, 1), false);

-- ============================================================================
-- END OF SCHEMA
-- ============================================================================
