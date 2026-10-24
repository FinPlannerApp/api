-- Settings for SplitGroups: simplify debts toggle and trip start/end dates
ALTER TABLE split."SplitGroups"
ADD COLUMN IF NOT EXISTS "SimplifyDebts" boolean NOT NULL DEFAULT true,
ADD COLUMN IF NOT EXISTS "StartDate" timestamp with time zone NULL,
ADD COLUMN IF NOT EXISTS "EndDate" timestamp with time zone NULL;
