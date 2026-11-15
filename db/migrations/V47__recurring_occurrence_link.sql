-- Link a posted transaction to the recurring occurrence that produced it and make a duplicate impossible.
ALTER TABLE transactions."Transactions" ADD COLUMN IF NOT EXISTS "RecurringTransactionId" integer NULL;
ALTER TABLE transactions."Transactions" ADD COLUMN IF NOT EXISTS "RecurringOccurrenceDate" timestamp with time zone NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_Transactions_RecurringOccurrence"
    ON transactions."Transactions" ("RecurringTransactionId", "RecurringOccurrenceDate")
    WHERE "RecurringTransactionId" IS NOT NULL AND "IsDeleted" = false;
