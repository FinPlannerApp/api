-- V44: Release notes for app 6.1.0 (minor release: has a blog article).
--
-- Pattern for every release (copy this file):
--   1. upsert the release row,
--   2. replace its items,
--   3. for minor/major releases, upsert the blog article and point BlogSlug at it.
-- Re-running is safe (everything is an upsert).

INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('app', '6.1.0', DATE '2026-11-14',
        'Signed-in devices, new-device alerts, more accurate dashboard numbers, card history and a data-health check.',
        'finplanner-app-6-1-release-devices-and-accuracy')
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

DELETE FROM accounts."ReleaseNoteItems"
WHERE "ReleaseNoteId" = (SELECT "Id" FROM accounts."ReleaseNotes" WHERE "Stream" = 'app' AND "Version" = '6.1.0');

INSERT INTO accounts."ReleaseNoteItems" ("ReleaseNoteId", "SortOrder", "Type", "Text")
SELECT r."Id", v.ord, v.kind, v.txt
FROM accounts."ReleaseNotes" r,
     (VALUES
        (1, 'new',      'Signed-in devices: see every device that is signed in to your account and sign any of them out (Settings, Account & Security)'),
        (2, 'new',      'Stay signed in on several devices at once, with no more "already logged in elsewhere" prompt'),
        (3, 'new',      'Get an email when your account is used from a device you have not used before'),
        (4, 'new',      'Statements and payment history for a credit card on the transactions page, and a Data health check on the Accounts page'),
        (5, 'improved', 'Dashboard, net worth and financial health now count spendable cash and credit-card bills more accurately; choose how each account counts when you add or edit it'),
        (6, 'improved', 'Adding many transactions at once is safer: rows are checked before saving, double clicks are ignored and up to 500 rows can be saved at a time')
     ) AS v(ord, kind, txt)
WHERE r."Stream" = 'app' AND r."Version" = '6.1.0';

INSERT INTO accounts."BlogPosts" ("Title", "Slug", "Excerpt", "ContentMarkdown", "IsPublished", "PublishedAt", "CreatedAt", "UpdatedAt", "IsDeleted")
VALUES (
    'FinPlanner 6.1: Your devices, and numbers you can trust',
    'finplanner-app-6-1-release-devices-and-accuracy',
    'Stay signed in on all your devices and see exactly where, get alerted when a new device appears, and rely on dashboard numbers that treat cash and credit-card bills the way you do.',
    $md$# FinPlanner 6.1: Your devices, and numbers you can trust

Version 6.1 is about two things: **knowing who is signed in to your account**, and **trusting the totals on your dashboard**.

## Sign in on every device you use

Until now, signing in on a second device could sign you out of the first one, and you were asked to confirm. That prompt is gone. You can stay signed in on your phone, laptop and tablet at the same time.

Open **Settings, Account & Security, Signed-in Devices** to see each device: the browser and system, its IP address, when it was last active and when you signed in. Anything you do not recognise can be signed out with one click, and **Sign out all other devices** keeps only the one you are using.

> [!TIP]
> If you see a device you do not recognise, sign it out and then use **Forgot password** on the sign-in page. Resetting your password signs out every device.

## A heads-up when a new device signs in

The first time your account is used from a device we have not seen before, we now send you an email with the browser, the system, the IP address and the time. If it was you, there is nothing to do. If it was not, change your password straight away.

## Safer sessions

Your sign-in on each device is now tied to that device. If a copy of a sign-in is ever reused from somewhere else, the session is ended instead of being trusted, and you are asked to sign in again. You will not notice any of this in normal use.

## Dashboard numbers that match your real money

Your dashboard, net worth and financial-health score now follow how you actually think about your money:

- **You choose how each account counts.** When you add or edit an account, **How should this count?** lets you mark it as automatic, spendable cash (for example a cash or UPI wallet), an emergency fund that is not counted as spendable, or money held for someone else that stays out of your net worth.
- **Credit cards count what you really owe.** For each card, what is owed is the larger of the balance we track and the unpaid bill you recorded, so a bill you have recorded but not fully tracked still shows up as a liability.

## Credit card history and a data-health check

- Pick a credit card on the transactions page and you now get its **statements and payment history**: each recorded statement with its due date, and the payments made against it.
- **Accounts, Data health** is a read-only report that compares your balances, transfers, duplicates and card bills with your transactions and tells you where they disagree. It never changes anything.

## Safer bulk entry

When you add many transactions at once, every row is now checked before anything is saved, repeated clicks on Save are ignored so you cannot add the same batch twice, and a single save takes up to 500 rows.
$md$,
    TRUE, TIMESTAMPTZ '2026-11-14 00:00:00+00', NOW(), NOW(), FALSE)
ON CONFLICT ("Slug") DO UPDATE
    SET "Title" = EXCLUDED."Title", "Excerpt" = EXCLUDED."Excerpt", "ContentMarkdown" = EXCLUDED."ContentMarkdown",
        "IsPublished" = TRUE, "PublishedAt" = EXCLUDED."PublishedAt", "UpdatedAt" = NOW(), "IsDeleted" = FALSE;
