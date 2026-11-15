-- TXN-05: a free-text note and tags on every transaction.
-- Tags are stored lower-case and pipe-delimited with both ends delimited ("|trip|work|") so one tag can be matched exactly.
ALTER TABLE transactions."Transactions" ADD COLUMN IF NOT EXISTS "Notes" text NULL;
ALTER TABLE transactions."Transactions" ADD COLUMN IF NOT EXISTS "Tags" text NULL;
CREATE INDEX IF NOT EXISTS "IX_Transactions_UserId_Tags" ON transactions."Transactions" ("UserId") WHERE "Tags" IS NOT NULL;
