// Run: node --test "tools/ci/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import { BASE_REF_ENV_VAR, NO_BASE, parseArguments, resolveBaseRef } from '../base-ref.mjs';

const BUILD_SCRIPT = fileURLToPath(new URL('../../build.ps1', import.meta.url));
const NO_ENV = Object.freeze({});
const tempRoots = [];

function git(root, ...args) {
  return execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
}

function newRepository() {
  const root = mkdtempSync(path.join(tmpdir(), 'base-ref-'));
  tempRoots.push(root);
  git(root, 'init', '-q', '-b', 'main');
  git(root, 'config', 'user.email', 'ci@example.test');
  git(root, 'config', 'user.name', 'ci');
  return root;
}

function commitFile(root, name, content) {
  writeFileSync(path.join(root, name), content);
  git(root, 'add', name);
  git(root, 'commit', '-q', '-m', name);
}

afterEach(() => {
  while (tempRoots.length > 0) rmSync(tempRoots.pop(), { recursive: true, force: true });
});

describe('resolveBaseRef', () => {
  it('returns NONE for a repository without commits', () => {
    assert.deepEqual(resolveBaseRef({ root: newRepository(), env: NO_ENV }), { ref: NO_BASE, error: null });
  });

  it('defaults to main for a feature branch with committed changes (DOCS005 must see them)', () => {
    const root = newRepository();
    commitFile(root, 'a.txt', 'a');
    git(root, 'checkout', '-q', '-b', 'feature/x');
    commitFile(root, 'b.txt', 'b');
    assert.deepEqual(resolveBaseRef({ root, env: NO_ENV }), { ref: 'main', error: null });
  });

  it('honours an explicit ref and the environment variable', () => {
    const root = newRepository();
    commitFile(root, 'a.txt', 'a');
    git(root, 'branch', 'release');
    assert.equal(resolveBaseRef({ root, explicitRef: 'release', env: NO_ENV }).ref, 'release');
    assert.equal(resolveBaseRef({ root, env: { [BASE_REF_ENV_VAR]: 'release' } }).ref, 'release');
  });

  it('fails instead of silently comparing with HEAD when the requested ref does not exist', () => {
    const root = newRepository();
    commitFile(root, 'a.txt', 'a');
    const result = resolveBaseRef({ root, explicitRef: 'origin/nope', env: NO_ENV });
    assert.equal(result.ref, null);
    assert.match(result.error, /origin\/nope/);
  });

  it('fails when no default ref exists', () => {
    const root = newRepository();
    commitFile(root, 'a.txt', 'a');
    git(root, 'branch', '-m', 'trunk');
    assert.equal(resolveBaseRef({ root, env: NO_ENV }).ref, null);
  });
});

describe('parseArguments', () => {
  it('reads --root and --base and rejects anything else', () => {
    assert.deepEqual(parseArguments(['--root', 'x', '--base', 'main']), { root: 'x', base: 'main' });
    assert.equal(parseArguments(['--base']), null);
    assert.equal(parseArguments(['--other', 'x']), null);
  });
});

describe('build.ps1 contract (regression: DOCS005 never ran in CI)', () => {
  it('resolves the base ref and passes --base to the docs gate', () => {
    const script = readFileSync(BUILD_SCRIPT, 'utf8');
    assert.match(script, /tools\/ci\/base-ref\.mjs/);
    assert.match(script, /'--base', \$baseRef/);
  });
});
