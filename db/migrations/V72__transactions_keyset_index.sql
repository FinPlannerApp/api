-- Keyset paging for transactions: the default order (newest first, ties by id) is read straight off this index, and the
-- next page is found by seeking from the last row seen instead of skipping rows with OFFSET.
-- Only live rows are in the index (the app always filters IsDeleted = false), which also keeps it small.
CREATE INDEX IF NOT EXISTS "IX_Transactions_AccountId_Date_Id_Live"
    ON transactions."Transactions" ("AccountId", "Date" DESC, "Id" DESC)
    WHERE "IsDeleted" = false;
