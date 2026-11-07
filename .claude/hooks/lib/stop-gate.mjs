// The Stop gate: when Claude finishes, check what changed (git status, plus commits since the merge base with
// main, so committing in the session does not hide a change) and run the docs gate, the compiled-rules
// drift check and the whole-file rules check on the changed files. Any failure blocks the stop with the reasons,
// so Claude fixes them before handing back. stop_hook_active=true (already blocked once) only warns, to avoid loops.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { findModuleForPath, loadCompiledRules, rulesForModule } from './compiled-rules.mjs';
import { findIntroducedViolations, formatViolations } from './evaluate.mjs';
import { blockStopOutput } from './hook-io.mjs';
import { matchesAnyGlob } from './glob.mjs';
import {
  ContextLimits,
  DefaultBaseRefs,
  EnvVars,
  HookRuleIds,
  ModuleScaffoldFiles,
  PrivateDotnetPath,
  RepoPaths,
  StopGateTimeouts,
} from './constants.mjs';

const PORCELAIN_STATUS_WIDTH = 3;
const RENAME_OR_COPY = /^[RC]/;
const DELETED = /D/;
const RULES_DIR_GLOB = 'rules/**';
const DOCS_DIR_GLOB = 'docs/**';
const MODULE_RULES_GLOB = `**/${ModuleScaffoldFiles.Rules}`;
const MAX_CHECKED_FILE_BYTES = 1_000_000;
const SUCCESS_STATUS = 0;
const HEAD_REF = 'HEAD';

/** Default command runner (spawnSync). Tests may inject another. */
export function runCommand({ command, args, cwd, timeoutMs, env }) {
  const result = spawnSync(command, args, {
    cwd, env, timeout: timeoutMs, encoding: 'utf8', windowsHide: true, maxBuffer: 16 * 1024 * 1024,
  });
  return { status: result.status, stdout: result.stdout ?? '', stderr: result.stderr ?? '', error: result.error };
}

/** Parses `git status --porcelain=v1 -z -uall` output into [{ path, deleted }]. */
export function parsePorcelain(output) {
  const entries = output.split('\0').filter(Boolean);
  const changed = [];
  for (let index = 0; index < entries.length; index += 1) {
    const entry = entries[index];
    const status = entry.slice(0, 2);
    changed.push({ path: entry.slice(PORCELAIN_STATUS_WIDTH), deleted: DELETED.test(status) });
    if (RENAME_OR_COPY.test(status)) index += 1;
  }
  return changed;
}

function git({ projectRoot, runner, args }) {
  return runner({ command: 'git', args, cwd: projectRoot, timeoutMs: StopGateTimeouts.GitMs });
}

/**
 * The commit the session's changes are compared with: the merge base of HEAD and the base ref
 * (CODELAB_BASE_REF, else origin/main or main). Falls back to HEAD (only uncommitted changes) when no base ref
 * exists, and to null when the repository has no commit.
 */
export function resolveBaseCommit({ projectRoot, env, runner }) {
  const candidates = env[EnvVars.BaseRef] ? [env[EnvVars.BaseRef]] : DefaultBaseRefs;
  for (const ref of candidates) {
    const result = git({ projectRoot, runner, args: ['merge-base', ref, HEAD_REF] });
    if (result.status === SUCCESS_STATUS && result.stdout.trim()) return result.stdout.trim();
  }
  const head = git({ projectRoot, runner, args: ['rev-parse', '--verify', '--quiet', HEAD_REF] });
  return head.status === SUCCESS_STATUS ? HEAD_REF : null;
}

function committedSince({ projectRoot, runner, base }) {
  if (!base || base === HEAD_REF) return [];
  const result = git({ projectRoot, runner, args: ['diff', '--name-only', '-z', base, HEAD_REF] });
  if (result.status !== SUCCESS_STATUS) return [];
  return result.stdout.split('\0').filter(Boolean)
    .map((file) => ({ path: file, deleted: !fs.existsSync(path.join(projectRoot, file)) }));
}

function listChangedFiles({ projectRoot, runner, base }) {
  const result = git({ projectRoot, runner, args: ['status', '--porcelain=v1', '-z', '-uall'] });
  if (result.status !== SUCCESS_STATUS) return null;
  const changed = parsePorcelain(result.stdout);
  const known = new Set(changed.map((file) => file.path));
  return [...changed, ...committedSince({ projectRoot, runner, base }).filter((file) => !known.has(file.path))];
}

/** Decides which checks the change set needs. Exported for tests. */
export function planChecks({ changed, rules }) {
  const live = changed.filter((file) => !file.deleted).map((file) => file.path);
  const all = changed.map((file) => file.path);
  const touchesModules = all.some((file) => findModuleForPath(file, rules) !== null);
  const touchesDocs = all.some((file) => matchesAnyGlob(file, [DOCS_DIR_GLOB, `**/${ModuleScaffoldFiles.Docs}`]));
  const touchesRules = all.some((file) => matchesAnyGlob(file, [RULES_DIR_GLOB, MODULE_RULES_GLOB]));
  return { runDocsGate: touchesModules || touchesDocs, runRulesCheck: touchesRules, filesToCheck: live };
}

function tail(text) {
  const trimmed = text.trim();
  return trimmed.length <= ContextLimits.CommandOutputTailChars
    ? trimmed
    : `...${trimmed.slice(-ContextLimits.CommandOutputTailChars)}`;
}

function resolveDotnet(env) {
  if (env[EnvVars.Dotnet]) return env[EnvVars.Dotnet];
  return fs.existsSync(PrivateDotnetPath) ? PrivateDotnetPath : 'dotnet';
}

function hasDocsGate(projectRoot) {
  const dir = path.join(projectRoot, RepoPaths.DocsGateProject);
  return fs.existsSync(dir) && fs.readdirSync(dir).some((name) => name.endsWith('.csproj'));
}

/** A failed check, led by its rule id so the decision log can name it ([hooks.stop-docs-gate] Docs gate ...). */
function commandFailure({ ruleId, label, result }) {
  if (result.status === SUCCESS_STATUS) return null;
  const why = result.error ? result.error.message : `exit ${result.status}`;
  return `[${ruleId}] ${label} failed (${why}):\n${tail(`${result.stdout}\n${result.stderr}`)}`;
}

function docsGateArgs(base) {
  const args = ['run', '--project', RepoPaths.DocsGateProject, '--', 'check', '--root', '.'];
  return base && base !== HEAD_REF ? [...args, '--base', base] : args;
}

function runDocsGate({ projectRoot, env, runner, notes, base }) {
  if (!hasDocsGate(projectRoot)) { notes.push(`docs gate skipped: ${RepoPaths.DocsGateProject} does not exist yet.`); return null; }
  const result = runner({
    command: resolveDotnet(env),
    args: docsGateArgs(base),
    cwd: projectRoot, timeoutMs: StopGateTimeouts.DocsGateMs, env,
  });
  return commandFailure({
    ruleId: HookRuleIds.StopDocsGate, label: 'Docs gate (`dotnet run --project tools/docs-gate -- check --root .`)', result,
  });
}

function runRulesCheck({ projectRoot, env, runner, notes }) {
  if (!fs.existsSync(path.join(projectRoot, RepoPaths.RulesCompiler))) {
    notes.push(`rules drift check skipped: ${RepoPaths.RulesCompiler} does not exist yet.`);
    return null;
  }
  const result = runner({
    command: process.execPath, args: [RepoPaths.RulesCompiler, '--check'],
    cwd: projectRoot, timeoutMs: StopGateTimeouts.RulesCheckMs, env,
  });
  return commandFailure({
    ruleId: HookRuleIds.StopRulesDrift, label: 'Rules check (`node tools/rules-engine/compile.mjs --check`)', result,
  });
}

function baseVersion({ projectRoot, relativePath, runner, base }) {
  if (!base) return null;
  const result = git({ projectRoot, runner, args: ['show', `${base}:${relativePath}`] });
  return result.status === SUCCESS_STATUS ? result.stdout : null;
}

function readCheckable(absolutePath) {
  if (!fs.existsSync(absolutePath)) return null;
  const stats = fs.statSync(absolutePath);
  return stats.isFile() && stats.size <= MAX_CHECKED_FILE_BYTES ? fs.readFileSync(absolutePath, 'utf8') : null;
}

function checkFileRules({ projectRoot, rules, relativePath, runner, base }) {
  const fileRules = rulesForModule(rules, findModuleForPath(relativePath, rules));
  if (!fileRules.some((rule) => matchesAnyGlob(relativePath, rule.files))) return null;
  const after = readCheckable(path.join(projectRoot, relativePath));
  if (after === null) return null;
  const before = baseVersion({ projectRoot, relativePath, runner, base });
  const violations = findIntroducedViolations({ rules: fileRules, relativePath, before, after });
  return violations.length > 0 ? formatViolations(relativePath, violations) : null;
}

function buildReason(failures, notes) {
  const text = [
    'The Codelab stop gate found problems in your changes. Fix them (CI runs the same checks):',
    ...failures,
    ...(notes.length > 0 ? [`Notes: ${notes.join(' ')}`] : []),
  ].join('\n\n');
  return text.length <= ContextLimits.StopReasonChars ? text : `${text.slice(0, ContextLimits.StopReasonChars)}\n[...truncated]`;
}

/**
 * Runs the gate. Returns the hook output: a block decision on failures, a warning when stop_hook_active is set
 * (Claude was already sent back once), or null when everything passed or nothing changed.
 */
export function runStopGate({ input, projectRoot, env = process.env, runner = runCommand }) {
  const base = resolveBaseCommit({ projectRoot, env, runner });
  const changed = listChangedFiles({ projectRoot, runner, base });
  if (changed === null) return { systemMessage: 'codelab stop gate: git status failed; nothing was checked.' };
  if (changed.length === 0) return null;

  const { rules } = loadCompiledRules(projectRoot);
  const plan = planChecks({ changed, rules });
  const notes = [];
  const failures = [
    plan.runDocsGate ? runDocsGate({ projectRoot, env, runner, notes, base }) : null,
    plan.runRulesCheck ? runRulesCheck({ projectRoot, env, runner, notes }) : null,
    ...plan.filesToCheck.map((relativePath) => checkFileRules({ projectRoot, rules, relativePath, runner, base })),
  ].filter(Boolean);

  if (failures.length === 0) return null;
  const reason = buildReason(failures, notes);
  if (input?.stop_hook_active === true) return { systemMessage: `codelab stop gate still failing (not blocking again):\n${reason}` };
  return blockStopOutput(reason);
}
