-- PH1-03: store how much of each statement bill is paid / still owed instead of summing payments on every read.
-- Same attribution rule as the app: a payment counts toward a bill when it is linked to it, or when it has no bill link
-- and was made on/after the statement date and before the next (newer) statement on the same card.
ALTER TABLE accounts."CreditCardBills" ADD COLUMN IF NOT EXISTS "PaidAmount" numeric(18,2) NULL;
ALTER TABLE accounts."CreditCardBills" ADD COLUMN IF NOT EXISTS "RemainingAmount" numeric(18,2) NULL;

UPDATE accounts."CreditCardBills" b
SET "PaidAmount" = calc.paid,
    "RemainingAmount" = CASE WHEN b."IsPaid" THEN 0 ELSE GREATEST(0, b."BillAmount" - calc.paid) END
FROM (
    SELECT bb."Id" AS id,
           COALESCE((
               SELECT SUM(p."Amount")
               FROM accounts."CreditCardPayments" p
               WHERE p."CreditCardAccountId" = bb."AccountId"
                 AND p."IsDeleted" = false
                 AND (p."CreditCardBillId" = bb."Id"
                      OR (p."CreditCardBillId" IS NULL
                          AND p."Date" >= bb."StatementDate"
                          AND (nxt.d IS NULL OR p."Date" < nxt.d)))
           ), 0) AS paid
    FROM accounts."CreditCardBills" bb
    LEFT JOIN LATERAL (
        SELECT MIN(x."StatementDate") AS d
        FROM accounts."CreditCardBills" x
        WHERE x."AccountId" = bb."AccountId" AND x."IsDeleted" = false AND x."StatementDate" > bb."StatementDate"
    ) nxt ON true
) calc
WHERE calc.id = b."Id";
