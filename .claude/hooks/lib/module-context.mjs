// Builds the module context injected when Claude edits a module: the compiled summary, else the head of the
// module's MODULE.md and CLAUDE.md read from disk (which also covers a new module not compiled yet).
// Each module is injected once per session; the session state lives under os.tmpdir().
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { ContextLimits, ModuleScaffoldFiles, SessionStateDirName } from './constants.mjs';

const TRUNCATION_MARK = '\n[...truncated; read the file for the rest]';
const SAFE_SESSION_ID = /[^A-Za-z0-9_-]/g;

function readDocHead(absolutePath) {
  if (!fs.existsSync(absolutePath)) return null;
  const text = fs.readFileSync(absolutePath, 'utf8').trim();
  if (text.length <= ContextLimits.ModuleDocChars) return text;
  return text.slice(0, ContextLimits.ModuleDocChars) + TRUNCATION_MARK;
}

/** The context text for a module, or null when it has neither a compiled summary nor docs on disk. */
export function buildModuleContext({ module, projectRoot }) {
  const sections = [];
  if (module.compiled?.summary) sections.push(module.compiled.summary);
  for (const docName of [ModuleScaffoldFiles.Docs, ModuleScaffoldFiles.Agent]) {
    const head = readDocHead(path.join(projectRoot, module.folder, docName));
    if (head) sections.push(`--- ${module.folder}/${docName} ---\n${head}`);
  }
  if (sections.length === 0) return null;
  const compiledNote = module.compiled
    ? ''
    : '\nThis module is not in rules/rules.compiled.json yet, so only the base rules are checked; '
      + 'run `node tools/rules-engine/compile.mjs` after editing its module.rules.yaml.';
  return `Module ${module.name} (${module.folder}) guidance:${compiledNote}\n${sections.join('\n\n')}`;
}

function stateFile(sessionId) {
  const safeId = String(sessionId).replace(SAFE_SESSION_ID, '_');
  return path.join(os.tmpdir(), SessionStateDirName, `${safeId}.json`);
}

function readState(file) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch {
    return { injectedModules: [] };
  }
}

/**
 * Records that the module's context was injected in this session; returns false when it already was.
 * Without a session id every call returns true (inject every time). State-write failures are ignored: the
 * worst case is a repeated injection.
 */
export function markModuleInjected({ sessionId, moduleFolder }) {
  if (!sessionId) return true;
  const file = stateFile(sessionId);
  const state = readState(file);
  const key = moduleFolder.toLowerCase();
  if (state.injectedModules.includes(key)) return false;
  state.injectedModules.push(key);
  try {
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, JSON.stringify(state));
  } catch {
    // Best effort: a lost state file only means the module context is injected again.
  }
  return true;
}
