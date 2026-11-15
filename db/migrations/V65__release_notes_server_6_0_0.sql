-- V65: Release notes for server 6.0.0 (major release). Notes only: the user-facing write-up is the
-- app 7.0.0 article (V64), so this row has no blog article of its own.
-- The version must match what the backend pipeline assigns (`node .github/scripts/release-api.mjs dry`).

INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('server', '6.0.0', DATE '2026-11-15',
        'The server behind budgets, reports, undo and the accuracy fixes in app 7.0.',
        NULL)
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

DELETE FROM accounts."ReleaseNoteItems"
WHERE "ReleaseNoteId" = (SELECT "Id" FROM accounts."ReleaseNotes" WHERE "Stream" = 'server' AND "Version" = '6.0.0');

INSERT INTO accounts."ReleaseNoteItems" ("ReleaseNoteId", "SortOrder", "Type", "Text")
SELECT r."Id", v.ord, v.kind, v.txt
FROM accounts."ReleaseNotes" r,
     (VALUES
        (1, 'new',      'Budget alerts you can tune: a level per budget, and a log of the alerts you have had'),
        (2, 'new',      'Reports can be downloaded as Excel or PDF'),
        (3, 'new',      'Deleted transactions can be restored for ten minutes'),
        (4, 'improved', 'The dashboard, budgets, reports and financial health follow the day your money month starts'),
        (5, 'improved', 'Email alerts are off until you switch them on'),
        (6, 'fixed',    'An investment sold at a loss is no longer counted as spending'),
        (7, 'fixed',    'Deleting a loan payment removes its interest entry too'),
        (8, 'fixed',    'All unpaid card statements count toward what you owe'),
        (9, 'fixed',    'Importing or settling a split group twice at the same moment can no longer double up')
     ) AS v(ord, kind, txt)
WHERE r."Stream" = 'server' AND r."Version" = '6.0.0';
