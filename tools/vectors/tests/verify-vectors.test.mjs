import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { join, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { parseArgs, UsageError } from '../../extract/lib/cli-args.mjs';
import { EXIT_USAGE_OR_ERROR } from '../../extract/lib/exit-codes.mjs';
import {
  findVitestEntry, missingEntryPoints, missingVectorFiles, parseVerifyOptions, PINNED_PREFIX, planSteps, PRIVATE_DOTNET,
  RepoPaths, resolveDotnet, Variables, VECTOR_FILES,
} from '../lib/verify-plan.mjs';

const REPO_ROOT = resolve(fileURLToPath(new URL('../../..', import.meta.url)));
const CLI = join(REPO_ROOT, 'tools', 'vectors', 'verify-vectors.mjs');
const options = (...argv) => parseVerifyOptions(parseArgs(argv));
const nothingExists = () => false;
const everythingExists = () => true;
const planFor = (prefix) => planSteps({
  prefix, repoRoot: 'R', outputDirectory: prefix === PINNED_PREFIX ? null : 'OUT', dotnet: 'DOTNET', node: 'NODE', vitestEntry: 'VITEST',
});

test('a valid prefix is accepted, with or without --keep', () => {
  assert.deepEqual(options('--prefix', 'CODELABTEST'), { prefix: 'CODELABTEST', keep: false });
  assert.deepEqual(options('--prefix', 'ALTPFX', '--keep'), { prefix: 'ALTPFX', keep: true });
  assert.deepEqual(options('--prefix', 'ZX9'), { prefix: 'ZX9', keep: false });
});

test('a missing, empty or malformed prefix is a usage error', () => {
  for (const argv of [[], ['--prefix'], ['--prefix', 'Q'], ['--prefix', 'altpfx'], ['--prefix', '9ABC'], ['--prefix', 'A-B'], ['--prefix', 'ABCDEFGHIJKLMNOPQ']]) {
    assert.throws(() => options(...argv), UsageError, `argv ${JSON.stringify(argv)}`);
  }
});

test('unknown options, positional arguments and a valued --keep are usage errors', () => {
  assert.throws(() => options('--prefix', 'ALTPFX', '--force'), /unknown option --force/);
  assert.throws(() => options('ALTPFX'), /unexpected argument/);
  assert.throws(() => options('--prefix', 'ALTPFX', '--keep', 'yes'), /--keep takes no value/);
});

test('dotnet resolves like build.ps1: override, then the private SDK, then PATH', () => {
  assert.equal(resolveDotnet({ [Variables.dotnet]: 'X:\\custom\\dotnet.exe' }, everythingExists), 'X:\\custom\\dotnet.exe');
  assert.equal(resolveDotnet({}, (path) => path === PRIVATE_DOTNET), PRIVATE_DOTNET);
  assert.equal(resolveDotnet({}, nothingExists), 'dotnet');
});

test('the pinned prefix runs both runners against packages/vectors and switches generation off', () => {
  const [csharp, typescript] = planFor(PINNED_PREFIX);
  assert.equal(csharp.command, 'DOTNET');
  assert.deepEqual(csharp.args.slice(-3), ['--filter', 'Category=GoldenVectors', '-nologo']);
  assert.deepEqual(csharp.env, { [Variables.regenerate]: '', [Variables.output]: '', [Variables.generatedPrefix]: '' });
  assert.equal(csharp.expectsFiles, null);
  assert.equal(typescript.command, 'NODE');
  assert.deepEqual(typescript.args, ['VITEST', 'run', 'src/vectors']);
  assert.deepEqual(typescript.env, { [Variables.tsDirectory]: join('R', RepoPaths.vectors), [Variables.tsPrefix]: PINNED_PREFIX });
});

test('another prefix generates a set with C# and verifies that set with TypeScript', () => {
  const [csharp, typescript] = planFor('ALTPFX');
  assert.deepEqual(csharp.env, { [Variables.regenerate]: '', [Variables.output]: 'OUT', [Variables.generatedPrefix]: 'ALTPFX' });
  assert.equal(csharp.expectsFiles, 'OUT');
  assert.deepEqual(typescript.env, { [Variables.tsDirectory]: 'OUT', [Variables.tsPrefix]: 'ALTPFX' });
  assert.equal(typescript.cwd, join('R', RepoPaths.ngSecurity));
});

test('absent entry points and generated files are reported, never skipped', () => {
  assert.equal(missingEntryPoints('R', nothingExists).length, 4);
  assert.deepEqual(missingEntryPoints(REPO_ROOT), []);
  assert.deepEqual(missingVectorFiles('OUT', nothingExists), [...VECTOR_FILES]);
  assert.deepEqual(missingVectorFiles(join(REPO_ROOT, RepoPaths.vectors)), []);
});

test('the vitest entry is found upwards from the package folder', () => {
  assert.ok(findVitestEntry(join(REPO_ROOT, RepoPaths.ngSecurity))?.endsWith(join('vitest', 'vitest.mjs')));
  assert.equal(findVitestEntry(join(REPO_ROOT, RepoPaths.ngSecurity), nothingExists), null);
});

test('the CLI exits with the usage code on a bad prefix, without starting a runner', () => {
  const result = spawnSync(process.execPath, [CLI, '--prefix', 'bad'], { encoding: 'utf8' });
  assert.equal(result.status, EXIT_USAGE_OR_ERROR);
  assert.match(result.stderr, /usage: --prefix "bad"/);
  assert.doesNotMatch(result.stdout, /---/);
});
