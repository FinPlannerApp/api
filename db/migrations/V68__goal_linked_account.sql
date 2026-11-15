-- A goal can follow a whole account (e.g. a bank account kept only for the emergency fund),
-- not just a savings bucket: progress then tracks that account's live balance.
ALTER TABLE accounts."Goals" ADD COLUMN "LinkedAccountId" integer;
ALTER TABLE accounts."Goals"
    ADD CONSTRAINT "FK_Goals_Accounts_LinkedAccountId" FOREIGN KEY ("LinkedAccountId") REFERENCES accounts."Accounts" ("Id") ON DELETE SET NULL;
CREATE INDEX "IX_Goals_LinkedAccountId" ON accounts."Goals" ("LinkedAccountId");
