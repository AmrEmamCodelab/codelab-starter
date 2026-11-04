#!/usr/bin/env node
// SessionStart: tells Claude how the guardrails work and what rules are compiled. See README.md.
import { contextOutput, runHook } from './lib/hook-io.mjs';
import { buildSessionContext } from './lib/session-context.mjs';
import { resolveProjectRoot } from './lib/paths.mjs';
import { HookEvents } from './lib/constants.mjs';

await runHook('session-start', (input) => contextOutput({
  hookEventName: HookEvents.SessionStart,
  additionalContext: buildSessionContext(resolveProjectRoot(input)),
}));
