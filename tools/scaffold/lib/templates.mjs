// Template rendering: every file under tools/scaffold/templates/<set>/ becomes one output file. File names carry
// path tokens (__Module__), contents carry {{Token}}s. An unknown token is an error, so a template typo never
// reaches a generated file.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { CONTENT_TOKEN, PATH_TOKEN, TEMPLATE_EXTENSION, UTF8 } from './constants.mjs';

export const TEMPLATES_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'templates');

function listFiles(dir, prefix = '') {
  return fs.readdirSync(dir, { withFileTypes: true })
    .sort((left, right) => left.name.localeCompare(right.name))
    .flatMap((entry) => {
      const relative = prefix ? `${prefix}/${entry.name}` : entry.name;
      return entry.isDirectory() ? listFiles(path.join(dir, entry.name), relative) : [relative];
    });
}

/** Replaces every token in `text`; throws on a token `values` does not define. */
export function fill(text, values, pattern, where) {
  return text.replace(pattern, (token, name) => {
    if (!Object.hasOwn(values, name)) throw new Error(`Unknown template token ${token} in ${where}.`);
    return values[name];
  });
}

/**
 * Renders the template set `set` (a folder under templates/).
 * @param {string} set
 * @param {Record<string, string>} values token name -> value
 * @returns {{ path: string, content: string }[]} output paths (relative, '/' separators) and contents
 */
export function renderTemplateSet(set, values, templatesRoot = TEMPLATES_ROOT) {
  const dir = path.join(templatesRoot, set);
  return listFiles(dir)
    .filter((file) => file.endsWith(TEMPLATE_EXTENSION))
    .map((file) => ({
      path: fill(file.slice(0, -TEMPLATE_EXTENSION.length), values, PATH_TOKEN, file),
      content: fill(fs.readFileSync(path.join(dir, file), UTF8), values, CONTENT_TOKEN, file),
    }));
}
