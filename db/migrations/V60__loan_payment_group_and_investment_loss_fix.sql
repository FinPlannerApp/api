-- BUG-05: link the rows of one loan payment so deleting a payment removes exactly its interest row.
ALTER TABLE transactions."Transactions" ADD COLUMN IF NOT EXISTS "PaymentGroupId" uuid NULL;
CREATE INDEX IF NOT EXISTS "IX_Transactions_PaymentGroupId" ON transactions."Transactions" ("PaymentGroupId") WHERE "PaymentGroupId" IS NOT NULL;

-- BUG-01: a realised LOSS on an investment withdrawal is a change in value, not spending.
-- Give existing loss rows a group id so every spending total skips them (new losses get one in code).
-- Kind 7 = InvestmentGain, Type 1 = Expense.
UPDATE transactions."Transactions"
   SET "TransferGroupId" = gen_random_uuid()
 WHERE "Kind" = 7 AND "Type" = 1 AND "TransferGroupId" IS NULL AND "IsDeleted" = false;
