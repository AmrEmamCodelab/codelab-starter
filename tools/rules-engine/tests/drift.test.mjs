import { test, describe, beforeEach, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { rmSync } from 'node:fs';
import path from 'node:path';
import { compileRepository, findDrift, writeOutputs } from '../lib/engine.mjs';
import { PATHS } from '../lib/constants.mjs';
import { parseArguments } from '../compile.mjs';
import {
  COMPILE_SCRIPT, REPO_ROOT, createWorkspace, removeWorkspace, writeWorkspaceFile, readWorkspaceFile, moduleYaml,
  copyFixtureModule,
} from './helpers/workspace.mjs';

const runCli = (...args) => spawnSync(process.execPath, [COMPILE_SCRIPT, ...args], { encoding: 'utf8' });

describe('writeOutputs / findDrift', () => {
  let root;
  beforeEach(() => {
    root = createWorkspace();
    copyFixtureModule(root, 'src/Modules/Sample');
  });
  afterEach(() => removeWorkspace(root));

  test('before writing every output is missing; after writing nothing drifts; a rewrite writes nothing', () => {
    const { outputs } = compileRepository(root);
    assert.ok(findDrift(root, outputs).every((item) => item.reason === 'missing'));
    assert.equal(writeOutputs(root, outputs).length, outputs.length);
    assert.deepEqual(findDrift(root, outputs), []);
    assert.deepEqual(writeOutputs(root, outputs), []);
  });

  test('a hand-edited generated file is reported as out of date', () => {
    const { outputs } = compileRepository(root);
    writeOutputs(root, outputs);
    writeWorkspaceFile(root, 'src/Modules/Sample/BannedSymbols.generated.txt', 'hand edited\n');
    assert.deepEqual(findDrift(root, outputs), [{ path: 'src/Modules/Sample/BannedSymbols.generated.txt', reason: 'out of date' }]);
  });

  test('a rules change without recompiling is drift in the compiled JSON', () => {
    writeOutputs(root, compileRepository(root).outputs);
    const rulesFile = 'src/Modules/Sample/module.rules.yaml';
    writeWorkspaceFile(root, rulesFile, readWorkspaceFile(root, rulesFile).replace('methodLines: 30', 'methodLines: 20'));
    const drifted = findDrift(root, compileRepository(root).outputs).map((item) => item.path);
    assert.ok(drifted.includes(PATHS.compiled));
    assert.ok(drifted.includes('src/Modules/Sample/CLAUDE.md'));
  });

  test('CRLF line endings from a Windows checkout are not drift', () => {
    const { outputs } = compileRepository(root);
    writeOutputs(root, outputs);
    writeWorkspaceFile(root, PATHS.compiled, outputs[0].content.replace(/\n/g, '\r\n'));
    assert.deepEqual(findDrift(root, outputs), []);
  });

  test('generated files left behind by a removed module are orphans; hand-written ones are not', () => {
    writeOutputs(root, compileRepository(root).outputs);
    writeWorkspaceFile(root, 'docs/CLAUDE.md', '# Hand-written\n');
    rmSync(path.join(root, 'src', 'Modules', 'Sample', 'module.rules.yaml'));
    const orphans = findDrift(root, compileRepository(root).outputs).filter((item) => item.reason.startsWith('orphaned'));
    assert.deepEqual(orphans.map((item) => item.path).sort(), [
      'src/Modules/Sample/.editorconfig',
      'src/Modules/Sample/BannedSymbols.generated.txt',
      'src/Modules/Sample/CLAUDE.md',
      'src/Modules/Sample/CodeMetricsConfig.txt',
    ]);
  });
});

describe('compile.mjs CLI', () => {
  let root;
  beforeEach(() => {
    root = createWorkspace();
    writeWorkspaceFile(root, 'src/Modules/Records/module.rules.yaml', moduleYaml('Records'));
  });
  afterEach(() => removeWorkspace(root));

  test('--check exits 1 and lists drift, compile writes, then --check exits 0', () => {
    const before = runCli('--check', '--root', root);
    assert.equal(before.status, 1);
    assert.match(before.stderr, /rules\/rules\.compiled\.json: missing/);
    const write = runCli('--root', root);
    assert.equal(write.status, 0, write.stderr);
    assert.match(write.stdout, /wrote src\/Modules\/Records\/CLAUDE\.md/);
    const after = runCli('--check', '--root', root);
    assert.equal(after.status, 0, after.stderr);
  });

  test('invalid rules exit 1 with the problem, and nothing is written', () => {
    writeWorkspaceFile(root, 'src/Modules/Records/module.rules.yaml', moduleYaml('Records', 'bogus: true\n'));
    const result = runCli('--root', root);
    assert.equal(result.status, 1);
    assert.match(result.stderr, /src\/Modules\/Records\/module\.rules\.yaml: schema: .*'bogus'/);
    assert.equal(runCli('--check', '--root', root).status, 1);
  });

  test('an unknown argument exits 2 with usage', () => {
    const result = runCli('--frobnicate');
    assert.equal(result.status, 2);
    assert.match(result.stderr, /Usage:/);
  });

  test('a root without rules sources exits 1', () => {
    rmSync(path.join(root, 'rules'), { recursive: true, force: true });
    assert.equal(runCli('--check', '--root', root).status, 1);
  });

  test('parseArguments defaults to the repo root', () => {
    assert.deepEqual(parseArguments([]), { check: false, root: REPO_ROOT, help: false });
    assert.equal(parseArguments(['--check']).check, true);
    assert.throws(() => parseArguments(['--root']));
  });
});

describe('the real repository', () => {
  test('rules compile and the committed generated files are up to date', () => {
    const result = runCli('--check');
    assert.equal(result.status, 0, `${result.stdout}${result.stderr}`);
  });
});
