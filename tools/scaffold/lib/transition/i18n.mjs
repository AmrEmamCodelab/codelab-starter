// Pure edits of the frontend ar/en bundles for new-transition: add nested keys (status wording, action labels)
// without touching any other key. Existing values are kept (a translator may have refined them), never overwritten.
import { ScaffoldRefusal } from '../csharp-edit.mjs';
import { I18N_SEPARATOR, JSON_INDENT } from './constants.mjs';

/**
 * Adds `entries` ({ key: 'a.b.c', value }) to a bundle's JSON text.
 * @returns {{ text: string, added: string[], kept: string[] }} `kept`: keys that already held a different value
 */
export function addBundleKeys(jsonText, entries, bundlePath) {
  let bundle;
  try {
    bundle = JSON.parse(jsonText);
  } catch (error) {
    throw new ScaffoldRefusal(`${bundlePath} is not valid JSON (${error.message}).`);
  }
  const added = [];
  const kept = [];
  for (const { key, value } of entries) {
    const segments = key.split(I18N_SEPARATOR);
    const parent = segments.slice(0, -1).reduce((node, segment, depth) => {
      if (node[segment] === undefined) node[segment] = {};
      if (typeof node[segment] !== 'object' || node[segment] === null || Array.isArray(node[segment])) {
        throw new ScaffoldRefusal(`${bundlePath}: '${segments.slice(0, depth + 1).join(I18N_SEPARATOR)}' is a text, so '${key}' cannot be added under it.`);
      }
      return node[segment];
    }, bundle);
    const leaf = segments[segments.length - 1];
    if (parent[leaf] === undefined) {
      parent[leaf] = value;
      added.push(key);
    } else if (typeof parent[leaf] === 'string') {
      if (parent[leaf] !== value) kept.push(key);
    } else {
      throw new ScaffoldRefusal(`${bundlePath}: '${key}' is a group of keys, not a text.`);
    }
  }
  return { text: `${JSON.stringify(bundle, null, JSON_INDENT)}\n`, added, kept };
}

/** True when `key` holds a text in the bundle. */
export function bundleHas(jsonText, key) {
  if (jsonText === null) return false;
  const value = key.split(I18N_SEPARATOR).reduce((node, segment) => (node && typeof node === 'object' ? node[segment] : undefined), JSON.parse(jsonText));
  return typeof value === 'string';
}
