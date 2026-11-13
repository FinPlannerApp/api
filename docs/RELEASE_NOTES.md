# How to write release notes and blog articles

This is the complete procedure for telling users what changed. It is written for developers and for AI agents: follow it step by step and you do not need any other context.

## How it works (read this once)

| Thing | Where it lives | Who sets it |
|---|---|---|
| **UI ("app") version** | `ui/package.json`, `ui/src/environments/*.ts` (`appVersion`), `ui/release.json` | Automatic. The `Release Version` GitHub Action in the **ui** repo, after CI passes on `main`. |
| **Backend ("server") version** | `<VersionPrefix>` in `api/src/Presentation/API.csproj`, `api/.github/release.json` | Automatic. The backend pipeline in the **api** repo, before migrations and deploy. |
| **Release notes** (what users read) | PostgreSQL tables `accounts."ReleaseNotes"` and `accounts."ReleaseNoteItems"` | **You**, as a Flyway migration in the **api** repo. |
| **Blog article** for a minor/major release | `accounts."BlogPosts"` row | **You**, in the same migration. |

The two versions are independent. A release is either of the `app` stream (UI) or the `server` stream (backend).

Users see the notes in two places:

- the **"What's new" window**, which opens once after an update, or from the menu footer (`App 6.2.0 · Server 5.1.0 · What's new`). It calls `GET /api/Releases/recent`, remembers the newest release id it has shown (`localStorage` key `finplanner_last_seen_release_id`), and on a first visit shows only the latest release;
- the **blog** (`/blog`): a "Recent releases" strip, and one article per **minor or major** release.

**Nothing about release notes is generated from commit messages and nothing is bundled in the UI.** If you do not write a migration for a release, users simply are not told about it (a "silent" release).

## Which releases get what

The level comes from the version number: `X.0.0` is **major**, `X.Y.0` is **minor**, `X.Y.Z` with `Z > 0` is **patch**.

| Level | Notes for the window (`ReleaseNoteItems`) | Blog article |
|---|---|---|
| **Patch** (6.2.1) | Yes: a few short lines | **No.** Do not create one. It is listed automatically at the bottom of the next minor/major article. |
| **Minor** (6.3.0) | Yes | **Yes.** Covers the new features. The patches since the previous minor/major (6.2.1, 6.2.2, ...) are appended automatically under "Also fixed and improved since 6.2.0". |
| **Major** (7.0.0) | Yes | **Yes.** Bigger write-up for the big changes. The last patches are appended automatically in the same way. |

Do **not** copy patch notes into the minor article by hand; the API adds them.

## Step by step

### 1. Decide the version

The version is assigned by the pipelines from the commit types since the last release (Conventional Commits):

- `feat!:` or a `BREAKING CHANGE:` line in the body gives a **major** bump;
- any `feat:` gives a **minor** bump;
- anything else (`fix`, `perf`, `refactor`, `chore`, `docs`, `test`, `ci`, or no prefix) gives a **patch** bump.

So make your commits first, then ask the tooling what number they will produce. **The version in your migration must match it.**

```bash
# UI release (run in the ui repo, with your commits made)
npm run release:dry
#   App v6.2.0 -> v6.3.0 (2 commit(s))

# Backend release (run in the api repo, with your commits made)
node .github/scripts/release-api.mjs dry
#   Server v5.1.0 -> v5.2.0 (1 commit(s))
```

If it prints "Nothing to release", there are no commits to release yet.

If you push other commits before this one, the number can change. Re-run the dry run right before you push and correct the migration if it differs. A mismatch is not fatal: the notes just sit under a version that never ships (see Troubleshooting).

### 2. Pick the next Flyway number

Flyway migrations are applied in version order and never edited after they have run in production.

```bash
ls db/migrations | sort -V | tail -3     # highest existing V number + 1 is yours
```

### 3. Create the migration file

Name: `db/migrations/V<n>__release_notes_<stream>_<x_y_z>.sql`

- `<stream>` is `app` or `server`.
- `<x_y_z>` is the version with underscores.
- Examples: `V46__release_notes_app_6_3_0.sql`, `V47__release_notes_server_5_2_0.sql`.

Keep the `release_notes_<stream>_<x_y_z>` part of the file name exactly. The backend pipeline looks for `release_notes_server_<version>` and warns when a server release has none, and a commit that **only** adds files named `V<n>__release_notes_*.sql` does not trigger a new backend release.

One file per release. Copy `db/migrations/V44__release_notes_app_6_1_0.sql` (a minor release with an article) or use the patch template below.

### 4. Fill in the release row and the items

Every migration does the same three things, all idempotent (safe to run twice):

1. upsert the release row,
2. replace its items,
3. for minor/major only, upsert the blog article.

#### Template: patch release (no article)

```sql
-- V47: Release notes for app 6.2.1 (patch: notes only, no blog article).

INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('app', '6.2.1', DATE '2026-11-20', NULL, NULL)
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

DELETE FROM accounts."ReleaseNoteItems"
WHERE "ReleaseNoteId" = (SELECT "Id" FROM accounts."ReleaseNotes" WHERE "Stream" = 'app' AND "Version" = '6.2.1');

INSERT INTO accounts."ReleaseNoteItems" ("ReleaseNoteId", "SortOrder", "Type", "Text")
SELECT r."Id", v.ord, v.kind, v.txt
FROM accounts."ReleaseNotes" r,
     (VALUES
        (1, 'fixed',    'Fixed the dashboard showing last month''s total after midnight'),
        (2, 'improved', 'The transactions page opens faster')
     ) AS v(ord, kind, txt)
WHERE r."Stream" = 'app' AND r."Version" = '6.2.1';
```

#### Template: minor or major release (with article)

Same as above, with `"BlogSlug"` set, and one more statement at the end:

```sql
INSERT INTO accounts."ReleaseNotes" ("Stream", "Version", "ReleasedOn", "Summary", "BlogSlug")
VALUES ('app', '6.3.0', DATE '2026-12-01',
        'One-sentence summary shown under the heading in the What''s new window.',
        'finplanner-app-6-3-release-short-name')
ON CONFLICT ("Stream", "Version") DO UPDATE
    SET "ReleasedOn" = EXCLUDED."ReleasedOn", "Summary" = EXCLUDED."Summary", "BlogSlug" = EXCLUDED."BlogSlug";

-- ... DELETE + INSERT of items exactly as in the patch template, with '6.3.0' ...

INSERT INTO accounts."BlogPosts" ("Title", "Slug", "Excerpt", "ContentMarkdown", "IsPublished", "PublishedAt", "CreatedAt", "UpdatedAt", "IsDeleted")
VALUES (
    'FinPlanner 6.3: Short, friendly title',
    'finplanner-app-6-3-release-short-name',
    'One or two sentences shown on the blog card and in search results.',
    $md$# FinPlanner 6.3: Short, friendly title

Opening paragraph: what this release is about, in one or two sentences.

## First feature

What it does for the user and where to find it.

> [!TIP]
> Optional tip box (also supported: NOTE, IMPORTANT, WARNING, CAUTION).

## Second feature

...
$md$,
    TRUE, TIMESTAMPTZ '2026-12-01 00:00:00+00', NOW(), NOW(), FALSE)
ON CONFLICT ("Slug") DO UPDATE
    SET "Title" = EXCLUDED."Title", "Excerpt" = EXCLUDED."Excerpt", "ContentMarkdown" = EXCLUDED."ContentMarkdown",
        "IsPublished" = TRUE, "PublishedAt" = EXCLUDED."PublishedAt", "UpdatedAt" = NOW(), "IsDeleted" = FALSE;
```

#### Field rules

| Field | Rule |
|---|---|
| `Stream` | `'app'` for UI releases, `'server'` for backend releases. Anything else is rejected by a `CHECK`. |
| `Version` | Exactly the version the pipeline assigns, `X.Y.Z`. Unique per stream. |
| `ReleasedOn` | The release date, `DATE 'YYYY-MM-DD'`. |
| `Summary` | One sentence, or `NULL`. Shown under the heading in the window. Good for minor/major releases. |
| `BlogSlug` | `NULL` for patches. For minor/major, the slug of the article below. Use lowercase words and hyphens, and **include the word `release`**: the blog page decides its "Release" tag from that, and tag filtering matches the slug. Convention: `finplanner-<app\|server>-<major>-<minor>-release-<short-name>`. |
| Item `kind` | One of `new`, `improved`, `fixed` (a `CHECK` rejects others). |
| Item `ord` | `1, 2, 3, ...`: the order within the release. The window groups by kind (New, Improved, Fixed) and keeps this order inside each group. |
| Item `txt` | One line, a single sentence. |
| `Title` / `Excerpt` | Title is the article heading. Excerpt is a short teaser. |
| `PublishedAt` | `TIMESTAMPTZ 'YYYY-MM-DD 00:00:00+00'`. Newest first on the blog. |

### 5. Write it for the user, not for developers

- Say what **the person** can do or notice now. Name the screen: "Settings, Account & Security, Signed-in Devices".
- Plain language. No class names, endpoints, table names, commit hashes, or words like "refactor", "interceptor", "migration".
- Only claim what you have checked in the app. If you are not sure a label or screen exists, look it up in the UI code first.
- One idea per line. Lead with the benefit.

| Bad | Good |
|---|---|
| `Refactored AuthService to support multi-session` | `Stay signed in on several devices at once` |
| `Fixed null reference in DashboardService` | `Fixed the dashboard showing zero net worth when you have no cards` |
| `Added ValidateBulkTransactionsCommand` | `Rows are checked before a bulk save, so one bad row no longer loses the batch` |

Anything users would not notice (internal refactors, CI, tests, dependency bumps) gets **no** line. If a whole release has nothing user-visible, write no migration: it stays silent.

### 6. SQL pitfalls

- **Apostrophes** inside normal `'...'` strings are doubled: `What''s new`. Inside `$md$ ... $md$` (the article body) they are written normally.
- The article body is wrapped in `$md$ ... $md$`. Do not put the text `$md$` inside it, and avoid `${...}` anywhere (Flyway placeholders).
- Keep `ON CONFLICT` upserts: migrations must be re-runnable.
- Never edit a migration that has already run in production. To change wording of a released note, add a new migration that upserts the same row (same `Stream` + `Version`, same `Slug`).
- Do not insert `ReleaseNoteItems` by hand with an explicit `Id`; always select the release id from `ReleaseNotes` as in the templates.

### 7. Commit and push

- Commit in the **api** repo with a Conventional Commit. A commit that contains **only** release-notes migration files does not cause a new backend release, so use `docs: release notes for app 6.3.0`.
- If your notes go with a backend change, they can ship in the same commit as that change (the migration is applied by the same pipeline run, before the deploy).
- UI releases have no UI-side file to change: the UI reads the notes from the API.
- Order for an app release: push the **api** repo (the notes), then the **ui** repo. If the UI goes first, the window just finds no new release until the notes arrive.

### 8. Verify after deploy

1. Backend pipeline is green, including **Run Flyway Migrations (Production)**.
2. `GET <api>/api/Releases/recent?count=3` lists your release with the right `version`, `level`, `blogSlug` and `items`.
3. `GET <api>/api/Blog/published/<slug>` returns the article. For a minor/major release it ends with "Also fixed and improved since X" when patches exist.
4. `GET <api>/api/version` shows the server version; the menu footer shows the app version (after the UI deploy).
5. Open the app signed in. To see the window again, remove `finplanner_last_seen_release_id` from the browser's local storage, or use the menu footer.

## Quick checklist

- [ ] Commits made with Conventional Commit types.
- [ ] `npm run release:dry` / `node .github/scripts/release-api.mjs dry` run; version noted.
- [ ] Next free `V<n>` number chosen.
- [ ] File named `V<n>__release_notes_<app|server>_<x_y_z>.sql`.
- [ ] Release row, items replaced, and (minor/major only) article included; slug contains `release`.
- [ ] Text is user-facing, plain, and checked against the real app.
- [ ] Patches: no article. Minor/major: article, and no hand-copied patch notes.
- [ ] Pushed api first, then ui.
- [ ] Verified `GET /api/Releases/recent`.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| The window never opens for the new release | No row for it, or the pipeline assigned a different version than your migration. Compare `GET /api/Releases/recent` with the footer version; add a migration for the real version. The person may also have seen it already (clear `finplanner_last_seen_release_id`). |
| Pipeline notice: "Server vX has no release notes" | Informational. Add a `release_notes_server_<x_y_z>` migration if users should hear about it; otherwise ignore. |
| The version in my migration is wrong | The release has not happened or took a different bump. Add a new migration with the correct version. Do not edit the old one if it already ran. If it never ran, you may edit it. |
| Flyway fails with a syntax error | Usually an unescaped apostrophe in a normal string, a stray `$md$`, or a missing comma in the `VALUES` list. Test the file on a local database first. |
| The article is not on the blog | `IsPublished` must be `TRUE`, `IsDeleted` `FALSE`, and the slug unique. The blog lists published posts newest first, six per page. |
| The article has no "Release" tag | The slug must contain `release`. |
| Patches missing from the article | They must be in the **same stream**, with versions strictly between the previous minor/major and this release, and have items. They are not added to a patch release's own page. |
| A new backend release was triggered by a notes commit | The commit also touched other files, or a file name does not match `V<n>__release_notes_*.sql`. |

## Reference

- Schema: `db/migrations/V43__release_notes.sql`
- Examples: `V44__release_notes_app_6_1_0.sql` (minor with article), `V45__release_notes_app_6_2_0.sql`
- API: `src/Presentation/Controllers/ReleasesController.cs`, `src/Application/Services/ReleaseNotesService.cs` (including the patch roll-up), `src/Presentation/Controllers/BlogController.cs`
- Backend versioning: `.github/scripts/release-api.mjs`, `.github/release.json`
- UI versioning: `ui/scripts/release.mjs`, `ui/release.json`, `ui/.github/workflows/release-version.yml`
- UI window and blog strip: `ui/src/app/core/services/changelog.service.ts`, `ui/src/app/shared/components/whats-new/whats-new.ts`
