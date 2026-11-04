// Shared test helpers: temp folders, file trees, CLI runs and the fixture samples.
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const TESTS_DIRECTORY = dirname(fileURLToPath(import.meta.url));
export const TOOL_DIRECTORY = resolve(TESTS_DIRECTORY, '..');
export const REPO_ROOT = resolve(TOOL_DIRECTORY, '..', '..');
export const TERMS_FILE = join(TOOL_DIRECTORY, 'modhs-terms.txt');
export const EXCEPTIONS_FILE = join(TOOL_DIRECTORY, 'scrub-exceptions.txt');
export const MANIFEST_FILE = join(TOOL_DIRECTORY, 'allowlist.manifest.yaml');
export const DIGEST_FILE = join(TOOL_DIRECTORY, 'secret-digests.json');

/** The term-bearing samples (kept under the excepted tests/fixtures folder). */
export const samples = JSON.parse(readFileSync(join(TESTS_DIRECTORY, 'fixtures', 'scrub-samples.json'), 'utf8'));

/** Creates a temp folder and registers its removal with the test context. */
export function makeTempDirectory(testContext) {
  const directory = mkdtempSync(join(tmpdir(), 'codelab-extract-test-'));
  testContext.after(() => rmSync(directory, { recursive: true, force: true }));
  return directory;
}

/** Writes { "relative/path": content } under root. Buffers are written as-is. */
export function writeTree(root, files) {
  for (const [path, content] of Object.entries(files)) {
    const full = join(root, path);
    mkdirSync(dirname(full), { recursive: true });
    writeFileSync(full, content);
  }
}

/** Runs one of the tool CLIs with node and returns { status, stdout, stderr }. */
export function runTool(scriptName, args) {
  const result = spawnSync(process.execPath, [join(TOOL_DIRECTORY, scriptName), ...args], { encoding: 'utf8' });
  return { status: result.status, stdout: result.stdout, stderr: result.stderr };
}

/** Joins parts at run time, so a test source never spells a scanned-for secret shape in one piece. */
export function joined(...parts) {
  return parts.join('');
}
