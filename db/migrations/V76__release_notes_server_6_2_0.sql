-- V76: Release notes for server 6.2.0 (minor release). Notes only: the user-facing write-up is the
-- app 7.3.0 article (V75), so this row has no blog article of its own.
-- The version must match what the backend pipeline assigns (`node .github/scripts/release-api.mjs dry`).

INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('server', '6.2.0', DATE '2026-11-16',
        'Keyset paging with indexes for every growing list, a My reports list, report fingerprints, and paged comments.',
        NULL)
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

DELETE FROM accounts."ReleaseNoteItems"
WHERE "ReleaseNoteId" = (SELECT "Id" FROM accounts."ReleaseNotes" WHERE "Stream" = 'server' AND "Version" = '6.2.0');

INSERT INTO accounts."ReleaseNoteItems" ("ReleaseNoteId", "SortOrder", "Type", "Text")
SELECT r."Id", v.ord, v.kind, v.txt
FROM accounts."ReleaseNotes" r,
     (VALUES
        (1, 'new',      'My reports: a person''s own reports with their status, paged from an index'),
        (2, 'new',      'Reports of the same problem are recognised, so people are told when others reported it too'),
        (3, 'improved', 'Transactions, merchants, categories, accounts, the decision journal, budgets, the blog, Feedback Hub issues and comments, and Split history continue from the last item seen, with indexes, instead of skipping rows'),
        (4, 'improved', 'On 400,000 transactions a deep page went from about 200-350 ms to under 1 ms'),
        (5, 'improved', 'Feedback Hub and blog comments are loaded a page of top-level comments at a time, with their replies'),
        (6, 'fixed',    'A report with no screenshot or details no longer answers "not found"')
     ) AS v(ord, kind, txt)
WHERE r."Stream" = 'server' AND r."Version" = '6.2.0';
