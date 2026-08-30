CREATE TABLE IF NOT EXISTS accounts."CreditCardRewardPoints" (
    "Id" SERIAL PRIMARY KEY,
    "UserId" text NOT NULL,
    "CreditCardAccountId" integer NOT NULL,
    "CreditCardBillId" integer NOT NULL,
    "PointsEarned" numeric NOT NULL,
    "PointsRedeemed" numeric NOT NULL DEFAULT 0,
    "PointsExpired" numeric NOT NULL DEFAULT 0,
    "ExpiryDate" timestamp with time zone NULL,
    "RedemptionNote" text NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    "DeletedAt" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_CreditCardRewardPoints_Accounts_CreditCardAccountId'
    ) THEN
        ALTER TABLE accounts."CreditCardRewardPoints"
        ADD CONSTRAINT "FK_CreditCardRewardPoints_Accounts_CreditCardAccountId"
        FOREIGN KEY ("CreditCardAccountId") REFERENCES accounts."Accounts" ("Id") ON DELETE CASCADE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_CreditCardRewardPoints_CreditCardBills_CreditCardBillId'
    ) THEN
        ALTER TABLE accounts."CreditCardRewardPoints"
        ADD CONSTRAINT "FK_CreditCardRewardPoints_CreditCardBills_CreditCardBillId"
        FOREIGN KEY ("CreditCardBillId") REFERENCES accounts."CreditCardBills" ("Id") ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CreditCardRewardPoints_CreditCardAccountId"
ON accounts."CreditCardRewardPoints" ("CreditCardAccountId");

CREATE INDEX IF NOT EXISTS "IX_CreditCardRewardPoints_CreditCardBillId"
ON accounts."CreditCardRewardPoints" ("CreditCardBillId");

CREATE INDEX IF NOT EXISTS "IX_CreditCardRewardPoints_ExpiryDate"
ON accounts."CreditCardRewardPoints" ("ExpiryDate");


CREATE TABLE IF NOT EXISTS accounts."PaymentAppWalletLedgerEntries" (
    "Id" SERIAL PRIMARY KEY,
    "UserId" text NOT NULL,
    "PaymentAppName" text NOT NULL,
    "Amount" numeric NOT NULL,
    "Type" integer NOT NULL,
    "CreditCardPaymentId" integer NOT NULL,
    "Date" timestamp with time zone NOT NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    "DeletedAt" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_PaymentAppWalletLedgerEntries_CreditCardPayments_CreditCardPaymentId'
    ) THEN
        ALTER TABLE accounts."PaymentAppWalletLedgerEntries"
        ADD CONSTRAINT "FK_PaymentAppWalletLedgerEntries_CreditCardPayments_CreditCardPaymentId"
        FOREIGN KEY ("CreditCardPaymentId") REFERENCES accounts."CreditCardPayments" ("Id") ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_PaymentAppWalletLedgerEntries_UserId_PaymentAppName"
ON accounts."PaymentAppWalletLedgerEntries" ("UserId", "PaymentAppName");

CREATE INDEX IF NOT EXISTS "IX_PaymentAppWalletLedgerEntries_CreditCardPaymentId"
ON accounts."PaymentAppWalletLedgerEntries" ("CreditCardPaymentId");

