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

### Rule 2: Sub-Repository Awareness
`G:\aidlypms` contains two distinct Git repositories:
- `aidlypms-backend/` (Backend .NET 10 Web API)
- `sme-software-frontend/` (Frontend Angular Monorepo)

Do not run git commands directly in `G:\aidlypms` root expecting a single repository. Always specify the repository:
```bash
git -C aidlypms-backend pull --rebase origin main
git -C aidlypms-backend push origin main

git -C sme-software-frontend pull --rebase origin main
git -C sme-software-frontend push origin main
```

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


