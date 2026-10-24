-- Direct one-to-one friend ledgers flag for SplitGroups
ALTER TABLE split."SplitGroups"
ADD COLUMN IF NOT EXISTS "IsDirect" boolean NOT NULL DEFAULT false;
