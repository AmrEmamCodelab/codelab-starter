#!/usr/bin/env node
// Secret scan: private keys, provider tokens, connection-string passwords, secret-named settings with literal values,
// and the SHA-256 digest denylist of known source-product key material. gitleaks runs in addition from build.ps1 /
// extract.ps1 when it is installed.
//
//   node tools/extract/secret-scan.mjs --root . [--digests <file>] [--json]
//
// Exit 0 = clean, 1 = findings, 2 = usage or internal error.
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs, runCli } from './lib/cli-args.mjs';
import { EXIT_CLEAN, EXIT_FINDINGS } from './lib/exit-codes.mjs';
import { reportFindings } from './lib/findings.mjs';
import { loadDigestDenylist, scanTreeForSecrets } from './lib/secret-scanner.mjs';

const TOOL_NAME = 'secret-scan';
const TOOL_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const DEFAULT_DIGEST_FILE = join(TOOL_DIRECTORY, 'secret-digests.json');
const DEFAULT_ROOT = '.';
const JSON_INDENT = 2;

await runCli(TOOL_NAME, () => {
  const { options } = parseArgs(process.argv.slice(2));
  const root = resolve(typeof options.root === 'string' ? options.root : DEFAULT_ROOT);
  const digests = loadDigestDenylist(resolve(typeof options.digests === 'string' ? options.digests : DEFAULT_DIGEST_FILE));
  const { findings, fileCount } = scanTreeForSecrets(root, digests);
  if (options.json === true) {
    console.log(JSON.stringify({ root, fileCount, digestCount: digests.size, findings }, null, JSON_INDENT));
  } else {
    reportFindings(TOOL_NAME, findings, fileCount);
    console.log(`${TOOL_NAME}: ${digests.size} known-secret digest(s) checked.`);
  }
  return findings.length === 0 ? EXIT_CLEAN : EXIT_FINDINGS;
});
