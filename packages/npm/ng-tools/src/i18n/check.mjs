// The i18n check (base rule base.fe-no-raw-text): the translation files agree (same keys in every language, ar and en
// present, every leaf non-empty text), every Arabic text has Arabic letters, and every key the code uses literally
// exists in every language.
import fs from 'node:fs';
import { isFolder, listSourceFiles, toPosixRelative } from '../shared/source-files.mjs';
import { ARABIC_LANGUAGE, findLeavesWithoutArabic } from './arabic.mjs';
import { DEFAULT_TRANSLATE_PIPES, findUsedKeys } from './key-usage.mjs';
import { I18nProblemKinds, REQUIRED_LANGUAGES, readTranslations } from './translations.mjs';

export const I18N_CHECK_FIX = 'Add the key with real text to every translation file (Arabic in ar.json), or fix the key in the code.';

function unknownKeyProblems(usages, languages) {
  const problems = [];
  const reported = new Set();
  for (const { key, file, line } of usages) {
    const lacking = languages.filter(({ leaves }) => !leaves.has(key)).map(({ language }) => language);
    if (lacking.length === 0 || reported.has(key)) continue;
    reported.add(key);
    problems.push({ kind: I18nProblemKinds.unknownKey, key, message: `${key} is used in ${file}:${line} but missing from ${lacking.join(', ')}` });
  }
  return problems;
}

/**
 * Runs the check.
 * @param {{ dir: string, sourceDirs?: string[], allowLatin?: string[], pipes?: string[], requiredLanguages?: string[], baseDir?: string }} options
 *   `dir`: the translation folder; `sourceDirs`: folders whose templates and TypeScript are scanned for used keys
 *   (none: the usage check is skipped); `allowLatin`: Arabic keys allowed to be Latin (`key` or `group.*`); `pipes`:
 *   the translate pipe names; `baseDir`: usages are reported relative to it (default the working directory)
 * @returns {{ problems: { kind: string, message: string, language?: string, key?: string }[], languages: string[], usedKeyCount: number }}
 */
export function checkI18n({
  dir, sourceDirs = [], allowLatin = [], pipes = DEFAULT_TRANSLATE_PIPES, requiredLanguages = REQUIRED_LANGUAGES, baseDir = process.cwd(),
}) {
  const { languages, problems } = readTranslations(dir, requiredLanguages);
  const arabic = languages.find(({ language }) => language === ARABIC_LANGUAGE);
  if (arabic !== undefined) problems.push(...findLeavesWithoutArabic(arabic.leaves, allowLatin));
  const missingSource = sourceDirs.find((sourceDir) => !isFolder(sourceDir));
  if (missingSource !== undefined) problems.push({ kind: I18nProblemKinds.missingFolder, message: `source folder not found: ${missingSource}` });
  const usages = missingSource !== undefined ? [] : sourceDirs.flatMap((sourceDir) => listSourceFiles(sourceDir)).flatMap((file) => {
    const relative = toPosixRelative(baseDir, file);
    return findUsedKeys(file, fs.readFileSync(file, 'utf8'), { pipes }).map((usage) => ({ ...usage, file: relative }));
  });
  if (languages.length > 0) problems.push(...unknownKeyProblems(usages, languages));
  return { problems, languages: languages.map(({ language }) => language), usedKeyCount: new Set(usages.map(({ key }) => key)).size };
}

/** The report of a failed check: one line per problem, then the fix. */
export function formatI18nProblems(problems, fix = I18N_CHECK_FIX) {
  return `i18n check (base.fe-no-raw-text) failed (${problems.length}):\n${problems.map(({ message }) => `  ${message}`).join('\n')}\nFix: ${fix}`;
}
