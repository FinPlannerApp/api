-- V46: uniqueness that ignores soft-deleted rows, indexes for per-user queries, vote value guards.
-- Safe on a populated database: partial unique indexes cover a SUBSET of rows that were already unique,
-- every statement is idempotent, and nothing is dropped without being re-created in the same file.

-- 1. A deleted "Food" category / merchant / subscription / bill must not block creating a new one.
DROP INDEX IF EXISTS accounts."IX_AccountCategories_UserId_Name";
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AccountCategories_UserId_Name"
    ON accounts."AccountCategories" ("UserId", "Name") WHERE "IsDeleted" = false;

DROP INDEX IF EXISTS transactions."IX_TransactionCategories_UserId_Name";
CREATE UNIQUE INDEX IF NOT EXISTS "IX_TransactionCategories_UserId_Name"
    ON transactions."TransactionCategories" ("UserId", "Name") WHERE "IsDeleted" = false;

DROP INDEX IF EXISTS transactions."IX_Merchants_UserId_Name";
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Merchants_UserId_Name"
    ON transactions."Merchants" ("UserId", "Name") WHERE "IsDeleted" = false;

DROP INDEX IF EXISTS transactions."IX_Subscriptions_UserId_Name";
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Subscriptions_UserId_Name"
    ON transactions."Subscriptions" ("UserId", "Name") WHERE "IsDeleted" = false;

DROP INDEX IF EXISTS accounts."IX_CreditCardBills_AccountId_StatementDate";
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CreditCardBills_AccountId_StatementDate"
    ON accounts."CreditCardBills" ("AccountId", "StatementDate") WHERE "IsDeleted" = false;

-- 2. Per-user query paths.
CREATE INDEX IF NOT EXISTS "IX_Accounts_UserId" ON accounts."Accounts" ("UserId") WHERE "IsDeleted" = false;
CREATE INDEX IF NOT EXISTS "IX_Transactions_UserId_Date" ON transactions."Transactions" ("UserId", "Date" DESC) WHERE "IsDeleted" = false;
CREATE INDEX IF NOT EXISTS "IX_CreditCardBills_UserId" ON accounts."CreditCardBills" ("UserId");

-- 3. A vote is -1, 0 or +1. NOT VALID: enforced for new/changed rows without failing on any old bad row.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'IssueVotes' AND column_name = 'Value')
       AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_IssueVotes_Value') THEN
        ALTER TABLE "IssueVotes" ADD CONSTRAINT "CK_IssueVotes_Value" CHECK ("Value" IN (-1, 0, 1)) NOT VALID;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'CommentVotes' AND column_name = 'Value')
       AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_CommentVotes_Value') THEN
        ALTER TABLE "CommentVotes" ADD CONSTRAINT "CK_CommentVotes_Value" CHECK ("Value" IN (-1, 0, 1)) NOT VALID;
    END IF;
END $$;
