#!/usr/bin/env node
// Stop: runs the docs gate, the compiled-rules drift check and the whole-file rules check on what git status
// reports as changed, and blocks the stop with the reasons when something fails. See README.md.
import { runHook } from './lib/hook-io.mjs';
import { runStopGate } from './lib/stop-gate.mjs';
import { resolveProjectRoot } from './lib/paths.mjs';

await runHook('on-stop-gate', (input) => runStopGate({ input, projectRoot: resolveProjectRoot(input) }));
