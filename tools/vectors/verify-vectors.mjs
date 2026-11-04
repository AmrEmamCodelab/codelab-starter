#!/usr/bin/env node
// Cross-language golden-vector check: the C# implementation (Codelab.Security) and the TypeScript one
// (@codelab/ng-security) must produce the same canonical bytes, signatures, session keys and PublicId tokens.
//
//   node tools/vectors/verify-vectors.mjs --prefix CODELABTEST   both runners against packages/vectors
//   node tools/vectors/verify-vectors.mjs --prefix ALTPFX [--keep]
//        the C# generator writes an ALTPFX set to a temp folder, then the TypeScript runner verifies it
//        (--keep leaves the folder for inspection)
//
// Exit 0 = both runners agree, 1 = a runner failed or an entry point is MISSING, 2 = usage or internal error.
// dotnet: CODELAB_DOTNET, else the private SDK, else PATH (as tools/build.ps1). Node: this process's node.
import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs, runCli } from '../extract/lib/cli-args.mjs';
import { EXIT_CLEAN, EXIT_FINDINGS } from '../extract/lib/exit-codes.mjs';
import {
  findVitestEntry, missingEntryPoints, missingVectorFiles, parseVerifyOptions, planSteps, PINNED_PREFIX, RepoPaths,
  resolveDotnet,
} from './lib/verify-plan.mjs';

const TOOL_NAME = 'verify-vectors';
const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const TEMP_FOLDER_PREFIX = 'codelab-vectors-';

function fail(message) {
  console.error(`${TOOL_NAME}: ${message}`);
  return EXIT_FINDINGS;
}

function runStep(step) {
  console.log(`${TOOL_NAME}: --- ${step.name}`);
  const result = spawnSync(step.command, step.args, { cwd: step.cwd, env: { ...process.env, ...step.env }, stdio: 'inherit' });
  if (result.error) return `${step.name}: could not start ${step.command} (${result.error.message})`;
  if (result.status !== EXIT_CLEAN) return `${step.name}: exit ${result.status}`;
  const missing = step.expectsFiles ? missingVectorFiles(step.expectsFiles) : [];
  return missing.length > 0 ? `${step.name}: MISSING generated ${missing.join(', ')}` : null;
}

function verify(prefix, outputDirectory) {
  const missing = missingEntryPoints(REPO_ROOT);
  if (missing.length > 0) return fail(`MISSING entry point(s): ${missing.join(', ')}`);
  const vitestEntry = findVitestEntry(join(REPO_ROOT, RepoPaths.ngSecurity));
  if (vitestEntry === null) return fail('MISSING vitest (run npm ci at the repository root)');
  const steps = planSteps({ prefix, repoRoot: REPO_ROOT, outputDirectory, dotnet: resolveDotnet(process.env), node: process.execPath, vitestEntry });
  for (const step of steps) {
    const failure = runStep(step);
    if (failure !== null) return fail(`FAILED ${failure}`);
  }
  console.log(`${TOOL_NAME}: PASSED: C# and TypeScript agree for prefix ${prefix}`);
  return EXIT_CLEAN;
}

await runCli(TOOL_NAME, () => {
  const { prefix, keep } = parseVerifyOptions(parseArgs(process.argv.slice(2)));
  if (prefix === PINNED_PREFIX) return verify(prefix, null);
  const outputDirectory = mkdtempSync(join(tmpdir(), TEMP_FOLDER_PREFIX));
  try {
    return verify(prefix, outputDirectory);
  } finally {
    if (keep) console.log(`${TOOL_NAME}: kept the generated set in ${outputDirectory}`);
    else rmSync(outputDirectory, { recursive: true, force: true });
  }
});
