#!/usr/bin/env node
// Facts-drift gate: every path, command, type, skill and agent that an agent, skill or doc names must exist, and no
// stale-fact or source-product term may appear. Scope, checks and configuration: tools/facts-drift/MODULE.md.
//
//   node tools/facts-drift/check.mjs --root . [--json] [--config <file>] [--stale-terms <file>] [--allowlist <file>]
//
// Exit 0 = clean, 1 = findings, 2 = usage or internal error.
import { runCli } from '../extract/lib/cli-args.mjs';
import { EXIT_CLEAN, EXIT_FINDINGS } from '../extract/lib/exit-codes.mjs';
import { reportFindings } from '../extract/lib/findings.mjs';
import { checkRepository } from './lib/checker.mjs';
import { parseCommandLine } from './lib/cli.mjs';
import { loadConfiguration } from './lib/config.mjs';
import { JSON_INDENT, TOOL_NAME } from './lib/constants.mjs';

function printUnusedAllowlist(unused, write) {
  for (const entry of unused) {
    write(`${TOOL_NAME}: allowlist line ${entry.line} suppresses nothing (${entry.ruleId} | ${entry.fileGlob} | ${entry.subject}); remove it.`);
  }
}

await runCli(TOOL_NAME, () => {
  const { root, json, ...files } = parseCommandLine(process.argv.slice(2));
  const configuration = loadConfiguration(files);
  const result = checkRepository(root, configuration);
  if (json) {
    console.log(JSON.stringify({
      root,
      fileCount: result.fileCount,
      findings: result.findings,
      suppressed: result.suppressed,
      unusedAllowlist: result.unusedAllowlist,
    }, null, JSON_INDENT));
  } else {
    reportFindings(TOOL_NAME, result.findings, result.fileCount);
    if (result.suppressed.length > 0) console.log(`${TOOL_NAME}: ${result.suppressed.length} finding(s) allowlisted.`);
    printUnusedAllowlist(result.unusedAllowlist, console.error);
  }
  return result.findings.length === 0 ? EXIT_CLEAN : EXIT_FINDINGS;
});
