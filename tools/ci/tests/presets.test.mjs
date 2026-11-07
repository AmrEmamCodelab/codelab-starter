// Run: node --test tools/ci/tests/presets.test.mjs
// Contract: the CI preset matrix (phase 10): the presets follow the init -ConfigFile contract, a leg runs init, the
// fork checks and the full CI in a fresh fork, and the workflow only calls repo scripts (ADR-007).
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  BUILD_SCRIPT,
  CI_GIT_IDENTITY,
  EXIT_USAGE,
  FIXTURE_KEYS_SCRIPT,
  FORK_CHECKS_SCRIPT,
  FORK_FOLDER,
  INIT_SCRIPT,
  OPTIONAL_MODULE_IDS,
  StepId,
  StepKind,
  guardWorkDir,
  legEnvironment,
  listPresets,
  main,
  parseArguments,
  planLeg,
  readPreset,
  runLeg,
  validatePresetConfig,
} from '../presets/preset-leg.mjs';

const REPO_ROOT = fileURLToPath(new URL('../../..', import.meta.url));
const WORKFLOW = readFileSync(path.join(REPO_ROOT, '.github', 'workflows', 'presets.yml'), 'utf8');
const EXPECTED_PRESETS = ['all', 'core-only', 'sso-only'];
const SOURCE = path.resolve(path.sep, 'src', 'starter');
const WORK = path.resolve(path.sep, 'tmp', 'leg');

function preset(name) {
  const result = readPreset(name);
  assert.equal(result.error, undefined, result.error);
  return result.config;
}

function validConfig() {
  return structuredClone(preset('all'));
}

describe('the preset files', () => {
  it('are exactly all, core-only and sso-only, each valid for init -ConfigFile', () => {
    assert.deepEqual(listPresets(), EXPECTED_PRESETS);
    for (const name of EXPECTED_PRESETS) assert.deepEqual(validatePresetConfig(preset(name)), [], name);
  });

  it('all keeps every optional module and every login mode', () => {
    const config = preset('all');
    assert.ok(OPTIONAL_MODULE_IDS.every((id) => config.optionalModules[id] === true));
    assert.deepEqual([...config.loginModes].sort(), ['ad', 'basic', 'sso']);
  });

  it('core-only removes attachments, portal, sms and ops', () => {
    const config = preset('core-only');
    assert.ok(OPTIONAL_MODULE_IDS.every((id) => config.optionalModules[id] === false));
  });

  it('sso-only signs in with SSO only', () => {
    assert.deepEqual(preset('sso-only').loginModes, ['sso']);
  });

  it('use distinct prefixes and skip the database (CI has no init database)', () => {
    const configs = EXPECTED_PRESETS.map(preset);
    assert.equal(new Set(configs.map((config) => config.prefix)).size, EXPECTED_PRESETS.length);
    assert.ok(configs.every((config) => config.database.skip === true && config.database.connectionString === null));
  });
});

describe('validatePresetConfig', () => {
  const cases = [
    ['a lower-case prefix', (config) => { config.prefix = 'acme'; }, /prefix/],
    ['a one-letter prefix', (config) => { config.prefix = 'A'; }, /prefix/],
    ['a reserved prefix', (config) => { config.prefix = 'STARTER'; }, /reserved/],
    ['an empty Arabic name', (config) => { config.nameAr = ' '; }, /nameAr/],
    ['a short colour', (config) => { config.primaryColor = '#fff'; }, /primaryColor/],
    ['no login mode', (config) => { config.loginModes = []; }, /loginModes/],
    ['an unknown login mode', (config) => { config.loginModes = ['otp']; }, /loginModes/],
    ['a repeated login mode', (config) => { config.loginModes = ['sso', 'sso']; }, /loginModes/],
    ['a missing optional module', (config) => { delete config.optionalModules.ops; }, /optionalModules/],
    ['a non-boolean module flag', (config) => { config.optionalModules.sms = 'yes'; }, /optionalModules/],
    ['an unknown module', (config) => { config.optionalModules.records = true; }, /optionalModules/],
    ['a database without skip', (config) => { config.database = { connectionString: null }; }, /database/],
    ['an empty logo path', (config) => { config.logoPath = ''; }, /logoPath/],
  ];
  for (const [label, mutate, expected] of cases) {
    it(`refuses ${label}`, () => {
      const config = validConfig();
      mutate(config);
      const errors = validatePresetConfig(config);
      assert.ok(errors.some((error) => expected.test(error)), `${label}: ${errors.join('; ')}`);
    });
  }

  it('refuses a non-object', () => {
    assert.equal(validatePresetConfig([]).length, 1);
    assert.equal(validatePresetConfig(null).length, 1);
  });
});

describe('readPreset', () => {
  it('refuses an unknown or path-like name', () => {
    assert.match(readPreset('nope').error, /unknown preset/);
    assert.match(readPreset('../all').error, /unknown preset/);
  });
});

describe('planLeg', () => {
  const config = preset('core-only');
  const steps = planLeg({ preset: 'core-only', presetFile: 'core-only.json', config, sourceRoot: SOURCE, workDir: WORK });
  const fork = path.join(WORK, FORK_FOLDER);
  const step = (id) => steps.find((candidate) => candidate.id === id);

  it('clones, copies the feed, installs, inits, checks, then runs the full CI in the fork', () => {
    assert.deepEqual(steps.map((candidate) => candidate.id),
      [StepId.Clone, StepId.Feed, StepId.NpmCi, StepId.Init, StepId.ForkChecks, StepId.FixtureKeys, StepId.Build]);
    assert.ok(steps.every((candidate) => candidate.preset === 'core-only'));
  });

  it('clones the source into <work>/fork and copies feed/nuget and feed/npm next to it', () => {
    assert.deepEqual(step(StepId.Clone).args.slice(-2), [SOURCE, fork]);
    assert.equal(step(StepId.Feed).kind, StepKind.Copy);
    assert.deepEqual(step(StepId.Feed).copies.map((copy) => copy.to), [path.join(fork, 'feed', 'nuget'), path.join(fork, 'feed', 'npm')]);
  });

  it('runs init.ps1 -ConfigFile <preset> -SkipDatabase inside the fork', () => {
    const init = step(StepId.Init);
    assert.equal(init.cwd, fork);
    assert.ok(init.args.includes(INIT_SCRIPT));
    assert.deepEqual(init.args.slice(init.args.indexOf(INIT_SCRIPT) + 1), ['-ConfigFile', 'core-only.json', '-SkipDatabase']);
  });

  it('omits -SkipDatabase when the preset gives a database', () => {
    const withDatabase = { ...config, database: { connectionString: 'Server=x', skip: false } };
    const init = planLeg({ preset: 'p', presetFile: 'p.json', config: withDatabase, sourceRoot: SOURCE, workDir: WORK }).find((candidate) => candidate.id === StepId.Init);
    assert.ok(!init.args.includes('-SkipDatabase'));
  });

  it('runs the starter (source) copies of the checks against the fork, and build.ps1 -Target CI in the fork', () => {
    assert.deepEqual(step(StepId.ForkChecks).args, [path.join(SOURCE, FORK_CHECKS_SCRIPT), '--root', fork]);
    assert.deepEqual(step(StepId.FixtureKeys).args, [path.join(SOURCE, FIXTURE_KEYS_SCRIPT), fork]);
    const build = step(StepId.Build);
    assert.equal(build.cwd, fork);
    assert.deepEqual(build.args.slice(-3), [BUILD_SCRIPT, '-Target', 'CI']);
  });
});

describe('guardWorkDir', () => {
  it('refuses a work folder inside the source repo or containing it', () => {
    assert.match(guardWorkDir(SOURCE, path.join(SOURCE, 'tmp'), () => false), /overlaps/);
    assert.match(guardWorkDir(SOURCE, path.dirname(SOURCE), () => false), /overlaps/);
  });

  it('refuses an existing fork folder instead of deleting it', () => {
    assert.match(guardWorkDir(SOURCE, WORK, () => true), /already exists/);
  });

  it('accepts a separate, fresh work folder', () => {
    assert.equal(guardWorkDir(SOURCE, WORK, () => false), undefined);
  });
});

describe('runLeg', () => {
  const steps = [{ id: 'a', preset: 'p' }, { id: 'b', preset: 'p' }, { id: 'c', preset: 'p' }];

  it('stops at the first failed step and names it', () => {
    const ran = [];
    const failed = runLeg(steps, { env: {}, log: () => {}, run: (step) => { ran.push(step.id); return step.id !== 'b'; } });
    assert.equal(failed, 'b');
    assert.deepEqual(ran, ['a', 'b']);
  });

  it('returns undefined when every step passes', () => {
    assert.equal(runLeg(steps, { env: {}, log: () => {}, run: () => true }), undefined);
  });
});

describe('legEnvironment', () => {
  it('adds a git identity only where the runner has none', () => {
    const env = legEnvironment({ GIT_AUTHOR_NAME: 'Someone', PATH: 'x' });
    assert.equal(env.GIT_AUTHOR_NAME, 'Someone');
    assert.equal(env.GIT_COMMITTER_EMAIL, CI_GIT_IDENTITY.GIT_COMMITTER_EMAIL);
    assert.equal(env.PATH, 'x');
  });
});

describe('the CLI', () => {
  it('parses plan and run with a preset and an optional work folder', () => {
    assert.deepEqual(parseArguments(['run', '--preset', 'all', '--work', 'w']), { command: 'run', preset: 'all', workDir: 'w' });
    assert.ok(parseArguments(['run']).error);
    assert.ok(parseArguments(['go', '--preset', 'all']).error);
    assert.ok(parseArguments(['plan', '--preset']).error);
    assert.ok(parseArguments(['plan', '--preset', 'all', '--bogus', 'x']).error);
  });

  it('refuses an unknown preset and a work folder inside the repo with exit 2', () => {
    const quiet = { log: () => {}, logError: () => {} };
    assert.equal(main(['plan', '--preset', 'nope'], quiet), EXIT_USAGE);
    assert.equal(main(['plan', '--preset', 'all', '--work', path.join(REPO_ROOT, 'artifacts', 'leg')], quiet), EXIT_USAGE);
  });
});

describe('.github/workflows/presets.yml', () => {
  const matrix = WORKFLOW.match(/preset:\s*\[([^\]]*)\]/);

  it('runs one leg per preset file', () => {
    assert.ok(matrix, 'the workflow declares a preset matrix');
    assert.deepEqual(matrix[1].split(',').map((name) => name.trim()).sort(), listPresets());
  });

  it('uses only first-party actions', () => {
    const uses = [...WORKFLOW.matchAll(/uses:\s*(\S+)/g)].map((match) => match[1]);
    assert.ok(uses.length > 0);
    assert.ok(uses.every((action) => action.startsWith('actions/')), uses.join(', '));
  });

  it('only calls repo scripts and npm ci (ADR-007: no CI logic in the YAML)', () => {
    const runs = [...WORKFLOW.matchAll(/^\s*run:\s*(.+)$/gm)].map((match) => match[1].trim());
    const allowed = [
      /^npm ci\b/,
      /^powershell -NoProfile -ExecutionPolicy Bypass -File tools\/build\.ps1\b/,
      /^node tools\/ci\/presets\/preset-leg\.mjs run --preset \$\{\{ matrix\.preset \}\}/,
      /^node tools\/extract\/scrub-sweep\.mjs --root \.$/,
    ];
    assert.ok(runs.length > 0);
    for (const run of runs) assert.ok(allowed.some((pattern) => pattern.test(run)), `not a repo-script call: ${run}`);
    assert.ok(runs.some((run) => run.includes('preset-leg.mjs run')), 'each leg runs the preset leg');
  });

  it('runs the scrub sweep on release tags', () => {
    assert.match(WORKFLOW, /tags:\s*\['v\*'\]/);
    assert.match(WORKFLOW, /if:\s*startsWith\(github\.ref, 'refs\/tags\/'\)/);
  });
});
