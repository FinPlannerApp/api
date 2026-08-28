ALTER TABLE accounts."CreditCardPayments"
ADD COLUMN IF NOT EXISTS "CreditCardBillId" integer NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_CreditCardPayments_CreditCardBills_CreditCardBillId'
    ) THEN
        ALTER TABLE accounts."CreditCardPayments"
        ADD CONSTRAINT "FK_CreditCardPayments_CreditCardBills_CreditCardBillId"
        FOREIGN KEY ("CreditCardBillId") REFERENCES accounts."CreditCardBills" ("Id") ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CreditCardPayments_CreditCardBillId" 
ON accounts."CreditCardPayments" ("CreditCardBillId");

