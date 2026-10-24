# Changelog

All notable changes to this project will be documented in this file. See [commit-and-tag-version](https://github.com/absolute-version/commit-and-tag-version) for commit guidelines.

## [1.13.0](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.12.9...v1.13.0) (2026-10-24)


### 🚀 Features

* **inventory:** add unreceived-stock placeholder, stocktake engine, and ledger integrity ([d666fd4](https://github.com/asibul-hasan/aidlypms-backend/commit/d666fd48a4aabbcbf2aa61ec5e848d812f1e9339))

## [1.12.9](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.12.8...v1.12.9) (2026-10-23)


### 🐛 Bug Fixes

* **inventory:** prevent opening stock and purchase batch double-counting ([dd39aff](https://github.com/asibul-hasan/aidlypms-backend/commit/dd39aff937fb1a0ac3a922447862264ed0d0dfa5))

## [1.12.8](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.12.7...v1.12.8) (2026-10-23)


### 🐛 Bug Fixes

* **pos:** support return_req and property aliases in exchange request ([ad660fd](https://github.com/asibul-hasan/aidlypms-backend/commit/ad660fdad991d5bb32f5e03bddbab3a37a27fa28))

## [1.12.7](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.12.6...v1.12.7) (2026-10-22)


### 🐛 Bug Fixes

* **migrations:** add missing Npgsql import to migration runner and sync database schema ([41cc7ee](https://github.com/asibul-hasan/aidlypms-backend/commit/41cc7eee1891e35e4efb817ea7e2856a40c19cf3))

## [1.12.6](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.12.5...v1.12.6) (2026-10-22)


### 🐛 Bug Fixes

* **subscription:** auto-activate bKash renewal and decouple pending from confirm flow ([8ac1a76](https://github.com/asibul-hasan/aidlypms-backend/commit/8ac1a76a63b355000fe3c9564dd3b6c3179af094))

## [1.12.5](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.12.4...v1.12.5) (2026-10-22)


### 🐛 Bug Fixes

* **prod:** auto-run migrations on startup and harden product registration ([f9eb26c](https://github.com/asibul-hasan/aidlypms-backend/commit/f9eb26cbb338e872fda103a97e9eaf6f006f7108))

## [1.12.4](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.12.3...v1.12.4) (2026-10-22)


### 🐛 Bug Fixes

* **tests:** align customer and supplier seed schema in soft-delete integration test ([d9d514b](https://github.com/asibul-hasan/aidlypms-backend/commit/d9d514bacf60ec744e434d43ae57dd6e307ff16c))

## [1.12.3](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.12.2...v1.12.3) (2026-10-22)


### 🐛 Bug Fixes

* **tests:** supply created_at and updated_at for prod_product_batches in tests and opening stock batch insert ([fb6e6a9](https://github.com/asibul-hasan/aidlypms-backend/commit/fb6e6a9254862a87e653641b74aac8ff4f646bdc))

## [1.12.2](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.12.1...v1.12.2) (2026-10-22)


### 🐛 Bug Fixes

* **tests:** provide created_at for prod_product_batches and redact sensitive properties in audit log endpoint ([32496b1](https://github.com/asibul-hasan/aidlypms-backend/commit/32496b1df9016b1d23afe690698f84d4970ae533))

## [1.12.1](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.12.0...v1.12.1) (2026-10-22)


### 🐛 Bug Fixes

* **tests:** sync prod_products seed schema, user full_name mapping, and subscription fk ([7f65f05](https://github.com/asibul-hasan/aidlypms-backend/commit/7f65f057075e1c1008f2a75407870aa9cf652e50))

## [1.12.0](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.11.0...v1.12.0) (2026-10-22)


### 🚀 Features

* complete items 0-7: cleanups, soft-delete, audit logs, subscription expiry, and integration tests ([a7d1762](https://github.com/asibul-hasan/aidlypms-backend/commit/a7d17624b06ec94deb49d042bbe9a64b89ac0d1d))

## [1.11.0](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.10.4...v1.11.0) (2026-10-22)


### 🚀 Features

* **test:** add isolated schema harness, zero defaults, sequences and WAF tests ([d1b6a9e](https://github.com/asibul-hasan/aidlypms-backend/commit/d1b6a9e4220c2f3ce170c0782522819643691ce8))


### 🐛 Bug Fixes

* **sales:** balance POS journal on partial payment and resolve ledger accounts dynamically per pharmacy ([0752797](https://github.com/asibul-hasan/aidlypms-backend/commit/0752797ca64124b74d99646dd0e655a9b94ab89a))

## [1.10.4](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.10.3...v1.10.4) (2026-10-22)


### 🐛 Bug Fixes

* **sales:** support top-level customer_no in CreateSalePayload and allow null customer_no for walk-in sales ([e6a321c](https://github.com/asibul-hasan/aidlypms-backend/commit/e6a321c2bf6e1936cf0aa3a3967fd775c753097e))

## [1.10.3](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.10.2...v1.10.3) (2026-10-21)


### 🐛 Bug Fixes

* **sales:** pass qInBox parameter when auto-creating pre-purchase batch ([68d5577](https://github.com/asibul-hasan/aidlypms-backend/commit/68d5577740742d1ba337df24d68babf5d8d9ca5c))

## [1.10.2](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.10.1...v1.10.2) (2026-10-21)


### 🐛 Bug Fixes

* **tests:** enable out-of-stock sales in test seed config for integration tests ([cea34dc](https://github.com/asibul-hasan/aidlypms-backend/commit/cea34dc45419c894ba129be91d86d337d44467f5))


### 🧪 Tests

* **roles:** add tests verifying Menu and Role Permission endpoints, security attributes, and schema ([49e9141](https://github.com/asibul-hasan/aidlypms-backend/commit/49e91410f73571868ff803bafe0a3cd2e0c09257))

## [1.10.1](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.10.0...v1.10.1) (2026-10-21)


### 🐛 Bug Fixes

* **tests:** add DEFAULT TRUE to sys_roles.status and include status in test seeds ([10108ce](https://github.com/asibul-hasan/aidlypms-backend/commit/10108ce68bb4174fba59dad2a13c233912d58123))

## [1.10.0](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.9.0...v1.10.0) (2026-10-21)


### 🚀 Features

* **roles:** add menu and role permissions management endpoints and schema ([dc139a1](https://github.com/asibul-hasan/aidlypms-backend/commit/dc139a13e6cb98ed65d10ad43b33e33141b78ef1))


### 🐛 Bug Fixes

* **tests:** correct cfg_sale_configs seed columns and schema-scoped fk check ([2cc3c0d](https://github.com/asibul-hasan/aidlypms-backend/commit/2cc3c0d6c19dde99c1c6b14fa3610747bb51333d))


### 📝 Documentation

* add AGENTS.md with mandatory pull before push rule ([420ad7b](https://github.com/asibul-hasan/aidlypms-backend/commit/420ad7b7dfbcb7b55cfd3a76ea838644caa1af92))

## [1.9.0](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.8.4...v1.9.0) (2026-10-21)


### 🚀 Features

* **security:** enforce controller permissions and sequence-based monotonic IDs ([173ed3b](https://github.com/asibul-hasan/aidlypms-backend/commit/173ed3b544fe3b4ac5cb6773f4c5662cd08d79f0))

## [1.8.4](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.8.3...v1.8.4) (2026-10-21)


### 🐛 Bug Fixes

* **security:** implement 10-point audit hardening and tenant isolation ([3276844](https://github.com/asibul-hasan/aidlypms-backend/commit/32768448bc7c28564b963d86bd0c5ca7aef52319))

## [1.8.3](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.8.2...v1.8.3) (2026-10-21)


### 🧪 Tests

* **integration:** enforce throwaway postgres container, delete migration tests, isolate test pharmacy 9001, and add CI schema load checks ([c5fea7c](https://github.com/asibul-hasan/aidlypms-backend/commit/c5fea7c4dcc9a1d010ad4593c3b9640fff924020))

## [1.8.2](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.8.1...v1.8.2) (2026-10-21)


### 🐛 Bug Fixes

* **db-parity:** resolve column mismatches, strict narcotic block, and add real PostgreSQL integration test suite ([63a4d7a](https://github.com/asibul-hasan/aidlypms-backend/commit/63a4d7a218e86e212bd240423efafaa745555784))

## [1.8.1](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.8.0...v1.8.1) (2026-10-21)


### 🧪 Tests

* **db:** add automated migration verification test for DGDA and BMDC compliance ([15b194f](https://github.com/asibul-hasan/aidlypms-backend/commit/15b194fa416109b1b0cf384503662a7b82c15534))

## [1.8.0](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.7.0...v1.8.0) (2026-10-21)


### 🚀 Features

* **compliance:** add DGDA dar_number, drug_schedule classification, and doctor bmdc_reg_no ([74a2673](https://github.com/asibul-hasan/aidlypms-backend/commit/74a26737099c407220a3e236f3819a92d477e0ff))


### 📝 Documentation

* add strict architecture rules for frontend-driven lookups and zero DB defaults/constraints ([03f0cb0](https://github.com/asibul-hasan/aidlypms-backend/commit/03f0cb06e4253ff7ce625ab3b59a8d2da632c852))

## [1.7.0](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.6.0...v1.7.0) (2026-10-21)


### 🚀 Features

* **sales:** replace test runner with real xUnit suite, enforce void reason, add 4-check day-end reconciliation, and dynamic settlement ([1b77436](https://github.com/asibul-hasan/aidlypms-backend/commit/1b77436370ac1c6a278c0ec695d13373939a1929))

## [1.6.0](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.5.1...v1.6.0) (2026-10-21)


### 🚀 Features

* **sales:** add safe sales return, exchange engine, day-end financial tie-out, void reversal, and acceptance tests ([a7a6f0f](https://github.com/asibul-hasan/aidlypms-backend/commit/a7a6f0fa816296dd665bd1f93c3292ba4fe4478e))

## [1.5.1](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.5.0...v1.5.1) (2026-10-21)


### 🐛 Bug Fixes

* **pos:** harden sale void with ledger reversal, due/loyalty rollback and shift/return guards ([c172b98](https://github.com/asibul-hasan/aidlypms-backend/commit/c172b983921d3d8568c91e1796d863088f2f490e))

## [1.5.0](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.4.0...v1.5.0) (2026-10-21)


### 🚀 Features

* **clinical-inventory:** add FEFO expiry guards, inventory adjustments, transfers, clinical reports, and audit filter ([20b2e22](https://github.com/asibul-hasan/aidlypms-backend/commit/20b2e22aeb5f456d0dcbb9aa2266553b9918f8b9))

## [1.4.0](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.3.4...v1.4.0) (2026-10-18)


### 🚀 Features

* **requisition:** enforce minimum restock quantity suggestions and save validation ([8dc5b5b](https://github.com/asibul-hasan/aidlypms-backend/commit/8dc5b5bd8d53a5909ffd7e43392cf1ea6786d24a))

## [1.3.4](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.3.3...v1.3.4) (2026-10-18)


### 🐛 Bug Fixes

* **pos:** auto-cap applied payments against change amount to prevent double-entry imbalance ([5b38c5d](https://github.com/asibul-hasan/aidlypms-backend/commit/5b38c5db7c24cdc8a9738d6b6cedda2224f9191d))

## [1.3.3](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.3.2...v1.3.3) (2026-10-18)


### 🐛 Bug Fixes

* **sales:** enhance sale return processing with proportional discount and due adjustment ([8c66c78](https://github.com/asibul-hasan/aidlypms-backend/commit/8c66c780196f92d595c51ee257ce253d20f4072f))

## [1.3.2](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.3.1...v1.3.2) (2026-10-18)


### 🐛 Bug Fixes

* **products:** ensure stock calculations check valid expiry and positive quantity ([f08cba2](https://github.com/asibul-hasan/aidlypms-backend/commit/f08cba2aa6df072a3a7006a6b66356b195699527))

## [1.3.1](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.3.0...v1.3.1) (2026-10-17)


### 🐛 Bug Fixes

* **common:** support dd-MM-yyyy date parsing and end-of-day normalization for date filters ([84b1e8c](https://github.com/asibul-hasan/aidlypms-backend/commit/84b1e8cce7b99c3906aee6a230c8c481c9e42b57))

## [1.3.0](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.2.0...v1.3.0) (2026-10-17)


### 🚀 Features

* **products:** add stock valuation and inventory report endpoint ([d5569e9](https://github.com/asibul-hasan/aidlypms-backend/commit/d5569e9fffed6be2f4b08fd186958ff3c5a8c089))

## [1.2.0](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.1.4...v1.2.0) (2026-10-17)


### 🚀 Features

* **reports:** add date range and summary endpoints for accounting and dashboard reports ([a6095ff](https://github.com/asibul-hasan/aidlypms-backend/commit/a6095ffdafe18feac5b72494a6ef28e05f76fdc7))

## [1.1.4](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.1.3...v1.1.4) (2026-10-17)


### 🐛 Bug Fixes

* **accounting:** prevent deletion of accounts with transactions or active balance ([def8b1b](https://github.com/asibul-hasan/aidlypms-backend/commit/def8b1bf8d57bf3c03a79afb50198f5cf3cbe301))

## [1.1.3](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.1.2...v1.1.3) (2026-10-17)


### 🐛 Bug Fixes

* **requisition:** lock fulfilled requisitions from editing and unapproving ([404c67f](https://github.com/asibul-hasan/aidlypms-backend/commit/404c67fd1177747ae253af592155ebb09ff14580))

## [1.1.2](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.1.1...v1.1.2) (2026-10-17)


### 🐛 Bug Fixes

* **requisition:** prevent updating or cancelling approved requisitions ([49a666a](https://github.com/asibul-hasan/aidlypms-backend/commit/49a666ad3849bcff812cccb5878b2c975a3ad04b))

## [1.1.1](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.1.0...v1.1.1) (2026-10-17)


### 🐛 Bug Fixes

* **due:** resolve route ambiguity and table schema errors for due workbench ([e0bfcb6](https://github.com/asibul-hasan/aidlypms-backend/commit/e0bfcb60320fda44b0a79bf3d59339420eae5331))

## [1.1.0](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.0.1...v1.1.0) (2026-10-17)


### 🚀 Features

* **sales:** support retrieving sales invoice by sales_number or id with items ([a347941](https://github.com/asibul-hasan/aidlypms-backend/commit/a3479416b06300a055fe02fda90648c9d1ec0248))

## [1.0.1](https://github.com/asibul-hasan/aidlypms-backend/compare/v1.0.0...v1.0.1) (2026-10-17)


### 📝 Documentation

* **memory:** update release and semver tagging documentation in GEMINI.md ([4e74726](https://github.com/asibul-hasan/aidlypms-backend/commit/4e747269c3db4a99381d78565f53e715160ba5de))


### 🤖 Continuous Integration

* **release:** enhance release notes preparation and GitHub Release action ([fe4aa6f](https://github.com/asibul-hasan/aidlypms-backend/commit/fe4aa6f736dde9014ff94a1ebcf00bc0dd70b3af))

## 1.0.0 (2026-10-17)

### 🚀 Features

* add delete endpoints, route aliases, due collection controller, and NotFoundResponse helper ([01bed70](https://github.com/asibul-hasan/aidlypms-backend/commit/01bed70103062cb2481b065ab8cac2940e46c947))
* **backend:** initialize Aidly PMS .NET 10 Web API with modular architecture and Supabase integration ([4b816c5](https://github.com/asibul-hasan/aidlypms-backend/commit/4b816c522f7d4e1447690744fb09e5c65abe8f80))
* **docker:** add net10 dockerfile for huggingface deployment and api improvements ([7bb33f2](https://github.com/asibul-hasan/aidlypms-backend/commit/7bb33f245c823c2c29629e4d3fe5c46652731c5d))
* enforce settings, role-based restrictions, and dynamic stock thresholds ([631b95e](https://github.com/asibul-hasan/aidlypms-backend/commit/631b95e3e29dcce92ce079c64e6009cf1aa7b798))
* **products:** add Excel product import with dynamic lookup and find-or-create upsert ([a07fde1](https://github.com/asibul-hasan/aidlypms-backend/commit/a07fde1951ffcd547e4a719d56e058f20c1ecf78))
* **pur:** link approved requisitions to purchase orders and fulfill on intake ([168f1d6](https://github.com/asibul-hasan/aidlypms-backend/commit/168f1d674b6e35ab2fb392df7442fd044e3ee8c5))
* **requisition:** add update requisition endpoint for master and items ([3c7372d](https://github.com/asibul-hasan/aidlypms-backend/commit/3c7372db285f5ac8a82c3e20fc4d3d117c63b259))

### 🐛 Bug Fixes

* **auth:** support username login and prevent duplicate pkey error in user seeding ([1e9ace1](https://github.com/asibul-hasan/aidlypms-backend/commit/1e9ace17890d0b0e0ae06c7138f378d05b742509))
* **git-hooks:** ensure POSIX regex compatibility in commit-msg hook ([c3066f0](https://github.com/asibul-hasan/aidlypms-backend/commit/c3066f0d9c08761e385fea0f9f4394367c3d3bee))

### 📝 Documentation

* add GEMINI.md project memory and architecture guide ([6dfb6fb](https://github.com/asibul-hasan/aidlypms-backend/commit/6dfb6fbf9175a7fda7391c8dee1c2e27ccd074c7))
* **hf:** add hugging face space metadata readme with docker sdk config ([95a1d37](https://github.com/asibul-hasan/aidlypms-backend/commit/95a1d373985b180765bfbfe3328db5f349c7decf))
* **memory:** document Conventional Commits enforcement in GEMINI.md ([1e67af9](https://github.com/asibul-hasan/aidlypms-backend/commit/1e67af9bdf8f3abae4d932258c9e7ebc7ab35dd7))
* **memory:** update comprehensive master developer memory and account specs ([ec239d5](https://github.com/asibul-hasan/aidlypms-backend/commit/ec239d5f95006b4ac01e730066b9fd95b4256126))

### 🤖 Continuous Integration

* **deploy:** add pre-flight token validation and target asibul-07 space directly ([f6b492e](https://github.com/asibul-hasan/aidlypms-backend/commit/f6b492e073f2a8b5b955e74a278a116dccaaeb28))
* **git-hooks:** integrate conventional commits with Husky.Net and commit-msg hook ([819a341](https://github.com/asibul-hasan/aidlypms-backend/commit/819a341b7b91b09931bfae981e151a24db832306))
* **hf:** resolve hugging face space owner to asibul-07 and fix deployment remote ([3a9aa5d](https://github.com/asibul-hasan/aidlypms-backend/commit/3a9aa5ded08d3c9f873be61d03b771eaff44d37e))
* **release:** add automated tagging and versioning workflow with commit-and-tag-version ([28f9f43](https://github.com/asibul-hasan/aidlypms-backend/commit/28f9f436d2b8bae93a28720a1f9f49ee428eec13))
