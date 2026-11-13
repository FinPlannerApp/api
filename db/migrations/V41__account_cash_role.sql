-- How an account counts toward spendable cash. 0=Auto (Bank/Cash type = spendable),
-- 1=Spendable (e.g. a cash wallet), 2=EmergencyFund (not spendable, counts for emergency
-- coverage), 3=HeldForOthers (custodial money: not spendable, not in net worth).
ALTER TABLE accounts."Accounts" ADD COLUMN IF NOT EXISTS "CashRole" integer NOT NULL DEFAULT 0;

-- Interest/fees the user states are included in a recorded card bill. Anything else in the
-- gap between the bill and tracked purchases is untracked card spend, not interest.
ALTER TABLE accounts."CreditCardBills" ADD COLUMN IF NOT EXISTS "InterestAndFees" numeric(18,2) NOT NULL DEFAULT 0;
ALTER TABLE accounts."CreditCardBills" ADD COLUMN IF NOT EXISTS "OtherCharges" numeric(18,2) NOT NULL DEFAULT 0;
