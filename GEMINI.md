# GEMINI.md — Aidly PMS & ERP System Architecture & Developer Memory

> **IMPORTANT**: This document is the primary project memory and architectural blueprint for **Aidly PMS (Pharmacy Management System) & Aidly ERP**. When setting up a new development environment, restoring Windows, or resuming development with any AI agent (Gemini, Antigravity, Claude), load this file first to restore complete context.

---

## 1. Project Overview & Git Repositories

Aidly PMS is a high-performance, enterprise-grade multi-tenant Pharmacy Management and Retail/Wholesale ERP application built with modern architecture:
- **Backend**: .NET 10 Web API (C# 13, Dapper, Npgsql, PostgreSQL 17)
- **Frontend**: Angular 19+ (Standalone Components, Signals, Ng-Zorro AntD, TailwindCSS, Chart.js, Nx Monorepo)
- **Database**: Supabase PostgreSQL 17.6 on AWS (57 Tables, 736 Columns)

### Git Repositories
| Layer | GitHub Repository URL | Branch | Local Directory |
|---|---|---|---|
| **Frontend** | `https://github.com/asibul-hasan/aidlypms-frontend.git` | `main` | `sme-software-frontend` |
| **Backend** | `https://github.com/asibul-hasan/aidlypms-backend.git` | `main` | `aidlypms-backend` |

---

## 2. Environment & Network Configuration

### Backend (.NET 10 Web API)
- **Path**: `aidlypms-backend/src/Host/AidlyPms.Api`
- **Application URL**: `http://localhost:5275`
- **Launch Profile**: `http` (configured in `Properties/launchSettings.json`)
- **Run Command**:
  ```bash
  cd aidlypms-backend
  dotnet run --project src/Host/AidlyPms.Api/AidlyPms.Api.csproj --launch-profile http
  ```
  *(Or execute `run-pms-backend.bat` from workspace root)*

### Frontend (Angular 19+ / Nx)
- **Path**: `sme-software-frontend/apps/aidlyPms`
- **Application URL**: `http://localhost:4203` (or `4202` for multi-client)
- **Proxy Configuration**: `apps/aidlyPms/proxy.conf.json` forwards `/api` -> `http://localhost:5275`
- **Run Command**:
  ```bash
  cd sme-software-frontend
  npm run start:pms
  # runs: nx serve aidlyPms --port=4203
  ```
- **Build Command**:
  ```bash
  cd sme-software-frontend
  npx nx build aidlyPms
  ```

### Database (Supabase Cloud PostgreSQL 17.6)
- **Connection String (Transaction Pooler)**:
  ```
  Host=aws-0-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.rjxkplqdubxxxhyubdjq;Password=Asibul@2303;SSL Mode=Require;Trust Server Certificate=true
  ```
- **Direct Connection String**:
  ```
  Host=db.rjxkplqdubxxxhyubdjq.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=Asibul@2303;SSL Mode=Require;Trust Server Certificate=true
  ```
- **Schema & DDL**: Contained in `db_schema_dump.tsv` and `db_script.sql` (57 tables, 736 columns). Fully migrated on cloud.

---

## 3. Core Architectural Rules & Conventions

### Rule 1: Multi-Tenancy (Option A — Pure Pharmacy Mode)
- Every table has `pharmacy_no` and `branch_no` (composite tenant keys).
- **Every Dapper SQL query MUST filter by tenant**:
  ```sql
  WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
  ```
- `BaseController` automatically resolves `CurrentPharmacyNo` (default: 1), `CurrentBranchNo` (default: 1), and `CurrentUserNo` (default: 1) from `ITenantContext`.

### Rule 2: Synchronous Dual-Ledger Posting Engine
Every transaction in the system posts to **both** ledgers simultaneously in a single atomic database transaction:
1. **Universal Stock Movement Ledger (`inv_stock_movement`)**:
   - Tracks every SKU piece-level movement: `movement_type` (1=Purchase, 2=Sale, 3=Sale Return, 4=Purchase Return, 5=Adjustment, 6=Handover).
   - Records `quantity_pcs`, `balance_after_pcs`, `unit_cost_price`, `unit_sale_price`.
2. **Universal Financial General Ledger (`acc_transaction_ledger`)**:
   - Double-entry bookkeeping: every transaction creates balanced Debit and Credit rows.
   - **Strict Mathematical Equality**: $\sum \text{Dr} \equiv \sum \text{Cr}$ across every transaction.
   - Verified accounts: `10101` (Counter Cash), `10102` (Main Cash Vault), `10201` (Bank Accounts), `10301` (MFS Accounts), `10401` (Accounts Receivable/Due), `20101` (Accounts Payable/Suppliers), `30101` (Capital/Equity), `40101` (Sales Revenue), `50101` (Cost of Goods Sold), `50201` (Operating Expenses).

### Rule 3: Zero Mock / Zero Hardcoded Data in Frontend
- **NEVER** use static mock arrays for tables, select dropdowns, or KPI summaries.
- All lists must be Angular `signal<T[]>([])` populated via reactive RxJS subscriptions in `ngOnInit()`.
- Modals for creating new items must invalidate or update local signals on success.
- Delete operations must prompt confirmation via `ModalService` and call the corresponding API `delete*()` endpoint.

### Rule 4: Conventional Commits Enforcement
Both repositories enforce the [Conventional Commits](https://www.conventionalcommits.org/) standard via automated Git `commit-msg` hooks:
- **Specification Format**: `<type>(<optional-scope>): <subject>`
- **Permitted Types**: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`
- **Frontend Implementation**:
  - Enforced via `@commitlint/cli`, `@commitlint/config-conventional`, and `commitlint.config.js`.
  - Managed by `husky` v9 with hook at `.husky/commit-msg` executing `npx --no -- commitlint --edit "$1"`.
  - Automatically configured upon running `npm install` (via `"prepare": "husky"` script in `package.json`).
- **Backend Implementation**:
  - Enforced via `Husky.Net` (v0.9.1) with hook at `.husky/commit-msg` validating commit messages using POSIX regex.
  - Managed via `.config/dotnet-tools.json` and MSBuild target in `AidlyPms.Api.csproj`.
  - Automatically restored and activated during `dotnet restore` / `dotnet build`.

---

## 4. All 64 Screen & Form IDs Mapping (`menu.md` Spec)

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

## 5. Domain Models Field Reference & Naming Conventions

The database uses PostgreSQL `snake_case`. Models in `core/models/` have 1:1 mapping with compatibility aliases.

### Products (`product.models.ts`):
- `ProdCompany`: `company_no`, `name`, `is_active`
- `ProdGeneric`: `generic_no`, `name`, `is_active`
- `ProdProduct`: `product_no`, `product_name`, `company_no`, `generic_no`, `purchase_price_per_piece`, `sale_price_per_piece`, `quantity_per_box`, `min_stock_qty_pcs`, `barcode_value`, `is_active`
  - Compatibility aliases: `brand_name`, `generic_name`, `company_name`, `stock_qty`
- `ProdProductBatch`: `batch_no`, `batch_number`, `expiry_date`, `box_quantity`, `quantity_in_box`, `total_quantity_pcs`, `cost_per_box`, `sale_price_per_box`, `product_name`

### Suppliers (`supplier.models.ts`):
- `SuppSupplier`: `supplier_no`, `name`, `phone`, `current_due_balance`, `note`, `is_active` *(Note: No address column on SQL table!)*
- `PurPurchaseInvoice`: `purchase_invoice_no`, `inventory_number`, `purchase_date`, `supplier_no`, `gross_total_price`, `discount_value`, `final_price`, `paid_amount`, `due_amount`, `items`, `supplier`

### Customers & Sales (`customer.models.ts`):
- `CustCustomer`: `customer_no`, `name`, `phone`, `gender`, `customer_type`, `discount_percent`, `address`, `note` (alias: `notes`), `has_due`, `current_due` (alias: `total_due`), `is_loyalty_member`
- `SaleInvoice`: `sale_invoice_no`, `sales_number`, `sale_timestamp`, `customer_no`, `customer_name`, `customer`, `gross_total_price`, `final_price`, `due_amount`, `payment_method`, `items`
- `SaleInvoiceItem`: `product_no`, `product_name`, `product`, `sale_qty`, `unit_sale_price`, `total_price`

### Accounting (`accounting.models.ts`):
- `AccTransactionAccount`: `account_no`, `account_name`, `account_type` (1=Cash, 2=Bank, 4=MFS), `bank_name`, `bank_account_number`, `phone_number`, `opening_balance`, `current_balance`
- `AccExpenseCategory`: `expense_category_no`, `name`, `is_active`
- `AccExpense`: `expense_no`, `voucher_no`, `expense_category_no`, `account_no`, `amount`, `payment_type`, `payment_method`, `note`, `expense_date`, `category_name`, `account_name`
- `AccIncomeCategory`: `income_category_no`, `name`, `is_active`
- `AccIncome`: `income_no`, `receipt_no`, `income_category_no`, `account_no`, `amount`, `payment_type`, `payment_method`, `received_from`, `note`, `income_date`, `category_name`, `account_name`

### Doctors (`doctor.models.ts`):
- `DocDoctor`: `doctor_no`, `first_name_en`, `last_name_en`, `first_name_bn`, `last_name_bn`, `phone`, `gender` (`number | string` - 1=Male, 2=Female, 3=Other), `description_en`, `is_active`

---

## 6. Critical Developer Gotchas & Patterns

1. **Readonly Arrays from APIs**:
   - `PagedResult<T>.items` is typed as `readonly T[]`.
   - In Angular signals typed as `signal<T[]>`, direct `.set(res.data.items)` causes TS2345.
   - **Fix**: Always spread: `this.items.set([...res.data.items]);`
2. **TypeScript Narrowing in Closures**:
   - Inside `.subscribe({ next: res => { ... } })`, directly accessing `res.data` inside `.update(list => [...list, res.data])` fails type narrowing because `res.data` can be `undefined`.
   - **Fix**: Extract to const first:
     ```typescript
     const item = res.data;
     if (item) {
       this.items.update(list => [...list, item]);
     }
     ```
3. **Windows Binary Locking**:
   - Running `AidlyPms.Api.exe` locks the binary in `bin/Debug/net10.0/`.
   - Before running `dotnet build` or after controller changes, terminate the process:
     ```powershell
     taskkill /F /IM AidlyPms.Api.exe
     ```
4. **Route Aliasing on Backend Controllers**:
   - Angular services sometimes use singular and sometimes plural names.
   - Always ensure dual route attributes on controllers:
     - `[HttpGet("income")]` and `[HttpGet("incomes")]`
     - `[Route("api/v1/acc/additional-balances")]` and `[Route("api/v1/acc/additional-balance")]`
     - `[HttpGet("sms-configs")]` and `[HttpGet("bulk-sms-configs")]`
5. **BaseController Response Helpers**:
   - `OkResponse<T>(data, message)` -> HTTP 200 `{ success: true, data: ..., message: ... }`
   - `PagedResponse<T>(paged, message)` -> HTTP 200 `{ success: true, data: { items: [...], totalCount: ... } }`
   - `NotFoundResponse<T>(message)` -> HTTP 404 `{ success: false, message: ... }`
   - `FailResponse<T>(error, message, statusCode)` -> HTTP [statusCode] `{ success: false, errors: [...] }`

---

## 7. Reinstallation & New Machine Setup Guide

When setting up on a freshly installed Windows machine:

### Step 1: Install Software Prerequisites
1. **Git**: [https://git-scm.com/download/win](https://git-scm.com/download/win)
2. **Node.js**: v20.x LTS or v22.x LTS ([https://nodejs.org](https://nodejs.org))
3. **.NET 10 SDK**: [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0)
4. **PowerShell 7** (recommended)

### Step 2: Clone Repositories
```powershell
mkdir c:\pridesys\apps\sme\aidlyErp
cd c:\pridesys\apps\sme\aidlyErp

# Clone Backend
git clone https://github.com/asibul-hasan/aidlypms-backend.git aidlypms-backend

# Clone Frontend
git clone https://github.com/asibul-hasan/aidlypms-frontend.git sme-software-frontend
```

### Step 3: Set Up Backend
```powershell
cd c:\pridesys\apps\sme\aidlyErp\aidlypms-backend
dotnet restore src/Host/AidlyPms.Api/AidlyPms.Api.csproj
dotnet build src/Host/AidlyPms.Api/AidlyPms.Api.csproj
# Start Backend on Port 5275:
dotnet run --project src/Host/AidlyPms.Api/AidlyPms.Api.csproj --launch-profile http
```
Verify at `http://localhost:5275/api/v1/dashboard/summary`.

### Step 4: Set Up Frontend
```powershell
cd c:\pridesys\apps\sme\aidlyErp\sme-software-frontend
npm install
npm run build:pms
# Start Frontend on Port 4203:
npm run start:pms
```
Open browser at `http://localhost:4203`.

---
*Created on 2026-09-23. Maintained for Aidly PMS Enterprise Architecture.*
