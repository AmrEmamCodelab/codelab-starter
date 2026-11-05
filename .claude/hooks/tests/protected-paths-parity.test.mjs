// Parity: the hook's PlatformOwnedGlobs, .github/CODEOWNERS and tools/build.ps1 $ProtectedPathPattern must protect
// the same paths. A guardrail file missing from one list is edited without review or without a CI report.
import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { matchesAnyGlob } from '../lib/glob.mjs';
import { ModuleRulesFileGlob, PlatformOwnedGlobs } from '../lib/constants.mjs';

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const CODEOWNERS_PATH = path.join(REPO_ROOT, '.github', 'CODEOWNERS');
const BUILD_SCRIPT_PATH = path.join(REPO_ROOT, 'tools', 'build.ps1');
const PROTECTED_PATTERN_LINE = /^\$ProtectedPathPattern\s*=\s*'([^']+)'/m;
const COMMENT_PREFIX = '#';
const DIRECTORY_SUFFIX = '/';
const ANY_BELOW = '**';
// PowerShell -match is case-insensitive.
const POWERSHELL_MATCH_FLAGS = 'i';
const NESTED_PREFIX = 'backend/src/Modules/Sample/';

/** CODEOWNERS patterns as globs ('/x/' -> '/x/**'; a leading '/' keeps the glob root-anchored, 'x' matches at any depth). */
function readCodeownersGlobs() {
  return fs.readFileSync(CODEOWNERS_PATH, 'utf8')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith(COMMENT_PREFIX))
    .map((line) => line.split(/\s+/)[0])
    .map((pattern) => (pattern.endsWith(DIRECTORY_SUFFIX) ? `${pattern}${ANY_BELOW}` : pattern));
}

function readBuildScriptPattern() {
  const match = PROTECTED_PATTERN_LINE.exec(fs.readFileSync(BUILD_SCRIPT_PATH, 'utf8'));
  assert.ok(match, 'tools/build.ps1 must declare $ProtectedPathPattern on one line');
  return new RegExp(match[1], POWERSHELL_MATCH_FLAGS);
}

/** One concrete path per glob, plus a nested copy for globs that match at any depth. */
function samplesFor(glob) {
  const concrete = glob.replace(/^\//, '').replace(/^\*\*\//, '').replace(/\*\*/g, 'sample.txt').replace(/\*/g, 'props');
  const anyDepth = glob.startsWith(`${ANY_BELOW}/`) || !glob.includes('/');
  return anyDepth ? [concrete, `${NESTED_PREFIX}${concrete}`] : [concrete];
}

// The finding's paths and ordinary source files (must stay unprotected in all three lists).
const NAMED_SAMPLES = Object.freeze([
  'frontend/starter-web/eslint.config.mjs',
  'frontend/starter-web/tools/lint/starter-eslint-config.mjs',
  'frontend/starter-web/stylelint.config.mjs',
  '.editorconfig',
  'backend/src/Modules/Access/.editorconfig',
  'backend/Directory.Build.props',
  'backend/Directory.Build.targets',
  'backend/nuget.config',
  'tools/extract/scan.mjs',
  'docs/standards/SKILL.md',
]);
const UNPROTECTED_SAMPLES = Object.freeze([
  'backend/src/Modules/Access/Access.Application/UseCase.cs',
  'frontend/starter-web/src/app/features/admin/admin.page.ts',
  'frontend/starter-web/package.json',
  'docs/README.md',
  'tools/README.md',
  'CONTEXT.md',
]);

describe('protected-path parity (hook globs, CODEOWNERS, build.ps1)', () => {
  const codeownersGlobs = readCodeownersGlobs();
  const buildPattern = readBuildScriptPattern();
  const hookGlobs = [...PlatformOwnedGlobs, ModuleRulesFileGlob];
  const samples = [...new Set([...hookGlobs.flatMap(samplesFor), ...codeownersGlobs.flatMap(samplesFor), ...NAMED_SAMPLES])];

  it('regression: every protected sample is protected by all three lists', () => {
    const disagreements = samples
      .map((sample) => ({
        sample,
        hook: matchesAnyGlob(sample, hookGlobs),
        codeowners: matchesAnyGlob(sample, codeownersGlobs),
        buildScript: buildPattern.test(sample),
      }))
      .filter((row) => !(row.hook && row.codeowners && row.buildScript));
    assert.deepEqual(disagreements, []);
  });

  it('leaves ordinary source and docs files unprotected in all three lists', () => {
    for (const sample of UNPROTECTED_SAMPLES) {
      assert.equal(matchesAnyGlob(sample, hookGlobs), false, `hook: ${sample}`);
      assert.equal(matchesAnyGlob(sample, codeownersGlobs), false, `CODEOWNERS: ${sample}`);
      assert.equal(buildPattern.test(sample), false, `build.ps1: ${sample}`);
    }
  });
});
