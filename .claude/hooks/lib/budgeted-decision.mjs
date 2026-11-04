// Runs the PreToolUse decision in a worker thread with a wall-clock budget. A rule regex with catastrophic
// backtracking would otherwise run past the Claude Code hook timeout, and a timed-out hook does NOT block the tool
// call (it fails open). On budget exhaustion the call is denied with a clear reason instead.
import { Worker } from 'node:worker_threads';
import { denyOutput } from './hook-io.mjs';
import { EnvVars, EvaluationBudgets, HookRuleIds } from './constants.mjs';

const WORKER_URL = new URL('./decision-worker.mjs', import.meta.url);

function budgetMs(env) {
  const configured = Number(env[EnvVars.EvaluationBudgetMs]);
  return Number.isFinite(configured) && configured > 0 ? configured : EvaluationBudgets.PreToolUseMs;
}

function timeoutRefusal(input, budget) {
  const target = input?.tool_input?.file_path ?? input?.tool_input?.command ?? 'this change';
  return denyOutput(`[${HookRuleIds.EvaluationTimedOut}] checking the content rules for ${target} took longer than `
    + `${budget} ms, so the change was not verified. Fix: report the slow rule to the platform owner (a regex with `
    + 'nested quantifiers); split very large writes into smaller edits.');
}

/** decidePreToolUse(input) in a worker; resolves to its output, or to a deny when the budget runs out. */
export function decideWithBudget(input, env = process.env) {
  const budget = budgetMs(env);
  return new Promise((resolve, reject) => {
    const worker = new Worker(WORKER_URL, { workerData: { input } });
    const timer = setTimeout(() => {
      worker.unref();
      worker.terminate();
      resolve(timeoutRefusal(input, budget));
    }, budget);
    worker.once('message', (output) => { clearTimeout(timer); resolve(output); });
    worker.once('error', (error) => { clearTimeout(timer); reject(error); });
  });
}
