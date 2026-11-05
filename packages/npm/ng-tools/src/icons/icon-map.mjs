// The icon code point map contract. @codelab/ng-dga ships it as `assets/icons/<stem>.json` next to the icon font
// (`@codelab/ng-dga/assets/icons/codelab-icons.json`):
//
//   { "fontFamily": "Codelab Icons", "icons": { "<name>": { "codepoint": "E001", "mirrorInRtl": false }, ... } }
//
// Names are kebab case, code points four upper-case hex digits in the Private Use Area; the order of `icons` is the
// font's glyph order. ng-tools reads only this file (and, for a subset font, the generator contract in subset.mjs), so
// it never depends on ng-dga's code.
import fs from 'node:fs';

const ICON_NAME = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;
const CODEPOINT = /^[0-9A-F]{4}$/;

/** Every problem with a parsed map; an empty list means it is usable. */
export function validateIconMap(map) {
  if (map === null || typeof map !== 'object' || Array.isArray(map)) return ['the icon map is not a JSON object.'];
  const problems = [];
  if (typeof map.fontFamily !== 'string' || map.fontFamily.trim() === '') problems.push('fontFamily is missing.');
  const icons = map.icons;
  if (icons === null || typeof icons !== 'object' || Array.isArray(icons) || Object.keys(icons).length === 0) {
    return [...problems, 'icons must be an object with at least one icon.'];
  }
  for (const [name, icon] of Object.entries(icons)) {
    if (!ICON_NAME.test(name)) problems.push(`icon name '${name}' must be kebab case.`);
    if (!CODEPOINT.test(icon?.codepoint ?? '')) problems.push(`icon '${name}' needs a four-digit upper-case hex codepoint.`);
  }
  return problems;
}

/**
 * Reads and validates the map at `file`.
 * @returns {{ map: { fontFamily: string, icons: Record<string, { codepoint: string, mirrorInRtl?: boolean }> } } | { error: string }}
 */
export function readIconMap(file) {
  let map;
  try {
    map = JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (error) {
    return { error: `cannot read the icon map ${file}: ${error.message}` };
  }
  const problems = validateIconMap(map);
  return problems.length === 0 ? { map } : { error: `invalid icon map ${file}:\n  ${problems.join('\n  ')}` };
}

/** The icon names of a map, in font order. */
export function iconNamesOf(map) {
  return Object.keys(map.icons);
}
