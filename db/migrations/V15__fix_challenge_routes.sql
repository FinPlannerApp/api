-- Prefix fix: every existing route is missing "/app" — every single
-- "Go do this" button currently 404s.
UPDATE "challenges"."ChallengeDays"
SET "ActionRoute" = '/app' || "ActionRoute"
WHERE "ActionRoute" IS NOT NULL;

-- Day 9: "/categories" doesn't exist as a route at all — the real pages
-- are account-categories and transaction-categories, two separate
-- pages. Day 9 is about sorting SPENDING, so transaction-categories is
-- the correct fit.
UPDATE "challenges"."ChallengeDays"
SET "ActionRoute" = '/app/transaction-categories'
WHERE "DayNumber" = 9;

-- Day 13: "Pick Your Money Days" (fixed days for bills/investing) never
-- had a route at all — recurring-transactions is directly what this is
-- describing.
UPDATE "challenges"."ChallengeDays"
SET "ActionRoute" = '/app/recurring-transactions'
WHERE "DayNumber" = 13;

-- Day 15: "List Every Debt" never had a route — this is exactly what
-- adding loan/credit-card accounts is for.
UPDATE "challenges"."ChallengeDays"
SET "ActionRoute" = '/app/accounts'
WHERE "DayNumber" = 15;

-- Day 22: "Secure Your Emergency Fund" never had a route — Goals didn't
-- exist when this was originally written. It's now the correct home for
-- this, complete with automatic progress tracking if linked to a bucket.
UPDATE "challenges"."ChallengeDays"
SET "ActionRoute" = '/app/goals'
WHERE "DayNumber" = 22;
