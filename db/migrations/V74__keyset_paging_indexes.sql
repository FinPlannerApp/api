-- Indexes that serve keyset ("seek") paging: each list is read in its default order straight from an index on
-- (owner, sort key, id), and the next page is found by seeking from the last row seen, so deep pages cost the same as the first.
-- Live rows only (the app always filters IsDeleted = false), which also keeps them small.

-- Merchants A-Z by name.
CREATE INDEX IF NOT EXISTS "IX_Merchants_UserId_Name_Id_Live"
    ON transactions."Merchants" ("UserId", "Name", "Id");

-- Decision journal, newest decision first.
CREATE INDEX IF NOT EXISTS "IX_DecisionJournalEntries_UserId_DecisionDate_Id_Live"
    ON transactions."DecisionJournalEntries" ("UserId", "DecisionDate" DESC, "Id" DESC) WHERE "IsDeleted" = false;

-- All-accounts transaction list (the account list uses IX_Transactions_AccountId_Date_Id_Live).
CREATE INDEX IF NOT EXISTS "IX_Transactions_UserId_Date_Id_Live"
    ON transactions."Transactions" ("UserId", "Date" DESC, "Id" DESC) WHERE "IsDeleted" = false;

-- Public blog list, newest published first.
CREATE INDEX IF NOT EXISTS "IX_BlogPosts_Published_PublishedAt_Id_Live"
    ON accounts."BlogPosts" (COALESCE("PublishedAt", "CreatedAt") DESC, "Id" DESC) WHERE "IsPublished" = true AND "IsDeleted" = false;

-- Blog comments per post, top-level first by newest.
CREATE INDEX IF NOT EXISTS "IX_BlogPostComments_Post_Parent_CreatedAt_Id"
    ON accounts."BlogPostComments" ("BlogPostId", "ParentCommentId", "CreatedAt" DESC, "Id" DESC);

-- Feedback Hub comments per issue, oldest first, top-level then replies.
CREATE INDEX IF NOT EXISTS "IX_IssueComments_Issue_Parent_CreatedAt_Id"
    ON "IssueComments" ("IssueId", "ParentCommentId", "CreatedAt", "Id");

-- Feedback Hub issue list in each of its sort orders (the default is pain score).
CREATE INDEX IF NOT EXISTS "IX_Issues_PainScore_Id_Live" ON "Issues" ("PainScore" DESC, "Id" DESC) WHERE "IsDeleted" = false;
CREATE INDEX IF NOT EXISTS "IX_Issues_CreatedAt_Id_Live" ON "Issues" ("CreatedAt" DESC, "Id" DESC) WHERE "IsDeleted" = false;
CREATE INDEX IF NOT EXISTS "IX_Issues_Votes_Id_Live" ON "Issues" ("Votes" DESC, "Id" DESC) WHERE "IsDeleted" = false;
CREATE INDEX IF NOT EXISTS "IX_Issues_PainVelocity_Id_Live" ON "Issues" ("PainVelocity" DESC, "Id" DESC) WHERE "IsDeleted" = false;

-- Split group history.
CREATE INDEX IF NOT EXISTS "IX_SplitSettlements_Group_When_Id"
    ON split."SplitSettlements" ("SplitGroupId", (COALESCE("CompletedAt", "CreatedAt")) DESC, "Id" DESC);
CREATE INDEX IF NOT EXISTS "IX_SplitExpenses_Group_Deleted_When_Id"
    ON split."SplitExpenses" ("SplitGroupId", (COALESCE("DeletedAt", "CreatedAt")) DESC, "Id" DESC) WHERE "IsDeleted" = true;

-- Accounts A-Z (case-insensitive) with the id tie-break, for the default account list.
CREATE INDEX IF NOT EXISTS "IX_Accounts_UserId_NameLower_Id_Live"
    ON accounts."Accounts" ("UserId", lower("Name"), "Id" DESC) WHERE "IsDeleted" = false;
