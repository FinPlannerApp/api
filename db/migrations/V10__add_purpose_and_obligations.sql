ALTER TABLE accounts."Accounts" ADD COLUMN "Purpose" text;
ALTER TABLE transactions."RecurringTransactions" ADD COLUMN "IsObligation" boolean NOT NULL DEFAULT false;
