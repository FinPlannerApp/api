-- V80: Release notes for server 6.3.0 (minor release). Notes only: the user-facing write-up is the
-- app 7.4.0 article (V79), so this row has no blog article of its own.
-- Follows docs/RELEASE_NOTES.md. The version matches what `node .github/scripts/release-api.mjs dry` assigns.

INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('server', '6.3.0', DATE '2026-11-17',
        'Stored credit card statement balances, expiring Split public links, parameterized keyset cursors, and deferred cache invalidation.',
        NULL)
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

DELETE FROM accounts."ReleaseNoteItems"
WHERE "ReleaseNoteId" = (SELECT "Id" FROM accounts."ReleaseNotes" WHERE "Stream" = 'server' AND "Version" = '6.3.0');

INSERT INTO accounts."ReleaseNoteItems" ("ReleaseNoteId", "SortOrder", "Type", "Text")
SELECT r."Id", v.ord, v.kind, v.txt
FROM accounts."ReleaseNotes" r,
     (VALUES
        (1, 'new',      'Credit card bills store paid and remaining amounts directly, avoiding dynamic payment aggregation on every read'),
        (2, 'new',      'Split groups support expiring public share links and balances for departed members'),
        (3, 'improved', 'Keyset pagination cursors are parameterized so EF Core reuses compiled SQL execution plans'),
        (4, 'improved', 'Dashboard cache invalidation waits for the surrounding database transaction to commit before flushing'),
        (5, 'improved', 'Audit logs and entity changes commit in a single atomic transaction'),
        (6, 'improved', 'Issue voting and infra monitor email alerts execute with atomic race guards'),
        (7, 'improved', 'Mobile app refresh token grace window extended to avoid accidental logouts on app swipe')
     ) AS v(ord, kind, txt)
WHERE r."Stream" = 'server' AND r."Version" = '6.3.0';

