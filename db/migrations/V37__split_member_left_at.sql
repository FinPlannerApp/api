-- A member who leaves (or is removed from) a group keeps their row so past
-- expenses still show their name; LeftAt marks them as no longer active.
ALTER TABLE split."SplitGroupMembers"
ADD COLUMN IF NOT EXISTS "LeftAt" timestamp with time zone NULL;
