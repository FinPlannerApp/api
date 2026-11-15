ALTER TABLE accounts."InsurancePolicies" ADD COLUMN IF NOT EXISTS "PayFromAccountId" integer NULL;
ALTER TABLE accounts."InsurancePolicies" ADD COLUMN IF NOT EXISTS "AutoRecord" boolean NOT NULL DEFAULT false;
ALTER TABLE accounts."InsurancePolicies" ADD COLUMN IF NOT EXISTS "RecurringTransactionId" integer NULL;
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_InsurancePolicies_Accounts_PayFromAccountId') THEN
        ALTER TABLE accounts."InsurancePolicies" ADD CONSTRAINT "FK_InsurancePolicies_Accounts_PayFromAccountId"
            FOREIGN KEY ("PayFromAccountId") REFERENCES accounts."Accounts"("Id") ON DELETE SET NULL;
    END IF;
END $$;
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_InsurancePolicies_RecurringTransactions_RecurringTransactionId') THEN
        ALTER TABLE accounts."InsurancePolicies" ADD CONSTRAINT "FK_InsurancePolicies_RecurringTransactions_RecurringTransactionId"
            FOREIGN KEY ("RecurringTransactionId") REFERENCES transactions."RecurringTransactions"("Id") ON DELETE SET NULL;
    END IF;
END $$;
