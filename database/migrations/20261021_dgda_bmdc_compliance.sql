-- Migration: 20261021_dgda_bmdc_compliance.sql
-- Description: Add DGDA DAR Number, Drug Schedule Classification, and Doctor BMDC Registration Number.
-- NOTE: In accordance with Aidly architectural rules, options are maintained frontend-side (label-value style).
-- No DB CHECK constraints or DB defaults are applied.

-- 1. DGDA Compliance on Medicines / Products
ALTER TABLE prod_products 
ADD COLUMN IF NOT EXISTS dar_number VARCHAR(50),
ADD COLUMN IF NOT EXISTS drug_schedule VARCHAR(50);

CREATE INDEX IF NOT EXISTS idx_prod_products_dar_number ON prod_products(dar_number);
CREATE INDEX IF NOT EXISTS idx_prod_products_drug_schedule ON prod_products(drug_schedule);

-- 2. Doctor BMDC Registration Number
ALTER TABLE doc_doctors 
ADD COLUMN IF NOT EXISTS bmdc_reg_no VARCHAR(50);

CREATE INDEX IF NOT EXISTS idx_doc_doctors_bmdc_reg_no ON doc_doctors(bmdc_reg_no);
