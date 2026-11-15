-- Every email alert is opt-in. The legacy per-user flag is no longer read, but is switched off so it can never
-- be mistaken for (or accidentally revive) an "on by default" alert.
ALTER TABLE identity."Users" ALTER COLUMN "OverspendAlertsEnabled" SET DEFAULT false;
UPDATE identity."Users" SET "OverspendAlertsEnabled" = false WHERE "OverspendAlertsEnabled" = true;
