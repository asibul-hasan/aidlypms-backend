# Git & Development Rules for AI Agents and Developers

## Mandatory Git Workflow Rules

### Rule 1: Always Pull Before Push (`pull --rebase` is Mandatory)
**Never run `git push` without pulling first.**
Before executing any push to remote (`origin main` or any feature branch):
1. **Always pull latest remote changes first with rebase:**
   ```bash
   git pull --rebase origin main
   ```
2. **Only after `pull --rebase` completes cleanly, push to remote:**
   ```bash
   git push origin main
   ```

#### Why This Rule is Enforced:
- The repositories (`aidlypms-backend` and `sme-software-frontend`) have automated GitHub Actions CI/CD pipelines configured with `semantic-release`.
- Whenever code is merged or pushed, the automated CI pipeline automatically generates release tags and commits release notes directly to `origin/main` (e.g., `chore(release): 1.8.4 [skip ci]`).
- If you attempt to push without pulling first, Git will reject the push:
  `! [rejected] main -> main (fetch first) - Updates were rejected because the remote contains work that you do not have locally.`
- Using `--rebase` keeps the commit history linear and clean, replaying your local commits neatly on top of the latest release commits without merge bubbles.

---

### Rule 2: Sub-Repository Awareness & Push Order
`G:\aidlypms` contains two distinct Git repositories:
- `aidlypms-backend/` (Backend .NET 10 Web API)
- `sme-software-frontend/` (Frontend Angular Monorepo)

Do not run git commands directly in `G:\aidlypms` root expecting a single repository. Always specify the repository:
```bash
# 1. Push frontend first
git -C sme-software-frontend pull --rebase origin main
git -C sme-software-frontend push origin main

# 2. Push backend second
git -C aidlypms-backend pull --rebase origin main
git -C aidlypms-backend push origin main
```

#### Mandatory Push Order (Frontend FIRST, Backend SECOND):
- **Always push `sme-software-frontend` first, then push `aidlypms-backend`.**
- **Why**: Step 5 of the backend GitHub Actions workflow clones the remote frontend repository's `main` branch to verify database migration and schema parity. If the backend is pushed first, its CI pipeline will execute against a frontend `main` that does not yet contain the new migration, causing the parity step to fail.

---

### Rule 3: Database & Migration Schema Parity
- Every schema change or new sequence must be maintained in sync across:
  - `aidlypms-backend/database/schema.sql` and `db_script.sql`
  - `sme-software-frontend/apps/aidlyPms/database/schema.sql` and `db_script.sql`
- All conditional constraint checks (e.g., circular FKs or unique keys) must be schema-scoped using:
  ```sql
  IF NOT EXISTS (
      SELECT 1 FROM information_schema.table_constraints
      WHERE table_schema = current_schema()
        AND table_name = 'table_name'
        AND constraint_name = 'constraint_name'
  )
  ```
  to avoid cross-schema catalog collisions in multi-tenant or integration testing.
- Any test seed data must strictly match real table columns.

---

### Rule 4: Verification Before Commit
- Run `dotnet build` on `aidlypms-backend` to ensure 0 warnings, 0 errors.
- Run `dotnet test` on unit tests to ensure all pass before pushing.
- Run `npm run verify:pms-schema` on `sme-software-frontend` to ensure 100% schema parity.
- Run `npm run build:pms -- --configuration production` on `sme-software-frontend`.

---

### Rule 5: Zero Defaults & Sequence Standards
- Database columns in Aidly PMS strictly forbid database-level `DEFAULT` values (no `DEFAULT TRUE`, no `DEFAULT 'xxx'`).
- Columns must be explicitly provided by backend / seed data.
- All primary keys and monotonic transaction identifiers must use PostgreSQL sequences (`nextval('seq_...')`) instead of `SELECT MAX(...) + 1`.

---

### Rule 6: CORS & Environment Variable Policy
- `appsettings.json` must NOT contain hardcoded frontend domains or `"AllowedOrigins": []`.
- Production CORS origins are supplied strictly via the `ALLOWED_ORIGINS` environment variable (comma or semicolon-separated, e.g. `ALLOWED_ORIGINS=https://app.aidlypms.com,https://admin.aidlypms.com`).
- Localhost origins are restricted strictly to `appsettings.Development.json`.
- When no origins are configured via `ALLOWED_ORIGINS` or development configuration, the backend logs a startup warning (`"No CORS AllowedOrigins configured! Cross-origin requests will not be permitted."`) and forbids cross-origin requests.

---

### Rule 7: NBR Statutory Invoice Compliance (Future Ticket)
- [ ] Statutory NBR-compliant tax invoice formatting:
  Aidly PMS receipts currently print the pharmacy's Business Identification Number (BIN) and Drug License (DL) for operational and identification purposes. No statutory NBR Mushak-6.3 compliance is claimed in code or documentation.
  To achieve full statutory NBR-style tax invoice compliance, the following required fields must be added and validated:
  - Buyer name and address (when required by VAT rules / registered buyer purchases).
  - Explicit fiscal invoice serial number.
  - Date and time of supply.
  - Itemized value (excluding VAT) per line item.
  - Applicable VAT rate and VAT amount explicitly itemized per line item.
  - Total VAT and gross payable breakdown.
  Reference: National Board of Revenue (NBR) Value Added Tax and Supplementary Duty Act & Rules.

---

### Rule 8: Unreceived Stock Settlement & Valuation Limitation
- When out-of-stock sales occur, the COGS and sale prices are recorded using estimated/product master costs. On subsequent GRN purchase receipt and placeholder settlement, stock quantities are trued up (placeholder returns toward 0 and real batch is reduced), but historical sale COGS and profit margins are not retroactively re-costed.

---

### Rule 9: Offline POS Plain Sales Architecture & Safety Guarantees
- **Pure Pharmacy Plain Sales Offline**: Offline selling is strictly limited to new plain sales (Cash, Card, MFS, Split, Customer Due) using cached products, batches, and customers (or Walking Customer). Returns, exchanges, voids, purchases, inventory transfers, and new customer registration are strictly blocked when offline.
- **Clinical Safety Restrictions**: Any product flagged as narcotic (`is_narcotic = TRUE`), prescription-required (`is_prescription_required = TRUE`), or belonging to POM/NARCOTIC drug schedules is strictly prohibited from offline checkout to prevent regulatory non-compliance.
- **Server Authority & Late Sync**: The server recomputes and decides all final item pricing, taxes, and total amounts (`price_variance = server_total - offline_client_total`). If an offline sale was created during a shift/date whose cash closer is already committed, the sale is posted with `sale_timestamp = UtcNow` and marked `late_sync = TRUE` without mutating historical closed cashbooks.
- **Idempotency & Replay Protection**: Every offline sale carries a client-generated UUID `idempotency_key` and SHA256 payload hash (`uq_sale_invoices_idempotency`). Replaying the identical transaction safely returns the original invoice (`200 OK`, `idempotent_replay = TRUE`) with zero duplicate stock movements or GL postings. Mismatched payloads return `409 Conflict`.

---

### Rule 10: Scope, Credential Safety & CI Log Policy
- **Scope Restriction**: All modifications are strictly scoped to Aidly PMS. Do NOT change Login, seed-defaults, demo users, or credentials under any circumstances.
- **Credential Protection**: AI agents and developers must NEVER read credential stores, Windows Credential Manager, git credential helpers, secret vaults, or tokens.
- **CI/CD Diagnostics**: If CI build or test logs are required to diagnose a pipeline failure, do not attempt to query remote CI runner environments or credential-backed APIs; ask the user to paste the CI logs directly.

---

## How to Add a Screen (DB-Driven RBAC Architecture)

To add a new screen or form to Aidly PMS while adhering to DB-driven menus, permissions, and CI parity gates, follow this mandatory 4-step checklist:

### 1. Database Migration & Seed Parity
1. Create identical migrations in both repositories:
   - `aidlypms-backend/database/migrations/YYYYMMDD_HH_add_screen_xyz.sql`
   - `sme-software-frontend/apps/aidlyPms/database/migrations/YYYYMMDD_HH_add_screen_xyz.sql`
2. Insert the screen into `sys_menus` under its parent submodule (`parent_menu_no`):
   ```sql
   INSERT INTO sys_menus (menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at)
   SELECT nextval('seq_sys_menus_pk'), 'MOD_1234', 'Screen Title', 'বাংলা শিরোনাম', '/route/path', '/route/path', 'icon-name',
          (SELECT menu_no FROM sys_menus WHERE menu_code = 'SUB_MODULE_CODE'), 10, 'SCREEN', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
   WHERE NOT EXISTS (SELECT 1 FROM sys_menus WHERE menu_code = 'MOD_1234');
   ```
3. Grant permissions in `sys_role_menu_permissions` to existing manager/admin roles.
4. Keep `schema.sql` and `db_script.sql` in both repositories strictly in sync.

### 2. Backend Controller Annotation
1. Annotate the API controller or endpoint with `[RequireMenu("MOD_1234")]`:
   ```csharp
   [ApiController]
   [Route("api/v1/[controller]")]
   [RequireMenu("MOD_1234")]
   public class MyNewFeatureController : BaseController
   ```
2. For high-impact operational writes (e.g. sale return, invoice void, price change, shift close, stock adjustment, user management, role management), also require the relevant special permission attribute: `[RequirePermission("special.code")]`.
3. Every public endpoint must be annotated with `[RequireMenu]`, `[RequirePermission]`, `[AllowAnonymous]`, or `[AuthenticatedOnly]`. The reflection test (`MenuAndRolePermissionsTests`) will fail CI if any action is unannotated.

### 3. Frontend Route Registry & Guard
1. Register screen metadata in `SYSTEM_SCREENS` in `apps/aidlyPms/src/app/core/config/menu.config.ts`:
   ```ts
   {
     form_id: 'MOD_1234',
     title: 'Screen Title',
     type: 'Main Screen',
     route: '/route/path',
     permission_code: 'MOD_1234',
     description: 'Functional description of the screen.',
     is_in_sidebar: true,
   }
   ```
2. Add the route in `apps/aidlyPms/src/app/app.routes.ts`:
   ```ts
   { path: 'route/path', component: MyComponent, canActivate: [menuGuard], data: { menuCode: 'MOD_1234' } },
   ```
3. Control UI element visibility/interaction inside components using directives:
   - Menu actions: `*appMenuAction="'MOD_1234:create'"` (or `:edit`, `:delete`, `:view`)
   - Special permissions: `*appHasPermission="'special.code'"`

### 4. Verification Gates
Run all verification gates before committing:
```bash
# 1. Verify schema, migration, and menu route parity (100% required)
npm --prefix sme-software-frontend run verify:pms-schema

# 2. Verify backend reflection test & business unit tests
export PATH="$HOME/.dotnet:$PATH"
dotnet test aidlypms-backend/tests/AidlyPms.Api.Tests/AidlyPms.Api.Tests.csproj

# 3. Verify Angular production build
npm --prefix sme-software-frontend run build:pms -- --configuration production
```





