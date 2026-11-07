#!/usr/bin/env node
// Comment counter and comments-only differ for the reduce-comments skill. Zero dependencies (Node 24).
//
//   node .claude/skills/reduce-comments/scripts/comment-diff.mjs count  <file> [...] [--json]
//   node .claude/skills/reduce-comments/scripts/comment-diff.mjs verify <baseline> <edited> [--json]
//
// verify exit codes: 0 only comments changed, 1 code changed (revert), 2 usage, unreadable or unsupported file.
// Languages: C# (.cs), TypeScript/JavaScript (.ts .mts .cts .js .mjs .cjs), HTML templates (.html), SQL (.sql).
import { run } from './lib/cli.mjs';

process.exitCode = run(process.argv.slice(2), {
  out: (line) => process.stdout.write(`${line}\n`),
  err: (line) => process.stderr.write(`${line}\n`),
});
