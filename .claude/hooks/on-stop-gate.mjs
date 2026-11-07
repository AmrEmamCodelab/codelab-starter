#!/usr/bin/env node
// Stop: runs the docs gate, the compiled-rules drift check and the whole-file rules check on what git status
// reports as changed, and blocks the stop with the reasons when something fails. With CODELAB_HOOK_LOG set, the
// outcome is appended to that file as one JSON line (lib/decision-log.mjs). See README.md.
import { runHook } from './lib/hook-io.mjs';
import { runStopGate } from './lib/stop-gate.mjs';
import { recordDecision } from './lib/decision-log.mjs';
import { resolveProjectRoot } from './lib/paths.mjs';
import { HookNames } from './lib/constants.mjs';

await runHook(HookNames.StopGate, (input) => runStopGate({ input, projectRoot: resolveProjectRoot(input) }), {
  onOutput: (input, output) => recordDecision({
    hook: HookNames.StopGate, input, output, projectRoot: resolveProjectRoot(input),
  }),
});
