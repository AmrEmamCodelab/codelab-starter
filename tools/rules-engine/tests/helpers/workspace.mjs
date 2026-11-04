// Temporary workspaces for rules-engine tests: a throwaway repo root with the real schema and base rules, plus
// whatever module files a test writes. Never touches the real repo.
import { cpSync, mkdtempSync, rmSync, writeFileSync, mkdirSync, readFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { PATHS } from '../../lib/constants.mjs';

export const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
export const ENGINE_DIR = path.join(REPO_ROOT, 'tools', 'rules-engine');
export const FIXTURE_MODULE_DIR = path.join(ENGINE_DIR, 'fixtures', 'SampleModule');
export const COMPILE_SCRIPT = path.join(ENGINE_DIR, 'compile.mjs');

const TEMP_PREFIX = 'codelab-rules-';
// The base rules' recipes must exist for the compiler to accept them (checkRecipes).
const BASE_RECIPES = Object.freeze(['docs/standards/SKILL.md']);

/** Creates a temp root holding copies of rules/rules.schema.json and rules/base.rules.yaml. */
export function createWorkspace() {
  const root = mkdtempSync(path.join(os.tmpdir(), TEMP_PREFIX));
  for (const file of [PATHS.schema, PATHS.baseRules, ...BASE_RECIPES]) {
    copyFromRepo(root, file);
  }
  return root;
}

export function removeWorkspace(root) {
  rmSync(root, { recursive: true, force: true, maxRetries: 3 });
}

export function copyFromRepo(root, relativePath) {
  const target = path.join(root, relativePath);
  mkdirSync(path.dirname(target), { recursive: true });
  cpSync(path.join(REPO_ROOT, relativePath), target);
}

/** Writes a file under the workspace (repo-relative posix path). */
export function writeWorkspaceFile(root, relativePath, content) {
  const target = path.join(root, ...relativePath.split('/'));
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, content, 'utf8');
  return target;
}

export function readWorkspaceFile(root, relativePath) {
  return readFileSync(path.join(root, ...relativePath.split('/')), 'utf8');
}

const RECIPE_LINE = /^\s+-\s+(docs\/\S+\.md)\s*$/gm;
const RECIPE_STUB = '# Recipe stub for rules-engine tests\n';

/** The compiler rejects missing recipes, so a copied fixture gets a stub for every docs/*.md it lists. */
function writeRecipeStubs(root, rulesText) {
  for (const match of rulesText.matchAll(RECIPE_LINE)) writeWorkspaceFile(root, match[1], RECIPE_STUB);
}

/** Copies the SampleModule fixture to `relativeDir` inside the workspace (plus stubs for its recipes). */
export function copyFixtureModule(root, relativeDir) {
  const target = path.join(root, ...relativeDir.split('/'));
  cpSync(FIXTURE_MODULE_DIR, target, {
    recursive: true,
    filter: (source) => !/(?:^|[\\/])(?:bin|obj)(?:[\\/]|$)/.test(path.relative(FIXTURE_MODULE_DIR, source)),
  });
  writeRecipeStubs(root, readFileSync(path.join(target, 'module.rules.yaml'), 'utf8'));
  return target;
}

/** A minimal valid module.rules.yaml; `extra` is appended verbatim (YAML). */
export function moduleYaml(name, extra = '') {
  return `schemaVersion: 1\nmodule: ${name}\n${extra}`;
}
