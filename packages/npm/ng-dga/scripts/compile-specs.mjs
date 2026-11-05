#!/usr/bin/env node
// Compiles the library sources and specs ahead of time (ngc, full compilation) into .angular/spec, which Vitest then
// runs under jsdom. AOT is required: signal inputs are only visible to Angular through the compiler, so the
// components are tested exactly as an app compiles them. .angular/ is gitignored and never packed.
//
// Usage: node scripts/compile-specs.mjs
// Exit codes: 0 compiled, 1 compile errors (type, template or strict-template errors).
import { spawnSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { rmSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { PACKAGE_ROOT, SPEC_OUTPUT_DIR, SPEC_TSCONFIG } from './lib/package-paths.mjs';

export const EXIT_OK = 0;
export const EXIT_FAILED = 1;
const COMPILER_PACKAGE = '@angular/compiler-cli/package.json';
const NGC_BIN = 'ngc';

/** The ngc entry script of the installed @angular/compiler-cli. */
export function ngcEntry() {
  const require = createRequire(import.meta.url);
  const manifestPath = require.resolve(COMPILER_PACKAGE);
  const manifest = require(COMPILER_PACKAGE);
  return path.join(path.dirname(manifestPath), manifest.bin[NGC_BIN]);
}

export function main() {
  rmSync(SPEC_OUTPUT_DIR, { recursive: true, force: true });
  const result = spawnSync(process.execPath, [ngcEntry(), '-p', SPEC_TSCONFIG], { cwd: PACKAGE_ROOT, stdio: 'inherit' });
  return result.status === EXIT_OK ? EXIT_OK : EXIT_FAILED;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = main();
}
