-- One recorded interest credit per account per period-end date (Kind 6 = InterestCredit). Makes double posting impossible.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Transactions_InterestCredit_Day"
    ON transactions."Transactions" ("AccountId", "Date")
    WHERE "Kind" = 6 AND "IsDeleted" = false;
