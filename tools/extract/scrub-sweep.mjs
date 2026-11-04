#!/usr/bin/env node
// Scrub sweep: source-product terms, brands, IPv4 addresses, national-ID-like numbers, internal hosts, LDAP domains
// and the DGA package check, over EVERY file under --root (rules/, .claude/, docs/ and every CLAUDE.md included).
//
//   node tools/extract/scrub-sweep.mjs --root . [--terms <file>] [--exceptions <file>] [--json]
//
// Exit 0 = clean, 1 = findings, 2 = usage or internal error.
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs, runCli } from './lib/cli-args.mjs';
import { EXIT_CLEAN, EXIT_FINDINGS } from './lib/exit-codes.mjs';
import { reportFindings } from './lib/findings.mjs';
import { loadScrubConfiguration, scrubTree } from './lib/scrubber.mjs';

const TOOL_NAME = 'scrub-sweep';
const TOOL_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const DEFAULT_TERMS_FILE = join(TOOL_DIRECTORY, 'modhs-terms.txt');
const DEFAULT_EXCEPTIONS_FILE = join(TOOL_DIRECTORY, 'scrub-exceptions.txt');
const DEFAULT_ROOT = '.';
const JSON_INDENT = 2;

await runCli(TOOL_NAME, () => {
  const { options } = parseArgs(process.argv.slice(2));
  const root = resolve(typeof options.root === 'string' ? options.root : DEFAULT_ROOT);
  const configuration = loadScrubConfiguration({
    termsFile: resolve(typeof options.terms === 'string' ? options.terms : DEFAULT_TERMS_FILE),
    exceptionsFile: resolve(typeof options.exceptions === 'string' ? options.exceptions : DEFAULT_EXCEPTIONS_FILE),
  });
  const { findings, fileCount } = scrubTree(root, configuration);
  if (options.json === true) {
    console.log(JSON.stringify({ root, fileCount, findings }, null, JSON_INDENT));
  } else {
    reportFindings(TOOL_NAME, findings, fileCount);
  }
  return findings.length === 0 ? EXIT_CLEAN : EXIT_FINDINGS;
});
