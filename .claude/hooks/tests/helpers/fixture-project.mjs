// Test helpers: a throwaway project in the OS temp dir (never inside the repo, so fixture module.rules.yaml files
// are not picked up by the rules compiler or the docs gate) and a runner that pipes fixture JSON into a hook.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
export const HOOKS_DIR = path.resolve(HERE, '..', '..');
export const FIXTURES_DIR = path.resolve(HERE, '..', 'fixtures');
const PROJECT_PLACEHOLDER = /__PROJECT__/g;
// Hooks run with their temp dir inside the fixture project, so session state is deleted with the project.
const FIXTURE_TEMP_DIR = '.hook-tmp';
const FIXTURE_GITIGNORE = `bin/\nobj/\nnode_modules/\n/feed/*\n/${FIXTURE_TEMP_DIR}/\n`;
const TEMP_ENV_VARS = ['TEMP', 'TMP', 'TMPDIR'];
const HOOK_ENV_VARS_TO_CLEAR = [
  'CLAUDE_PROJECT_DIR', 'CODELAB_PLATFORM_OWNER', 'CODELAB_DOCS_NOT_NEEDED', 'CODELAB_BASE_REF', 'CODELAB_HOOK_BUDGET_MS',
];

/** Files of the standard fixture project: one compiled module (ServiceRequests) with docs and code. */
export const STANDARD_FILES = Object.freeze({
  'backend/src/Modules/ServiceRequests/module.rules.yaml': 'module: ServiceRequests\n',
  'backend/src/Modules/ServiceRequests/MODULE.md':
    '# ServiceRequests\n\n## Purpose\nThe sample request module.\n\n## Rules\nUse TimeProvider; controllers carry [HasPermission].\n',
  'backend/src/Modules/ServiceRequests/CLAUDE.md': '# ServiceRequests (generated)\nRead MODULE.md before editing.\n',
  'backend/src/Modules/ServiceRequests/Application/SubmitRequest.cs':
    'namespace Starter.Modules.ServiceRequests.Application;\n\npublic sealed class SubmitRequest\n{\n}\n',
  'backend/src/Modules/ServiceRequests/Api/ServiceRequestsEndpoint.cs':
    'namespace Starter.Modules.ServiceRequests.Api;\n\npublic sealed class ServiceRequestsEndpoint\n{\n    // dependencies\n}\n',
  'backend/src/Modules/ServiceRequests/Api/ServiceRequestsController.cs':
    'namespace Starter.Modules.ServiceRequests.Api;\n\n[HasPermission(Permissions.ServiceRequests.View)]\npublic sealed class ServiceRequestsController\n{\n}\n',
});

function git(root, args) {
  const result = spawnSync('git', args, { cwd: root, encoding: 'utf8', windowsHide: true });
  if (result.status !== 0) throw new Error(`git ${args.join(' ')} failed: ${result.stderr}`);
  return result.stdout;
}

/** Writes files (relative path -> text) under root. */
export function writeFiles(root, files) {
  for (const [relativePath, text] of Object.entries(files)) {
    const absolutePath = path.join(root, relativePath);
    fs.mkdirSync(path.dirname(absolutePath), { recursive: true });
    fs.writeFileSync(absolutePath, text);
  }
}

/**
 * Creates a git-initialised temp project. withCompiledRules copies fixtures/rules.compiled.json to
 * rules/rules.compiled.json. commit=true commits everything so later changes show up in git status.
 */
export function createFixtureProject({ files = STANDARD_FILES, withCompiledRules = true, commit = false } = {}) {
  const root = fs.realpathSync(fs.mkdtempSync(path.join(os.tmpdir(), 'codelab-hooks-test-')));
  git(root, ['init', '-q']);
  writeFiles(root, { '.gitignore': FIXTURE_GITIGNORE, ...files });
  if (withCompiledRules) {
    writeFiles(root, { 'rules/rules.compiled.json': fs.readFileSync(path.join(FIXTURES_DIR, 'rules.compiled.json'), 'utf8') });
  }
  if (commit) {
    git(root, ['add', '-A']);
    git(root, ['-c', 'user.name=hooks-test', '-c', 'user.email=hooks-test@example.invalid', 'commit', '-q', '-m', 'fixture']);
  }
  return root;
}

export function removeProject(root) {
  fs.rmSync(root, { recursive: true, force: true });
}

/** Loads tests/fixtures/inputs/<name>.json with __PROJECT__ replaced by the project root (forward slashes). */
export function loadInput(name, projectRoot, overrides = {}) {
  const text = fs.readFileSync(path.join(FIXTURES_DIR, 'inputs', `${name}.json`), 'utf8');
  const input = JSON.parse(text.replace(PROJECT_PLACEHOLDER, projectRoot.replace(/\\/g, '/')));
  return { ...input, session_id: `${input.session_id}-${process.pid}-${Date.now()}`, ...overrides };
}

/** A PreToolUse input built in code, for cases that need no fixture file. */
export function toolInput({ projectRoot, toolName, toolInputFields, sessionId = `inline-${process.pid}-${Date.now()}-${Math.random()}` }) {
  return { session_id: sessionId, cwd: projectRoot, hook_event_name: 'PreToolUse', tool_name: toolName, tool_input: toolInputFields };
}

/** Runs a hook script with the input on stdin. Returns { status, output (parsed JSON or null), stderr }. */
export function runHookScript(scriptName, input, { projectRoot, env = {} } = {}) {
  const childEnv = { ...process.env };
  for (const name of HOOK_ENV_VARS_TO_CLEAR) delete childEnv[name];
  if (projectRoot) {
    const tempDir = path.join(projectRoot, FIXTURE_TEMP_DIR);
    fs.mkdirSync(tempDir, { recursive: true });
    childEnv.CLAUDE_PROJECT_DIR = projectRoot;
    for (const name of TEMP_ENV_VARS) childEnv[name] = tempDir;
  }
  Object.assign(childEnv, env);
  const result = spawnSync(process.execPath, [path.join(HOOKS_DIR, scriptName)], {
    input: JSON.stringify(input), env: childEnv, encoding: 'utf8', windowsHide: true,
  });
  const stdout = result.stdout.trim();
  return { status: result.status, output: stdout ? JSON.parse(stdout) : null, stderr: result.stderr };
}

/** The permissionDecision of a PreToolUse output, or null. */
export function decisionOf(output) {
  return output?.hookSpecificOutput?.permissionDecision ?? null;
}

export function reasonOf(output) {
  return output?.hookSpecificOutput?.permissionDecisionReason ?? '';
}

export function contextOf(output) {
  return output?.hookSpecificOutput?.additionalContext ?? '';
}
