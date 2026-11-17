-- V79: Release notes for app 7.4.0 (minor release: has a blog article).
-- Follows docs/RELEASE_NOTES.md. The version matches what `npm run release:dry` assigns.
-- Patches released since 7.3.0 are appended to the article automatically.

INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('app', '7.4.0', DATE '2026-11-17',
        'Split balances for members who left, expiring public share links, signed-in device IP privacy, and faster transactions loading.',
        'finplanner-app-7-4-release-split-privacy-performance')
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

DELETE FROM accounts."ReleaseNoteItems"
WHERE "ReleaseNoteId" = (SELECT "Id" FROM accounts."ReleaseNotes" WHERE "Stream" = 'app' AND "Version" = '7.4.0');

INSERT INTO accounts."ReleaseNoteItems" ("ReleaseNoteId", "SortOrder", "Type", "Text")
SELECT r."Id", v.ord, v.kind, v.txt
FROM accounts."ReleaseNotes" r,
     (VALUES
        (1, 'new',      'Split groups: option to keep members who left visible in balances so later adjustments can be settled'),
        (2, 'new',      'Split groups: public share links are now opt-in, expire after 30 days, and can be toggled by group admins'),
        (3, 'improved', 'Settings: Signed-in Devices no longer shows device IP addresses for greater privacy'),
        (4, 'improved', 'Transactions overview and side panels load simultaneously for a noticeably faster page load'),
        (5, 'improved', 'Mobile safe-area support: screen edges and notches keep comfortable margins on all devices'),
        (6, 'fixed',    'PWA updates bypass stuck service worker caches so you are alerted to new versions immediately')
     ) AS v(ord, kind, txt)
WHERE r."Stream" = 'app' AND r."Version" = '7.4.0';

INSERT INTO accounts."BlogPosts" ("Title", "Slug", "Excerpt", "ContentMarkdown", "IsPublished", "PublishedAt", "CreatedAt", "UpdatedAt", "IsDeleted")
VALUES (
    'FinPlanner 7.4: Fairer Split balances, expiring links, and privacy polish',
    'finplanner-app-7-4-release-split-privacy-performance',
    'Split balances for members who left, expiring public share links, signed-in device IP privacy, and faster transactions loading.',
    $md$# FinPlanner 7.4: Fairer Split balances, expiring links, and privacy polish

This release brings smart improvements to group expense settlements, tighter privacy defaults across your account settings, and snappier page performance.

## Keep left members in Split balances

When friends or roommates leave a Split group, later edits to old expenses can leave them owing or being owed money. 

Group admins can now turn on **"Show balances of members who left"** in group settings. Those members remain clearly listed with a "Left" badge so any outstanding balances can be settled properly.

## Expiring public share links

Sharing a live breakdown of group expenses is great, but open links shouldn't stay public forever. 

Public links are now **opt-in and automatically expire after 30 days**. Group admins can turn them on or off anytime with a single toggle, or reset the link if needed.

## Privacy & Performance Improvements

- **Signed-in Devices:** Your active sessions list in **Settings → Security** no longer displays IP addresses, keeping your network details private.
- **Faster Transactions:** Transaction lists now stream their breakdown cards concurrently, reducing initial page wait times.
- **Notch & Edge Comfort:** Padding adapts smoothly to device notches and gestures so cards and buttons never collide with screen edges.
- **Reliable PWA updates:** Installed app instances now verify server updates directly, avoiding stale service worker caches.
$md$,
    TRUE, TIMESTAMPTZ '2026-11-17 00:00:00+00', NOW(), NOW(), FALSE)
ON CONFLICT ("Slug") DO UPDATE
    SET "Title" = EXCLUDED."Title", "Excerpt" = EXCLUDED."Excerpt", "ContentMarkdown" = EXCLUDED."ContentMarkdown",
        "IsPublished" = TRUE, "PublishedAt" = EXCLUDED."PublishedAt", "UpdatedAt" = NOW(), "IsDeleted" = FALSE;

