DROP INDEX IF EXISTS accounts."IX_CreditCardBills_AccountId_StatementDate";

CREATE UNIQUE INDEX "IX_CreditCardBills_AccountId_StatementDate" 
ON accounts."CreditCardBills" ("AccountId", "StatementDate");
