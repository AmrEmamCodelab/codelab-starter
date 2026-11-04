// stdin/stdout plumbing shared by every hook entry point (Claude Code hooks contract:
// https://code.claude.com/docs/en/hooks). A hook prints at most one JSON object and exits 0.
import { ExitCodes, HookEvents, PermissionDecisions } from './constants.mjs';

/** Reads the whole of stdin and parses it as JSON (empty stdin -> {}). */
export async function readHookInput(stream = process.stdin) {
  const chunks = [];
  for await (const chunk of stream) chunks.push(chunk);
  const text = Buffer.concat(chunks).toString('utf8').trim();
  return text ? JSON.parse(text) : {};
}

/** PreToolUse refusal. permissionDecisionReason is shown to Claude, which then corrects the change. */
export function denyOutput(reason) {
  return {
    hookSpecificOutput: {
      hookEventName: HookEvents.PreToolUse,
      permissionDecision: PermissionDecisions.Deny,
      permissionDecisionReason: reason,
    },
  };
}

/** PreToolUse "ask": the user decides (used when the hook cannot tell whether a shell command writes source). */
export function askOutput(reason) {
  return {
    hookSpecificOutput: {
      hookEventName: HookEvents.PreToolUse,
      permissionDecision: PermissionDecisions.Ask,
      permissionDecisionReason: reason,
    },
  };
}

/**
 * Context for Claude without a permission decision (the normal permission flow still applies).
 * systemMessage, when given, is a warning shown to the user.
 */
export function contextOutput({ hookEventName, additionalContext, systemMessage }) {
  const output = {};
  if (additionalContext) output.hookSpecificOutput = { hookEventName, additionalContext };
  if (systemMessage) output.systemMessage = systemMessage;
  return Object.keys(output).length > 0 ? output : null;
}

/** Stop-hook refusal: Claude keeps working and receives the reason as its next instruction. */
export function blockStopOutput(reason) {
  return { decision: 'block', reason };
}

/**
 * Runs a hook: reads stdin, calls handler(input), prints its output (if any) and exits 0.
 * A crash never blocks the tool: it is reported as a warning, because CI is the authoritative gate (ADR-006).
 */
export async function runHook(hookName, handler) {
  let output;
  try {
    output = await handler(await readHookInput());
  } catch (error) {
    output = { systemMessage: `codelab ${hookName} hook failed and did not check this action: ${error.message}` };
  }
  if (output) process.stdout.write(`${JSON.stringify(output)}\n`);
  process.exitCode = ExitCodes.Success;
}
