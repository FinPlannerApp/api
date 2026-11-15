-- Statement import: remember which entries came from / were matched to a bank statement row.
ALTER TABLE transactions."Transactions" ADD COLUMN IF NOT EXISTS "ImportRef" varchar(64) NULL;
ALTER TABLE transactions."Transactions" ADD COLUMN IF NOT EXISTS "ReconciledAt" timestamp with time zone NULL;

-- The same bank reference can never be imported twice into one account.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Transactions_Account_ImportRef"
    ON transactions."Transactions" ("AccountId", "ImportRef")
    WHERE "ImportRef" IS NOT NULL AND "IsDeleted" = false;
