// Rules dogfood (build plan, phase 2): in a scratch snapshot of the working tree, `new-module Demo`, one GET and one
// POST `new-endpoint` and a `new-permission` must produce a module that builds with -warnaserror and passes the
// architecture, contract and Demo tests with no manual edit, and git status must show only generator output.
//
// Slow (a full solution build). CODELAB_SKIP_DOGFOOD=1 skips it, for local loops only; CI always runs it.
// The test deletes only the scratch folder it created (after unlinking its node_modules junction).
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { BACKEND_SOLUTION_FOLDER, NODE_MODULES_DIR, SOLUTION_FILE, UTF8 } from '../lib/constants.mjs';
import { addSolutionProjects } from '../lib/project-files.mjs';
import { DEFAULT_ROOT, resolveDotnet } from '../lib/workspace.mjs';

const SKIP_ENV = 'CODELAB_SKIP_DOGFOOD';
const SCRATCH_PREFIX = 'codelab-dogfood-';
const TEST_TIMEOUT_MS = 45 * 60 * 1000;
const MAX_OUTPUT_BYTES = 256 * 1024 * 1024;
const BACKEND_PROJECT_ROOTS = Object.freeze(['backend/src', 'backend/tests', 'backend/tools']);
const CSPROJ = '.csproj';
const SKIPPED_DIRS = new Set(['bin', 'obj', NODE_MODULES_DIR]);
const GIT_IDENTITY = Object.freeze(['-c', 'user.name=dogfood', '-c', 'user.email=dogfood@invalid', '-c', 'core.autocrlf=false', '-c', 'core.quotePath=false']);
const DOTNET_BUILD_FLAGS = Object.freeze(['-warnaserror', '-nodeReuse:false', '-p:UseSharedCompilation=false']);
const CROSS_CUTTING_TESTS = Object.freeze([
  'backend/tests/Starter.Architecture.Tests/Starter.Architecture.Tests.csproj',
  'backend/tests/Starter.Contract.Tests/Starter.Contract.Tests.csproj',
]);
const DEMO_TESTS = 'backend/tests/Modules/Starter.Modules.Demo.Tests/Starter.Modules.Demo.Tests.csproj';
/** Derived outputs the downstream generators (rules compile, DocsGen, api:gen) may rewrite anywhere. */
const DERIVED_OUTPUT = /^(?:rules\/[^/]+\.(?:compiled\.json|globalconfig)|docs\/generated\/.*|frontend\/starter-web\/src\/app\/core\/(?:api\/generated\/.*|permissions\/permission-keys\.generated\.ts)|.*\/(?:CLAUDE\.md|BannedSymbols\.generated\.txt|\.editorconfig|CodeMetricsConfig\.txt))$/;
const REPORTED_FILE = /^(?:created|changed) (\S+)$/;
/** Where the permission added by the dogfood's new-permission must appear after the downstream generators ran. */
const CATALOG_OUTPUTS = Object.freeze([
  ['docs/generated/permissions.json', '"demo.approve"'],
  ['frontend/starter-web/src/app/core/permissions/permission-keys.generated.ts', "DemoApprove: 'demo.approve'"],
]);

const GENERATORS = Object.freeze([
  ['tools/scaffold/new-module.mjs', '--name', 'Demo'],
  ['tools/scaffold/new-endpoint.mjs', '--module', 'Demo', '--name', 'GetWidget', '--method', 'GET', '--route', 'widgets'],
  ['tools/scaffold/new-endpoint.mjs', '--module', 'Demo', '--name', 'CreateWidget', '--method', 'POST', '--route', 'widgets'],
  ['tools/scaffold/new-permission.mjs', '--module', 'Demo', '--action', 'approve'],
]);

function run(command, args, cwd) {
  const result = spawnSync(command, args, { cwd, encoding: UTF8, maxBuffer: MAX_OUTPUT_BYTES, windowsHide: true });
  if (result.error) throw result.error;
  return { code: result.status, output: `${result.stdout ?? ''}${result.stderr ?? ''}` };
}

function check(command, args, cwd) {
  const result = run(command, args, cwd);
  assert.equal(result.code, 0, `${command} ${args.join(' ')} failed:\n${result.output.slice(-8000)}`);
  return result.output;
}

function snapshotWorkingTree(scratch) {
  const listed = check('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], DEFAULT_ROOT).split('\0').filter(Boolean);
  for (const file of listed.filter((candidate) => fs.existsSync(path.join(DEFAULT_ROOT, candidate)))) {
    fs.mkdirSync(path.dirname(path.join(scratch, file)), { recursive: true });
    fs.copyFileSync(path.join(DEFAULT_ROOT, file), path.join(scratch, file));
  }
}

function backendProjects(root) {
  const found = [];
  const walk = (dir) => {
    if (!fs.existsSync(dir)) return;
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      if (entry.isDirectory() && !SKIPPED_DIRS.has(entry.name)) walk(path.join(dir, entry.name));
      else if (entry.name.endsWith(CSPROJ)) found.push(path.relative(root, path.join(dir, entry.name)).replaceAll('\\', '/'));
    }
  };
  for (const relative of BACKEND_PROJECT_ROOTS) walk(path.join(root, relative));
  return found.sort();
}

/** Registers backend projects the shared slnx does not list yet, in the SCRATCH copy only (reported). */
function registerBackendProjects(scratch) {
  const file = path.join(scratch, SOLUTION_FILE);
  const result = addSolutionProjects(fs.readFileSync(file, UTF8), backendProjects(scratch), BACKEND_SOLUTION_FOLDER);
  fs.writeFileSync(file, result.text, UTF8);
  if (result.added.length > 0) console.log(`dogfood: registered in the scratch ${SOLUTION_FILE} only: ${result.added.join(', ')}`);
}

function linkNodeModules(scratch) {
  const target = path.join(DEFAULT_ROOT, NODE_MODULES_DIR);
  if (!fs.existsSync(target)) return null;
  const link = path.join(scratch, NODE_MODULES_DIR);
  fs.symlinkSync(target, link, 'junction');
  return link;
}

function cleanUp(scratch, link) {
  if (link && fs.existsSync(link)) fs.unlinkSync(link);
  assert.ok(!link || !fs.existsSync(link), 'the node_modules junction must be gone before the scratch folder is removed');
  fs.rmSync(scratch, { recursive: true, force: true, maxRetries: 5 });
}

function changedPaths(scratch) {
  return check('git', ['status', '--porcelain', '-uall', '-z'], scratch)
    .split('\0')
    .filter(Boolean)
    .map((entry) => entry.slice(3));
}

/** new-permission updated both catalogs: the generated permission docs and the frontend PermissionKeys carry the key. */
function assertBothCatalogs(scratch) {
  for (const [file, expected] of CATALOG_OUTPUTS) {
    const full = path.join(scratch, file);
    if (!fs.existsSync(full)) {
      console.log(`dogfood: ${file} not in the tree yet, catalog not checked`);
      continue;
    }
    assert.ok(fs.readFileSync(full, UTF8).includes(expected), `${file} lacks ${expected}`);
  }
}

describe('rules dogfood: new-module, new-endpoint and new-permission build and pass unmodified', { skip: process.env[SKIP_ENV] === '1' && `${SKIP_ENV}=1` }, () => {
  it('scaffolds Demo with a GET, a POST and a permission, builds -warnaserror and passes the tests', { timeout: TEST_TIMEOUT_MS }, () => {
    const scratch = fs.mkdtempSync(path.join(os.tmpdir(), SCRATCH_PREFIX));
    let link = null;
    try {
      snapshotWorkingTree(scratch);
      link = linkNodeModules(scratch);
      registerBackendProjects(scratch);
      check('git', ['init', '--quiet'], scratch);
      check('git', [...GIT_IDENTITY, 'add', '-A'], scratch);
      check('git', [...GIT_IDENTITY, 'commit', '--quiet', '--no-verify', '-m', 'dogfood snapshot'], scratch);

      const reported = new Set();
      for (const [script, ...args] of GENERATORS) {
        const output = check(process.execPath, [script, ...args], scratch);
        console.log(`dogfood: node ${script} ${args.join(' ')}
${output.trim()}`);
        for (const line of output.split(/\r?\n/)) {
          const match = REPORTED_FILE.exec(line.trim());
          if (match) reported.add(match[1]);
        }
      }

      const unexpected = changedPaths(scratch).filter((file) => !reported.has(file) && !DERIVED_OUTPUT.test(file));
      assert.deepEqual(unexpected, [], 'git status shows files no generator reported');
      assertBothCatalogs(scratch);

      const dotnet = resolveDotnet();
      check(dotnet, ['build', SOLUTION_FILE, ...DOTNET_BUILD_FLAGS], scratch);
      const testProjects = [...CROSS_CUTTING_TESTS.filter((project) => fs.existsSync(path.join(scratch, project))), DEMO_TESTS];
      const missing = CROSS_CUTTING_TESTS.filter((project) => !testProjects.includes(project));
      if (missing.length > 0) console.log(`dogfood: not in the tree yet, not run: ${missing.join(', ')}`);
      const failures = testProjects
        .map((project) => ({ project, result: run(dotnet, ['test', project, '--no-build'], scratch) }))
        .filter(({ result }) => result.code !== 0)
        .map(({ project, result }) => `${project}:
${result.output.slice(-6000)}`);
      assert.deepEqual(failures, [], 'test projects failed');
    } finally {
      cleanUp(scratch, link);
    }
  });
});
