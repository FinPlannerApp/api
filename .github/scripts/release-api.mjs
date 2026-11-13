#!/usr/bin/env node
/**
 * Automatic backend version number. Runs in the backend pipeline on every push to main, after the
 * tests and BEFORE migrations and deploy, so the image Render builds carries the new number.
 *
 *   node .github/scripts/release-api.mjs prepare   bump <VersionPrefix> in API.csproj, record it in
 *                                                  .github/release.json, push the bot commit
 *   node .github/scripts/release-api.mjs dry       print the next version, change nothing
 *
 * Version bump:  breaking change (type! or BREAKING CHANGE)  -> major
 *                any feat                                    -> minor
 *                anything else                               -> patch (a push always gets a number)
 *
 * .github/release.json holds the last released version and the commit it was computed at. The next
 * version is always computed from that pair, so re-running (after a failed pipeline, say) gives the
 * same number instead of skipping one.
 *
 * Commits that only add a release-notes migration (db/migrations/V*__release_notes_*.sql) do not
 * count: writing the notes for a release must not cause another release.
 *
 * What users read is NOT generated here: it is written by hand as a Flyway migration (see
 * db/migrations/V44__release_notes_app_6_1_0.sql for the pattern). This script only reminds you,
 * with a warning, when a server release has no notes migration.
 */
import { execFileSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const CSPROJ = 'src/Presentation/API.csproj';
const STATE = '.github/release.json';
const BOT_AUTHORS = new Set(['github-actions[bot]']);
const NOTES_ONLY = /^db\/migrations\/V\d+__release_notes_[^/]+\.sql$/;

// ── Pure helpers (unit-tested in release-api.test.mjs) ────────────────────────────

export function parseCommit({ subject, body = '' }) {
  const m = /^(\w+)(?:\(([^)]*)\))?(!)?:\s*(.+)$/.exec(subject.trim());
  return {
    type: m ? m[1].toLowerCase() : 'other',
    breaking: Boolean(m?.[3]) || /^BREAKING[ -]CHANGE:/m.test(body),
  };
}

export function bumpVersion(current, commits) {
  const [major, minor, patch] = current.split('.').map(Number);
  const parsed = commits.map(parseCommit);
  if (parsed.some((c) => c.breaking)) return `${major + 1}.0.0`;
  if (parsed.some((c) => c.type === 'feat')) return `${major}.${minor + 1}.0`;
  return `${major}.${minor}.${patch + 1}`;
}

export function readVersion(csprojXml) {
  return /<VersionPrefix>\s*([^<\s]+)\s*<\/VersionPrefix>/.exec(csprojXml)?.[1];
}

export function writeVersion(csprojXml, version) {
  return csprojXml.replace(/(<VersionPrefix>)\s*[^<]*(<\/VersionPrefix>)/, `$1${version}$2`);
}

/** True when every changed file is a release-notes migration (so the commit is not a release trigger). */
export function isNotesOnly(files) {
  return files.length > 0 && files.every((f) => NOTES_ONLY.test(f));
}

// ── git ──────────────────────────────────────────────────────────────────────────

const git = (...args) => execFileSync('git', args, { cwd: ROOT, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }).trim();

function commitsSince(sha) {
  const raw = git('log', '--reverse', '--no-merges', '--format=%H%x1f%an%x1f%s%x1f%b%x1e', `${sha}..HEAD`);
  return raw
    .split('\x1e')
    .map((r) => r.trim())
    .filter(Boolean)
    .map((r) => {
      const [sha, author, subject, body] = r.split('\x1f');
      return { sha, author, subject, body: body ?? '' };
    })
    .filter((c) => !BOT_AUTHORS.has(c.author) && !/^chore\(release\)/i.test(c.subject))
    .filter((c) => !isNotesOnly(git('show', '--name-only', '--format=', c.sha).split('\n').filter(Boolean)));
}

function plan() {
  const state = JSON.parse(readFileSync(resolve(ROOT, STATE), 'utf8'));
  const head = git('rev-parse', 'HEAD');
  const commits = commitsSince(state.commit);
  if (commits.length === 0) return { state, head, commits, next: undefined };
  return { state, head, commits, next: bumpVersion(state.version, commits) };
}

function warnIfNoNotes(version) {
  const dir = resolve(ROOT, 'db/migrations');
  const wanted = `release_notes_server_${version.replaceAll('.', '_')}`;
  const found = existsSync(dir) && readdirSync(dir).some((f) => f.includes(wanted));
  if (!found) {
    console.log(`::notice::Server v${version} has no release notes (db/migrations/V<n>__${wanted}.sql). That is fine for internal changes; users will not be told about this release.`);
  }
}

function pushWithRetry() {
  for (let attempt = 1; attempt <= 5; attempt++) {
    try {
      git('pull', '--rebase', 'origin', 'main');
      git('push', 'origin', 'HEAD:main');
      return;
    } catch (e) {
      console.log(`push failed (${attempt}/5): ${e.message.split('\n')[0]}`);
      if (attempt === 5) throw e;
      execFileSync('sleep', [String(attempt * 3)]);
    }
  }
}

// ── modes ────────────────────────────────────────────────────────────────────────

function prepare(dryRun) {
  const { state, head, commits, next } = plan();
  if (!next) {
    console.log(`No new backend commits since v${state.version} (${state.commit.slice(0, 7)}). Nothing to release.`);
    return;
  }
  console.log(`Server v${state.version} -> v${next} (${commits.length} commit(s))`);
  for (const c of commits) console.log(`  ${c.sha.slice(0, 7)} ${c.subject}`);
  warnIfNoNotes(next);
  if (dryRun) return;

  const csprojPath = resolve(ROOT, CSPROJ);
  writeFileSync(csprojPath, writeVersion(readFileSync(csprojPath, 'utf8'), next));
  writeFileSync(resolve(ROOT, STATE), JSON.stringify({ version: next, commit: head }, null, 2) + '\n');

  git('config', 'user.name', 'github-actions[bot]');
  git('config', 'user.email', '41898282+github-actions[bot]@users.noreply.github.com');
  git('add', CSPROJ, STATE);
  git('commit', '-m', `chore(release): api v${next} [skip ci]`);
  pushWithRetry();
  console.log(`Released server v${next}.`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const mode = process.argv[2];
  if (mode === 'prepare') prepare(false);
  else if (mode === 'dry') prepare(true);
  else {
    console.error('usage: release-api.mjs prepare | dry');
    process.exit(2);
  }
}
