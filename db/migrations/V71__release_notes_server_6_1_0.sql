-- V71: Release notes for server 6.1.0 (minor release). Notes only: the user-facing write-up is the
-- app 7.2.0 article (V70), so this row has no blog article of its own.
-- The version must match what the backend pipeline assigns (`node .github/scripts/release-api.mjs dry`).

INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('server', '6.1.0', DATE '2026-11-16',
        'The server behind bug reports, goals that follow an account, bucket top-ups and overdue-bill suggestions, plus paging for long lists.',
        NULL)
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

DELETE FROM accounts."ReleaseNoteItems"
WHERE "ReleaseNoteId" = (SELECT "Id" FROM accounts."ReleaseNotes" WHERE "Stream" = 'server' AND "Version" = '6.1.0');

INSERT INTO accounts."ReleaseNoteItems" ("ReleaseNoteId", "SortOrder", "Type", "Text")
SELECT r."Id", v.ord, v.kind, v.txt
FROM accounts."ReleaseNotes" r,
     (VALUES
        (1, 'new',      'Bug reports from the app arrive in the Feedback Hub with a screenshot and technical details'),
        (2, 'new',      'Goals can follow the balance of a bank or cash account'),
        (3, 'new',      'Add money to a savings bucket without re-entering the bucket'),
        (4, 'new',      'Overdue bills that look already paid are suggested for confirmation, and a card statement can be marked paid'),
        (5, 'improved', 'Long lists are sent a page at a time and never more than 1000 items in one response'),
        (6, 'improved', 'Merchants can be searched and paged on the server')
     ) AS v(ord, kind, txt)
WHERE r."Stream" = 'server' AND r."Version" = '6.1.0';
