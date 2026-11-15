ALTER TABLE transactions."RecurringTransactions" ADD COLUMN IF NOT EXISTS "PausedAt" timestamp with time zone NULL;
