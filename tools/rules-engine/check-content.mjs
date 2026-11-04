#!/usr/bin/env node
// CI content check: evaluates every forbidden and required rule in rules/rules.compiled.json against every
// tracked (and untracked, not ignored) file, in ABSOLUTE state. The hooks enforce the same rules as a ratchet on
// each edit; this step is what makes ADR-006 true ("every rule a hook enforces is also enforced by CI"), so a hook
// bypass (hooks disabled, another editor, a shell write, a commit the Stop gate never saw) still fails the build.
//
// Usage: node tools/rules-engine/check-content.mjs [--root <path>]
// Exit codes: 0 no error-severity violation, 1 violations found (or rules.compiled.json missing/invalid), 2 usage.
//
// One evaluator: the rules are loaded and matched by .claude/hooks/lib (compiled-rules.mjs, evaluate.mjs).

import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { findModuleForPath, loadCompiledRules, RULE_SEVERITIES, rulesForModule } from '../../.claude/hooks/lib/compiled-rules.mjs';
import { findViolations } from '../../.claude/hooks/lib/evaluate.mjs';
import { matchesAnyGlob } from '../../.claude/hooks/lib/glob.mjs';
import { listFiles, readTextFile } from '../extract/lib/files.mjs';

export const EXIT_CLEAN = 0;
export const EXIT_VIOLATIONS = 1;
export const EXIT_USAGE = 2;

const ROOT_OPTION = '--root';
const DEFAULT_ROOT = '.';
const STATUS_OK = 'ok';
/**
 * Test inputs that break rules on purpose (hook and rules-engine fixtures). Nothing else is excluded: a real
 * exception belongs in a module.rules.yaml `exemptions` entry, which needs platform-owner review.
 */
export const FIXTURE_GLOBS = Object.freeze([
  '.claude/hooks/tests/fixtures/**',
  'tools/rules-engine/fixtures/**',
  'tools/rules-engine/tests/**',
]);

/**
 * Every rule violation in the tree.
 * @param {string} root absolute repo root
 * @returns {{ status: string, error?: string, violations: { path: string, rule: object }[], fileCount: number }}
 */
export function checkContent(root) {
  const loaded = loadCompiledRules(root);
  if (loaded.status !== STATUS_OK) return { status: loaded.status, error: loaded.error, violations: [], fileCount: 0 };
  const paths = listFiles(root).filter((relativePath) => !matchesAnyGlob(relativePath, FIXTURE_GLOBS));
  const violations = [];
  for (const relativePath of paths) {
    const rules = rulesForModule(loaded.rules, findModuleForPath(relativePath, loaded.rules));
    const text = readTextFile(path.join(root, relativePath));
    if (text === null) continue;
    for (const rule of findViolations({ rules, relativePath, text })) violations.push({ path: relativePath, rule });
  }
  return { status: STATUS_OK, violations, fileCount: paths.length };
}

function describe({ path: relativePath, rule }) {
  const message = rule.message ? ` ${rule.message}` : '';
  const fix = rule.fix ? ` Fix: ${rule.fix}` : '';
  return `${relativePath}: [${rule.id}] (${rule.severity})${message}${fix}`;
}

export function main(argv) {
  if (argv.length > 0 && (argv[0] !== ROOT_OPTION || argv.length !== 2)) {
    console.error(`Usage: node tools/rules-engine/check-content.mjs [${ROOT_OPTION} <path>]`);
    return EXIT_USAGE;
  }
  const root = path.resolve(argv[1] ?? DEFAULT_ROOT);
  const result = checkContent(root);
  if (result.status !== STATUS_OK) {
    console.error(`content check: rules/rules.compiled.json is ${result.status}${result.error ? ` (${result.error})` : ''}.`);
    return EXIT_VIOLATIONS;
  }
  const errors = result.violations.filter((item) => item.rule.severity !== RULE_SEVERITIES.Warning);
  const warnings = result.violations.filter((item) => item.rule.severity === RULE_SEVERITIES.Warning);
  for (const item of warnings) console.warn(`warning ${describe(item)}`);
  for (const item of errors) console.error(`error   ${describe(item)}`);
  console.log(`content check: ${result.fileCount} file(s), ${errors.length} error(s), ${warnings.length} warning(s).`);
  return errors.length > 0 ? EXIT_VIOLATIONS : EXIT_CLEAN;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = main(process.argv.slice(2));
}
