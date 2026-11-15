-- V75: Release notes for app 7.3.0 (minor release: has a blog article).
-- Same pattern as V44/V45/V64/V66/V70. The version must match what the UI release pipeline assigns
-- (run `npm run release:dry` in the ui repo to see it before pushing).
-- Patches released since 7.2.0 are appended to the article automatically.

INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('app', '7.3.0', DATE '2026-11-16',
        'Report a problem without slowing the page, screenshots you can see on your report, a My reports page, a tidier Feedback Hub, and long lists that load faster.',
        'finplanner-app-7-3-release-faster-lists-better-reports')
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

DELETE FROM accounts."ReleaseNoteItems"
WHERE "ReleaseNoteId" = (SELECT "Id" FROM accounts."ReleaseNotes" WHERE "Stream" = 'app' AND "Version" = '7.3.0');

INSERT INTO accounts."ReleaseNoteItems" ("ReleaseNoteId", "SortOrder", "Type", "Text")
SELECT r."Id", v.ord, v.kind, v.txt
FROM accounts."ReleaseNotes" r,
     (VALUES
        (1, 'new',      'My reports: everything you reported, with where each one stands, and how many others reported the same problem'),
        (2, 'new',      'The screenshot and details you send with a report now show on its Feedback Hub page'),
        (3, 'new',      'Report a problem: paste a screenshot with Ctrl+V, capture your screen, or capture the page. Amounts and typed text can be hidden in the picture'),
        (4, 'new',      'Your report text is kept as a draft, a report written offline is sent when you are back online, and the app offers to report an error when something goes wrong'),
        (5, 'improved', 'Report a problem opens instantly and no longer makes the browser stop responding'),
        (6, 'improved', 'Long lists continue where you stopped, so transactions, merchants, issues and comments stay quick however much you have'),
        (7, 'improved', 'The Feedback Hub is tidier: Load more instead of page numbers, a compact filter bar, and a proper activity timeline'),
        (8, 'fixed',    'A report no longer shows a "Not found" error where its screenshot should be'),
        (9, 'fixed',    'Messages no longer appear on top of the header buttons, and text no longer overlaps in the Home page preview')
     ) AS v(ord, kind, txt)
WHERE r."Stream" = 'app' AND r."Version" = '7.3.0';

INSERT INTO accounts."BlogPosts" ("Title", "Slug", "Excerpt", "ContentMarkdown", "IsPublished", "PublishedAt", "CreatedAt", "UpdatedAt", "IsDeleted")
VALUES (
    'FinPlanner 7.3: Faster lists and better problem reports',
    'finplanner-app-7-3-release-faster-lists-better-reports',
    'Report a problem without slowing the page, screenshots you can see on your report, a My reports page, a tidier Feedback Hub, and long lists that load faster.',
    $md$# FinPlanner 7.3: Faster lists and better problem reports

## Report a problem, without the wait

In 7.2 the report button took a screenshot of the page before it opened, and on a busy page that could make the browser say it was not responding. That is fixed. **Report a problem now opens at once** and does nothing heavy by itself.

A screenshot is optional, and you choose how:

- **Paste** one you already copied (**Ctrl+V**). It is instant.
- **Capture screen** uses your browser's own screen capture, so the page never slows down.
- **Capture this page** draws the page for you. On a large page it can pause for a few seconds, so it only runs when you ask for it.

Amounts and form fields can be covered with grey boxes in the picture. What you type is kept as a **draft**, a report written while you are **offline** is sent when you are back online, and if the app hits an error it offers to report it for you, with the details already filled in.

## See your report as the team sees it

Open a report in the Feedback Hub and you now see what you sent: the **screenshot**, the page, your device, any failed requests and errors, and the steps before it. Before, the screenshot was stored but never shown, and the page showed a "Not found" error instead.

The new **My reports** page lists everything you reported with where each one stands, such as *Received*, *Being fixed* or *Fixed in an update*. If other people reported the same problem, it tells you.

## Lists that stay fast

Long lists used to count everything and skip ahead every time you asked for the next page, which gets slower the further you go. They now **continue from the last item you saw**, the way a bookmark works, so page 500 is as quick as page 2. This applies to transactions, merchants, categories, accounts, the decision journal, budgets, the blog, Feedback Hub issues and comments, and a Split group's history.

## A tidier Feedback Hub and public pages

- **Load more** replaces page numbers for issues and comments.
- The filter bar is compact and the sidebar no longer stretches into an empty box.
- The activity feed on an issue is a clear timeline.
- Messages now appear below the header instead of on top of its buttons.
- The Home page preview no longer has overlapping text.
$md$,
    TRUE, TIMESTAMPTZ '2026-11-16 00:00:00+00', NOW(), NOW(), FALSE)
ON CONFLICT ("Slug") DO UPDATE
    SET "Title" = EXCLUDED."Title", "Excerpt" = EXCLUDED."Excerpt", "ContentMarkdown" = EXCLUDED."ContentMarkdown",
        "IsPublished" = TRUE, "PublishedAt" = EXCLUDED."PublishedAt", "UpdatedAt" = NOW(), "IsDeleted" = FALSE;
