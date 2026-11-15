-- "My reports": a person's own issues, newest first, paged by keyset (CreatedAt DESC, Id DESC) from this index.
CREATE INDEX IF NOT EXISTS "IX_Issues_CreatorUserId_CreatedAt_Id_Live"
    ON "Issues" ("CreatorUserId", "CreatedAt" DESC, "Id" DESC)
    WHERE "IsDeleted" = false;

-- Duplicate detection for quick bug reports: reports with the same page, first error and first failed call share a fingerprint,
-- so "N other people reported this" and grouping for the team need no text matching.
ALTER TABLE "IssueDiagnostics" ADD COLUMN "Fingerprint" text;
CREATE INDEX IF NOT EXISTS "IX_IssueDiagnostics_Fingerprint" ON "IssueDiagnostics" ("Fingerprint") WHERE "Fingerprint" IS NOT NULL AND "IsDeleted" = false;
