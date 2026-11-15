-- V66: Release notes for app 7.1.0 (minor release: has a blog article).
-- Same pattern as V44/V45/V64. The version must match what the UI release pipeline assigns
-- (run `npm run release:dry` in the ui repo to see it before pushing).
-- Patches released since 7.0.0 are appended to the article automatically.

INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('app', '7.1.0', DATE '2026-11-16',
        'Pages now use the whole screen on large displays, Interest has its own tab, and phones, tablets and foldables fit better.',
        'finplanner-app-7-1-release-full-width-interest-tab')
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

DELETE FROM accounts."ReleaseNoteItems"
WHERE "ReleaseNoteId" = (SELECT "Id" FROM accounts."ReleaseNotes" WHERE "Stream" = 'app' AND "Version" = '7.1.0');

INSERT INTO accounts."ReleaseNoteItems" ("ReleaseNoteId", "SortOrder", "Type", "Text")
SELECT r."Id", v.ord, v.kind, v.txt
FROM accounts."ReleaseNotes" r,
     (VALUES
        (1, 'new',      'Interest has its own tab in Accounts, and an Interest switch inside accounts that earn interest, instead of a card that pushed the page down'),
        (2, 'improved', 'Pages use the whole width of laptop, desktop and ultrawide screens, with settings and cards laid out side by side'),
        (3, 'improved', 'Phones, tablets and foldable devices: buttons and long names wrap instead of running off the screen'),
        (4, 'fixed',    'Paying an older credit card bill now settles that bill, not the current one'),
        (5, 'fixed',    'The site works again after a security header change blocked it from reaching the server')
     ) AS v(ord, kind, txt)
WHERE r."Stream" = 'app' AND r."Version" = '7.1.0';

INSERT INTO accounts."BlogPosts" ("Title", "Slug", "Excerpt", "ContentMarkdown", "IsPublished", "PublishedAt", "CreatedAt", "UpdatedAt", "IsDeleted")
VALUES (
    'FinPlanner 7.1: Full-width pages and a tab for Interest',
    'finplanner-app-7-1-release-full-width-interest-tab',
    'Pages now use the whole screen on large displays, Interest has its own tab, and phones, tablets and foldables fit better.',
    $md$# FinPlanner 7.1: Full-width pages and a tab for Interest

Version 7.1 is about space: using all of it on a big screen, and not wasting any of it on a small one.

## Pages that use your whole screen

On a laptop, a desktop monitor or an ultrawide display, pages used to sit in a narrow column in the middle. Now they use the width you have. Overview pages fill the screen, and on Settings, Rules and What-ifs the cards sit side by side: in What-ifs, your inputs are on the left and the results on the right, so you see the effect of a change as you type.

## Interest in its own tab

Interest used to appear as a card at the top of your accounts whenever your bank had probably credited some, and it pushed everything else down. It now has its own place:

- **Accounts, Interest** shows what your accounts have earned, and any interest waiting for you to confirm.
- Inside an account that earns interest, switch between **Transactions** and **Interest**.

Nothing is recorded until you confirm it, as before.

## Phones, tablets and foldables

We checked every page on small phones, large phones, tablets, laptops and foldable devices, both folded and unfolded. Rows of buttons now wrap to a second line instead of running off the edge, and long names shorten instead of pushing the amount out of view.

## Also fixed

- **Paying an older credit card bill** now settles that bill. Before, a payment with no statement chosen could be counted against the current bill, leaving the old one overdue in your upcoming obligations.
- A security header change had briefly stopped the site from reaching the server. That is fixed.
$md$,
    TRUE, TIMESTAMPTZ '2026-11-16 00:00:00+00', NOW(), NOW(), FALSE)
ON CONFLICT ("Slug") DO UPDATE
    SET "Title" = EXCLUDED."Title", "Excerpt" = EXCLUDED."Excerpt", "ContentMarkdown" = EXCLUDED."ContentMarkdown",
        "IsPublished" = TRUE, "PublishedAt" = EXCLUDED."PublishedAt", "UpdatedAt" = NOW(), "IsDeleted" = FALSE;
