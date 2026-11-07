// Run: node --test "tools/ci/tests/**/*.test.mjs"
// Contract: the phase 7 exit checks are CI steps of tools/build.ps1, not scripts only a human runs.
import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const REPO_ROOT = fileURLToPath(new URL('../../..', import.meta.url));
const BUILD_SCRIPT = path.join(REPO_ROOT, 'tools', 'build.ps1');
const ANGULAR_JSON = path.join(REPO_ROOT, 'frontend', 'starter-web', 'angular.json');

const NO_DEV_AUTOFILL_SCRIPT = 'tools/ci/no-dev-autofill.mjs';
const DELETABILITY_SCRIPT = 'tools/ci/module-deletability.ps1';
const SAMPLE_MODULE = 'ServiceRequests';
const WEB_PROJECT = 'starter-web';
const WEB_FOLDER = 'frontend/starter-web';
const NPM_BUILD_STEP = '11 npm build+lint+test';
const NO_DEV_AUTOFILL_STEP = '20 no dev autofill';
const DELETABILITY_STEP = '21 module deletability';
const NOT_FOUND = -1;
const DEPLOY_KIT_TESTS_STEP = '19 deploy-kit tests';
const DEPLOY_KIT_SCRIPT = 'tools/kit/New-DeployKit.ps1';
// Every folder that holds a Pester suite must be run by step 19; these are the ones known today (a new one found by
// the repository walk below fails the test until build.ps1 runs it).
const PESTER_FOLDERS = ['deploy/kits/tests', 'tools/kit/tests', 'tools/sql-kit/tests', 'deploy/seq/tests',
  'tools/pipeline-proofs/tests', 'tools/ai-evals/tests', '.claude/skills/usecase-testing/scripts/tests'];
const PESTER_SUITE = /\.Tests\.ps1$/;
// Folders never walked for Pester suites: git data, build output and installed packages.
const SKIPPED_FOLDERS = new Set(['.git', 'node_modules', 'bin', 'obj', 'artifacts', 'publish', 'dist', '.angular', 'feed']);

const script = readFileSync(BUILD_SCRIPT, 'utf8');

/** Repo-relative folders (forward slashes) that directly hold a *.Tests.ps1 suite. */
function pesterSuiteFolders(relative = '') {
  const entries = readdirSync(path.join(REPO_ROOT, relative), { withFileTypes: true });
  const own = entries.some((entry) => entry.isFile() && PESTER_SUITE.test(entry.name)) ? [relative] : [];
  const nested = entries
    .filter((entry) => entry.isDirectory() && !SKIPPED_FOLDERS.has(entry.name))
    .flatMap((entry) => pesterSuiteFolders(relative === '' ? entry.name : `${relative}/${entry.name}`));
  return [...own, ...nested];
}

/** The `@{ Name = '<name>'; ... }` line of a CI step in Invoke-CiTarget, or undefined. */
function stepLine(name) {
  return script.split(/\r?\n/).find((line) => line.includes(`Name = '${name}'`));
}

/** The body of a PowerShell function of build.ps1 (up to the next top-level function). */
function functionBody(name) {
  const start = script.indexOf(`function ${name}`);
  assert.notEqual(start, NOT_FOUND, `build.ps1 declares ${name}`);
  const next = script.indexOf('\nfunction ', start + 1);
  return script.slice(start, next === NOT_FOUND ? undefined : next);
}

/** The value of a `$Name = '<value>'` constant of build.ps1. */
function constant(name) {
  const match = script.match(new RegExp(`^\\$${name}\\s*=\\s*'([^']+)'`, 'm'));
  assert.ok(match, `build.ps1 declares $${name}`);
  return match[1];
}

/** The values of a `$Name = @('a', 'b')` array constant of build.ps1. */
function arrayConstant(name) {
  const match = script.match(new RegExp(`^\\$${name}\\s*=\\s*@\\(([^)]*)\\)`, 'm'));
  assert.ok(match, `build.ps1 declares $${name}`);
  return [...match[1].matchAll(/'([^']+)'/g)].map((value) => value[1]);
}

/** The folder `ng build` writes the production bundle of the web app to (angular.json outputPath, default dist/<project>). */
function webDistFolder() {
  const angular = JSON.parse(readFileSync(ANGULAR_JSON, 'utf8'));
  const outputPath = angular.projects[WEB_PROJECT].architect.build.options.outputPath;
  const relative = typeof outputPath === 'string' ? outputPath : outputPath?.base ?? `dist/${WEB_PROJECT}`;
  return `${WEB_FOLDER}/${relative}`;
}

describe('build.ps1 wires the phase 7 exit checks', () => {
  it('runs the no-DevAutofill check as a step after step 11 built the production bundle', () => {
    const line = stepLine(NO_DEV_AUTOFILL_STEP);
    assert.ok(line, `CI step '${NO_DEV_AUTOFILL_STEP}' is declared`);
    assert.doesNotMatch(line, /Heavy\s*=\s*\$true/, 'the check is cheap and runs in -Quick too');
    assert.ok(script.indexOf(`'${NPM_BUILD_STEP}'`) < script.indexOf(`'${NO_DEV_AUTOFILL_STEP}'`), 'step 11 builds the dist first');

    const body = functionBody('Invoke-NoDevAutofillStep');
    assert.match(body, /\$NoDevAutofillScript/);
    assert.match(body, /'--dist'/);
    assert.equal(constant('NoDevAutofillScript'), NO_DEV_AUTOFILL_SCRIPT);
    assert.equal(constant('StarterWebDist'), webDistFolder(), 'the check scans the folder ng build writes');
  });

  it('runs the module deletability proof of the sample module as a heavy step', () => {
    const line = stepLine(DELETABILITY_STEP);
    assert.ok(line, `CI step '${DELETABILITY_STEP}' is declared`);
    assert.match(line, /Heavy\s*=\s*\$true/, 'a copy, build and full test run: skipped by -Quick like steps 13, 14, 18');

    const body = functionBody('Invoke-ModuleDeletabilityStep');
    assert.match(body, /\$DeletabilityScript/);
    assert.match(body, /'-Name',\s*\$DeletableSampleModule/);
    assert.equal(constant('DeletabilityScript'), DELETABILITY_SCRIPT);
    assert.equal(constant('DeletableSampleModule'), SAMPLE_MODULE);
  });
});

describe('build.ps1 wires the phase 8 deploy side', () => {
  it('runs every deploy-side Pester suite (install scripts, header parity, kit, SQL kit, Seq) in step 19', () => {
    const line = stepLine(DEPLOY_KIT_TESTS_STEP);
    assert.ok(line, `CI step '${DEPLOY_KIT_TESTS_STEP}' is declared`);
    assert.doesNotMatch(line, /Heavy\s*=\s*\$true/, 'the suites need no build and run in -Quick too');

    const folders = arrayConstant('PesterTestFolders');
    assert.deepEqual(folders, PESTER_FOLDERS);
    const body = functionBody('Invoke-KitModuleTestStep');
    assert.match(body, /\$PesterTestFolders/);
    assert.match(body, /-EnableExit/);
    for (const folder of folders) {
      const absolute = path.join(REPO_ROOT, folder);
      assert.ok(existsSync(absolute), `${folder} exists`);
      assert.ok(readdirSync(absolute).some((file) => PESTER_SUITE.test(file)), `${folder} holds a *.Tests.ps1 suite`);
    }
  });

  it('runs every Pester suite folder of the repository in step 19, the ai-evals delete guards included', () => {
    const folders = arrayConstant('PesterTestFolders');
    for (const folder of pesterSuiteFolders()) {
      assert.ok(folders.includes(folder), `${folder} holds a *.Tests.ps1 suite but step 19 does not run it`);
    }
    assert.ok(folders.includes('tools/ai-evals/tests'));
  });

  it('declares -Kit and -KitComponent and builds the kit through the builder, after CI and pack', () => {
    assert.match(script, /\[switch\]\s*\$Kit\b/);
    assert.match(script, /\[string\[\]\]\s*\$KitComponent\b/);
    assert.equal(constant('DeployKitScript'), DEPLOY_KIT_SCRIPT);
    assert.match(functionBody('Get-DeployKitArguments'), /'-Component'/);
    const main = script.slice(script.indexOf("if ($Target -eq 'CI')"));
    assert.ok(main.indexOf('Invoke-CiTarget') < main.indexOf('Invoke-KitTarget'), 'the kit is built only after a green CI run');
    assert.ok(main.indexOf('Invoke-PackTarget') < main.indexOf('Invoke-KitTarget'), 'the kit is built after the pack');
  });
});
