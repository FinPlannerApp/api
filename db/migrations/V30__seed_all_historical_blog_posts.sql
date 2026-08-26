-- V30: Seed all historical blog posts into accounts."BlogPosts"

INSERT INTO accounts."BlogPosts" ("Title", "Slug", "Excerpt", "ContentMarkdown", "IsPublished", "PublishedAt", "CreatedAt", "UpdatedAt", "IsDeleted")
VALUES
(
    'v6.0.0 — Progressive Web App, UI Overhaul & Responsiveness',
    'v6-0-0-release',
    'Financial Planner is now an installable PWA with instant loading, plus a complete UI overhaul — responsive layouts, template extraction, filter bar redesigns, and dashboard polish.',
    '# v6.0.0 — Progressive Web App, UI Overhaul & Responsiveness

Version 6.0.0 is a dual release: the app is now a **Progressive Web App** (installable, offline-ready, instant loading), and the entire frontend has received a **comprehensive UI and responsiveness overhaul** across every major page.

---

## Progressive Web App

Financial Planner can now be **installed** on any device — phone, tablet, or desktop. It launches in its own window like a native app, loads **instantly from cache** on repeat visits, and notifies you when a new version is available.

* **Install Banner:** A glassmorphism-styled banner slides up at the bottom when the app is installable. One click to add to your home screen.
* **Instant Boot:** The app no longer blocks while waiting for the Render backend. Public pages load immediately — the backend wakes up silently in the background.
* **Update Toast:** When a new version is deployed, a "Reload Now" toast appears.

---

## Dashboard Layout Cleanup

The dashboard template has been restructured for cleaner nesting and better responsive behavior:
* Removed unnecessary wrapper `<div class="p-4">` — content flows naturally within the layout shell without double-padding.
* Summary cards grid uses `col-12 sm:col-6 lg:col-3` — stacking on mobile, 2-up on tablet, 4-across on desktop.

---

## Resource Page Responsive Redesign

The generic `ResourcePage` (Accounts, Categories, Budgets) received a full responsive overhaul:
* Header bar now uses `flex-column md:flex-row` — filter controls stack vertically on mobile, sit inline on desktop.
* Search bar + category filter + "New" button properly wrap with `gap-2` spacing.
',
    TRUE, '2026-05-31 00:00:00+00', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE
),
(
    'v5.0.1 — Urgent Fix: UI Polish & Support System Consolidation',
    'v5-0-1-release',
    'An urgent patch release to resolve a layout regression in generic views and consolidate the feedback channels by removing the obsolete legacy support form.',
    '# v5.0.1 — Urgent Fix: UI Polish & Support System Consolidation

Following the successful launch of **v5.0.0**, we noticed a minor layout regression on generic resource views and identified a redundancy in our user support flows. This patch release quickly addresses these issues.

---

## Support Channels Consolidation

With the introduction of the powerful full-stack **Feedback Hub** in v5.0.0, the old, simple support form is now fully obsolete.
* **Obsolescence Cleanup:** The legacy `/app/support` page has been completely removed.
* **Unified Feedback:** Users can direct all bugs, questions, and feature requests to the **Feedback Hub**.
',
    TRUE, '2026-05-29 00:00:00+00', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE
),
(
    'v5.0.0 — Community Feedback Hub, Pain-Driven Prioritization & Gamification',
    'v5-0-0-release',
    'Our biggest release yet: a full-stack community feedback system with pain-score ranking, voting, threaded discussions, product roadmap, gamified karma, badges, and an analytics dashboard.',
    '# v5.0.0 — Community Feedback Hub, Pain-Driven Prioritization & Gamification

Version 5.0.0 transforms Financial Planner from a personal finance tool into a **community-driven product**. Users can now report bugs, request features, vote on priorities, participate in threaded discussions, earn karma points, collect badges, and track the product roadmap.

---

## Pain-Driven Prioritization

Traditional issue trackers let you pick "High / Medium / Low" priority. We replaced that with a **Pain Score** — a mathematically computed ranking that surfaces the issues causing the most real damage.

```mermaid
graph TD
    A[Issue Submitted] --> B{Calculate Pain Score}
    B --> C[Impact × Frequency × Severity]
    C --> D[Add Financial Risk ₹]
    D --> E[Add Trust Penalty]
    E --> F[Ranked on Community Hub]
```

---

## Gamification & Karma

Every contribution earns **karma points** (+5 for upvotes, +10 for verified solutions/root cause comments).
',
    TRUE, '2026-05-29 00:00:00+00', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE
),
(
    'Pain Score Deep Dive: How We Calculate Issue Priority',
    'pain-score-deep-dive',
    'Traditional priority dropdowns are lazy. Here is how our weighted Pain Score formula mathematically surfaces the issues causing the most real damage to users.',
    '# Pain Score Deep Dive: How We Calculate Issue Priority

Every project management tool gives you a priority dropdown. The problem? **Everything becomes "High".** When everything is high priority, nothing is. Pain Score replaces subjective priority with a **mathematically weighted ranking**.

---

## The Formula

`Pain Score = (Impact × Frequency × Severity) + Financial Risk + Trust Penalty`

```mermaid
flowchart LR
    Impact[Impact 100x if Money] --> Sum
    Freq[Frequency 1x-10x] --> Sum
    Sev[Severity 1x-5x] --> Sum
    Sum --> Total[Total Pain Score]
```
',
    TRUE, '2026-05-29 00:00:00+00', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE
),
(
    'Feedback Hub: Your Voice Shapes the Product',
    'feedback-hub-guide',
    'A complete user guide to the Feedback Hub — how to report bugs, request features, vote on priorities, filter issues, and switch between List and Kanban views.',
    '# Feedback Hub: Your Voice Shapes the Product

The Feedback Hub is a **community-driven issue tracker** built directly into Financial Planner. Instead of filing bugs through email, you can report issues, request features, and ask questions directly.

---

## Key Features
- **Search & Filter:** Search titles, filter by category or severity.
- **Kanban Board:** Switch between List view and Kanban drag-and-drop workflow columns.
',
    TRUE, '2026-05-28 00:00:00+00', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE
),
(
    'Gamification & Karma: Earn Points, Badges, and Climb the Leaderboard',
    'gamification-karma-guide',
    'Every contribution earns karma. Unlock badges like Bug Hunter and Legend. Climb the community leaderboard and earn your contributor tag.',
    '# Gamification & Karma: Earn Points, Badges, and Climb the Leaderboard

Quality feedback is the lifeblood of a good product. Gamification rewards users who invest time in reporting bugs, confirming reproduction steps, and suggesting solutions.

---

## Karma Rewards
* **Issue upvote received:** +5 karma
* **Helpful comment:** +5 karma
* **Root cause analysis:** +10 karma
',
    TRUE, '2026-05-28 00:00:00+00', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE
),
(
    'Product Roadmap & Kanban: See What is Coming Next',
    'roadmap-kanban-guide',
    'The public product roadmap lets you see what is planned, what is being built, and what is been released. Upvote items to influence our development priorities.',
    '# Product Roadmap & Kanban: See What is Coming Next

Our product roadmap is **publicly visible** at `/feedback/roadmap`. We believe in radical transparency — you should always know what we are building, what is next, and what has already shipped.

---

```mermaid
graph LR
    A[Planned] --> B[In Progress]
    B --> C[Released]
```
',
    TRUE, '2026-05-27 00:00:00+00', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE
),
(
    'v4.7.0 — Redis Utter Peace & Infrastructure Stabilization',
    'v4-7-0-release',
    'Drastically reducing Redis command overhead, fixing critical production migrations, and implementing robust service connectivity.',
    '# v4.7.0 — Redis Utter Peace & Infrastructure Stabilization

Managing serverless Redis on a free tier requires extreme efficiency. Version 4.7.0 introduces "Utter Peace" for our infrastructure.

---

## Infrastructure Highlights
* **Hangfire Memory Storage:** Background jobs migrated to In-Memory storage.
* **Worker Optimization:** Email queue polling interval optimized from 1s to 1m.
',
    TRUE, '2026-05-15 00:00:00+00', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE
),
(
    'v4.6.0 — Seamless Navigation & Bulk Financial Insights',
    'v4-6-0-release',
    'Enhancing the user experience with smarter navigation, optimized transaction performance, real-time financial summaries, and new account search capabilities.',
    '# v4.6.0 — Seamless Navigation & Bulk Financial Insights

Financial Planner has an official domain: `https://finplanner.ska97homelab.uk`.

---

## Major Additions
* **Unified Month-Year Picker:** Filter your entire financial history with a single date selector.
* **On-Demand Search:** Fast keyboard search with 300ms input debouncing.
',
    TRUE, '2026-03-29 00:00:00+00', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE
),
(
    'v4.5.0 — Unified Transactions, Bulk Entry & Advanced Security',
    'v4-5-0-release',
    'Our biggest update yet: a unified transactions engine with smart filtering, an advanced bulk entry grid with Excel support, and a complete security overhaul.',
    '# v4.5.0 — Unified Transactions, Bulk Entry & Advanced Security

Version 4.5.0 introduces the **Unified Transactions Page**. Query your entire financial history in a single pass with real-time balance calculations.
',
    TRUE, '2026-03-18 00:00:00+00', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE
),
(
    'Why We Use JWT + OTP Authentication',
    'jwt-otp-guide',
    'A plain-English explanation of how HTTP-Only cookies, refresh token rotation, and email OTP keep your financial data safe.',
    '# Why We Use JWT + OTP Authentication

Financial data is among the most sensitive personal information a web app can handle. Our authentication system was designed with three primary threats in mind: stolen passwords, XSS token theft, and abandoned sessions.

---

```mermaid
sequenceDiagram
    Client->>API: POST /api/Auth/login
    API->>Database: Verify Credentials
    API-->>Client: JWT + HTTP-Only Cookie
```
',
    TRUE, '2026-02-15 00:00:00+00', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE
),
(
    'Project Genesis: The Technology Blueprint',
    'project-genesis',
    'Before any feature was built, we designed the data model, chose the stack, and locked in Clean Architecture as the structural foundation.',
    '# Project Genesis: The Technology Blueprint

Financial Planner runs on **Angular 18+** (Standalone Components, Signals, OnPush) and **ASP.NET Core (.NET 10)** with Clean Architecture and PostgreSQL.
',
    TRUE, '2025-09-25 00:00:00+00', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE
)
ON CONFLICT ("Slug") DO UPDATE SET
    "Title" = EXCLUDED."Title",
    "Excerpt" = EXCLUDED."Excerpt",
    "ContentMarkdown" = EXCLUDED."ContentMarkdown",
    "IsPublished" = EXCLUDED."IsPublished",
    "PublishedAt" = EXCLUDED."PublishedAt",
    "UpdatedAt" = CURRENT_TIMESTAMP;
