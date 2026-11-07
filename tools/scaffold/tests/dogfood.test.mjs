// Rules dogfood (build plan, phases 2 and 9): in a scratch snapshot of the working tree, `new-module DemoCases` (a
// two-word name), GET and POST `new-endpoint` runs (a collection route, the module root and an id-then-suffix route),
// a `new-permission`, a `new-lookup` and three `new-transition` runs on the ServiceRequests sample (a new status with
// the transition into it, the way back, and a system-side way back) must produce code
// that builds with -warnaserror, passes the content rules (tools/rules-engine/check-content.mjs) and the rules
// compile check, and passes the architecture, contract, DemoCases, Lookups and generated workflow tests with no
// manual edit; git status must show only generator output. The sample's own whole-flow table tests are not run: they
// pin the flow and fail on purpose after a flow change (new-transition skill, step 3).
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
const DEMO_TESTS = 'backend/tests/Modules/Starter.Modules.DemoCases.Tests/Starter.Modules.DemoCases.Tests.csproj';
const LOOKUP_TESTS = 'backend/tests/Modules/Starter.Modules.Lookups.Tests/Starter.Modules.Lookups.Tests.csproj';
const WORKFLOW_TESTS = 'backend/tests/Modules/Starter.Modules.ServiceRequests.Tests/Starter.Modules.ServiceRequests.Tests.csproj';
/** Only the test classes new-transition generated (the sample's whole-flow tests fail on purpose after a flow change). */
const WORKFLOW_TEST_FILTER = 'FullyQualifiedName~OnHoldStatusTests|FullyQualifiedName~HoldFromUnderReviewTransitionTests|FullyQualifiedName~ResumeFromOnHoldTransitionTests|FullyQualifiedName~AutoResumeFromOnHoldTransitionTests';
const CONTENT_CHECK = 'tools/rules-engine/check-content.mjs';
const RULES_COMPILE = 'tools/rules-engine/compile.mjs';
/** Derived outputs the downstream generators (rules compile, DocsGen, api:gen) may rewrite anywhere. */
const DERIVED_OUTPUT = /^(?:rules\/[^/]+\.(?:compiled\.json|globalconfig)|docs\/generated\/.*|frontend\/starter-web\/src\/app\/core\/(?:api\/generated\/.*|permissions\/permission-keys\.generated\.ts)|.*\/(?:CLAUDE\.md|BannedSymbols\.generated\.txt|\.editorconfig|CodeMetricsConfig\.txt))$/;
const REPORTED_FILE = /^(?:created|changed) (\S+)$/;
/** Where the permission added by the dogfood's new-permission must appear after the downstream generators ran. */
const CATALOG_OUTPUTS = Object.freeze([
  ['docs/generated/permissions.json', '"demo-cases.approve"'],
  ['frontend/starter-web/src/app/core/permissions/permission-keys.generated.ts', "DemoCasesApprove: 'demo-cases.approve'"],
]);

const SAMPLE_TRANSITION = Object.freeze(['--module', 'ServiceRequests', '--permission', 'service-requests.decide', '--side', 'staff', '--notify', 'none']);
const GENERATORS = Object.freeze([
  ['tools/scaffold/new-module.mjs', '--name', 'DemoCases'],
  ['tools/scaffold/new-endpoint.mjs', '--module', 'DemoCases', '--name', 'GetWidget', '--method', 'GET', '--route', 'widgets'],
  ['tools/scaffold/new-endpoint.mjs', '--module', 'DemoCases', '--name', 'CreateWidget', '--method', 'POST', '--route', 'widgets'],
  // The module root and an id-then-suffix route (the sample's create and draft endpoints have these shapes).
  ['tools/scaffold/new-endpoint.mjs', '--module', 'DemoCases', '--name', 'CreateDemoCase', '--method', 'POST', '--route', '.'],
  ['tools/scaffold/new-endpoint.mjs', '--module', 'DemoCases', '--name', 'GetDemoCaseDraft', '--method', 'GET', '--route', '{id}/draft'],
  ['tools/scaffold/new-permission.mjs', '--module', 'DemoCases', '--action', 'approve'],
  ['tools/scaffold/new-lookup.mjs', '--type', 'demo-kinds', '--name', 'DemoKind', '--codes', 'first,second',
    '--name-ar', 'أنواع تجريبية', '--names-ar', 'الأول;الثاني', '--name-en', 'Demo kinds', '--names-en', 'First;Second'],
  ['tools/scaffold/new-transition.mjs', ...SAMPLE_TRANSITION, '--status', 'on-hold', '--status-en', 'On hold', '--status-ar', 'معلّق',
    '--action', 'hold', '--from', 'under-review', '--to', 'on-hold', '--reason', 'required', '--label-en', 'Put on hold', '--label-ar', 'تعليق'],
  ['tools/scaffold/new-transition.mjs', ...SAMPLE_TRANSITION, '--action', 'resume', '--from', 'on-hold', '--to', 'under-review',
    '--label-en', 'Resume', '--label-ar', 'استئناف'],
  // A system-side rule: its generated tests must act from the system side, or the engine refuses them.
  ['tools/scaffold/new-transition.mjs', '--module', 'ServiceRequests', '--permission', 'service-requests.decide', '--side', 'system',
    '--notify', 'none', '--action', 'auto-resume', '--from', 'on-hold', '--to', 'under-review',
    '--label-en', 'Resume automatically', '--label-ar', 'استئناف تلقائي'],
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

describe('rules dogfood: every backend scaffold builds, passes the rules and its tests unmodified', { skip: process.env[SKIP_ENV] === '1' && `${SKIP_ENV}=1` }, () => {
  it('scaffolds DemoCases (GET, POST, module-root and id-suffix routes, permission), a lookup and three sample transitions, then checks rules, build and tests', { timeout: TEST_TIMEOUT_MS }, () => {
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
      check(process.execPath, [CONTENT_CHECK, '--root', '.'], scratch);
      check(process.execPath, [RULES_COMPILE, '--check'], scratch);

      const dotnet = resolveDotnet();
      check(dotnet, ['build', SOLUTION_FILE, ...DOTNET_BUILD_FLAGS], scratch);
      const testRuns = [
        ...CROSS_CUTTING_TESTS.filter((project) => fs.existsSync(path.join(scratch, project))).map((project) => [project]),
        [DEMO_TESTS],
        [LOOKUP_TESTS],
        [WORKFLOW_TESTS, '--filter', WORKFLOW_TEST_FILTER],
      ];
      const missing = CROSS_CUTTING_TESTS.filter((project) => !testRuns.some(([run]) => run === project));
      if (missing.length > 0) console.log(`dogfood: not in the tree yet, not run: ${missing.join(', ')}`);
      const failures = testRuns
        .map(([project, ...extra]) => ({ project, result: run(dotnet, ['test', project, '--no-build', ...extra], scratch) }))
        .filter(({ result }) => result.code !== 0)
        .map(({ project, result }) => `${project}:
${result.output.slice(-6000)}`);
      assert.deepEqual(failures, [], 'test projects failed');
    } finally {
      cleanUp(scratch, link);
    }
  });
});
