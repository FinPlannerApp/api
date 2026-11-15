-- V70: Release notes for app 7.2.0 (minor release: has a blog article).
-- Same pattern as V44/V45/V64/V66. The version must match what the UI release pipeline assigns
-- (run `npm run release:dry` in the ui repo to see it before pushing).
-- Patches released since 7.1.0 are appended to the article automatically.

INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('app', '7.2.0', DATE '2026-11-16',
        'Report a problem in one tap, goals that follow an account, more money into savings buckets, and suggestions for overdue bills that look already paid.',
        'finplanner-app-7-2-release-report-a-problem-goals-follow-accounts')
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

DELETE FROM accounts."ReleaseNoteItems"
WHERE "ReleaseNoteId" = (SELECT "Id" FROM accounts."ReleaseNotes" WHERE "Stream" = 'app' AND "Version" = '7.2.0');

INSERT INTO accounts."ReleaseNoteItems" ("ReleaseNoteId", "SortOrder", "Type", "Text")
SELECT r."Id", v.ord, v.kind, v.txt
FROM accounts."ReleaseNotes" r,
     (VALUES
        (1, 'new',      'Report a problem: tap the flag button (or press Alt+Shift+B) and your report reaches the Feedback Hub with a screenshot and the details needed to fix it'),
        (2, 'new',      'A goal can follow an account, such as one you keep only for your emergency fund, and updates by itself as the balance changes'),
        (3, 'new',      'Add more money to a savings bucket in one step'),
        (4, 'new',      'Overdue bills that a recent payment looks like it settled are suggested for you to confirm, and a card statement can be marked as paid in one tap'),
        (5, 'improved', 'One loader in the middle of the screen for loading and saving, instead of spinners in different places'),
        (6, 'improved', 'Merchants load a page at a time, with search, so long lists stay quick'),
        (7, 'improved', 'Each account card has a settings gear, and every page opens at the top'),
        (8, 'fixed',    'Home, Features, About and the other public pages now fill the screen instead of sitting in a narrow column')
     ) AS v(ord, kind, txt)
WHERE r."Stream" = 'app' AND r."Version" = '7.2.0';

INSERT INTO accounts."BlogPosts" ("Title", "Slug", "Excerpt", "ContentMarkdown", "IsPublished", "PublishedAt", "CreatedAt", "UpdatedAt", "IsDeleted")
VALUES (
    'FinPlanner 7.2: Report a problem in one tap, and goals that follow your accounts',
    'finplanner-app-7-2-release-report-a-problem-goals-follow-accounts',
    'A bug report button with screenshot, goals that follow an account, more money into savings buckets, and suggestions for overdue bills that look already paid.',
    $md$# FinPlanner 7.2: Report a problem in one tap, and goals that follow your accounts

## Report a problem, fast

Found something wrong? Tap the **flag button** (bottom right) or press **Alt+Shift+B** on a laptop or desktop. Type what went wrong and send.

Your report goes to the **Feedback Hub** as a bug, along with what we need to fix it, so you do not have to explain where you were or what you tapped:

- a **screenshot** of the screen,
- the **page**, your device and screen size,
- any **errors** and which requests to the server **failed**,
- your last few **taps**.

You see all of it before you send, and you can switch off the screenshot or the technical details. We never include passwords, what you typed into a field, or the data the server sent back.

## Goals that follow an account

A goal used to follow a savings bucket or a number you typed in. Now it can also follow **an account**. If you keep one bank account only for your emergency fund, point the goal at it: the goal grows by itself as you deposit and shrinks if you spend, with nothing to update. A bucket still takes priority if you choose one.

## More money into a bucket

Each savings bucket has a **plus** button to add money to it in one step. The account must have that much not yet set aside.

## Overdue bills that may already be paid

If a bill shows as overdue but a payment that looks like it is already in your records, FinPlanner now says so under **Obligations** and on the dashboard: *"May already be paid"*. For a credit card statement you can **mark it as paid** in one tap. Nothing changes until you confirm, and **Not paid** hides the suggestion.

## Smoother everywhere

- One **loader** in the middle of the screen for loading and saving, instead of spinners in different places. Quick actions do not flash it.
- **Merchants** load a page at a time, with search.
- Each account card has a **settings gear**, and every page opens at the top.
- The public pages, Home, Features, About and the rest, now fill the screen.
$md$,
    TRUE, TIMESTAMPTZ '2026-11-16 00:00:00+00', NOW(), NOW(), FALSE)
ON CONFLICT ("Slug") DO UPDATE
    SET "Title" = EXCLUDED."Title", "Excerpt" = EXCLUDED."Excerpt", "ContentMarkdown" = EXCLUDED."ContentMarkdown",
        "IsPublished" = TRUE, "PublishedAt" = EXCLUDED."PublishedAt", "UpdatedAt" = NOW(), "IsDeleted" = FALSE;
