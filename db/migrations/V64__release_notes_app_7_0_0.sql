-- V64: Release notes for app 7.0.0 (major release: has a blog article).
-- Same pattern as V44/V45. The version must match what the UI release pipeline assigns
-- (run `npm run release:dry` in the ui repo to see it before pushing).
-- Patches released since 6.2.0 are appended to the article automatically.

INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('app', '7.0.0', DATE '2026-11-15',
        'A budgets hub with smarter alerts, an Undo centre, reports you can download, notes and tags, and a lot of accuracy fixes.',
        'finplanner-app-7-0-release-budgets-undo-reports')
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

DELETE FROM accounts."ReleaseNoteItems"
WHERE "ReleaseNoteId" = (SELECT "Id" FROM accounts."ReleaseNotes" WHERE "Stream" = 'app' AND "Version" = '7.0.0');

INSERT INTO accounts."ReleaseNoteItems" ("ReleaseNoteId", "SortOrder", "Type", "Text")
SELECT r."Id", v.ord, v.kind, v.txt
FROM accounts."ReleaseNotes" r,
     (VALUES
        (1,  'new',      'Budgets is now a hub with Dashboard, Manage and Settings tabs: see every budget''s pace, projection and status, and set an alert level from 50% to 100%'),
        (2,  'new',      'Undo centre: undo a deleted transaction for 10 minutes, with a preview of what it does to your balance and budget'),
        (3,  'new',      'Reports: view a month or a whole year on screen, then download it as Excel or PDF'),
        (4,  'new',      'Add notes and tags to transactions, and save your favourite filters'),
        (5,  'new',      'Transaction rules fill in the category and tags for you when a description matches'),
        (6,  'new',      'Quick add button, and one-tap Quick Cards for the things you buy again and again'),
        (7,  'new',      'Select several transactions to change their category, add a tag or delete them together'),
        (8,  'new',      'Sub-categories such as Food › Groceries in every category picker, with a tree to manage them'),
        (9,  'new',      '"Your month": choose which day your money month starts, and the dashboard, budgets and reports follow it'),
        (10, 'new',      'Overview and Settings tabs for Accounts, Transactions, Subscriptions and Recurring, plus a Settings page for each account'),
        (11, 'new',      'Credit cards follow statement cycles, and "Pay which statement?" lets you choose the bill you are paying'),
        (12, 'new',      'Split: co-admins, view-only guests, itemised bills with tax and tip, recurring group expenses and reminder emails'),
        (13, 'new',      'Investments, insurance policies, receipts and document links, and email reminders for payments that are due'),
        (14, 'new',      'What-if calculators for loans and credit cards, a Spending heatmap and Top merchants on the Transactions overview'),
        (15, 'new',      'Tips & how-tos: a short tip at the bottom of each page and a page that lists them all'),
        (16, 'improved', 'All email alerts are now opt-in: budget alerts, Feedback Hub updates and payment reminders stay off until you switch them on in Settings'),
        (17, 'improved', 'Amounts over a lakh use Indian grouping (₹3,12,000) and the dashboard tiles show short forms such as ₹3.12L'),
        (18, 'improved', 'Tap the dashboard tiles and category rows to see the details behind each number'),
        (19, 'improved', 'Goals show whether you are on track, with milestones and a projected finish month'),
        (20, 'fixed',    'Selling an investment at a loss is no longer counted as money you spent'),
        (21, 'fixed',    'Deleting a loan payment now removes its interest entry as well'),
        (22, 'fixed',    'Every unpaid card statement now counts toward what you owe, not just the latest one'),
        (23, 'fixed',    'The Subscriptions monthly estimate now handles quarterly and half-yearly plans correctly'),
        (24, 'fixed',    'Importing a split group to your ledger twice, or settling with someone from two devices, can no longer double up'),
        (25, 'fixed',    'Save Profile in Settings now actually saves, and you can change your email address safely')
     ) AS v(ord, kind, txt)
WHERE r."Stream" = 'app' AND r."Version" = '7.0.0';

INSERT INTO accounts."BlogPosts" ("Title", "Slug", "Excerpt", "ContentMarkdown", "IsPublished", "PublishedAt", "CreatedAt", "UpdatedAt", "IsDeleted")
VALUES (
    'FinPlanner 7.0: Budgets that talk back, Undo, and reports you can download',
    'finplanner-app-7-0-release-budgets-undo-reports',
    'A new budgets hub with alert levels, an Undo centre, downloadable reports, notes and tags, sub-categories, and many accuracy fixes.',
    $md$# FinPlanner 7.0: Budgets that talk back, Undo, and reports you can download

Version 7.0 is our biggest update so far. It makes budgets more useful, makes mistakes easy to take back, and makes every number on the screen easier to trust.

> [!IMPORTANT]
> **Email alerts are now off by default.** Budget alerts, Feedback Hub updates and payment reminders stay off until you switch them on in **Settings, Notifications**. Sign-in and security emails are not affected.

## A budgets hub

**Budgets** now has three tabs.

- **Dashboard** shows each budget's pace for the month, where it is heading, and its status, with a log of the alerts you have had and a **Spending behaviour** card that shows when you tend to overspend.
- **Manage** is where you add and edit budgets. Each budget can have its own alert level.
- **Settings** sets your default alert level (anywhere from 50% to 100%) and whether alerts appear on screen, by email, or both.

Tap a budget to see how it splits across sub-categories, and tap again to look at the transactions behind it.

## Undo centre

Deleted something by mistake? For ten minutes after you delete a transaction you can bring it back, either from the **Undo** button that appears right after deleting, or from **Undo centre** in the menu. Each item shows a countdown, your balance now and after undoing, and what it would do to the related budget. If undoing would overdraw an account, it tells you why instead.

Transfers, loan and card payments and system entries cannot be undone this way.

## Reports you can download

Open **Reports**, pick a month or a whole year, and read it on screen. When you want a copy, download it as **Excel** or **PDF**. The numbers match your dashboard.

## Your money, organised your way

- **Notes and tags** on any transaction. Tap a tag to see everything with it.
- **Saved filters** you can bring back with one tap.
- **Transaction rules**: when a description contains a word you choose, the category and tags are filled in for you.
- **Quick add** from any page, and **Quick Cards** in Bulk add for the things you record often.
- **Select** several transactions to change their category, add a tag, or delete them together.
- **Sub-categories** such as *Food › Groceries*, in every category picker, with a tree view to manage them.
- A **Spending heatmap** and **Top merchants** on the Transactions overview.

## "Your month"

If your salary arrives on the 25th, your month probably starts on the 25th too. Choose the day in **Settings, Preferences, Your month** and the dashboard, budgets, reports and financial health all follow it.

## Accounts, cards and loans

- Accounts, Transactions, Subscriptions and Recurring each have their own **Overview** and **Settings** tabs.
- Each account has a **Settings** page behind its gear icon, and tapping an account opens its transactions.
- Credit card pages follow **statement cycles**, and **Pay which statement?** lets you choose the bill you are paying.
- **Investments** and **Insurance** have their own pages, and you can attach receipts and document links.
- **What-if calculators** show how extra payments change a loan, and what a credit card balance really costs.
- **Email reminders** for payments that are due, if you want them.

## Split gets roles

Group creators can make **co-admins**, mark members as **view-only guests**, split an **itemised bill** with tax and tip, set up **recurring group expenses**, and send **reminder emails** to people who owe.

## Numbers you can trust

We went looking for places where two screens could disagree, and fixed what we found:

- Selling an investment at a loss is no longer counted as spending.
- Deleting a loan payment removes its interest entry too.
- Every unpaid card statement now counts toward what you owe.
- The Subscriptions monthly estimate handles quarterly and half-yearly plans.
- Importing a split group to your ledger, or settling with someone, can no longer double up if you tap twice or use two devices.
- **Save Profile** now saves, and changing your email address sends a code to the new one first.

## Smaller touches

Amounts over a lakh use Indian grouping, such as ₹3,12,000, and dashboard tiles show short forms like ₹3.12L (hover to see the full amount). Goals show whether you are on track. A short tip appears at the bottom of each page, and **Tips & how-tos** in the menu lists them all.
$md$,
    TRUE, TIMESTAMPTZ '2026-11-15 00:00:00+00', NOW(), NOW(), FALSE)
ON CONFLICT ("Slug") DO UPDATE
    SET "Title" = EXCLUDED."Title", "Excerpt" = EXCLUDED."Excerpt", "ContentMarkdown" = EXCLUDED."ContentMarkdown",
        "IsPublished" = TRUE, "PublishedAt" = EXCLUDED."PublishedAt", "UpdatedAt" = NOW(), "IsDeleted" = FALSE;
