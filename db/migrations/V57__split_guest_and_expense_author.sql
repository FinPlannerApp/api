-- Split roles: view-only guests, and who added each expense (so members can only change their own).
ALTER TABLE split."SplitGroupMembers"
    ADD COLUMN IF NOT EXISTS "IsGuest" boolean NOT NULL DEFAULT false;

ALTER TABLE split."SplitExpenses"
    ADD COLUMN IF NOT EXISTS "AddedByUserId" text NULL;
