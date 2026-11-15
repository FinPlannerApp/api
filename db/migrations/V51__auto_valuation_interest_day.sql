-- Automatic investment valuation (units × price) and bank-interest credit date / auto-post.
ALTER TABLE accounts."InvestmentDetails" ADD COLUMN IF NOT EXISTS "Units" numeric(18,4) NULL;
ALTER TABLE accounts."InvestmentDetails" ADD COLUMN IF NOT EXISTS "AutoValuation" boolean NOT NULL DEFAULT false;
ALTER TABLE accounts."InvestmentDetails" ADD COLUMN IF NOT EXISTS "LastPrice" numeric(18,4) NULL;
ALTER TABLE accounts."InvestmentDetails" ADD COLUMN IF NOT EXISTS "LastPriceDate" timestamp with time zone NULL;
ALTER TABLE accounts."InvestmentDetails" ADD COLUMN IF NOT EXISTS "LastPriceError" text NULL;

ALTER TABLE accounts."BankAccountDetails" ADD COLUMN IF NOT EXISTS "InterestCreditDay" integer NULL;
ALTER TABLE accounts."BankAccountDetails" ADD COLUMN IF NOT EXISTS "AutoPostInterest" boolean NOT NULL DEFAULT false;
