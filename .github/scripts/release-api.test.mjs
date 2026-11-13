import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseCommit, bumpVersion, readVersion, writeVersion, isNotesOnly } from './release-api.mjs';

const c = (subject, body = '') => ({ subject, body });

test('feat is a minor bump, fix a patch bump, breaking a major bump', () => {
  assert.equal(bumpVersion('5.0.1', [c('feat: x')]), '5.1.0');
  assert.equal(bumpVersion('5.0.1', [c('fix: x')]), '5.0.2');
  assert.equal(bumpVersion('5.0.1', [c('feat!: x')]), '6.0.0');
  assert.equal(bumpVersion('5.0.1', [c('refactor: x', 'BREAKING CHANGE: gone')]), '6.0.0');
  assert.equal(bumpVersion('5.0.1', [c('fix: a'), c('feat: b')]), '5.1.0');
});

test('housekeeping-only pushes still get a patch version', () => {
  assert.equal(bumpVersion('5.0.1', [c('chore: deps'), c('docs: readme')]), '5.0.2');
  assert.equal(bumpVersion('5.0.1', [c('no conventional prefix')]), '5.0.2');
});

test('commit type and breaking flag are parsed', () => {
  assert.deepEqual(parseCommit(c('feat(auth)!: x')), { type: 'feat', breaking: true });
  assert.deepEqual(parseCommit(c('Fix: x')), { type: 'fix', breaking: false });
});

test('VersionPrefix is read and replaced in the csproj', () => {
  const xml = '<Project><PropertyGroup>\n\t  <VersionPrefix>5.0.1</VersionPrefix>\n</PropertyGroup></Project>';
  assert.equal(readVersion(xml), '5.0.1');
  assert.equal(readVersion(writeVersion(xml, '5.1.0')), '5.1.0');
  assert.ok(writeVersion(xml, '5.1.0').includes('<Project>'));
});

test('a commit that only adds release notes is not a release trigger', () => {
  assert.equal(isNotesOnly(['db/migrations/V46__release_notes_app_6_3_0.sql']), true);
  assert.equal(isNotesOnly(['db/migrations/V46__release_notes_server_5_2_0.sql', 'db/migrations/V47__release_notes_app_6_3_1.sql']), true);
  assert.equal(isNotesOnly(['db/migrations/V46__release_notes_app_6_3_0.sql', 'src/Presentation/Program.cs']), false);
  assert.equal(isNotesOnly(['db/migrations/V47__add_something.sql']), false);
  assert.equal(isNotesOnly([]), false);
});
