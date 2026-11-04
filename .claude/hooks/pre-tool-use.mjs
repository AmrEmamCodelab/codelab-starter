#!/usr/bin/env node
// PreToolUse (Edit|Write|Bash|PowerShell): refuses changes that break rules/rules.compiled.json, edits to generated
// or platform-owned files, code in a module without module.rules.yaml, and shell writes into source paths; asks
// when a shell write cannot be resolved. Injects the module's MODULE.md/CLAUDE.md the first time a module is edited
// in a session. The decision runs under a time budget (lib/budgeted-decision.mjs). See README.md.
import { runHook } from './lib/hook-io.mjs';
import { decideWithBudget } from './lib/budgeted-decision.mjs';

await runHook('pre-tool-use', (input) => decideWithBudget(input));
