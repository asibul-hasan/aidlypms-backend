# Aidly PMS Backend (.NET 10 Web API)

Backend service for Aidly Pharmacy Management System (PMS), built with C# 13, .NET 10, Dapper, and PostgreSQL.

---

## STRICT ARCHITECTURAL RULE: Frontend-Driven Lookups & Zero DB Defaults / Constraints

1. **Frontend-Driven Lookup Source of Truth (`label-values.constant.ts`)**:
   - ALL static lookup choices, classifications, categories, and dropdown options (e.g., `drug_schedule`, `payment_method`, `medicine_type`, `unit_type`, `paper_type`, `billing_cycle`, etc.) MUST be defined exclusively in the frontend within `apps/aidlyPms/src/app/shared/constants/label-values.constant.ts`.
   - Every lookup list must follow the standard `{ label: string, value: string | number }` (`LabelOption`) pattern.
   - Frontend forms and components bind directly to these shared constants and submit the plain string or integer key value.

2. **Database Storage: Raw Key Values Only**:
   - The PostgreSQL database must ONLY store the raw key `value` (e.g. `VARCHAR(50)` for strings, or integer IDs).
   - **NEVER** add `DEFAULT '...'` values in PostgreSQL column definitions or migration scripts (e.g., NO `drug_schedule VARCHAR(20) DEFAULT 'OTC'`). Defaults must be handled on the frontend form level.
   - **NEVER** add database-level `CHECK (...)` constraints for lookup values in PostgreSQL DDL or migrations (e.g., NO `CHECK (drug_schedule IN ('OTC', 'POM', ...))`).
   - Hardcoded DB defaults and DB check constraints make schema evolutions brittle and break backward compatibility. Validation belongs in frontend forms and API input DTOs.

3. **Bangladesh Retail Pharmacy Practical Alignment**:
   - Do NOT implement theoretical or foreign features that are not practiced in the Bangladesh retail pharmacy market (e.g., US DEA 222 signature gates, complex clinical hard-stop DUR popups that obstruct cashier speed).
   - Only implement practical Bangladesh pharmacy workflows: DGDA DAR registration, BMDC doctor credentials, Schedule-G Antibiotic & Schedule-II Narcotic flags, FEFO/batch expiry tracking, multi-tender split payments (Cash + bKash/Nagad/Card), and customer due tracking.
