// The hook decision log: when CODELAB_HOOK_LOG names a file, the PreToolUse hook and the Stop gate append one JSON
// line per decision ({ ts, hook, tool, path, paths, decision, ruleIds, skill }). `paths` lists every repo file the
// call targets (an Edit/Write file_path, or each file a Bash/PowerShell command writes or deletes); `path` is the
// first of them. The pipeline proofs read it to show a refused edit that was then corrected. Logging is
// best-effort: it never throws and never changes the decision.
import fs from 'node:fs';
import path from 'node:path';
import { toAbsolute, toRepoRelative } from './paths.mjs';
import { detectShellWrites } from './shell-writes.mjs';
import { isNullDevice } from './path-policy.mjs';
import {
  EnvVars,
  LoggedDecisions,
  PermissionDecisions,
  RuleIdReferencePattern,
  ShellToolNames,
  SkillReferencePattern,
} from './constants.mjs';

const LINE_END = '\n';
const STOP_BLOCK = 'block';

/** The text a decision carries: the refusal reason, the Stop block reason or the warning. */
function decisionText(output) {
  return [
    output?.hookSpecificOutput?.permissionDecisionReason,
    output?.reason,
    output?.systemMessage,
  ].filter(Boolean).join('\n');
}

/**
 * allow | deny | warn for a hook output. A PreToolUse deny or a Stop block is deny; an "ask" (the user decides) or a
 * warning without a decision is warn; anything else (silence, context only) is allow.
 */
export function classifyDecision(output) {
  const permission = output?.hookSpecificOutput?.permissionDecision;
  if (permission === PermissionDecisions.Deny || output?.decision === STOP_BLOCK) return LoggedDecisions.Deny;
  if (permission === PermissionDecisions.Ask || output?.systemMessage) return LoggedDecisions.Warn;
  return LoggedDecisions.Allow;
}

/** Every distinct rule id ([base.time-provider], [hooks.module-without-rules]) named in the text, in order. */
export function ruleIdsIn(text) {
  return [...new Set([...String(text ?? '').matchAll(RuleIdReferencePattern)].map((match) => match[1]))];
}

/** The first skill named as "Use the `<skill>` skill", or null. */
export function skillIn(text) {
  return String(text ?? '').match(SkillReferencePattern)?.[1] ?? null;
}

/** The repo-relative path of the tool's file (forward slashes), the raw path when outside the project, or null. */
function loggedFilePath(input, projectRoot) {
  const filePath = input?.tool_input?.file_path;
  if (!filePath) return null;
  if (!projectRoot) return filePath;
  return toRepoRelative(toAbsolute(filePath, projectRoot), projectRoot) ?? filePath;
}

/**
 * The repo files a shell command writes or deletes (repo-relative, distinct, in command order). Targets outside the
 * project, the null device and targets nobody can name statically are left out; without a project root the raw
 * targets are kept.
 */
function loggedShellPaths(input, projectRoot) {
  const targets = detectShellWrites(input?.tool_input?.command ?? '')
    .map((write) => write.target)
    .filter((target) => target !== null && !isNullDevice(target));
  if (!projectRoot) return [...new Set(targets)];
  const baseDir = input?.cwd || projectRoot;
  const relative = targets
    .map((target) => toRepoRelative(toAbsolute(target, baseDir), projectRoot))
    .filter((relativePath) => relativePath !== null && relativePath !== '');
  return [...new Set(relative)];
}

/** Every file the call targets: the Edit/Write file, or the files a Bash/PowerShell command writes. */
export function loggedPaths(input, projectRoot) {
  if (ShellToolNames.includes(input?.tool_name)) return loggedShellPaths(input, projectRoot);
  const filePath = loggedFilePath(input, projectRoot);
  return filePath === null ? [] : [filePath];
}

/**
 * One log entry for a hook run.
 * @param {{ hook: string, input: object, output: object|null, projectRoot?: string, now?: Date }} args
 */
export function buildLogEntry({ hook, input, output, projectRoot, now = new Date() }) {
  const text = decisionText(output);
  const decision = classifyDecision(output);
  const paths = loggedPaths(input, projectRoot);
  return {
    ts: now.toISOString(),
    hook,
    tool: input?.tool_name ?? null,
    path: paths[0] ?? null,
    paths,
    decision,
    ruleIds: decision === LoggedDecisions.Allow ? [] : ruleIdsIn(text),
    skill: decision === LoggedDecisions.Allow ? null : skillIn(text),
  };
}

/**
 * Appends the entry as one JSON line to the file named by CODELAB_HOOK_LOG. Does nothing when the variable is unset.
 * Returns true when a line was written; any failure (bad path, locked file) returns false and is otherwise ignored.
 */
export function appendDecisionLog({ entry, env = process.env }) {
  const logPath = env[EnvVars.HookLog];
  if (!logPath) return false;
  try {
    fs.mkdirSync(path.dirname(path.resolve(logPath)), { recursive: true });
    fs.appendFileSync(logPath, `${JSON.stringify(entry)}${LINE_END}`, 'utf8');
    return true;
  } catch {
    return false;
  }
}

/** Builds and appends the entry for one hook run (best-effort; see appendDecisionLog). */
export function recordDecision({ hook, input, output, projectRoot, env = process.env, now = new Date() }) {
  if (!env[EnvVars.HookLog]) return false;
  try {
    return appendDecisionLog({ entry: buildLogEntry({ hook, input, output, projectRoot, now }), env });
  } catch {
    return false;
  }
}
