# Security hardening and SaaS platform-owner access

The platform owner is a trusted developer/support identity, separate from a client company's manager. Authority comes from `sys_role_capabilities`, not a role's alias, display name, email address, user number, browser state, or incoming admin claims.

## Owner and tenant boundaries

Migration `20261029_18_authentication_and_query_hardening.sql` assigns `PLATFORM_OWNER` to the existing protected system role 1 and `STORE_MANAGER` to the existing protected system role 9. Existing users keep their role assignments and password hashes. This does not promote a company manager to platform owner.

A platform-owner session can use every current and future menu/action and switch into any company/branch without creating client access rows, including a suspended company's context for support. The identity itself and its system role must remain active. Tenant staff still require a matching tenant role and their home branch or explicit branch membership. Store managers cannot use developer-only screens.

Platform users and roles are excluded from tenant staff/role lists, counts, and direct lookups. Attempted tenant updates return not-found. Tenant operational/audit responses replace platform actor details with `Platform support`; full audit records remain stored and visible to the platform owner. Platform accounts should be deactivated rather than deleted.

Only the platform owner can provision companies and branches. The existing Change Pharmacy page shows a Client setup form only to the owner. A company is created with a main branch and a standard chart of accounts; additional client settings and staff permissions are configured through the existing screens after switching into that branch. The owner automatically retains access to newly provisioned companies and branches.

The capability table has no tenant-facing write API. To bootstrap a new installation that has no existing developer account, an authorized database administrator must provision an account with an explicitly chosen password or assign an existing account to the protected owner role through a controlled administrative process. Do not invent shared/default developer passwords. A role name alone never grants authority.

## Deployment requirements

- Supply a fresh random signing value through `JWT_SIGNING_KEY` (at least 32 bytes). `Jwt:SigningKey` can be supplied by an external configuration provider. The legacy `Jwt:Key` field is ignored and the previously published fallback is rejected. No signing key is generated or committed by this change. Missing/invalid configuration stops startup.
- Production CORS origins come only from `ALLOWED_ORIGINS`. Development-only configured origins remain supported locally.
- Serve the browser and API over HTTPS in production. Browser authentication uses Secure, HttpOnly cookies with CSRF protection. Prefer a same-site API domain/reverse proxy; browsers that block third-party cookies can prevent cross-site hosted API sessions.
- Behind a TLS-terminating reverse proxy, configure `TRUSTED_PROXY_ADDRESSES` with its comma-separated IP addresses and forward the original scheme/client IP. Unknown proxies are not trusted. This is needed for correct secure-cookie handling behind an HTTP upstream.
- Deploy frontend and backend together because browser authentication no longer returns a bearer credential to JavaScript. The response `token: "session"` is only a compatibility marker. The server still validates authenticated bearer requests against registered sessions, where applicable.
- Run the identical migration in both repositories. Startup migrations are transactional; index creation is ordinary `CREATE INDEX`, not `CONCURRENTLY`. Schedule the rollout for the expected index-build/lock duration. No production migration has been run by the development checks.

Existing browser tokens are not migrated. Users sign in again after deployment. Existing password hashes are preserved byte-for-byte; blank or malformed hashes fail closed rather than accepting a shared fallback. New accounts require an explicit password of at least 12 characters and no more than 72 UTF-8 bytes. The public default-account provisioning/reset endpoint is disabled. Existing demo/account passwords are not changed by the migration.

## Browser and session protocol

1. GET `/api/v1/auth/csrf` with browser credentials and retain the returned request token in memory.
2. POST login with the `X-CSRF-TOKEN` header and credentials included. The server sets the HttpOnly session cookie.
3. Obtain a new CSRF token after login because it is bound to the authenticated identity. Send it on subsequent mutating browser requests. The Angular interceptor handles this only for the configured API base URL.
4. POST `/api/v1/auth/logout` to revoke the server session and remove its cookie.

Access sessions expire after 30 minutes by default (`Jwt:LifetimeMinutes`, constrained to 5–60 minutes). There is no silent refresh endpoint: expired sessions require sign-in. Account disable/delete, a password/role update through the API, or a changed user `updated_at` invalidates existing sessions. Role status, protected capabilities, memberships, special permissions and menu permissions are read live on each request in one database round trip; they are not trusted from stale JWT flags. Administrators doing direct database maintenance must update `updated_at` when changing credential material, or explicitly revoke the user's sessions.

Offline sales continue using the existing cached user/product/customer model; the browser stores no bearer credential in localStorage. Cached UI permissions do not authorize server writes. Logout while offline cannot revoke a server session until a server request succeeds; its normal expiry remains enforced. Periodically purge expired session rows according to the operator's retention policy using the expiry index. Keep financial/audit records separately.

## Query changes

- Executive dashboard aggregates share one `QueryMultipleAsync` batch; the expiry threshold is computed in SQL. Previously the action issued 22 sequential commands.
- Authorization shares one request access snapshot. Role listing fetches permissions for all listed roles in one query.
- Low-stock reporting aggregates six-month sales once per product. Product substring search uses independently indexable branches for name, barcode and generic name; trigram operator classes are resolved from the actual installed extension schema.
- Ledger daily totals compare the raw timestamp to a half-open UTC interval.
- Product lists accept optional `afterName`; sales lists accept `beforeTimestamp` plus `beforeInvoiceNo`. Existing offset pagination remains supported. Sales ordering includes the invoice-ID tiebreaker.
- New targeted indexes support returns, ledger day ranges, user membership/listing, normalized login lookups and substring search. Redundant ordinary indexes are removed only when their catalog definitions match the retained unique constraints or the replacement cursor index. Unique constraints remain intact.
- Operational record IDs, configuration IDs and document counters use PostgreSQL sequences. No column DEFAULTs were added.

Performance depends on real data and deployment latency. Validate the representative query plans and read/write latency on staging before making production performance claims.

## Verification

The regression suite covers non-null credential serialization, invalid hash rejection, forged role/admin claims, hidden owner identities, grant ceilings, alternate inventory permission gates, new-company access, cookie/CSRF flows, account/session invalidation, and upgrading the previous schema twice without changing passwords. Live checks use an isolated local PostgreSQL database, not customer data. Run the backend build/tests, schema/migration/menu parity and Angular production build before release.

Verified locally on 2026-10-29:

- Backend build: zero warnings and zero errors.
- Full suite against isolated PostgreSQL 16.15: 183 passed, zero failed, zero skipped.
- Four-file schema parity: 63 tables, 830 columns; 18 identical migrations and menu/route parity.
- Angular production build: passed; existing bundle-budget and CommonJS warnings remain.
- Migration upgrade/reapply preserves existing password hashes and does not rewind sequence counters.
- Both repositories pass `git diff --check`.

These checks did not modify a production database or deploy/push either repository.
