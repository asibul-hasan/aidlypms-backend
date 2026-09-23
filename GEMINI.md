# GEMINI.md — Aidly PMS & Aidly ERP Master Architecture & Developer Memory

> **CRITICAL DEVELOPER MEMORY & MASTER SPECIFICATION**  
> **Systems**: Aidly PMS (Pharmacy Management System) & Aidly ERP (Modular Enterprise Monolith)  
> **Author/Maintainer**: Asibul Hasan & Engineering Team  
> **Status**: Production Reference & Active Architecture Blueprint  
> **Primary Use**: When setting up a new development machine, restoring Windows, switching workstations, or onboarding AI agents (Gemini, Antigravity, Claude), **load this document first** to restore 100% system context, database connections, git remotes, accounting ledger engines, and domain models without loss.

---

## Table of Contents
1. [Executive Ecosystem Overview](#1-executive-ecosystem-overview)
2. [Git Repositories, Branches & Workspace Inventory](#2-git-repositories-branches--workspace-inventory)
3. [Preserved Assets & External Project Artifacts](#3-preserved-assets--external-project-artifacts)
4. [Cloud Database Infrastructure (Supabase PostgreSQL 17.6)](#4-cloud-database-infrastructure-supabase-postgresql-176)
5. [Complete Accounting Module Architecture (Field-to-Field & Transaction-to-Transaction)](#5-complete-accounting-module-architecture-field-to-field--transaction-to-transaction)
   - [5.1 Setup & Chart of Accounts Hierarchy](#51-setup--chart-of-accounts-hierarchy)
   - [5.2 Universal Double-Entry Posting Engine & Integrity Rules](#52-universal-double-entry-posting-engine--integrity-rules)
   - [5.3 Transaction-to-Transaction Posting Matrix](#53-transaction-to-transaction-posting-matrix)
   - [5.4 Field-to-Field Mapping Dictionary](#54-field-to-field-mapping-dictionary)
   - [5.5 Verification & Implementation Audit](#55-verification--implementation-audit)
6. [Aidly PMS Architecture (Option A: Pure Pharmacy Mode)](#6-aidly-pms-architecture-option-a-pure-pharmacy-mode)
   - [6.1 Multi-Tenancy & Isolation Engine](#61-multi-tenancy--isolation-engine)
   - [6.2 Synchronous Dual-Ledger Posting Engine](#62-synchronous-dual-ledger-posting-engine)
   - [6.3 All 64 Screen & Form IDs Registry (`menu.md` Spec)](#63-all-64-screen--form-ids-registry-menumd-spec)
   - [6.4 Domain Models & Compatibility Field Mapping](#64-domain-models--compatibility-field-mapping)
7. [Developer Guidelines, Gotchas & Conventional Commits](#7-developer-guidelines-gotchas--conventional-commits)
8. [Complete Windows Reinstallation & Machine Restoration Guide](#8-complete-windows-reinstallation--machine-restoration-guide)

---

## 1. Executive Ecosystem Overview

The Aidly workspace (`c:\pridesys\apps\sme\aidlyErp`) hosts two complementary, high-performance business applications sharing architectural standards, security rules, and financial ledger discipline:

```
                               AIDLY ENTERPRISE ECOSYSTEM
                                            │
        ┌───────────────────────────────────┴───────────────────────────────────┐
        ▼                                                                       ▼
 ┌──────────────────────────────────────┐                ┌──────────────────────────────────────┐
 │       AIDLY PMS (PHARMACY)           │                │          AIDLY SME ERP               │
 ├──────────────────────────────────────┤                ├──────────────────────────────────────┤
 │ • Backend: AidlyPms.Api (.NET 10)    │                │ • Backend: sme-dotnet-backend        │
 │ • Tech: C# 13, Dapper, Npgsql        │                │ • Tech: C# 13, EF Core / Dapper      │
 │ • Frontend: Angular 19+ (Port 4203)  │                │ • Frontend: Angular Web Client (4200)│
 │ • Target: Retail/Wholesale Pharmacy  │                │ • Target: Modular SME Business ERP   │
 │ • Model: Dual Ledger (Stock + Acc)   │                │ • Model: Modular Outbox Posting      │
 │ • DB: Supabase AWS Postgres 17.6     │                │ • Modules: FIN, SAL, PUR, INV, HRM   │
 └──────────────────────────────────────┘                └──────────────────────────────────────┘
```

---

## 2. Git Repositories, Branches & Workspace Inventory

| Layer / Sub-Project | GitHub / Remote Repository | Branch | Local Directory | Notes |
|---|---|---|---|---|
| **Aidly PMS Backend** | `https://github.com/asibul-hasan/aidlypms-backend.git` | `main` | `aidlypms-backend` | .NET 10 Web API, Dapper, Port 5275 |
| **Aidly PMS & ERP Frontend** | `https://github.com/asibul-hasan/aidlypms-frontend.git`<br/>`https://github.com/asibul-hasan/sme-software-frontend.git` | `main` | `sme-software-frontend` | Angular 19+ Nx Monorepo (apps: `aidlyPms`, `web-client`) |
| **Aidly SME Backend** | `https://github.com/asibul-hasan/aidly-backend.git`<br/>`https://huggingface.co/spaces/asibul-07/angulartail-nest-auth-flow` | `modular-monolith-restructure` | `sme-dotnet-backend` | Modular Monolith (FIN, SAL, PUR, INV, HRM, SYS) |
| **Business Docs & Schemas** | Local workspace directory | — | `aidly-business-doc` | 7 core business logic docs, schemas, form specs |
| **Preserved Legacy Assets** | Local workspace directory | — | `reference-assets` | Legacy UI screenshots, DB scripts, FSD document |

---

## 3. Preserved Assets & External Project Artifacts

To prevent data loss when reinstalling Windows or migrating hardware, all external assets previously located outside the project repository (in `c:\pridesys\apps\pharmacy` and `c:\pridesys\apps\New Text Document.txt`) have been consolidated into `reference-assets/`:

1. **Legacy Pharmacy UI Screenshots & Schema (`reference-assets/legacy-pharmacy/`)**:
   - Contains high-resolution PNG captures of all legacy pharmacy screens:
     - `Accounting/` (Additional Balance, Expenses, Income, Transaction / Create Account)
     - `Cash/` (Receive Cash, Checked All / Reconcile)
     - `Customer/` (Customer List, Add Customer, Return, Retail Sale, Wholesale Sale, Sale Record)
     - `Doctors Portal/` (Doctor List, Add Doctor, Prescription Settings)
     - `Due Collect/` (Due Collect, Paid List)
     - `products/` (Product List, Add Product, Company, Generics, Expired Products, Requisition)
     - `Reconciliation/` (Inter-account transfer workbench)
     - `Representative/` & `Roles/` (User reps, RBAC permissions)
     - `Settings/` (Bulk SMS, Invoice Thermal Layout, Loyalty, Product, Sale Terminal)
     - `Subscription Report/` (Billing, bKash gateway integration)
     - `Supplier/` (Supplier List, Purchase Invoice Entry, Purchase Record)
   - Original Database Artifacts:
     - `database/schema.sql`: Original legacy SQL schema
     - `database/db_script.sql`: Legacy seed data & table DDL
     - `database/label_values.json`: All UI label translations & mappings
     - `database/menu.json` & `database/menu.md`: Complete 64 screen registry
   - Backup archive: `MY.zip` (4.6 MB)
2. **Functional Specification Document (FSD)**:
   - Preserved at `reference-assets/OS_PMS_Functional_Specification_Document.md` and `aidly-business-doc/markdown/functional-spec/OS_PMS_Functional_Specification_Document.md`.
   - Comprehensive reverse-engineered specification covering screens `SCR-01` through `SCR-64`, bilingual English/Bengali requirements, and POS validation rules.
3. **Enterprise Business Logic Documentation (`aidly-business-doc/markdown/business-logic/`)**:
   - `acc-business-doc.md`: 71 KB exhaustive ERP Accounting specification (GAAP/IFRS, double-entry, ERPNext parity).
   - `fin-business.md`: 54 KB Finance/General Ledger production blueprint.
   - `inv-business.md`: 115 KB Inventory management & stock valuation ledger.
   - `pur-business.md`: 66 KB Procurement & Supplier Accounts Payable.
   - `sal-business.md`: 68 KB Sales, POS & Customer Accounts Receivable.
   - `hrm-business.md`: 69 KB Human Resource Management & Payroll sink.
   - `sys-business.md`: 133 KB System administration, multi-tenancy, RBAC, outbox.

---

## 4. Cloud Database Infrastructure (Supabase PostgreSQL 17.6)

Both Aidly PMS and Aidly ERP connect to the managed Supabase PostgreSQL 17.6 cluster hosted in AWS `ap-southeast-1` (Singapore):

### Connection Strings
- **Transaction Pooler (Port 5432 / Recommended for API queries & serverless)**:
  ```
  Host=aws-0-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.rjxkplqdubxxxhyubdjq;Password=Asibul@2303;SSL Mode=Require;Trust Server Certificate=true
  ```
- **Direct Connection (Port 5432 / For DDL migrations, long-lived sessions)**:
  ```
  Host=db.rjxkplqdubxxxhyubdjq.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=Asibul@2303;SSL Mode=Require;Trust Server Certificate=true
  ```

### Database Dimensions & Schema
- **Tables**: 57 Production Tables
- **Columns**: 736 Strongly-Typed Columns
- **Verification DDL**: Complete schema dump in `db_schema_dump.tsv` and `reference-assets/legacy-pharmacy/database/db_script.sql`.

---

## 5. Complete Accounting Module Architecture (Field-to-Field & Transaction-to-Transaction)

Based on `acc-business-doc.md` and `fin-business.md`, the Accounting engine serves as the immutable double-entry financial sink for all operational modules.

### 5.1 Setup & Chart of Accounts Hierarchy

```text
CHART OF ACCOUNTS HIERARCHY
├── 10000 ASSETS (Normal Balance: DEBIT)
│   ├── 10100 Cash & Liquid Funds (Group)
│   │   ├── 10101 Counter Cash (Leaf, Control: Cash)
│   │   └── 10102 Main Cash Vault (Leaf, Control: Cash)
│   ├── 10200 Bank Accounts (Group)
│   │   └── 10201 Commercial Bank Accounts (Leaf, Control: Bank)
│   ├── 10300 Mobile Financial Services (Group)
│   │   └── 10301 bKash / Nagad Merchant Accounts (Leaf, Control: MFS)
│   ├── 10400 Accounts Receivable (Group)
│   │   └── 10401 Customer Receivables / Due Ledger (Leaf, Control: AR)
│   └── 10500 Inventory & Stock (Group)
│       └── 10501 Stock / Medicine in Hand (Leaf, Control: Inventory)
├── 20000 LIABILITIES (Normal Balance: CREDIT)
│   ├── 20100 Accounts Payable (Group)
│   │   └── 20101 Supplier Payables / Due Ledger (Leaf, Control: AP)
│   └── 20200 Statutory Liabilities (Group)
│       └── 20201 Output VAT / GST Payable (Leaf, Control: Tax)
├── 30000 EQUITY & CAPITAL (Normal Balance: CREDIT)
│   ├── 30101 Owner Capital / Initial Equity (Leaf, Control: Capital)
│   └── 30201 Retained Earnings / Current Year Profit (Leaf, Control: Retained Earnings)
├── 40000 REVENUE / INCOME (Normal Balance: CREDIT)
│   ├── 40101 Sales Revenue (Leaf, Control: Revenue)
│   └── 40201 Miscellaneous / Additional Income (Leaf, Control: Other Income)
└── 50000 EXPENSES (Normal Balance: DEBIT)
    ├── 50101 Cost of Goods Sold (COGS) (Leaf, Control: Direct Expense)
    └── 50201 Operational Expenses (Leaf, Control: Indirect Expense)
```

#### Master Configuration Entities
1. **`fin_account_group`**: High-level categorization nodes (`is_group = 1`, parent-child hierarchy).
2. **`fin_account`**: Postable leaf nodes (`is_postable = 1`, `control_type`, `root_type`, `normal_balance`, `requires_cost_center`, `requires_party`).
3. **`fin_voucher_type`**: Configurable voucher categories (`base_kind`: 1=Journal, 2=Payment, 3=Receipt, 4=Contra, 5=Sales, 6=Purchase, 7=Opening, 8=Closing).
4. **`fin_bank_account`**: Links institutional bank accounts to specific COA GL leaf heads (1:1 enforcement).
5. **`fin_gl_map`**: Auto-posting rules mapping outbox events (`SALES_POSTED`, `PURCHASE_POSTED`, `STOCK_ADJUSTED`, `PAYROLL_APPROVED`) to destination GL debit/credit legs.

---

### 5.2 Universal Double-Entry Posting Engine & Integrity Rules

Every financial entry must pass strict mathematical and accounting assertions before being committed:

1. **Zero Imbalance Constraint ($\Delta \equiv 0$)**:
   $$\sum_{i=1}^{n} \text{Debit}_i - \sum_{i=1}^{n} \text{Credit}_i = 0.0000$$
   Any batch with $|\sum \text{Dr} - \sum \text{Cr}| > 0.0001$ is rejected with a `ValidationException`.
2. **Strict Sign Restrictions**:
   Negative debits or credits are prohibited. Reversals must swap legs or set `is_reversal = 1`.
3. **Terminal Leaf Enforcement**:
   Entries can only post to leaf accounts (`is_postable = 1`). Group heads strictly reject direct ledger postings.
4. **Accounting Period Guardrails**:
   `posting_date` must fall within an open fiscal year (`sys_fin_year`) and active period (`sys_fin_year_dtl.period_status = 1`). Posting to a closed or locked period (status 3) is hard-blocked.
5. **Append-Only Immutability**:
   `fin_ledger` and `acc_transaction_ledger` never perform `UPDATE` or `DELETE`. Adjustments and cancellations generate contra reversal entries.

---

### 5.3 Transaction-to-Transaction Posting Matrix

Every operational workflow generates exact balanced double entries across the General Ledger:

#### 1. Retail POS / Standard Credit Sale
- **Trigger**: POS Checkout or Sales Invoice submission.
- **Posting**:
  $$\begin{array}{llr} \textbf{Account Head} & \textbf{Type} & \textbf{Amount} \\ \hline \text{Dr. Cash (10101) / Bank (10201) / MFS (10301)} & \text{Asset (Paid Amount)} & \$1,000.00 \\ \text{Dr. Accounts Receivable / Due (10401)} & \text{Asset (Due Amount)} & \$150.00 \\ \quad \text{Cr. Sales Revenue Account (40101)} & \text{Income (Gross - Discount)} & \$1,100.00 \\ \quad \text{Cr. Output VAT / Tax Payable (20201)} & \text{Liability (Tax Collected)} & \$50.00 \\ \hline \text{Dr. Cost of Goods Sold (50101)} & \text{Expense (Item Cost)} & \$700.00 \\ \quad \text{Cr. Inventory / Stock in Hand (10501)} & \text{Asset (Item Cost)} & \$700.00 \\ \end{array}$$

#### 2. POS Sales Return & Refund (Credit Note)
- **Trigger**: Customer returns items at POS counter.
- **Posting**:
  $$\begin{array}{llr} \textbf{Account Head} & \textbf{Type} & \textbf{Amount} \\ \hline \text{Dr. Sales Return / Revenue (40101)} & \text{Income Reversal} & \$200.00 \\ \text{Dr. Output VAT Payable (20201)} & \text{Liability Reversal} & \$10.00 \\ \quad \text{Cr. Cash / Bank Refund (10101) or AR (10401)} & \text{Asset Reduction} & \$210.00 \\ \hline \text{Dr. Inventory / Stock in Hand (10501)} & \text{Asset Restored} & \$130.00 \\ \quad \text{Cr. Cost of Goods Sold (50101)} & \text{Expense Restored} & \$130.00 \\ \end{array}$$

#### 3. Purchase Invoice Entry (GRN / Procure-to-Pay)
- **Trigger**: Goods received and invoice posted from supplier.
- **Posting**:
  $$\begin{array}{llr} \textbf{Account Head} & \textbf{Type} & \textbf{Amount} \\ \hline \text{Dr. Inventory / Stock in Hand (10501)} & \text{Asset (Purchase Value)} & \$5,000.00 \\ \text{Dr. Input VAT / Tax Receivable (10601)} & \text{Asset (Tax Credit)} & \$250.00 \\ \quad \text{Cr. Accounts Payable / Supplier (20101)} & \text{Liability (Due to Supplier)} & \$5,000.00 \\ \quad \text{Cr. Cash / Bank (if direct cash purchase)} & \text{Asset Reduction (Paid)} & \$250.00 \\ \end{array}$$

#### 4. Purchase Return to Supplier (Debit Note)
- **Trigger**: Near-expiry or damaged goods returned to supplier.
- **Posting**:
  $$\begin{array}{llr} \textbf{Account Head} & \textbf{Type} & \textbf{Amount} \\ \hline \text{Dr. Accounts Payable / Supplier (20101)} & \text{Liability Reduction} & \$1,000.00 \\ \quad \text{Cr. Inventory / Stock in Hand (10501)} & \text{Asset Reduction} & \$950.00 \\ \quad \text{Cr. Input VAT Receivable (10601)} & \text{Asset Credit Reversal} & \$50.00 \\ \end{array}$$

#### 5. Customer Due Collection (Receipt Entry)
- **Trigger**: Due collection workbench receives payment from customer.
- **Posting**:
  $$\begin{array}{llr} \textbf{Account Head} & \textbf{Type} & \textbf{Amount} \\ \hline \text{Dr. Bank (10201) / Cash (10101) / MFS (10301)} & \text{Asset (Amount Collected)} & \$480.00 \\ \text{Dr. Early Payment Discount Allowed (50201)} & \text{Operating Expense} & \$20.00 \\ \quad \text{Cr. Accounts Receivable (10401)} & \text{Asset (Customer Due Cleared)} & \$500.00 \\ \end{array}$$

#### 6. Supplier Due Payment Voucher (Payment Entry)
- **Trigger**: Accounts payable payment released to pharmaceutical supplier.
- **Posting**:
  $$\begin{array}{llr} \textbf{Account Head} & \textbf{Type} & \textbf{Amount} \\ \hline \text{Dr. Accounts Payable / Supplier (20101)} & \text{Liability (Supplier Due Cleared)} & \$2,000.00 \\ \quad \text{Cr. Bank (10201) / Cash (10101)} & \text{Asset (Funds Disbursed)} & \$1,950.00 \\ \quad \text{Cr. Purchase Discount Received (40201)} & \text{Other Income} & \$50.00 \\ \end{array}$$

#### 7. Inter-Account Fund Transfer / Cash Closer Handover (Contra Entry)
- **Trigger**: Counter cash shifted to Main Vault, or Bank deposit/withdrawal.
- **Posting**:
  $$\begin{array}{llr} \textbf{Account Head} & \textbf{Type} & \textbf{Amount} \\ \hline \text{Dr. Main Cash Vault (10102) or Bank (10201)} & \text{Asset (Receiving Account)} & \$3,500.00 \\ \quad \text{Cr. Counter Cash (10101) or Sending Bank} & \text{Asset (Source Account)} & \$3,500.00 \\ \end{array}$$

#### 8. Operational Expense Voucher
- **Trigger**: Rent, electricity, pharmacy license renewal, or petty cash expense.
- **Posting**:
  $$\begin{array}{llr} \textbf{Account Head} & \textbf{Type} & \textbf{Amount} \\ \hline \text{Dr. Operational Expense Head (50201)} & \text{Expense (Specific Category)} & \$450.00 \\ \quad \text{Cr. Counter Cash (10101) or Bank (10201)} & \text{Asset (Funds Paid)} & \$450.00 \\ \end{array}$$

#### 9. Miscellaneous Income & Capital Additions
- **Trigger**: Cash injection from owner or scrap/commission income.
- **Posting**:
  $$\begin{array}{llr} \textbf{Account Head} & \textbf{Type} & \textbf{Amount} \\ \hline \text{Dr. Bank (10201) / Cash (10101)} & \text{Asset (Funds Deposited)} & \$10,000.00 \\ \quad \text{Cr. Owner Capital / Equity (30101)} & \text{Equity (Owner Contribution)} & \$10,000.00 \\ \end{array}$$

#### 10. Year-End Period Closing Voucher
- **Trigger**: Fiscal year-end close (`FIN_1401`).
- **Posting**:
  $$\begin{array}{llr} \textbf{Account Head} & \textbf{Type} & \textbf{Amount} \\ \hline \text{Dr. Total Operating Revenues (40000 Heads)} & \text{Zeroes Income Heads} & \$250,000.00 \\ \quad \text{Cr. Total Operating Expenses (50000 Heads)} & \text{Zeroes Expense Heads} & \$180,000.00 \\ \quad \text{Cr. Retained Earnings / Equity (30201)} & \text{Net Profit to Equity} & \$70,000.00 \\ \end{array}$$

---

### 5.4 Field-to-Field Mapping Dictionary

This matrix maps ERP business specification fields (`acc-business-doc.md`) to the Modular ERP schema (`fin_*`) and the Aidly PMS Cloud Database (`acc_*`):

| Business Concept / Doc Field | Spec Data Type | Modular ERP Table & Field | Aidly PMS DB Table & Field | Semantic Meaning & Constraints |
|---|---|---|---|---|
| **Account Code** | `String(20)` | `fin_account.account_code` | `acc_transaction_account.account_no` | Unique identifier (e.g. `10101`, `20101`) |
| **Account Title** | `String(140)` | `fin_account.account_name` | `acc_transaction_account.account_name` | Human display name on vouchers and reports |
| **Account Classification** | `Select (5)` | `fin_account.root_type` (1–5) | Inferred from account code range | 1=Asset, 2=Liability, 3=Equity, 4=Income, 5=Expense |
| **Account Type / Control** | `Select` | `fin_account.control_type` (1–7) | `acc_transaction_account.account_type` | 1=Cash, 2=Bank, 4=MFS, AR, AP, Tax, Inventory |
| **Is Group / Leaf** | `Boolean` | `fin_account.is_postable` (`0=Group, 1=Leaf`) | Always leaf on `acc_transaction_account` | Only leaf accounts accept direct ledger entries |
| **Parent Node** | `Link` | `fin_account.account_group_no` | Implicit in account range | Links account to tree category |
| **Active / Freeze** | `Select` | `fin_account.is_active` (`1=Active, 0=Frozen`) | `acc_transaction_account.is_active` | `0` blocks any further voucher creation |
| **Opening Balance** | `Currency` | `fin_account.opening_balance` | `acc_transaction_account.opening_balance` | Initial balance migrated at system inception |
| **Current Balance** | `Currency` | Calculated via `fin_account_balance` | `acc_transaction_account.current_balance` | Real-time running liquidity position |
| **Voucher Number** | `String(50)` | `fin_voucher.voucher_no` | `acc_transaction_ledger.voucher_no` | Auto-generated sequential voucher identifier |
| **Posting Date** | `Date` | `fin_voucher.voucher_date` | `acc_transaction_ledger.transaction_date` | Financial value date recognized in statements |
| **Voucher Category** | `Select` | `fin_voucher.voucher_type_no` (`base_kind`) | `acc_transaction_ledger.transaction_type` | 1=Journal, 2=Payment, 3=Receipt, 4=Contra, 5=Sales |
| **Debit Amount** | `Currency` | `fin_voucher_dtl.debit` / `fin_ledger.debit` | `acc_transaction_ledger.debit_amount` | Must be $\ge 0.00$; if $>0$, credit must be 0 |
| **Credit Amount** | `Currency` | `fin_voucher_dtl.credit` / `fin_ledger.credit` | `acc_transaction_ledger.credit_amount` | Must be $\ge 0.00$; if $>0$, debit must be 0 |
| **Party Sub-ledger** | `Dynamic` | `fin_voucher_dtl.party_no` (`party_type`) | Linked customer / supplier ID | Debtor or Creditor entity cleared by transaction |
| **Cost Center** | `Link` | `fin_voucher_dtl.cost_center_no` | `sys_branches.branch_no` | Branch/department profit or cost allocation |
| **Narration / Remark** | `Long Text` | `fin_voucher.narration` | `acc_transaction_ledger.narration` | Free-text audit trail explaining the entry |
| **Tenant Isolation** | `BigInt` | `fin_voucher.company_no`, `branch_no` | `pharmacy_no`, `branch_no` | Composite isolation keys mandatory on all rows |

---

### 5.5 Verification & Implementation Audit

| Phase & Milestone | Implementation Status | Covered Modules / Endpoints | Outstanding Roadmap |
|---|:---:|---|---|
| **P0: Core Engine** | **100% COMPLETE** | COA (`FIN_1001`), Groups (`FIN_1002`), Voucher Entry (`FIN_1101`), Outbox Posting (`FinPostingService`), GL Ledger (`fin_ledger`), Zero-imbalance assertion | Full parity achieved |
| **P1: Operational Trading** | **100% COMPLETE** | Sales Invoices (SAL), Purchase Invoices (PUR), Customer Receipts, Supplier Payments, Stock Movement Sync (`inv_stock_movement`) | Sub-ledgers cleanly reconcile with GL control heads |
| **P2: Treasury & Control** | **PARTIAL (75%)** | Bank Accounts (`FIN_1005`), Manual Bank Recon (`FIN_1102`), Cashbook Statements (`DASH_2303`), Inter-account transfers (`CASH_1203`) | Auto-matching statement parser (CSV/MT940), Multi-currency FX Realization engine, Spending budget caps |
| **P3: Advanced & Closing** | **PARTIAL (40%)** | Period Closing (`FIN_1401`), Retained Earnings rollup, Fiscal calendar guardrails | Fixed Asset Register & SLM/WDV depreciation schedules, Deferred revenue/expense monthly amortization |

---

## 6. Aidly PMS Architecture (Option A: Pure Pharmacy Mode)

### 6.1 Multi-Tenancy & Isolation Engine
Aidly PMS operates on a high-density, multi-tenant composite key model:
- Primary isolation keys: `pharmacy_no` (Tenant) and `branch_no` (Outlet).
- **Rule**: Every single Dapper query **must** contain:
  ```sql
  WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
  ```
- `BaseController` extracts these dimensions from the authenticated claims context:
  ```csharp
  protected long CurrentPharmacyNo => _tenantContext.PharmacyNo; // Default: 1
  protected long CurrentBranchNo   => _tenantContext.BranchNo;   // Default: 1
  protected long CurrentUserNo     => _tenantContext.UserNo;     // Default: 1
  ```

---

### 6.2 Synchronous Dual-Ledger Posting Engine
Every checkout, receipt, refund, or stock adjustment executes an atomic transaction writing to both ledgers:

```text
                               POS SALE ATOMIC COMMIT
                                         │
        ┌────────────────────────────────┴────────────────────────────────┐
        ▼                                                                 ▼
 ┌──────────────────────────────────────┐                ┌──────────────────────────────────────┐
 │     UNIVERSAL STOCK LEDGER           │                │    UNIVERSAL GENERAL LEDGER          │
 │       (inv_stock_movement)           │                │     (acc_transaction_ledger)         │
 ├──────────────────────────────────────┤                ├──────────────────────────────────────┤
 │ • movement_type: 2 (Sale)            │                │ • Dr: Counter Cash (10101)           │
 │ • quantity_pcs: -10                  │                │ • Dr: Customer Due / AR (10401)      │
 │ • balance_after_pcs: 90              │                │ • Cr: Sales Revenue (40101)          │
 │ • unit_cost_price & sale_price       │                │ • Assertion: Sum(Dr) == Sum(Cr)      │
 └──────────────────────────────────────┘                └──────────────────────────────────────┘
```

---

### 6.3 All 64 Screen & Form IDs Registry (`menu.md` Spec)

| Form ID | Screen Description | Frontend Route | Backend Endpoint |
|---|---|---|---|
| **ACC_1101** | Chart of Accounts Register | `/accounting/transaction` | `GET /api/v1/acc/accounts` |
| **ACC_1102** | Create Transaction Account | `/accounting/transaction/add` | `POST /api/v1/acc/accounts` |
| **ACC_1103** | Expense Vouchers Register | `/accounting/expenses` | `GET /api/v1/acc/expenses` |
| **ACC_1104** | Add Expense Voucher | `/accounting/expenses/add` | `POST /api/v1/acc/expenses` |
| **ACC_1105** | Add Expense Category | `/accounting/expense-cat/add` | `POST /api/v1/acc/expense-categories` |
| **ACC_1106** | Miscellaneous Income Register | `/accounting/income` | `GET /api/v1/acc/incomes` (or `/acc/income`) |
| **ACC_1107** | Add Income Voucher | `/accounting/income/add` | `POST /api/v1/acc/incomes` (or `/acc/income`) |
| **ACC_1108** | Add Income Category | `/accounting/income-cat/add` | `POST /api/v1/acc/income-categories` |
| **ACC_1109** | Capital / Equity Balance Log | `/accounting/additional-bal` | `GET /api/v1/acc/additional-balances` |
| **ACC_1110** | Add Capital Balance | `/accounting/additional-bal/add` | `POST /api/v1/acc/additional-balances` |
| **CASH_1201** | Received Cash Register | `/cash/receive` | `GET /api/v1/cash/receive` |
| **CASH_1202** | Bulk Cash Handover Action | `/cash/receive/check-all` | `POST /api/v1/cash/receive/reconcile` |
| **CASH_1203** | Inter-Account Fund Transfers | `/reconciliation` | `GET /api/v1/acc/reconciliations` |
| **CASH_1204** | Add Fund Transfer | `/reconciliation/add` | `POST /api/v1/acc/reconciliations` |
| **CASH_1205** | Daily Cash Register Closer | `/cash/closer` | `POST /api/v1/cash/close-shift` |
| **DUE_1301** | Due Collection Workbench | `/due-collect` | `GET /api/v1/cust/dues` |
| **DUE_1302** | Due Paid Collections Audit | `/pms-due-paid-payments` | `GET /api/v1/due/collections` |
| **SALE_1401** | Retail POS Counter | `/customer/sale` | `POST /api/v1/pos/sales` |
| **SALE_1402** | Wholesale POS Counter | `/customer/sale?mode=wholesale` | `POST /api/v1/pos/sales` (mode=2) |
| **SALE_1403** | POS Sales Return & Refund | `/customer/sale/return` | `POST /api/v1/pos/sales/return` |
| **SALE_1404** | Quick Add Product (POS) | Modal | `POST /api/v1/prod/products` |
| **SALE_1405** | Quick Add Company (POS) | Modal | `POST /api/v1/prod/companies` |
| **SALE_1406** | Quick Add Generic (POS) | Modal | `POST /api/v1/prod/generics` |
| **CUST_1407** | Customer Directory & Ledger | `/customer/list` | `GET /api/v1/cust/customers` |
| **CUST_1408** | Add / Edit Customer Profile | `/customer/add` | `POST/PUT /api/v1/cust/customers` |
| **SALE_1409** | Customer Return Invoices | `/customer/returns` | `GET /api/v1/pos/sales/returns` |
| **SALE_1410** | Sales Invoices Audit Log | `/customer/sales` | `GET /api/v1/pos/sales` |
| **SUPP_1501** | Supplier Directory & Ledger | `/supplier/list` | `GET /api/v1/supp/suppliers` |
| **SUPP_1502** | Add / Edit Supplier | `/supplier/add` | `POST/PUT /api/v1/supp/suppliers` |
| **PUR_1503** | Purchase Invoice Entry (GRN) | `/supplier/purchase` | `POST /api/v1/pur/purchases` |
| **PUR_1504** | Purchase Calculation Selector | Inside purchase form | Modes: Standard, Flat, Box, Bonus |
| **PUR_1505** | Quick Add Supplier (Purchase) | Modal | `POST /api/v1/supp/suppliers` |
| **PUR_1506** | Supplier Invoices Audit Log | `/supplier/purchases` | `GET /api/v1/pur/purchases` |
| **PUR_1507** | Supplier Payment Voucher | `/supplier/payments` | `POST /api/v1/pur/payments` |
| **PUR_1508** | Purchase Return to Supplier | `/supplier/return` | `POST /api/v1/pur/returns` |
| **PUR_1509** | Purchase Returns Audit Log | `/supplier/returns` | `GET /api/v1/pur/returns` |
| **PROD_1601** | Product Catalog & Inventory | `/products/list` | `GET /api/v1/prod/products` |
| **PROD_1602** | Add New Medicine / SKU | `/products/add` | `POST /api/v1/prod/products` |
| **PROD_1603** | Expired & Near-Expiry Batches | `/products/expired` | `GET /api/v1/prod/batches/near-expiry` |
| **PROD_1604** | Company / Manufacturer List | `/products/company/list` | `GET /api/v1/prod/companies` |
| **PROD_1605** | Add Company / Manufacturer | Modal / `/products/company/add` | `POST /api/v1/prod/companies` |
| **PROD_1606** | Generic Formulations Directory | `/products/generic/list` | `GET /api/v1/prod/generics` |
| **PROD_1607** | Add Generic Formulation | Modal / `/products/generic/add` | `POST /api/v1/prod/generics` |
| **PROD_1608** | Inventory Requisition Sheet | `/products/requisition` | `POST /api/v1/prod/requisitions` |
| **PROD_1609** | Requisition Invoices Audit Log | `/products/requisition-record` | `GET /api/v1/prod/requisitions` |
| **DOC_1701** | Doctor Directory & Profiles | `/doctors-portal/list` | `GET /api/v1/doc/doctors` |
| **DOC_1702** | Register New Doctor | `/doctors-portal/add` | `POST /api/v1/doc/doctors` |
| **DOC_1703** | Digital Prescription Writer | `/doctors-portal/prescription` | `POST /api/v1/doc/prescriptions` |
| **DOC_1704** | Prescriptions Audit Log | `/doctors-portal/prescriptions` | `GET /api/v1/doc/prescriptions` |
| **DOC_1705** | Clinic / Prescription Settings | `/doctors-portal/settings` | `GET/PUT /api/v1/doc/prescription-settings` |
| **ROLE_1801** | System User Representatives | `/representatives/list` | `GET /api/v1/sys/users` |
| **ROLE_1802** | Add System User / Employee | `/representatives/add` | `POST /api/v1/sys/users` |
| **ROLE_1803** | Security Roles & Permissions | `/roles/list` | `GET /api/v1/roles` |
| **ROLE_1804** | Create / Edit Security Role | `/roles/add` | `POST /api/v1/roles` |
| **CONF_1901** | POS Sales Terminal Config | `/settings/sale` | `GET/PUT /api/v1/config/sale-configs` |
| **CONF_1902** | Invoice & Thermal Print Layout | `/settings/invoice` | `GET/PUT /api/v1/config/invoice-configs` |
| **CONF_1903** | Product SKU & Barcode Config | `/settings/product` | `GET/PUT /api/v1/config/product-configs` |
| **CONF_1904** | Loyalty Points & Rewards Tier | `/settings/loyalty` | `GET/PUT /api/v1/config/loyalty-configs` |
| **CONF_1905** | Bulk SMS & Notification Gateway | `/settings/sms` | `GET/PUT /api/v1/config/sms-configs` |
| **SUB_2101** | License Tier & Cloud Billing | `/subscription` | `GET /api/v1/sub/subscription`, `/sub/payments` |
| **PHAR_2201**| Multi-Branch Outlet Switcher | `/change-pharmacy` | `GET /api/v1/sys/branches`, `POST /sys/switch-branch` |
| **DASH_2301**| Executive KPI Dashboard | `/dashboard` | `GET /api/v1/dashboard/summary` |
| **DASH_2302**| Fast Moving Analytics (Top/Low) | `/dashboard` | `GET /api/v1/dashboard/analytics` |
| **DASH_2303**| Cashbook Daily Statement | `/dashboard` | `GET /api/v1/dashboard/cashbook` |

---

### 6.4 Domain Models & Compatibility Field Mapping

The database uses PostgreSQL `snake_case`. Angular domain models in `core/models/` map 1:1 with bidirectional compatibility aliases:

#### Products (`product.models.ts`)
- `ProdCompany`: `company_no`, `name`, `is_active`
- `ProdGeneric`: `generic_no`, `name`, `is_active`
- `ProdProduct`: `product_no`, `product_name`, `company_no`, `generic_no`, `purchase_price_per_piece`, `sale_price_per_piece`, `quantity_per_box`, `min_stock_qty_pcs`, `barcode_value`, `is_active`
  - Aliases: `brand_name`, `generic_name`, `company_name`, `stock_qty`
- `ProdProductBatch`: `batch_no`, `batch_number`, `expiry_date`, `box_quantity`, `quantity_in_box`, `total_quantity_pcs`, `cost_per_box`, `sale_price_per_box`, `product_name`

#### Suppliers (`supplier.models.ts`)
- `SuppSupplier`: `supplier_no`, `name`, `phone`, `current_due_balance`, `note`, `is_active` *(Note: No address column on SQL table!)*
- `PurPurchaseInvoice`: `purchase_invoice_no`, `inventory_number`, `purchase_date`, `supplier_no`, `gross_total_price`, `discount_value`, `final_price`, `paid_amount`, `due_amount`, `items`, `supplier`

#### Customers & Sales (`customer.models.ts`)
- `CustCustomer`: `customer_no`, `name`, `phone`, `gender`, `customer_type`, `discount_percent`, `address`, `note` (alias: `notes`), `has_due`, `current_due` (alias: `total_due`), `is_loyalty_member`
- `SaleInvoice`: `sale_invoice_no`, `sales_number`, `sale_timestamp`, `customer_no`, `customer_name`, `customer`, `gross_total_price`, `final_price`, `due_amount`, `payment_method`, `items`
- `SaleInvoiceItem`: `product_no`, `product_name`, `product`, `sale_qty`, `unit_sale_price`, `total_price`

#### Accounting (`accounting.models.ts`)
- `AccTransactionAccount`: `account_no`, `account_name`, `account_type` (1=Cash, 2=Bank, 4=MFS), `bank_name`, `bank_account_number`, `phone_number`, `opening_balance`, `current_balance`
- `AccExpenseCategory`: `expense_category_no`, `name`, `is_active`
- `AccExpense`: `expense_no`, `voucher_no`, `expense_category_no`, `account_no`, `amount`, `payment_type`, `payment_method`, `note`, `expense_date`, `category_name`, `account_name`
- `AccIncomeCategory`: `income_category_no`, `name`, `is_active`
- `AccIncome`: `income_no`, `receipt_no`, `income_category_no`, `account_no`, `amount`, `payment_type`, `payment_method`, `received_from`, `note`, `income_date`, `category_name`, `account_name`

#### Doctors (`doctor.models.ts`)
- `DocDoctor`: `doctor_no`, `first_name_en`, `last_name_en`, `first_name_bn`, `last_name_bn`, `phone`, `gender` (`1=Male, 2=Female, 3=Other`), `description_en`, `is_active`

---

## 7. Developer Guidelines, Gotchas & Conventional Commits

### 1. Conventional Commits Standard
Both repositories strictly enforce [Conventional Commits](https://www.conventionalcommits.org/):
- **Format**: `<type>(<optional-scope>): <subject>`
- **Permitted Types**: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`
- **Frontend Hook**: Managed via `husky` + `@commitlint/cli` at `sme-software-frontend/.husky/commit-msg`.
- **Backend Hook**: Managed via `Husky.Net` (v0.9.1) at `aidlypms-backend/.husky/commit-msg`.

### 2. Readonly Arrays from Backend APIs
- `PagedResult<T>.items` is typed as `readonly T[]`.
- Direct assignment to an Angular `signal<T[]>` triggers `TS2345`.
- **Solution**: Always spread: `this.items.set([...res.data.items]);`

### 3. TypeScript Narrowing in Closures
- Accessing `res.data` inside an update lambda (`this.items.update(list => [...list, res.data])`) causes compiler failure because `res.data` may be `undefined`.
- **Solution**: Extract to local `const`:
  ```typescript
  const item = res.data;
  if (item) {
    this.items.update(list => [...list, item]);
  }
  ```

### 4. Windows Binary Locking
- Running `AidlyPms.Api.exe` locks the binary in `bin/Debug/net10.0/`.
- Always terminate before building or restarting:
  ```powershell
  taskkill /F /IM AidlyPms.Api.exe
  ```

### 5. Route Aliasing on Backend Controllers
- To accommodate singular and plural routes in Angular services, controllers support dual routes:
  - `[HttpGet("income")]` and `[HttpGet("incomes")]`
  - `[Route("api/v1/acc/additional-balances")]` and `[Route("api/v1/acc/additional-balance")]`
  - `[HttpGet("sms-configs")]` and `[HttpGet("bulk-sms-configs")]`

### 6. Standard API Response Structure
```csharp
// Success
return OkResponse<T>(data, "Fetched successfully"); // HTTP 200 { success: true, data: ..., message: ... }
// Paged Success
return PagedResponse<T>(pagedData, "List retrieved"); // HTTP 200 { success: true, data: { items: [...], totalCount: ... } }
// Errors
return NotFoundResponse<T>("Item not found"); // HTTP 404
return FailResponse<T>("Validation failed", "Error details", 400); // HTTP 400
```

---

## 8. Complete Windows Reinstallation & Machine Restoration Guide

Follow these sequential steps when restoring development after a fresh Windows installation:

### Step 1: Pre-Reinstallation Backup Checklist
Before formatting Windows or changing machines, back up:
1. The entire folder `c:\pridesys\apps\sme\aidlyErp\` to an external SSD/USB drive.
2. Confirm that all local Git commits in `aidlypms-backend` and `sme-software-frontend` are pushed to GitHub (`origin/main`).

### Step 2: Install Software Prerequisites
1. **Git for Windows**: [https://git-scm.com/download/win](https://git-scm.com/download/win) (Ensure Git Bash & Credential Manager are installed).
2. **Node.js (LTS v20.x or v22.x)**: [https://nodejs.org](https://nodejs.org).
3. **.NET 10 SDK**: [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0).
4. **PowerShell 7+**: `winget install Microsoft.PowerShell` (recommended terminal).

### Step 3: Clone or Restore Repositories
Create the target path:
```powershell
mkdir c:\pridesys\apps\sme\aidlyErp
cd c:\pridesys\apps\sme\aidlyErp

# Clone Backend (.NET 10 API)
git clone https://github.com/asibul-hasan/aidlypms-backend.git aidlypms-backend

# Clone Frontend (Angular 19+ Nx Monorepo)
git clone https://github.com/asibul-hasan/aidlypms-frontend.git sme-software-frontend

# Clone SME Monolith Backend (if working on SME ERP)
git clone -b modular-monolith-restructure https://github.com/asibul-hasan/aidly-backend.git sme-dotnet-backend
```

### Step 4: Setup & Launch Backend (Aidly PMS)
```powershell
cd c:\pridesys\apps\sme\aidlyErp\aidlypms-backend
dotnet restore src/Host/AidlyPms.Api/AidlyPms.Api.csproj
dotnet build src/Host/AidlyPms.Api/AidlyPms.Api.csproj

# Start Backend on Port 5275:
dotnet run --project src/Host/AidlyPms.Api/AidlyPms.Api.csproj --launch-profile http
```
Verify backend health in browser:
`http://localhost:5275/api/v1/dashboard/summary`

### Step 5: Setup & Launch Frontend (Aidly PMS)
```powershell
cd c:\pridesys\apps\sme\aidlyErp\sme-software-frontend
npm install
npm run build:pms

# Start Angular PMS on Port 4203:
npm run start:pms
```
Open browser: `http://localhost:4203` (All requests to `/api/*` automatically proxy to `http://localhost:5275`).

### Step 6: Verify Database Connectivity
The backend connects directly to Supabase cloud PostgreSQL. No local PostgreSQL installation is required. Ensure your outbound network allows port 5432 to AWS Singapore (`aws-0-ap-southeast-1.pooler.supabase.com`).

---
*Maintained by Engineering Team. Updated September 2026 for Aidly PMS & ERP Enterprise Architecture.*
