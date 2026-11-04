#!/usr/bin/env node
// ESLint over the @codelab/* npm package sources (packages/npm/*), CI step 11. It enforces, for the TypeScript and
// JavaScript that exists before the Angular app (P2):
//   base.no-magic-values     no-magic-numbers, allowed numbers from rules/eslint-boundaries.compiled.json
//   base.module-boundaries   a package never imports a package its module.rules.yaml lists in mustNotDependOn,
//                            by package name or by a relative path into that package's folder
// Test files (*.spec.ts, *.test.*, tests/) are not linted for magic numbers (base.no-magic-values excludeTests).
//
// Usage: node tools/ci/lint-packages.mjs [--root <repo>]
// Exit codes: 0 clean, 1 lint errors, 2 the compiled boundary data is missing or unreadable.

import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { ESLint } from 'eslint';
import tsParser from '@typescript-eslint/parser';

export const EXIT_OK = 0;
export const EXIT_LINT_ERRORS = 1;
export const EXIT_NO_DATA = 2;

export const COMPILED_BOUNDARIES = 'rules/eslint-boundaries.compiled.json';
export const NPM_PACKAGES_FOLDER = 'packages/npm';
export const SOURCE_PATTERNS = Object.freeze([
  `${NPM_PACKAGES_FOLDER}/*/src/**/*.{ts,mts,mjs,js}`,
  `${NPM_PACKAGES_FOLDER}/*/scripts/**/*.{mjs,js}`,
]);
export const TEST_PATTERNS = Object.freeze(['**/*.spec.ts', '**/*.test.*', '**/tests/**']);
const IGNORED = Object.freeze(['**/dist/**', '**/node_modules/**']);
const ANGULAR_KIND = 'angular';
const PACKAGE_MANIFEST = 'package.json';
const SUBPATH_SUFFIX = '/*';
const ERROR = 'error';
const ROOT_OPTION = '--root';

/** Reads the compiled boundary data the rules engine writes. */
export function loadBoundaries(root) {
  const file = path.join(root, COMPILED_BOUNDARIES);
  if (!existsSync(file)) return null;
  return JSON.parse(readFileSync(file, 'utf8'));
}

/** The npm package modules (kind angular, under packages/npm) with their package names. */
export function npmModules(root, boundaries) {
  return boundaries.modules
    .filter((module) => module.kind === ANGULAR_KIND && module.path.startsWith(`${NPM_PACKAGES_FOLDER}/`))
    .map((module) => {
      const manifest = path.join(root, module.path, PACKAGE_MANIFEST);
      const packageName = existsSync(manifest) ? JSON.parse(readFileSync(manifest, 'utf8')).name : null;
      return { ...module, packageName };
    });
}

/**
 * The no-restricted-imports option for one module: every module it must not depend on, by package name (and its
 * subpaths) and by any relative path that reaches into that module's folder.
 */
export function restrictedImportsFor(module, modules) {
  const byName = new Map(modules.map((candidate) => [candidate.module, candidate]));
  const denied = (module.mustNotDependOn ?? []).map((name) => byName.get(name)).filter((target) => target !== undefined);
  const patterns = denied.flatMap((target) => {
    const folder = path.posix.basename(target.path);
    const group = [`**/${folder}/**`, `**/${folder}`];
    if (target.packageName) group.push(target.packageName, `${target.packageName}${SUBPATH_SUFFIX}`);
    return [{ group, message: `${module.module} must not depend on ${target.module} (module.rules.yaml mustNotDependOn).` }];
  });
  return { patterns };
}

/** The flat config: TypeScript parsing, magic numbers for non-test sources, and one boundary block per module. */
export function buildConfig(boundaries, modules) {
  const magicNumbers = [ERROR, {
    ignore: boundaries.noMagicNumbers?.ignore ?? [],
    ignoreArrayIndexes: true,
    ignoreDefaultValues: true,
    enforceConst: true,
  }];
  const boundaryBlocks = modules.map((module) => ({
    name: `codelab/boundaries/${module.module}`,
    files: [`${module.path}/**`],
    rules: { 'no-restricted-imports': [ERROR, restrictedImportsFor(module, modules)] },
  }));
  return [
    { ignores: [...IGNORED] },
    {
      name: 'codelab/parse',
      files: ['**/*.{ts,mts,mjs,js}'],
      languageOptions: { parser: tsParser, ecmaVersion: 'latest', sourceType: 'module' },
    },
    { name: 'codelab/no-magic-values', files: ['**/*.{ts,mts,mjs,js}'], ignores: [...TEST_PATTERNS], rules: { 'no-magic-numbers': magicNumbers } },
    ...boundaryBlocks,
  ];
}

/** A linter rooted at the repository with the package config (no eslint.config.* lookup). */
export function createLinter(root) {
  const boundaries = loadBoundaries(root);
  if (boundaries === null) return null;
  return new ESLint({ cwd: root, overrideConfigFile: true, overrideConfig: buildConfig(boundaries, npmModules(root, boundaries)) });
}

export async function main(argv) {
  const rootIndex = argv.indexOf(ROOT_OPTION);
  const root = path.resolve(rootIndex >= 0 ? argv[rootIndex + 1] : '.');
  const linter = createLinter(root);
  if (linter === null) {
    console.error(`${COMPILED_BOUNDARIES} not found; run node tools/rules-engine/compile.mjs.`);
    return EXIT_NO_DATA;
  }
  const results = await linter.lintFiles([...SOURCE_PATTERNS]);
  const formatter = await linter.loadFormatter('stylish');
  const errors = results.reduce((sum, result) => sum + result.errorCount, 0);
  const output = await formatter.format(results);
  if (output) console.log(output);
  console.log(`package lint: ${results.length} file(s), ${errors} error(s).`);
  return errors === 0 ? EXIT_OK : EXIT_LINT_ERRORS;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = await main(process.argv.slice(2));
}
