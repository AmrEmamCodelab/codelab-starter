// The PreToolUse decision for Edit, Write, Bash and PowerShell calls. Pure apart from reading the project files
// and the session state, so tests call it directly and through the entry script.
import fs from 'node:fs';
import { askOutput, denyOutput, contextOutput } from './hook-io.mjs';
import { findModuleForPath, loadCompiledRules, rulesForModule } from './compiled-rules.mjs';
import { findIntroducedViolations, formatViolations } from './evaluate.mjs';
import { simulateEdit } from './edit-simulation.mjs';
import { detectShellWrites, WriteKinds } from './shell-writes.mjs';
import { buildModuleContext, markModuleInjected } from './module-context.mjs';
import {
  checkGenerated, checkModuleHasRules, checkModuleRulesExemptions, checkPlatformOwner, isGitIgnored, isNullDevice,
} from './path-policy.mjs';
import { resolveProjectRoot, toAbsolute, toRepoRelative } from './paths.mjs';
import {
  DeletionCommands, HookEvents, HookRuleIds, RemoveItemCmdlet, RepoPaths, TemporaryTargetPattern, ToolNames,
} from './constants.mjs';

const MISSING_RULES_WARNING = `${RepoPaths.CompiledRules} is missing or invalid: content rules were NOT checked `
  + '(path guards still apply). Run `node tools/rules-engine/compile.mjs`.';

function formatRefusal({ ruleId, reason }) {
  return `[${ruleId}] ${reason}`;
}

function readFileOrNull(absolutePath) {
  return fs.existsSync(absolutePath) ? fs.readFileSync(absolutePath, 'utf8') : null;
}

/** The file text before and after the call; after is null when the Edit cannot apply (the tool will fail). */
function contentChange(toolName, toolInput, absolutePath) {
  const before = readFileOrNull(absolutePath);
  if (toolName === ToolNames.Write) return { before, after: toolInput.content ?? '' };
  // An Edit with an empty old_string creates the file (or fills an empty one): it is a Write of new_string.
  if (!toolInput.old_string && (before === null || before === '')) return { before, after: toolInput.new_string ?? '' };
  if (before === null) return { before, after: null };
  const after = simulateEdit({
    original: before,
    oldString: toolInput.old_string,
    newString: toolInput.new_string ?? '',
    replaceAll: toolInput.replace_all === true,
  });
  return { before, after };
}

function pathRefusal(context) {
  return checkGenerated(context) ?? checkPlatformOwner(context) ?? checkModuleHasRules(context);
}

function moduleContextFor({ module, projectRoot, sessionId }) {
  if (!module) return null;
  const text = buildModuleContext({ module, projectRoot });
  if (!text) return null;
  return markModuleInjected({ sessionId, moduleFolder: module.folder }) ? text : null;
}

function decideFileTool({ input, env, projectRoot, loaded }) {
  const toolInput = input.tool_input ?? {};
  if (!toolInput.file_path) return null;
  const absolutePath = toAbsolute(toolInput.file_path, projectRoot);
  const relativePath = toRepoRelative(absolutePath, projectRoot);
  if (relativePath === null || relativePath === '') return null;

  const { rules } = loaded;
  const module = findModuleForPath(relativePath, rules);
  const refused = pathRefusal({ relativePath, absolutePath, rules, env, module, projectRoot });
  if (refused) return denyOutput(formatRefusal(refused));

  const { before, after } = contentChange(input.tool_name, toolInput, absolutePath);
  const exemptionsRefused = checkModuleRulesExemptions({ relativePath, before, after, env });
  if (exemptionsRefused) return denyOutput(formatRefusal(exemptionsRefused));
  if (after !== null) {
    const violations = findIntroducedViolations({ rules: rulesForModule(rules, module), relativePath, before, after });
    if (violations.length > 0) return denyOutput(formatViolations(relativePath, violations));
  }

  return contextOutput({
    hookEventName: HookEvents.PreToolUse,
    additionalContext: moduleContextFor({ module, projectRoot, sessionId: input.session_id }),
    systemMessage: loaded.status === 'ok' ? null : MISSING_RULES_WARNING,
  });
}

/** Shell commands whose write is a file deletion: no content to check, but no Edit or Write tool can do it either. */
const DELETION_KINDS = new Set([
  ...DeletionCommands.map((command) => `${command} ${WriteKinds.Remove}`),
  `${WriteKinds.PowerShellCmdlet} ${RemoveItemCmdlet}`,
]);

const WRITE_FIX = 'Fix: use the Edit or Write tool for source files '
  + '(scratch output belongs in the OS temp dir or a gitignored folder).';

/** The hook does not refuse git rm or git clean; the deletion still shows in the diff and CI checks the result. */
const DELETE_FIX = 'Fix: delete a source file with git so the removal is reviewed in the diff: '
  + '`git rm -- <path>` for a tracked file (`git rm -r -- <folder>` for a folder), '
  + '`git clean -f -- <path>` for an untracked file you created. Delete only files you created.';

function describeShellWrite({ write, relativePath, rules, module }) {
  const isDeletion = DELETION_KINDS.has(write.kind);
  const header = `[${HookRuleIds.ShellWriteIntoSource}] ${write.kind} ${isDeletion ? 'deletes' : 'writes into'} the source `
    + `path ${relativePath}. Shell writes bypass the rule checks. ${isDeletion ? DELETE_FIX : WRITE_FIX}`;
  if (write.content === null) return header;
  const violations = findIntroducedViolations({ rules: rulesForModule(rules, module), relativePath, before: null, after: write.content });
  return violations.length > 0 ? `${header}\n${formatViolations(relativePath, violations)}` : header;
}

const REPO_ROOT_DISPLAY = '.';

function shellWriteRefusal({ write, input, projectRoot, rules, env }) {
  if (write.target === null || isNullDevice(write.target)) return null;
  const absolutePath = toAbsolute(write.target, input.cwd || projectRoot);
  const relativePath = toRepoRelative(absolutePath, projectRoot);
  if (relativePath === null) return null;
  if (relativePath === '') return describeShellWrite({ write, relativePath: REPO_ROOT_DISPLAY, rules, module: null });
  const module = findModuleForPath(relativePath, rules);
  const pathRefused = checkGenerated({ relativePath, absolutePath, rules }) ?? checkPlatformOwner({ relativePath, env });
  if (pathRefused) return formatRefusal(pathRefused);
  if (isGitIgnored({ relativePath, projectRoot })) return null;
  return describeShellWrite({ write, relativePath, rules, module });
}

/** A write whose target nobody can name statically, unless it plainly goes to a temp or home folder. */
function isUnresolvedWrite(write) {
  if (write.opaque) return true;
  return write.target === null && !TemporaryTargetPattern.test(write.raw ?? '');
}

function describeUnresolved(writes) {
  const kinds = [...new Set(writes.map((write) => (write.raw ? `${write.kind} -> ${write.raw}` : write.kind)))];
  return `[${HookRuleIds.ShellWriteUnresolved}] this command may write files whose path the hook cannot resolve `
    + `(${kinds.join('; ')}). Shell writes into source bypass the rule checks. Fix: use the Edit or Write tool for `
    + 'source files, or write to an explicit temp path. Approve only if no tracked source file is written.';
}

function decideShellTool({ input, env, projectRoot, loaded }) {
  const writes = detectShellWrites(input.tool_input?.command ?? '');
  const refusals = writes
    .map((write) => shellWriteRefusal({ write, input, projectRoot, rules: loaded.rules, env }))
    .filter(Boolean);
  if (refusals.length > 0) return denyOutput(refusals.join('\n\n'));
  const unresolved = writes.filter(isUnresolvedWrite);
  return unresolved.length > 0 ? askOutput(describeUnresolved(unresolved)) : null;
}

const HANDLERS = Object.freeze({
  [ToolNames.Edit]: decideFileTool,
  [ToolNames.Write]: decideFileTool,
  [ToolNames.Bash]: decideShellTool,
  [ToolNames.PowerShell]: decideShellTool,
});

/** The hook output object for a PreToolUse input, or null to stay silent (normal permission flow). */
export function decidePreToolUse(input, env = process.env) {
  const handler = HANDLERS[input?.tool_name];
  if (!handler) return null;
  const projectRoot = resolveProjectRoot(input, env);
  return handler({ input, env, projectRoot, loaded: loadCompiledRules(projectRoot) });
}
