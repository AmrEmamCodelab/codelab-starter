// Run: node --test "tools/facts-drift/tests/**/*.test.mjs"
// End-to-end over fixture trees: the gate's checks, scope, allowlist and the CLI exit codes.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { afterEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { applyAllowlist, checkRepository } from '../lib/checker.mjs';
import { parseAllowlist, parseStaleTerms } from '../lib/config.mjs';
import { DEFAULT_STALE_TERMS_FILE, RuleIds } from '../lib/constants.mjs';
import { baseTree, removeTrees, shippedConfig, writeTree } from './helpers/tree.mjs';

const CHECK_SCRIPT = fileURLToPath(new URL('../check.mjs', import.meta.url));
const staleTerms = parseStaleTerms(readFileSync(DEFAULT_STALE_TERMS_FILE, 'utf8'), DEFAULT_STALE_TERMS_FILE);
// Built at run time so this file never holds the external DGA scope literally (the scrub sweep denies it).
const EXTERNAL_DGA_SCOPE = ['@dev', 'dga/ui'].join('-');

function run(files, { allowlist = '' } = {}) {
  const root = writeTree(baseTree(files));
  return checkRepository(root, { config: shippedConfig(), staleTerms, allowlist: parseAllowlist(allowlist, 'allowlist') }, { includeScrubTerms: false });
}

function summary(result) {
  return result.findings.map((finding) => `${finding.path}:${finding.line} ${finding.ruleId} ${finding.subject}`);
}

afterEach(removeTrees);

describe('checkRepository', () => {
  it('is clean when every reference resolves', () => {
    const doc = [
      'Run `node tools/check.mjs --root .` and `tools\\build.ps1 -Target CI -Pack`, see `tools/build.ps1` and `docs/`.',
      'Use `ApiService`, `UploadOptions.BlockedExtensions`, `[HasPermission]` and `NotInRing`.',
      'Load the `new-module` skill; hand off to agent `commit-gate`.',
      'Run `npm run lint` in `frontend/web` and `npm run api:gen -w @codelab/web`.',
      '',
      'Then, from `frontend/web`:',
      '',
      '```powershell',
      'npm run lint   # lint',
      'node e2e/visual.mjs --langs ar,en',
      'node --test "tools/tests/**/*.test.mjs"',
      '```',
    ].join('\n');
    const result = run({ 'docs/guide.md': doc });
    assert.deepEqual(summary(result), []);
    assert.equal(result.fileCount, 3); // the guide plus the base tree's skill and agent
  });

  it('reports each kind of drift with its rule and subject', () => {
    const doc = [
      'See `tools/gone.mjs` and `docs/**/*.yaml`.',
      'Run `npm run missing-script`, `npm run lint -w @codelab/none`, `node tools/none.mjs` and `tools\\build.ps1 -Bogus`.',
      'Call `GoneService` and `[NoSuchThing]`.',
      'Load the `gone-skill` skill and ask agent `gone-agent`.',
      'Still on Angular 19 with Karma and role-based access control.',
      '```',
      'node --test "tools/none/**/*.test.mjs"',
      '```',
    ].join('\n');
    assert.deepEqual(summary(run({ 'docs/guide.md': doc })).sort(), [
      `docs/guide.md:1 ${RuleIds.pathMissing} tools/gone.mjs`,
      `docs/guide.md:1 ${RuleIds.globEmpty} docs/**/*.yaml`,
      `docs/guide.md:2 ${RuleIds.npmScriptMissing} missing-script`,
      `docs/guide.md:2 ${RuleIds.npmPackageMissing} @codelab/none`,
      `docs/guide.md:2 ${RuleIds.commandFileMissing} tools/none.mjs`,
      `docs/guide.md:2 ${RuleIds.scriptSwitchUnknown} -Bogus`,
      `docs/guide.md:3 ${RuleIds.typeUnresolved} GoneService`,
      `docs/guide.md:3 ${RuleIds.typeUnresolved} NoSuchThing`,
      `docs/guide.md:4 ${RuleIds.skillMissing} gone-skill`,
      `docs/guide.md:4 ${RuleIds.agentMissing} gone-agent`,
      'docs/guide.md:5 stale-angular-old-version Angular 19',
      'docs/guide.md:5 stale-karma Karma',
      'docs/guide.md:5 stale-role-based-auth role-based access control',
      `docs/guide.md:7 ${RuleIds.globEmpty} tools/none/**/*.test.mjs`,
    ].sort());
  });

  it('flags every stale term in the shipped list, inside code blocks too', () => {
    const doc = ['Uses Swashbuckle.', '```text', `npm i ${EXTERNAL_DGA_SCOPE}`, 'role auth', '```'].join('\n');
    const ruleIds = run({ 'CLAUDE.md': doc }).findings.map((finding) => finding.ruleId);
    assert.deepEqual(ruleIds.sort(), ['stale-external-dga-scope', 'stale-role-based-auth', 'stale-swashbuckle']);
  });

  it('keeps to its scope: agents, skills, CLAUDE.md, CONTEXT.md and docs minus adr, evidence and generated', () => {
    const bad = 'See `tools/gone.mjs`.';
    const result = run({
      'CONTEXT.md': bad, '.claude/agents/x.md': bad, '.claude/skills/x/SKILL.md': bad, 'docs/a.md': bad,
      'docs/adr/0001.md': bad, 'docs/evidence/P9/x.md': bad, 'docs/generated/x.md': bad, 'README.md': bad,
      'tools/MODULE.md': bad, 'docs/notes.txt': bad,
    });
    assert.deepEqual(result.findings.map((finding) => finding.path).sort(),
      ['.claude/agents/x.md', '.claude/skills/x/SKILL.md', 'CONTEXT.md', 'docs/a.md']);
  });

  it('resolves a fenced command from a cd earlier in the same block only', () => {
    const doc = ['```', 'cd frontend/web', 'node e2e/visual.mjs', '```', '```', 'node e2e/visual.mjs', '```'].join('\n');
    assert.deepEqual(summary(run({ 'docs/a.md': doc })), [`docs/a.md:6 ${RuleIds.commandFileMissing} e2e/visual.mjs`]);
  });

  it('resolves "npm run <script> --workspace <folder>" in a prose code span of a nested skill', () => {
    const doc = [
      'The command reruns the rules compile and',
      '`npm run api:gen --workspace frontend/web` (`frontend/web/src/app/api.service.ts`). Also `npm run lint -w frontend/web`.',
      'A wrong one: `npm run gone --workspace frontend/web`.',
    ].join('\n');
    assert.deepEqual(summary(run({ '.claude/skills/new-permission/SKILL.md': doc })),
      [`.claude/skills/new-permission/SKILL.md:3 ${RuleIds.npmScriptMissing} gone`]);
  });

  it('ignores placeholders and links (links are the docs gate DOCS003)', () => {
    const doc = 'Write `docs/<slug>.md`, run `node <script>.mjs`, see [gone](gone.md) and `{name}.json`.';
    assert.deepEqual(summary(run({ 'docs/a.md': doc })), []);
  });

  it('suppresses allowlisted findings and reports unused entries', () => {
    const allowlist = 'path-missing | docs/** | tools/gone.mjs | created at runtime\nstale-* | docs/x.md | * | unused entry\n';
    const result = run({ 'docs/a.md': 'See `tools/gone.mjs`.' }, { allowlist });
    assert.deepEqual(result.findings, []);
    assert.equal(result.suppressed.length, 1);
    assert.equal(result.suppressed[0].reason, 'created at runtime');
    assert.deepEqual(result.unusedAllowlist.map((entry) => entry.line), [2]);
  });

  it('applies the allowlist by rule, file and subject', () => {
    const finding = { path: 'docs/a.md', ruleId: 'path-missing', subject: 'x.json' };
    const [entry] = parseAllowlist('path-missing | docs/b.md | x.json | other file\n', 'a');
    assert.deepEqual(applyAllowlist([finding], [entry]).kept, [finding]);
  });

  it('reports source-product terms from the scrub sweep list', () => {
    // The term is assembled at run time so this test file itself stays clean for the scrub sweep.
    const term = ['MO', 'DHS'].join('');
    const root = writeTree(baseTree({ 'docs/a.md': `Harvested from ${term}.` }));
    const result = checkRepository(root, { config: shippedConfig(), staleTerms, allowlist: [] });
    assert.ok(result.findings.some((finding) => finding.ruleId.startsWith('term-')));
  });
});

describe('check.mjs', () => {
  const runCli = (args) => spawnSync(process.execPath, [CHECK_SCRIPT, ...args], { encoding: 'utf8' });

  it('exits 0 on a clean tree and 1 with findings, in text and JSON', () => {
    const clean = writeTree(baseTree({ 'docs/a.md': 'See `tools/build.ps1`.' }));
    assert.equal(runCli(['--root', clean]).status, 0);
    const dirty = writeTree(baseTree({ 'docs/a.md': 'See `tools/gone.ps1`.' }));
    const text = runCli(['--root', dirty]);
    assert.equal(text.status, 1);
    assert.match(text.stdout, /docs\/a\.md:1:5 {2}path-missing/);
    const json = runCli(['--root', dirty, '--json']);
    assert.equal(json.status, 1);
    assert.deepEqual(JSON.parse(json.stdout).findings.map((finding) => finding.subject), ['tools/gone.ps1']);
  });

  it('exits 2 on usage errors', () => {
    assert.equal(runCli(['--nope']).status, 2);
    assert.equal(runCli(['--root', join(writeTree({}), 'missing')]).status, 2);
    assert.equal(runCli(['--json', 'x']).status, 2);
    assert.equal(runCli(['--root', writeTree({}), '--allowlist', 'no-such-file.txt']).status, 2);
  });
});
