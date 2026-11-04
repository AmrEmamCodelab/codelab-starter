import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { ESLint } from 'eslint';
import { buildConfig, createLinter, restrictedImportsFor } from '../lint-packages.mjs';

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

const MODULES = [
  { module: 'NgSecurity', kind: 'angular', path: 'packages/npm/ng-security', mustNotDependOn: ['NgTools'], packageName: '@codelab/ng-security' },
  { module: 'NgTools', kind: 'angular', path: 'packages/npm/ng-tools', mustNotDependOn: ['NgSecurity'], packageName: '@codelab/ng-tools' },
];
const BOUNDARIES = { noMagicNumbers: { ignore: [-1, 0, 1] }, modules: MODULES };

function linter() {
  return new ESLint({ cwd: REPO_ROOT, overrideConfigFile: true, overrideConfig: buildConfig(BOUNDARIES, MODULES) });
}

async function ruleIds(code, filePath) {
  const [result] = await linter().lintText(code, { filePath: path.join(REPO_ROOT, filePath) });
  return result.messages.map((message) => message.ruleId);
}

describe('package lint', () => {
  test('a denied module is restricted by package name, subpath and relative folder', () => {
    const { patterns } = restrictedImportsFor(MODULES[0], MODULES);
    assert.equal(patterns.length, 1);
    assert.deepEqual(patterns[0].group, ['**/ng-tools/**', '**/ng-tools', '@codelab/ng-tools', '@codelab/ng-tools/*']);
  });

  test('a magic number in package source is an error; allowed numbers and named constants pass', async () => {
    assert.deepEqual(await ruleIds('export const wait = (n: number) => n * 30;\n', 'packages/npm/ng-security/src/a.ts'), ['no-magic-numbers']);
    assert.deepEqual(await ruleIds('const LIMIT = 30;\nexport const wait = (n: number) => n * LIMIT + 1;\n', 'packages/npm/ng-security/src/a.ts'), []);
  });

  test('tests may use bare numbers', async () => {
    assert.deepEqual(await ruleIds('export const x = 30 * 2;\n', 'packages/npm/ng-security/src/a.spec.ts'), []);
  });

  test('importing a mustNotDependOn package is an error, by name or by relative path', async () => {
    assert.deepEqual(await ruleIds("import { x } from '@codelab/ng-tools/eslint';\nexport { x };\n", 'packages/npm/ng-security/src/a.ts'), ['no-restricted-imports']);
    assert.deepEqual(await ruleIds("import { x } from '../../ng-security/src/core';\nexport { x };\n", 'packages/npm/ng-tools/src/a.mjs'), ['no-restricted-imports']);
    assert.deepEqual(await ruleIds("import { x } from './core';\nexport { x };\n", 'packages/npm/ng-security/src/a.ts'), []);
  });

  test('the repository packages are clean', async () => {
    const results = await createLinter(REPO_ROOT).lintFiles(['packages/npm/*/src/**/*.{ts,mts,mjs,js}', 'packages/npm/*/scripts/**/*.{mjs,js}']);
    const errors = results.flatMap((result) => result.messages.map((message) => `${result.filePath}:${message.line} ${message.ruleId}`));
    assert.ok(results.length > 0);
    assert.deepEqual(errors, []);
  });
});
