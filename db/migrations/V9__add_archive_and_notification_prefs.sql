ALTER TABLE accounts."Accounts" ADD COLUMN "IsArchived" boolean NOT NULL DEFAULT false;
ALTER TABLE identity."Users" ADD COLUMN "OverspendAlertsEnabled" boolean NOT NULL DEFAULT true;
