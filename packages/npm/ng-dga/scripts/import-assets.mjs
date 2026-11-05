#!/usr/bin/env node
// Copies the third-party assets of @codelab/ng-dga from their pinned npm source packages (ADR-009) and records the
// sha256 of each copied file in assets/LICENCES.json. --check copies nothing and fails when a committed file differs
// from its source or from its recorded hash.
//
// Usage: node scripts/import-assets.mjs [--check]
// Exit codes: 0 in sync (or synced), 1 drift found by --check or a source package missing, 2 usage error.
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { planAssetImport, packageFinderFrom } from './lib/asset-import.mjs';
import { normalizedBytes, readJson, sha256, writeJson } from './lib/files.mjs';
import { ASSETS_DIR, ICON_MANIFEST_FILE, ICON_SVG_DIR, LICENCES_FILE, PACKAGE_ROOT } from './lib/package-paths.mjs';

export const EXIT_OK = 0;
export const EXIT_DRIFT = 1;
export const EXIT_USAGE = 2;
/** process.argv holds the node binary and the script path before the user arguments. */
const FIRST_USER_ARGUMENT = 2;
const CHECK_OPTION = '--check';

function sameContent(target, bytes) {
  if (!existsSync(target)) return false;
  return Buffer.compare(normalizedBytes(target, readFileSync(target)), normalizedBytes(target, bytes)) === 0;
}

export function main(argv) {
  const unknown = argv.filter((argument) => argument !== CHECK_OPTION);
  if (unknown.length > 0) {
    console.error(`Unknown option(s): ${unknown.join(' ')}. Usage: node scripts/import-assets.mjs [${CHECK_OPTION}]`);
    return EXIT_USAGE;
  }
  const check = argv.includes(CHECK_OPTION);
  const licences = readJson(LICENCES_FILE);
  const { errors, copies } = planAssetImport({
    licences,
    icons: readJson(ICON_MANIFEST_FILE),
    assetsDir: ASSETS_DIR,
    svgDir: ICON_SVG_DIR,
    findPackage: packageFinderFrom(PACKAGE_ROOT),
  });
  const drift = [...errors];
  for (const copy of copies) {
    const hash = sha256(normalizedBytes(copy.target, copy.bytes));
    if (check) {
      if (!sameContent(copy.target, copy.bytes)) drift.push(`${path.relative(PACKAGE_ROOT, copy.target)} differs from its source.`);
      if (copy.entry && copy.entry.sha256 !== hash) drift.push(`${copy.entry.path} sha256 in LICENCES.json is stale.`);
      continue;
    }
    mkdirSync(path.dirname(copy.target), { recursive: true });
    if (!sameContent(copy.target, copy.bytes)) writeFileSync(copy.target, copy.bytes);
    if (copy.entry) copy.entry.sha256 = hash;
  }
  if (!check && errors.length === 0) writeJson(LICENCES_FILE, licences);
  for (const message of drift) console.error(message);
  console.log(`assets: ${copies.length} file(s) ${check ? 'checked' : 'imported'}, ${drift.length} problem(s).`);
  return drift.length === 0 ? EXIT_OK : EXIT_DRIFT;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = main(process.argv.slice(FIRST_USER_ARGUMENT));
}
