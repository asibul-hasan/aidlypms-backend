# Changelog

All notable changes to this project will be documented in this file. See [commit-and-tag-version](https://github.com/absolute-version/commit-and-tag-version) for commit guidelines.

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
