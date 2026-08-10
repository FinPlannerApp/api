ALTER TABLE transactions."RecurringTransactions"
ADD COLUMN "LinkedLoanAccountId" integer NULL;

ALTER TABLE transactions."RecurringTransactions"
ADD CONSTRAINT "FK_RecurringTransactions_Accounts_LinkedLoanAccountId"
FOREIGN KEY ("LinkedLoanAccountId")
REFERENCES accounts."Accounts" ("Id")
ON DELETE SET NULL;
