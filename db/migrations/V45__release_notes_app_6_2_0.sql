-- V45: Release notes for app 6.2.0 (minor release: has a blog article).
-- Same pattern as V44. The version must match what the UI release pipeline assigns
-- (run `npm run release:dry` in the ui repo to see it before pushing).

INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('app', '6.2.0', DATE '2026-11-14',
        'A "What''s new" window after each update, and release notes on the blog.',
        'finplanner-app-6-2-release-whats-new')
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

DELETE FROM accounts."ReleaseNoteItems"
WHERE "ReleaseNoteId" = (SELECT "Id" FROM accounts."ReleaseNotes" WHERE "Stream" = 'app' AND "Version" = '6.2.0');

INSERT INTO accounts."ReleaseNoteItems" ("ReleaseNoteId", "SortOrder", "Type", "Text")
SELECT r."Id", v.ord, v.kind, v.txt
FROM accounts."ReleaseNotes" r,
     (VALUES
        (1, 'new',      'A "What''s new" window tells you what changed after each update; open it any time from the menu'),
        (2, 'new',      'Release notes on the blog: every major and minor release gets a write-up, with the smaller fixes in between listed under it'),
        (3, 'improved', 'The menu now shows both the app version and the server version')
     ) AS v(ord, kind, txt)
WHERE r."Stream" = 'app' AND r."Version" = '6.2.0';

INSERT INTO accounts."BlogPosts" ("Title", "Slug", "Excerpt", "ContentMarkdown", "IsPublished", "PublishedAt", "CreatedAt", "UpdatedAt", "IsDeleted")
VALUES (
    'FinPlanner 6.2: What''s new, every time',
    'finplanner-app-6-2-release-whats-new',
    'After each update a short "What''s new" window tells you what changed, and every major and minor release gets a write-up here on the blog.',
    $md$# FinPlanner 6.2: What's new, every time

Version 6.2 makes updates easier to follow.

## A "What's new" window after each update

When you open FinPlanner after an update, a short window lists what changed since your last visit, grouped into **New**, **Improved** and **Fixed**. Close it and it will not appear again until the next update. If you want to look again, open the menu and choose **What's new** next to the version number.

## Release notes on the blog

Every major and minor release gets a blog write-up like this one. Smaller fixes and improvements released in between are listed at the bottom of the next write-up, so nothing is lost, and you do not get a blog post for every small change.

## Two version numbers

The menu now shows both numbers: the **app** version (what you see and tap) and the **server** version (what runs behind it). They are released separately, so they can change at different times.
$md$,
    TRUE, TIMESTAMPTZ '2026-11-14 00:00:00+00', NOW(), NOW(), FALSE)
ON CONFLICT ("Slug") DO UPDATE
    SET "Title" = EXCLUDED."Title", "Excerpt" = EXCLUDED."Excerpt", "ContentMarkdown" = EXCLUDED."ContentMarkdown",
        "IsPublished" = TRUE, "PublishedAt" = EXCLUDED."PublishedAt", "UpdatedAt" = NOW(), "IsDeleted" = FALSE;
