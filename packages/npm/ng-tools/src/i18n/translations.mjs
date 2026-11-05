// Translation files: one `<language>.json` per language in a folder, nested groups of text. This module flattens them
// to dotted keys and compares the key sets (parity), reporting each problem as `{ kind, language?, key?, message }`.
import fs from 'node:fs';
import path from 'node:path';
import { isFolder } from '../shared/source-files.mjs';

export const I18nProblemKinds = Object.freeze({
  missingFolder: 'missing-folder',
  invalidJson: 'invalid-json',
  missingLanguage: 'missing-language',
  missingKey: 'missing-key',
  invalidValue: 'invalid-value',
  emptyValue: 'empty-value',
  emptyGroup: 'empty-group',
  missingArabic: 'missing-arabic',
  unknownKey: 'unknown-key',
});

/** Languages every app ships; Arabic first (the default language). */
export const REQUIRED_LANGUAGES = Object.freeze(['ar', 'en']);
export const TRANSLATION_EXTENSION = '.json';
export const KEY_SEPARATOR = '.';
const ROOT_LABEL = '(root)';

function describeValue(value) {
  return Array.isArray(value) ? 'an array' : typeof value;
}

/**
 * Flattens a translation document into `Map<dottedKey, text>`. Arrays, non-text leaves, blank text and empty groups
 * are pushed onto `problems` (with the dotted key; the caller adds the language).
 */
export function flattenTranslations(document, problems = []) {
  const leaves = new Map();
  const visit = (node, key) => {
    if (typeof node === 'string') {
      if (node.trim() === '') problems.push({ kind: I18nProblemKinds.emptyValue, key, message: `${key}: empty text` });
      leaves.set(key, node);
    } else if (node !== null && typeof node === 'object' && !Array.isArray(node)) {
      const entries = Object.entries(node);
      if (entries.length === 0 && key !== '') problems.push({ kind: I18nProblemKinds.emptyGroup, key, message: `${key}: empty group` });
      for (const [child, value] of entries) visit(value, key === '' ? child : key + KEY_SEPARATOR + child);
    } else {
      const label = key === '' ? ROOT_LABEL : key;
      problems.push({ kind: I18nProblemKinds.invalidValue, key: label, message: `${label}: expected text or a group, found ${describeValue(node)}` });
    }
  };
  visit(document, '');
  return leaves;
}

/** One missing-key problem per key a language lacks (against the union of every language), sorted by key. */
export function compareKeySets(languages) {
  const union = [...new Set(languages.flatMap(({ leaves }) => [...leaves.keys()]))].sort();
  return languages.flatMap(({ language, leaves }) => union.filter((key) => !leaves.has(key))
    .map((key) => ({ kind: I18nProblemKinds.missingKey, language, key, message: `${language}: missing key ${key}` })));
}

/**
 * Reads every `<language>.json` in `dir`.
 * @returns {{ languages: { language: string, leaves: Map<string, string> }[], problems: object[] }} languages sorted
 *   by file name; problems: missing folder, invalid JSON, invalid values, missing required languages and parity
 */
export function readTranslations(dir, requiredLanguages = REQUIRED_LANGUAGES) {
  if (!isFolder(dir)) {
    return { languages: [], problems: [{ kind: I18nProblemKinds.missingFolder, message: `translation folder not found: ${dir}` }] };
  }
  const files = fs.readdirSync(dir).filter((name) => name.endsWith(TRANSLATION_EXTENSION)).sort();
  const problems = [];
  const languages = [];
  for (const file of files) {
    const language = file.slice(0, -TRANSLATION_EXTENSION.length);
    let document;
    try {
      document = JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8'));
    } catch (error) {
      problems.push({ kind: I18nProblemKinds.invalidJson, language, message: `${file}: not valid JSON (${error.message})` });
      continue;
    }
    const fileProblems = [];
    const leaves = flattenTranslations(document, fileProblems);
    problems.push(...fileProblems.map((problem) => ({ ...problem, language, message: `${language}: ${problem.message}` })));
    languages.push({ language, leaves });
  }
  for (const required of requiredLanguages) {
    if (!files.includes(required + TRANSLATION_EXTENSION)) {
      problems.push({ kind: I18nProblemKinds.missingLanguage, language: required, message: `${required}: ${required}${TRANSLATION_EXTENSION} is missing` });
    }
  }
  return { languages, problems: [...problems, ...compareKeySets(languages)] };
}
