-- V67: Release notes for server 6.0.2 (patch: notes only, no blog article).
-- The version must match what the backend pipeline assigns (`node .github/scripts/release-api.mjs dry`).

INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('server', '6.0.2', DATE '2026-11-16',
        'Credit card payments now settle the right statement.',
        NULL)
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

DELETE FROM accounts."ReleaseNoteItems"
WHERE "ReleaseNoteId" = (SELECT "Id" FROM accounts."ReleaseNotes" WHERE "Stream" = 'server' AND "Version" = '6.0.2');

INSERT INTO accounts."ReleaseNoteItems" ("ReleaseNoteId", "SortOrder", "Type", "Text")
SELECT r."Id", v.ord, v.kind, v.txt
FROM accounts."ReleaseNotes" r,
     (VALUES
        (1, 'fixed', 'A credit card payment with no statement chosen now settles the oldest unpaid statement, not the newest'),
        (2, 'fixed', 'Paying or recording an older statement no longer creates a "not itemised" entry based on the current cycle')
     ) AS v(ord, kind, txt)
WHERE r."Stream" = 'server' AND r."Version" = '6.0.2';
