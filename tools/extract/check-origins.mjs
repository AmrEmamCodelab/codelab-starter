#!/usr/bin/env node
// Checks provenance/origins/*.json (see lib/origins-check.mjs): every entry is "rewritten" with an existing target, or
// "staged" with a note or a deferred field. Reads only; needs no harvest copy, so CI can run it.
//
//   node tools/extract/check-origins.mjs [--root <repo>] [--origins <dir>]
//
// Exit 0 = every entry valid, 1 = findings, 2 = usage or internal error.
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { parseArgs, runCli, UsageError } from './lib/cli-args.mjs';
import { EXIT_CLEAN, EXIT_FINDINGS } from './lib/exit-codes.mjs';
import { checkOriginsDocument, countPromotions, OriginFindingCode } from './lib/origins-check.mjs';

const TOOL_NAME = 'check-origins';
const DEFAULT_ORIGINS_FOLDER = join('provenance', 'origins');
const ORIGINS_FILE_EXTENSION = '.json';
const Options = Object.freeze({ Root: 'root', Origins: 'origins' });

function readDocument(directory, name) {
  try {
    return JSON.parse(readFileSync(join(directory, name), 'utf8'));
  } catch (error) {
    return { parseError: error.message };
  }
}

await runCli(TOOL_NAME, () => {
  const { options } = parseArgs(process.argv.slice(2));
  const root = resolve(typeof options[Options.Root] === 'string' ? options[Options.Root] : '.');
  const originsDirectory = resolve(root, typeof options[Options.Origins] === 'string' ? options[Options.Origins] : DEFAULT_ORIGINS_FOLDER);
  if (!existsSync(originsDirectory)) throw new UsageError(`origins folder not found: ${originsDirectory}`);

  const names = readdirSync(originsDirectory).filter((name) => name.endsWith(ORIGINS_FILE_EXTENSION)).sort();
  const documents = names.map((name) => ({ name, document: readDocument(originsDirectory, name) }));
  const targetExists = (path) => existsSync(join(root, path));
  const findings = documents.flatMap(({ name, document }) => (document.parseError
    ? [{ file: name, code: OriginFindingCode.MalformedFile, detail: document.parseError }]
    : checkOriginsDocument(name, document, targetExists)));

  for (const finding of findings) console.error(`${finding.file}: [${finding.code}] ${finding.detail}`);
  const counts = countPromotions(documents.map(({ document }) => document));
  const summary = Object.entries(counts).map(([promotion, count]) => `${count} ${promotion}`).join(', ');
  console.log(`${TOOL_NAME}: ${names.length} origins file(s), ${summary}; ${findings.length} finding(s).`);
  return findings.length === 0 ? EXIT_CLEAN : EXIT_FINDINGS;
});
