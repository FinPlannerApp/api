ALTER TABLE accounts."CreditCardDetails"
ADD COLUMN IF NOT EXISTS "NextAnnualFeeDate" timestamp with time zone NULL;

ALTER TABLE accounts."BankAccountDetails"
ADD COLUMN IF NOT EXISTS "PeriodicChargeAmount" numeric NULL;

ALTER TABLE accounts."BankAccountDetails"
ADD COLUMN IF NOT EXISTS "PeriodicChargeFrequency" integer NULL;

ALTER TABLE accounts."BankAccountDetails"
ADD COLUMN IF NOT EXISTS "NextPeriodicChargeDate" timestamp with time zone NULL;
