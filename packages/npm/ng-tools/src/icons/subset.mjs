// Icon subsetting: ship only the icons an app uses, so the icon font that blocks the first paint stays small.
//
// Inputs: the folders to scan (the app's sources AND the sources of every library that renders icons itself, such as
// @codelab/ng-dga's own components), the icon code point map (icon-map.mjs) and optional names to always keep.
// Outputs: a subset code point map in the same shape as the full one and, when a font generator is given, a subset
// WOFF2 with the same code points (so the glyph table in the library stays valid).
//
// Generator contract (implemented by @codelab/ng-dga's scripts/lib/icon-font.mjs, read from the source tree, never
// imported as a dependency): an ES module exporting
//   buildIconFont(manifest, readSvg) -> Promise<{ woff2: Buffer, codepointMap: string, typescript: string }>
// where manifest = { fontFamily, fileStem, icons: [{ name, codepoint, mirrorInRtl }] } and readSvg(name) returns the
// SVG source text of one icon (here: <svgDir>/<name>.svg).
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { isFolder, listSourceFiles, toPosixRelative } from '../shared/source-files.mjs';
import { iconNamesOf } from './icon-map.mjs';
import { findUnknownIconNames, findUsedIconNames, isIconSource } from './icon-usage.mjs';

export const SUBSET_JSON_EXTENSION = '.json';
export const SUBSET_FONT_EXTENSION = '.woff2';
export const SVG_EXTENSION = '.svg';
export const GENERATOR_EXPORT = 'buildIconFont';
const JSON_INDENT = 2;
const NEWLINE = '\n';

/**
 * Scans `dirs` for icon usage against `map`.
 * @param {string[]} dirs absolute folders
 * @param {{ fontFamily: string, icons: Record<string, object> }} map
 * @param {{ keep?: string[], baseDir?: string }} [options] `keep`: names always kept (they must be known);
 *   `baseDir`: findings are reported relative to it (default the working directory)
 * @returns {{ used: string[], unknown: { file: string, line: number, column: number, element?: string, detail: string }[], fileCount: number, error?: string }}
 *   `used` in font order
 */
export function collectIconUsage(dirs, map, { keep = [], baseDir = process.cwd() } = {}) {
  const missing = dirs.find((dir) => !isFolder(dir));
  if (missing !== undefined) return { used: [], unknown: [], fileCount: 0, error: `folder not found: ${missing}` };
  const known = new Set(iconNamesOf(map));
  const used = new Set();
  const unknown = keep.filter((name) => !known.has(name)).map((name) => ({ file: '--keep', line: 0, column: 0, detail: name }));
  for (const name of keep.filter((candidate) => known.has(candidate))) used.add(name);
  const files = dirs.flatMap((dir) => listSourceFiles(dir, isIconSource));
  for (const file of files) {
    const text = fs.readFileSync(file, 'utf8');
    for (const name of findUsedIconNames(file, text, known)) used.add(name);
    const relative = toPosixRelative(baseDir, file);
    unknown.push(...findUnknownIconNames(file, text, known).map((finding) => ({ file: relative, ...finding })));
  }
  return { used: iconNamesOf(map).filter((name) => used.has(name)), unknown, fileCount: files.length };
}

/** The code point map restricted to `used` (font order kept). */
export function subsetIconMap(map, used) {
  const keep = new Set(used);
  return { fontFamily: map.fontFamily, icons: Object.fromEntries(Object.entries(map.icons).filter(([name]) => keep.has(name))) };
}

/** A code point map as the JSON text ng-dga ships (two-space indent, trailing newline). */
export function renderIconMap(map) {
  return JSON.stringify(map, null, JSON_INDENT) + NEWLINE;
}

/** The generator manifest for `used` icons. */
export function subsetManifest(map, used, fileStem) {
  const icons = Object.entries(subsetIconMap(map, used).icons)
    .map(([name, icon]) => ({ name, codepoint: icon.codepoint, mirrorInRtl: icon.mirrorInRtl === true }));
  return { fontFamily: map.fontFamily, fileStem, icons };
}

/**
 * Builds the subset font with the generator module at `generator` and the SVG sources in `svgDir`.
 * @returns {Promise<Buffer>} the WOFF2 bytes
 * @throws {Error} when the module does not export the generator function or an SVG source is missing
 */
export async function buildSubsetFont({ map, used, fileStem, generator, svgDir }) {
  const module = await import(pathToFileURL(path.resolve(generator)).href);
  if (typeof module[GENERATOR_EXPORT] !== 'function') throw new Error(`${generator} does not export ${GENERATOR_EXPORT}(manifest, readSvg).`);
  const readSvg = (name) => fs.readFileSync(path.join(svgDir, `${name}${SVG_EXTENSION}`), 'utf8');
  const built = await module[GENERATOR_EXPORT](subsetManifest(map, used, fileStem), readSvg);
  return Buffer.from(built.woff2);
}

/**
 * The subset files to write under `outDir`: `<stem>.json` always, `<stem>.woff2` when `generator` is given.
 * @returns {Promise<{ file: string, bytes: Buffer }[]>}
 */
export async function subsetOutputs({ map, used, outDir, fileStem, generator, svgDir }) {
  const outputs = [{ file: path.join(outDir, `${fileStem}${SUBSET_JSON_EXTENSION}`), bytes: Buffer.from(renderIconMap(subsetIconMap(map, used)), 'utf8') }];
  if (generator !== undefined) {
    const woff2 = await buildSubsetFont({ map, used, fileStem, generator, svgDir });
    outputs.push({ file: path.join(outDir, `${fileStem}${SUBSET_FONT_EXTENSION}`), bytes: woff2 });
  }
  return outputs;
}

/** Text outputs compare without regard to line endings (a CRLF checkout is not drift). */
function comparable(file, bytes) {
  return file.endsWith(SUBSET_JSON_EXTENSION) ? Buffer.from(bytes.toString('utf8').replace(/\r\n/g, NEWLINE), 'utf8') : bytes;
}

/** The outputs whose file is missing or differs. */
export function driftedSubsetOutputs(outputs) {
  return outputs.filter(({ file, bytes }) => !fs.existsSync(file)
    || Buffer.compare(comparable(file, fs.readFileSync(file)), comparable(file, bytes)) !== 0);
}
