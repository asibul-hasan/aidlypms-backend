-- ============================================================================
-- Migration: 20261022_06_additional_sequences.sql
-- Description:
--   Creates Postgres sequences for monotonic, concurrency-safe IDs across all
--   remaining operational tables:
--   - seq_prod_batch_no (prod_product_batches)
--   - seq_sys_audit_no (sys_audit_logs)
--   - seq_cash_closer_no (cash_closers)
--   - seq_shift_no (cash_closers)
--   - seq_due_collection_no (due_collections)
--   - seq_supplier_payment_no (pur_supplier_payments)
--   - seq_purchase_return_no (pur_purchase_returns)
--   - seq_purchase_return_item_no (pur_purchase_return_items)
--   - seq_customer_no (cust_customers)
--   - seq_product_no (prod_products)
--   - seq_expense_no (acc_expenses)
--   - seq_income_no (acc_incomes)
--   - seq_account_no (acc_transaction_accounts)
--   - seq_adjustment_no (inv_adjustments)
--   - seq_adjustment_item_no (inv_adjustment_items)
--   - seq_transfer_no (inv_transfers)
--   - seq_transfer_item_no (inv_transfer_items)
-- ============================================================================

CREATE SEQUENCE IF NOT EXISTS seq_prod_batch_no;
SELECT setval('seq_prod_batch_no', GREATEST(COALESCE((SELECT MAX(batch_no) FROM prod_product_batches), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_sys_audit_no;
SELECT setval('seq_sys_audit_no', GREATEST(COALESCE((SELECT MAX(audit_no) FROM sys_audit_logs), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_cash_closer_no;
SELECT setval('seq_cash_closer_no', GREATEST(COALESCE((SELECT MAX(cash_closer_no) FROM cash_closers), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_shift_no;
SELECT setval('seq_shift_no', GREATEST(COALESCE((SELECT MAX(shift_no) FROM cash_closers), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_due_collection_no;
SELECT setval('seq_due_collection_no', GREATEST(COALESCE((SELECT MAX(due_collection_no) FROM due_collections), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_supplier_payment_no;
SELECT setval('seq_supplier_payment_no', GREATEST(COALESCE((SELECT MAX(supplier_payment_no) FROM pur_supplier_payments), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_purchase_return_no;
SELECT setval('seq_purchase_return_no', GREATEST(COALESCE((SELECT MAX(purchase_return_no) FROM pur_purchase_returns), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_purchase_return_item_no;
SELECT setval('seq_purchase_return_item_no', GREATEST(COALESCE((SELECT MAX(purchase_return_item_no) FROM pur_purchase_return_items), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_customer_no;
SELECT setval('seq_customer_no', GREATEST(COALESCE((SELECT MAX(customer_no) FROM cust_customers), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_product_no;
SELECT setval('seq_product_no', GREATEST(COALESCE((SELECT MAX(product_no) FROM prod_products), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_expense_no;
SELECT setval('seq_expense_no', GREATEST(COALESCE((SELECT MAX(expense_no) FROM acc_expenses), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_income_no;
SELECT setval('seq_income_no', GREATEST(COALESCE((SELECT MAX(income_no) FROM acc_incomes), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_account_no;
SELECT setval('seq_account_no', GREATEST(COALESCE((SELECT MAX(account_no) FROM acc_transaction_accounts), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_adjustment_no;
SELECT setval('seq_adjustment_no', GREATEST(COALESCE((SELECT MAX(adjustment_no) FROM inv_adjustments), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_adjustment_item_no;
SELECT setval('seq_adjustment_item_no', GREATEST(COALESCE((SELECT MAX(adjustment_item_no) FROM inv_adjustment_items), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_transfer_no;
SELECT setval('seq_transfer_no', GREATEST(COALESCE((SELECT MAX(transfer_no) FROM inv_transfers), 0) + 1, 1), false);

CREATE SEQUENCE IF NOT EXISTS seq_transfer_item_no;
SELECT setval('seq_transfer_item_no', GREATEST(COALESCE((SELECT MAX(transfer_item_no) FROM inv_transfer_items), 0) + 1, 1), false);
