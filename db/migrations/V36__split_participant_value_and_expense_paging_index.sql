ALTER TABLE split."SplitExpenseParticipants"
ADD COLUMN IF NOT EXISTS "SplitValue" numeric NULL;

CREATE INDEX IF NOT EXISTS "IX_SplitExpenses_Group_Date_Id"
ON split."SplitExpenses" ("SplitGroupId", "Date" DESC, "Id" DESC)
WHERE "IsDeleted" = false;