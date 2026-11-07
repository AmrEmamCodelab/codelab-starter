// Fixture trees for the facts-drift tests: written to a fresh %TEMP% folder per test, removed afterwards. Nothing is
// committed as a fixture file, so no other repo gate (docs gate, scrub sweep, content rules) ever sees the samples.
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { parseConfig } from '../../lib/config.mjs';
import { DEFAULT_CONFIG_FILE } from '../../lib/constants.mjs';

const TEMP_PREFIX = 'facts-drift-';
const createdRoots = [];

/**
 * Writes { 'relative/path': 'content' } under a new temporary folder and returns its absolute path.
 * @param {Record<string, string>} files
 */
export function writeTree(files) {
  const root = mkdtempSync(join(tmpdir(), TEMP_PREFIX));
  createdRoots.push(root);
  for (const [relativePath, content] of Object.entries(files)) {
    const fullPath = join(root, relativePath);
    mkdirSync(dirname(fullPath), { recursive: true });
    writeFileSync(fullPath, content);
  }
  return root;
}

/** Removes every tree this module created (call from afterEach). */
export function removeTrees() {
  while (createdRoots.length > 0) rmSync(createdRoots.pop(), { recursive: true, force: true });
}

/** The real, committed configuration, so tests exercise the shipped scope and lists. */
export function shippedConfig() {
  return parseConfig(readFileSync(DEFAULT_CONFIG_FILE, 'utf8'), DEFAULT_CONFIG_FILE);
}

/** A minimal starter-like tree most checker tests build on. */
export function baseTree(extra = {}) {
  return {
    'package.json': JSON.stringify({ name: 'root', scripts: { build: 'x', test: 'x' } }),
    'frontend/web/package.json': JSON.stringify({ name: '@codelab/web', scripts: { lint: 'x', 'api:gen': 'x' } }),
    'frontend/web/e2e/visual.mjs': '',
    'frontend/web/src/app/api.service.ts': 'export class ApiService {}\n',
    'backend/src/Domain/Options.cs': 'public sealed class UploadOptions { public string[] BlockedExtensions { get; set; } = []; }\n'
      + 'public enum GapReason { Missing = 1, NotInRing = 2 }\n'
      + 'public sealed class HasPermissionAttribute : System.Attribute {}\n',
    'tools/build.ps1': '[CmdletBinding()]\nparam(\n    [ValidateSet(\'CI\')]\n    [string] $Target,\n    [switch] $Pack,\n    [string] $Root = $PSScriptRoot\n)\nfunction Helper { param($Inner) }\n',
    'tools/check.mjs': '',
    'tools/tests/a.test.mjs': '',
    '.claude/skills/new-module/SKILL.md': '# new-module\n',
    '.claude/agents/commit-gate.md': '# commit-gate\n',
    ...extra,
  };
}
