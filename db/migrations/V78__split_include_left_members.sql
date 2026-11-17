-- Admin toggle: keep people who left the group visible in balances so later adjustments can be settled.
ALTER TABLE split."SplitGroups"
ADD COLUMN IF NOT EXISTS "IncludeLeftMembersInBalances" boolean NOT NULL DEFAULT false;

-- SEC-31: the public share link is now opt-in and expires. Existing links are switched off until an admin re-enables them.
ALTER TABLE split."SplitGroups"
ADD COLUMN IF NOT EXISTS "PublicLinkEnabled" boolean NOT NULL DEFAULT false,
ADD COLUMN IF NOT EXISTS "PublicLinkExpiresAt" timestamp with time zone NULL;
