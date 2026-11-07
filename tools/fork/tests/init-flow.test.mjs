// The full init (init.ps1 without -Minimal): prompt parsing, the interactive questions, the step plan (skip
// detection, blocked steps, -WhatIf), and an end-to-end run of init.ps1 -ConfigFile on a synthetic starter fork:
// -WhatIf changes nothing, the run makes the rename / modules / manifest commits, and a rerun skips every step.
// Run: node --test tools/fork/tests/init-flow.test.mjs (the end-to-end part spawns Windows PowerShell 5.1).
import assert from 'node:assert/strict';
import { existsSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { after, describe, it } from 'node:test';
import { UsageError } from '../lib/args.mjs';
import { reinstallNpm } from '../lib/init.mjs';
import {
  MAX_ATTEMPTS,
  parseColor,
  parseConnectionString,
  parseLoginModes,
  parseLogoPath,
  parseName,
  parsePrefix,
  parseYesNo,
  promptInitConfig,
} from '../lib/init-prompts.mjs';
import {
  formatPlannedStep,
  InitStep,
  plannedRenameMap,
  planInit,
  renamedSolutionToken,
  StepAction,
} from '../lib/init-steps.mjs';
import { makeTempDir, runScript } from './support/powershell.mjs';
import { cloneFork, commitAll, createFeed, createSyntheticStarter, git, read, write } from './support/synthetic-starter.mjs';

const EXIT = Object.freeze({ ok: 0, failed: 1, usage: 2 });
const ALL_KEPT = Object.freeze({ attachments: true, portal: true, sms: true, ops: true });
const ARABIC_NAME = 'بوابة أكمي';
const LOGO_SVG = '<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1"/>\n';

const CONFIG = () => ({
  prefix: 'ACME',
  nameEn: 'Acme Portal',
  nameAr: ARABIC_NAME,
  primaryColor: '#0a5c8f',
  logoPath: null,
  loginModes: ['basic', 'sso'],
  optionalModules: { ...ALL_KEPT },
  database: { connectionString: null, skip: true },
});

const OPTIONS = () => ({ skipSecrets: false, skipDatabase: false });

/** The facts of a fresh starter clone (nothing done yet). */
const FRESH = () => ({
  starterBase: null,
  recordedPrefix: 'STARTER',
  renameMap: [{ from: 'Starter', to: 'Acme' }],
  unticked: [],
  pendingRemovals: [],
  removalProblem: null,
  npmMissing: false,
  packagesPresent: true,
  feedProblem: null,
  secretsScript: true,
  secretsExist: false,
  databaseScript: true,
  apiSettingsFile: 'backend/src/Acme.Api/appsettings.json',
  recordedLoginModes: ['basic', 'ad', 'sso'],
  recordedOptionalModules: { ...ALL_KEPT },
  hashesRecorded: false,
});

/** The facts of a completed init with CONFIG(). */
const DONE = () => ({
  ...FRESH(),
  starterBase: 'a'.repeat(40),
  recordedPrefix: 'ACME',
  packagesPresent: false,
  secretsExist: true,
  recordedLoginModes: ['basic', 'sso'],
  hashesRecorded: true,
});

const actionOf = (plan, step) => plan.find((entry) => entry.step === step).action;

describe('prompt answer parsing', () => {
  it('parses yes/no with a default', () => {
    assert.deepEqual(parseYesNo('', true), { value: true });
    assert.deepEqual(parseYesNo(' YES ', false), { value: true });
    assert.deepEqual(parseYesNo('n', true), { value: false });
    assert.ok('error' in parseYesNo('maybe', true));
  });

  it('upper-cases and validates the prefix', () => {
    assert.deepEqual(parsePrefix(' acme1 '), { value: 'ACME1' });
    assert.ok('error' in parsePrefix('1ACME'));
    assert.ok('error' in parsePrefix('A'));
  });

  it('trims names and refuses blank ones', () => {
    assert.deepEqual(parseName('nameAr', `  ${ARABIC_NAME} `), { value: ARABIC_NAME });
    assert.ok('error' in parseName('nameEn', '  '));
  });

  it('accepts a colour with or without # and lower-cases it', () => {
    assert.deepEqual(parseColor('0A5C8F'), { value: '#0a5c8f' });
    assert.deepEqual(parseColor('#1b8354'), { value: '#1b8354' });
    assert.ok('error' in parseColor('#12345'));
    assert.ok('error' in parseColor('green'));
  });

  it('parses login modes by name or number, in mode order; empty is basic', () => {
    assert.deepEqual(parseLoginModes(''), { value: ['basic'] });
    assert.deepEqual(parseLoginModes('sso, basic'), { value: ['basic', 'sso'] });
    assert.deepEqual(parseLoginModes('3 2'), { value: ['ad', 'sso'] });
    assert.deepEqual(parseLoginModes('AD'), { value: ['ad'] });
    assert.ok('error' in parseLoginModes('4'));
    assert.ok('error' in parseLoginModes('basic basic'));
    assert.ok('error' in parseLoginModes('kerberos'));
  });

  it('treats an empty logo path as no logo and refuses a missing file', () => {
    assert.deepEqual(parseLogoPath('  ', (file) => file), { value: null });
    assert.ok('error' in parseLogoPath('missing.svg', (file) => join(makeTempDir().path, file)));
  });

  it('treats an empty connection string as skip', () => {
    assert.deepEqual(parseConnectionString(' '), { value: null });
    assert.deepEqual(parseConnectionString(' Server=x '), { value: 'Server=x' });
  });
});

/** A scripted `ask` and the questions it was asked. */
function scripted(answers) {
  const queue = [...answers];
  const questions = [];
  return { questions, ask: async (question) => { questions.push(question); return queue.length > 0 ? queue.shift() : null; } };
}

const quietIo = () => {
  const lines = [];
  return { lines, io: { log: (line) => lines.push(line) } };
};

describe('promptInitConfig', () => {
  it('asks every field and returns the same shape as a config file, re-asking after an invalid answer', async () => {
    const answers = ['Acme Portal', ARABIC_NAME, 'acme', '', 'not-a-colour', '0a5c8f', 'basic,sso', '', 'n', 'no', 'y', 'Server=(localdb)\\P2Proof'];
    const script = scripted(answers);
    const output = quietIo();
    const config = await promptInitConfig(script.ask, output.io, { resolve: (file) => file, skipDatabase: false });
    assert.deepEqual(config, {
      prefix: 'ACME',
      nameEn: 'Acme Portal',
      nameAr: ARABIC_NAME,
      primaryColor: '#0a5c8f',
      logoPath: null,
      loginModes: ['basic', 'sso'],
      optionalModules: { attachments: true, portal: false, sms: false, ops: true },
      database: { connectionString: 'Server=(localdb)\\P2Proof', skip: false },
    });
    assert.equal(script.questions.length, answers.length);
    assert.ok(output.lines.some((line) => /hex colour/.test(line)));
  });

  it('does not ask for the database with skipDatabase', async () => {
    const script = scripted(['Acme', ARABIC_NAME, 'ACME', '', '#0a5c8f', '', '', '', '', '']);
    const config = await promptInitConfig(script.ask, quietIo().io, { resolve: (file) => file, skipDatabase: true });
    assert.deepEqual(config.database, { connectionString: null, skip: true });
    assert.deepEqual(config.loginModes, ['basic']);
    assert.deepEqual(config.optionalModules, ALL_KEPT);
  });

  it('gives up when input closes or after too many invalid answers', async () => {
    await assert.rejects(promptInitConfig(scripted(['Acme']).ask, quietIo().io, { resolve: (file) => file, skipDatabase: true }), UsageError);
    const invalid = scripted(Array(MAX_ATTEMPTS).fill(' '));
    await assert.rejects(promptInitConfig(invalid.ask, quietIo().io, { resolve: (file) => file, skipDatabase: true }), /No valid answer/);
  });
});

describe('planInit', () => {
  it('runs every step on a fresh clone', () => {
    const config = { ...CONFIG(), database: { connectionString: 'Server=x', skip: false } };
    const plan = planInit(FRESH(), config, OPTIONS());
    assert.deepEqual(plan.map((entry) => entry.step), [InitStep.rename, InitStep.modules, InitStep.keys, InitStep.database, InitStep.manifest]);
    assert.ok(plan.every((entry) => entry.action === StepAction.run), JSON.stringify(plan));
    assert.match(plan[0].detail, /Starter -> Acme/);
    assert.match(plan[0].detail, /chore\(init\): rename Starter to Acme/);
    assert.match(plan[1].detail, /remove packages\/ and pin the feed versions/);
    assert.match(plan[4].detail, /Auth:Modes = basic,sso in backend\/src\/Acme.Api\/appsettings.json/);
    assert.ok(!plan[3].detail.includes('Server=x'), 'the connection string is never printed');
  });

  it('skips every step of a completed init (rerun)', () => {
    const plan = planInit(DONE(), CONFIG(), OPTIONS());
    assert.ok(plan.every((entry) => entry.action === StepAction.skip), JSON.stringify(plan));
    assert.match(plan[0].detail, /already done: starterBase a{40}\.$/);
  });

  it('notes a different prefix on rerun instead of refusing', () => {
    const plan = planInit(DONE(), { ...CONFIG(), prefix: 'OTHER' }, OPTIONS());
    assert.equal(actionOf(plan, InitStep.rename), StepAction.skip);
    assert.match(plan[0].detail, /keeps prefix ACME/);
  });

  it('continues a partial init: renamed, modules still pending', () => {
    const facts = { ...DONE(), packagesPresent: true, unticked: ['sms'], pendingRemovals: ['Sms'], hashesRecorded: false };
    const plan = planInit(facts, { ...CONFIG(), optionalModules: { ...ALL_KEPT, sms: false } }, OPTIONS());
    assert.equal(actionOf(plan, InitStep.rename), StepAction.skip);
    assert.equal(actionOf(plan, InitStep.modules), StepAction.run);
    assert.match(plan[1].detail, /remove module\(s\) Sms/);
    assert.equal(actionOf(plan, InitStep.manifest), StepAction.run);
  });

  it('runs only the module removal when packages/ is already gone', () => {
    const facts = { ...DONE(), unticked: ['ops'], pendingRemovals: ['Ops'] };
    const plan = planInit(facts, CONFIG(), OPTIONS());
    assert.equal(actionOf(plan, InitStep.modules), StepAction.run);
    assert.ok(!plan[1].detail.includes('packages/'));
  });

  it('reruns the manifest step when the login modes or modules change', () => {
    assert.equal(actionOf(planInit(DONE(), { ...CONFIG(), loginModes: ['sso'] }, OPTIONS()), InitStep.manifest), StepAction.run);
    const plan = planInit(DONE(), { ...CONFIG(), optionalModules: { ...ALL_KEPT, portal: false } }, OPTIONS());
    assert.equal(actionOf(plan, InitStep.manifest), StepAction.run);
    assert.ok(!plan[4].detail.includes('hashes'), 'recorded hashes are kept');
  });

  it('skips keys and database on request, without a script or a connection string', () => {
    const config = { ...CONFIG(), database: { connectionString: 'Server=x', skip: false } };
    assert.match(planInit(FRESH(), config, { skipSecrets: true, skipDatabase: true })[2].detail, /-SkipSecrets/);
    assert.equal(actionOf(planInit(FRESH(), config, { skipSecrets: false, skipDatabase: true }), InitStep.database), StepAction.skip);
    assert.equal(actionOf(planInit(FRESH(), CONFIG(), OPTIONS()), InitStep.database), StepAction.skip);
    assert.equal(actionOf(planInit({ ...FRESH(), databaseScript: false }, config, OPTIONS()), InitStep.database), StepAction.skip);
    assert.equal(actionOf(planInit({ ...FRESH(), secretsScript: false }, config, OPTIONS()), InitStep.keys), StepAction.skip);
  });

  it('passes the unticked secret-owning services to New-LocalSecrets.ps1', () => {
    const config = { ...CONFIG(), optionalModules: { attachments: false, portal: false, sms: true, ops: false } };
    assert.match(planInit(FRESH(), config, OPTIONS())[2].detail, /-Force -SkipService attachments,ops\.$/);
  });

  it('blocks when a contract module or a feed version is missing', () => {
    assert.equal(actionOf(planInit({ ...FRESH(), renameMap: null }, CONFIG(), OPTIONS()), InitStep.rename), StepAction.blocked);
    const reserved = planInit(FRESH(), { ...CONFIG(), prefix: 'STARTER' }, OPTIONS());
    assert.equal(actionOf(reserved, InitStep.rename), StepAction.blocked);
    assert.match(reserved[0].detail, /reserved/);
    const noRemoval = { ...FRESH(), unticked: ['sms'], pendingRemovals: null };
    assert.equal(actionOf(planInit(noRemoval, CONFIG(), OPTIONS()), InitStep.modules), StepAction.blocked);
    const refused = { ...FRESH(), unticked: ['portal'], removalProblem: 'Optional module removal refused.' };
    assert.equal(actionOf(planInit(refused, CONFIG(), OPTIONS()), InitStep.modules), StepAction.blocked);
    const noFeed = { ...FRESH(), feedProblem: 'The feed lacks Codelab.Security 0.5.1.' };
    assert.equal(actionOf(planInit(noFeed, CONFIG(), OPTIONS()), InitStep.modules), StepAction.blocked);
  });

  it('blocks the rename and the module removal until npm ci has run, and only those steps', () => {
    const plan = planInit({ ...FRESH(), npmMissing: true }, CONFIG(), OPTIONS());
    assert.equal(actionOf(plan, InitStep.rename), StepAction.blocked);
    assert.match(plan[0].detail, /run npm ci first/);
    assert.equal(actionOf(plan, InitStep.modules), StepAction.blocked);
    assert.match(plan[1].detail, /run npm ci first/);
    assert.equal(actionOf(plan, InitStep.manifest), StepAction.run);
    const renamedOnly = { ...DONE(), npmMissing: true, packagesPresent: true };
    assert.equal(actionOf(planInit(renamedOnly, CONFIG(), OPTIONS()), InitStep.rename), StepAction.skip);
    assert.equal(actionOf(planInit(renamedOnly, CONFIG(), OPTIONS()), InitStep.modules), StepAction.blocked);
    assert.equal(actionOf(planInit({ ...DONE(), npmMissing: true }, CONFIG(), OPTIONS()), InitStep.modules), StepAction.skip);
  });

  it('formats plan lines', () => {
    assert.equal(formatPlannedStep({ step: InitStep.keys, action: StepAction.skip, detail: 'skipped.' }), '[skip] 3 keys: skipped.');
    assert.equal(formatPlannedStep({ step: InitStep.rename, action: StepAction.blocked, detail: 'x' }), '[BLOCKED] 1 rename: x');
  });
});

describe('reinstallNpm', () => {
  const logs = () => {
    const lines = [];
    return { lines, io: { log: (line) => lines.push(line) } };
  };

  it('does nothing with -SkipNpmLock or without a package-lock.json', () => {
    const skipped = logs();
    reinstallNpm({ root: '.', skipNpmLock: true }, skipped.io);
    assert.deepEqual(skipped.lines, ['Skipped the npm reinstall (-SkipNpmLock).']);
    const temp = makeTempDir();
    try {
      const noLock = logs();
      reinstallNpm({ root: temp.path, skipNpmLock: false }, noLock.io);
      assert.deepEqual(noLock.lines, ['No package-lock.json; nothing to reinstall.']);
    } finally {
      temp.cleanup();
    }
  });
});

describe('rename helpers', () => {
  it('names the renamed solution token, else the prefix', () => {
    assert.equal(renamedSolutionToken([{ from: 'Starter', to: 'Acme' }], 'ACME'), 'Acme');
    assert.equal(renamedSolutionToken([], 'ACME'), 'ACME');
  });

  it('adds the protected manifest names to the default map', () => {
    const map = plannedRenameMap(({ prefix }) => [{ from: 'Starter', to: prefix }], CONFIG());
    assert.deepEqual(map[0], { from: 'Starter', to: 'ACME' });
    assert.ok(map.some((entry) => entry.from === 'codelab.starter' && entry.to === 'codelab.starter'));
  });
});

/** A stand-in for tools/fork/lib/rename-defaults.mjs (task init-rename), so this test does not depend on its map. */
const RENAME_DEFAULTS_STUB = `export function defaultRenameMap() {
  return [{ from: 'Starter', to: 'Acme' }];
}
`;

describe('init.ps1 -ConfigFile on a synthetic starter fork', () => {
  const temp = makeTempDir();
  after(() => temp.cleanup());
  const feed = createFeed(join(temp.path, 'feed-source'));
  const starter = join(temp.path, 'starter');
  createSyntheticStarter(starter);
  write(starter, 'tools/fork/lib/rename-defaults.mjs', RENAME_DEFAULTS_STUB);
  commitAll(starter, 'stub rename defaults');
  const fork = cloneFork(starter, join(temp.path, 'fork'));
  write(temp.path, 'config/logo.svg', LOGO_SVG);
  const configFile = write(temp.path, 'config/init.json', `${JSON.stringify({ ...CONFIG(), logoPath: 'logo.svg' }, null, 2)}\n`);
  const init = (extra = []) => runScript(join(fork, 'init.ps1'),
    ['-ConfigFile', configFile, '-FeedDir', feed, '-SkipSecrets', '-SkipNpmLock', ...extra], { cwd: fork });
  const head = () => git(fork, ['rev-parse', 'HEAD']);

  it('-WhatIf prints the plan and changes nothing', () => {
    const before = head();
    const result = init(['-WhatIf']);
    assert.equal(result.code, EXIT.ok, result.output);
    assert.match(result.output, /\[run\] 1 rename: rename Starter -> Acme/);
    assert.match(result.output, /\[skip\] 3 keys: skipped \(-SkipSecrets\)/);
    assert.match(result.output, /WhatIf: nothing was changed/);
    assert.equal(head(), before);
    assert.equal(git(fork, ['status', '--porcelain']), '');
  });

  it('runs the steps in order with one commit each', () => {
    const before = head();
    const result = init();
    assert.equal(result.code, EXIT.ok, result.output);
    const subjects = git(fork, ['log', '--format=%s', `${before}..HEAD`]).split('\n').reverse();
    assert.deepEqual(subjects, [
      'chore(init): rename Starter to Acme',
      'chore(init): remove modules and packages',
      'chore(init): record login modes and the fork manifest',
    ]);
    assert.equal(git(fork, ['status', '--porcelain']), '');
    assert.ok(existsSync(join(fork, 'backend/src/Acme.Api/Acme.Api.csproj')));
    assert.ok(!existsSync(join(fork, 'packages')));
    assert.deepEqual(JSON.parse(read(fork, 'backend/src/Acme.Api/appsettings.json')).Auth, { Modes: ['basic', 'sso'] });
    const web = JSON.parse(read(fork, 'frontend/acme-web/public/config.json'));
    assert.deepEqual(web.branding, { primaryColor: '#0a5c8f', logoSrc: 'branding/logo.svg', appName: { ar: ARABIC_NAME, en: 'Acme Portal' } });
    assert.equal(web.security.protocolPrefix, 'ACME');
    assert.equal(read(fork, 'frontend/acme-web/public/branding/logo.svg'), LOGO_SVG);
    const manifest = JSON.parse(read(fork, 'codelab.starter.json'));
    assert.equal(manifest.starterBase, before);
    assert.equal(manifest.productPrefix, 'ACME');
    assert.deepEqual(manifest.productName, { en: 'Acme Portal', ar: ARABIC_NAME });
    assert.deepEqual(manifest.loginModes, ['basic', 'sso']);
    assert.deepEqual(manifest.optionalModules, ALL_KEPT);
    assert.ok(manifest.renameMap.some((entry) => entry.from === 'Starter' && entry.to === 'Acme'));
    assert.ok(Object.keys(manifest.hashes).length > 0);
    assert.equal(manifest.starterVersion, '1.0.0');
  });

  it('a rerun skips every step with a notice and commits nothing', () => {
    const before = head();
    const result = init();
    assert.equal(result.code, EXIT.ok, result.output);
    assert.match(result.output, /\[skip\] 1 rename: already done/);
    assert.match(result.output, /\[skip\] 2 modules: already done/);
    assert.match(result.output, /\[skip\] 5 manifest: already done/);
    assert.equal(head(), before);
  });

  it('refuses an invalid config with exit 1 and a missing config file', () => {
    const bad = write(temp.path, 'config/bad.json', `${JSON.stringify({ ...CONFIG(), primaryColor: 'blue' })}\n`);
    const result = runScript(join(fork, 'init.ps1'), ['-ConfigFile', bad], { cwd: fork });
    assert.equal(result.code, EXIT.failed, result.output);
    assert.match(result.output, /primaryColor/);
    const missing = runScript(join(fork, 'init.ps1'), ['-ConfigFile', join(temp.path, 'none.json')], { cwd: fork });
    assert.equal(missing.code, EXIT.failed, missing.output);
  });

  it('refuses -Minimal options with -ConfigFile and -ConfigFile with -Minimal (usage)', () => {
    const mixed = runScript(join(fork, 'init.ps1'), ['-ConfigFile', configFile, '-ProductPrefix', 'ACME'], { cwd: fork });
    assert.equal(mixed.code, EXIT.usage, mixed.output);
    const minimal = runScript(join(fork, 'init.ps1'), ['-Minimal', '-ConfigFile', configFile], { cwd: fork });
    assert.equal(minimal.code, EXIT.usage, minimal.output);
  });

  it('an interactive run with closed input is a usage error', () => {
    writeFileSync(join(temp.path, 'empty.txt'), '');
    const result = runScript(join(fork, 'init.ps1'), ['-WhatIf'], { cwd: fork });
    assert.equal(result.code, EXIT.usage, result.output);
    assert.match(result.output, /Input closed/);
  });
});
