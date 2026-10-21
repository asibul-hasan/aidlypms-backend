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
- This repository has automated GitHub Actions CI/CD pipelines configured with `semantic-release`.
- Whenever code is pushed to `main`, CI automatically generates release tags and commits release notes directly to `origin/main` (e.g., `chore(release): 1.x.x [skip ci]`).
- Attempting to push without pulling first causes non-fast-forward rejection (`fetch first`).
- Using `--rebase` keeps commit history linear and clean without unnecessary merge commits.

---

### Rule 2: Database & Migration Schema Parity
- Every schema change or new sequence must be reflected in `database/schema.sql` and `database/db_script.sql`.
- All conditional constraint checks must be schema-scoped using:
  ```sql
  IF NOT EXISTS (
      SELECT 1 FROM information_schema.table_constraints
      WHERE table_schema = current_schema()
        AND table_name = 'table_name'
        AND constraint_name = 'constraint_name'
  )
  ```
  to avoid cross-schema collisions.
- Any test seed data must strictly match real table columns.

---

### Rule 3: Verification Before Commit
- Run `dotnet build` to ensure 0 warnings, 0 errors.
- Run `dotnet test` on unit tests before pushing.
