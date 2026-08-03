-- V3__drop_orphaned_issue_schema_and_duplicate_history.sql
--
-- ═══════════════════════════════════════════════════════════════════════════
-- DO NOT RUN THIS UNTIL:
--   1. V1 and V2 have been applied successfully and verified
--   2. You have taken a Neon branch/snapshot backup (Neon makes this trivial —
--      Neon dashboard → Branches → Create branch from "main" at current time,
--      gives you a full point-in-time restore point in under a minute)
--   3. You've spot-checked that the tables being dropped genuinely have no
--      current application traffic (they shouldn't, per the code audit, but
--      "shouldn't" and "verified" are different levels of confidence for
--      something irreversible)
-- ═══════════════════════════════════════════════════════════════════════════
--
-- WHAT THIS REMOVES, AND WHY IT'S SAFE (per the code audit):
--
-- 1. The entire "issue" schema (17 tables — Badges, CommentReactions,
--    CommentVotes, IssueActivities, IssueAssignees, IssueAttachments,
--    IssueComments, IssueLabelAssignments, IssueLabels, IssueMilestones,
--    IssueRelations, IssueStatusHistories, IssueTaxonomies, IssueVotes,
--    Issues, UserBadges, UserGamificationProfiles).
--    ApplicationDbContext.cs configures every one of these entities with
--    the schema-less ToTable("X") overload, which resolves to "public" —
--    confirmed no HasDefaultSchema() override exists anywhere in the
--    codebase. Nothing in the current code can read or write to "issue.*".
--    The live, actually-used copies are the "public" schema versions.
--
-- 2. identity.__EFMigrationsHistory — a duplicate, unused history table.
--    The live one (confirmed via MigrationsHistoryTable("__EFMigrationsHistory")
--    using the schema-less overload) is public.__EFMigrationsHistory, which
--    is now also retired in favor of flyway_schema_history as of V1/V2.
--
-- Note: rename this file by removing the `.hold` extension to activate it.
--

DROP SCHEMA IF EXISTS "issue" CASCADE;

DROP TABLE IF EXISTS "identity"."__EFMigrationsHistory";

-- Optional, do this last and separately once you're fully confident:
-- public.__EFMigrationsHistory itself is also now retired (Flyway doesn't
-- use it). Left commented out deliberately — it's harmless to leave sitting
-- there as a historical record of the old EF era, and there's no upside to
-- rushing its removal.
-- DROP TABLE IF EXISTS "public"."__EFMigrationsHistory";
