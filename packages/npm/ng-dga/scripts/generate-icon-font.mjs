#!/usr/bin/env node
// Generates the owned icon font of @codelab/ng-dga (ADR-009) from icons/icons.json and icons/svg:
//   assets/icons/<stem>.woff2, assets/icons/<stem>.json and src/lib/icon/dga-icon-names.generated.ts.
// --check regenerates in memory and fails when any committed output differs (the generator is deterministic).
//
// Usage: node scripts/generate-icon-font.mjs [--check]
// Exit codes: 0 generated (or in sync), 1 invalid manifest or drift found by --check, 2 usage error.
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { listFilesRelative, normalizedBytes, readJson } from './lib/files.mjs';
import { buildIconFont, validateIconManifest } from './lib/icon-font.mjs';
import { ICON_MANIFEST_FILE, ICON_OUTPUT_DIR, ICON_SVG_DIR, ICON_TYPESCRIPT_FILE, PACKAGE_ROOT } from './lib/package-paths.mjs';

export const EXIT_OK = 0;
export const EXIT_FAILED = 1;
export const EXIT_USAGE = 2;
/** process.argv holds the node binary and the script path before the user arguments. */
const FIRST_USER_ARGUMENT = 2;
const CHECK_OPTION = '--check';
const SVG_EXTENSION = '.svg';
const WOFF2_EXTENSION = '.woff2';
const JSON_EXTENSION = '.json';

/** The SVG source names present in icons/svg. */
export function svgSourceNames() {
  return new Set(listFilesRelative(ICON_SVG_DIR).filter((file) => file.endsWith(SVG_EXTENSION)).map((file) => path.basename(file, SVG_EXTENSION)));
}

/** Reads one SVG source with LF line endings, so a CRLF checkout builds the same font. */
export function readSvgSource(name) {
  const file = path.join(ICON_SVG_DIR, `${name}${SVG_EXTENSION}`);
  return normalizedBytes(file, readFileSync(file)).toString('utf8');
}

/** Builds every output in memory: [{ file (absolute), bytes }]. Throws with the manifest problems when invalid. */
export async function generateOutputs() {
  const manifest = readJson(ICON_MANIFEST_FILE);
  const errors = validateIconManifest(manifest, svgSourceNames());
  if (errors.length > 0) throw new Error(`icons/icons.json is invalid:\n  ${errors.join('\n  ')}`);
  const built = await buildIconFont(manifest, readSvgSource);
  return [
    { file: path.join(ICON_OUTPUT_DIR, `${manifest.fileStem}${WOFF2_EXTENSION}`), bytes: built.woff2 },
    { file: path.join(ICON_OUTPUT_DIR, `${manifest.fileStem}${JSON_EXTENSION}`), bytes: Buffer.from(built.codepointMap, 'utf8') },
    { file: ICON_TYPESCRIPT_FILE, bytes: Buffer.from(built.typescript, 'utf8') },
  ];
}

/** The outputs whose committed content differs from a fresh build (line endings ignored for text). */
export function driftedOutputs(outputs) {
  return outputs.filter(({ file, bytes }) => {
    if (!existsSync(file)) return true;
    return Buffer.compare(normalizedBytes(file, readFileSync(file)), normalizedBytes(file, bytes)) !== 0;
  });
}

export async function main(argv) {
  const unknown = argv.filter((argument) => argument !== CHECK_OPTION);
  if (unknown.length > 0) {
    console.error(`Unknown option(s): ${unknown.join(' ')}. Usage: node scripts/generate-icon-font.mjs [${CHECK_OPTION}]`);
    return EXIT_USAGE;
  }
  let outputs;
  try {
    outputs = await generateOutputs();
  } catch (error) {
    console.error(error.message);
    return EXIT_FAILED;
  }
  const drifted = driftedOutputs(outputs);
  if (argv.includes(CHECK_OPTION)) {
    for (const { file } of drifted) console.error(`${path.relative(PACKAGE_ROOT, file)} is out of date; run npm run icons:generate -w @codelab/ng-dga.`);
    console.log(`icon font: ${outputs.length} output(s) checked, ${drifted.length} out of date.`);
    return drifted.length === 0 ? EXIT_OK : EXIT_FAILED;
  }
  for (const { file, bytes } of drifted) writeFileSync(file, bytes);
  console.log(`icon font: ${outputs.length} output(s), ${drifted.length} written.`);
  return EXIT_OK;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = await main(process.argv.slice(FIRST_USER_ARGUMENT));
}
