// The renameMap engine (ADR-003). One renamer rewrites paths and text the same way, so a rename replayed on an
// upstream diff lands exactly where init's rename put the fork's files.
//
// Rules:
// - Each { from, to } entry also maps its lower-case and upper-case variants (Starter/starter/STARTER); an explicit
//   entry wins over a variant derived from another entry.
// - The longest `from` wins at any position; an identity entry ({ from: x, to: x }) protects x from shorter entries.
// - A match must sit on identifier boundaries, so "Starter" is renamed in "StarterDev", "IStarter" and "starter-web",
//   but not in "Restarter" or "Starters".

/** @typedef {{ from: string, to: string }} RenameEntry */
/** @typedef {{ from: string, to: string }} RenameVariant */

const LOWER = /\p{Ll}/u;
const UPPER = /\p{Lu}/u;
const LETTER_OR_DIGIT = /[\p{L}\p{N}]/u;
const DIGIT = /\p{N}/u;

function isLower(character) {
  return character !== undefined && LOWER.test(character);
}

function isUpper(character) {
  return character !== undefined && UPPER.test(character);
}

function isWordCharacter(character) {
  return character !== undefined && LETTER_OR_DIGIT.test(character);
}

/**
 * True when `from` matched at `text[index]` starts a word: after a non-word character, after a camelCase hump
 * ("myStarter"), or at the end of an acronym ("IStarter": the match continues with a lower-case letter).
 */
export function isLeftBoundary(text, index, from) {
  const before = text[index - 1];
  if (!isWordCharacter(before)) return true;
  if (!isUpper(from[0])) return false;
  if (isLower(before) || DIGIT.test(before)) return true;
  return isUpper(before) && isLower(from[1]);
}

/**
 * True when `from` matched at `text[index]` ends a word: before a non-word character or a digit, or before an
 * upper-case letter when the match ends in a lower-case one ("StarterDev", "starterWeb").
 */
export function isRightBoundary(text, index, from) {
  const after = text[index + from.length];
  if (!isWordCharacter(after)) return true;
  if (DIGIT.test(after)) return true;
  return isUpper(after) && isLower(from[from.length - 1]);
}

function addVariant(variants, from, to) {
  if (from.length > 0 && !variants.has(from)) variants.set(from, to);
}

/**
 * Expands a renameMap into its case variants, longest `from` first (ties keep map order).
 * @param {RenameEntry[]} renameMap
 * @returns {RenameVariant[]}
 */
export function expandRenameMap(renameMap) {
  const variants = new Map();
  for (const { from, to } of renameMap) addVariant(variants, from, to);
  for (const { from, to } of renameMap) addVariant(variants, from.toLowerCase(), to.toLowerCase());
  for (const { from, to } of renameMap) addVariant(variants, from.toUpperCase(), to.toUpperCase());
  const ordered = [...variants.entries()].map(([from, to]) => ({ from, to }));
  return ordered.sort((left, right) => right.from.length - left.from.length);
}

/** True when the map renames nothing (empty, or identity entries only). */
export function isIdentityMap(renameMap) {
  return renameMap.every(({ from, to }) => from === to);
}

function matchAt(text, index, variants) {
  for (const variant of variants) {
    if (text.startsWith(variant.from, index)
      && isLeftBoundary(text, index, variant.from)
      && isRightBoundary(text, index, variant.from)) {
      return variant;
    }
  }
  return undefined;
}

/**
 * Rewrites every boundary-respecting occurrence in one left-to-right pass (a replaced span is never revisited).
 * @param {string} text
 * @param {RenameVariant[]} variants from expandRenameMap
 */
export function renameWithVariants(text, variants) {
  if (variants.length === 0) return text;
  const firstCharacters = new Set(variants.map((variant) => variant.from[0]));
  let result = '';
  let index = 0;
  while (index < text.length) {
    const variant = firstCharacters.has(text[index]) ? matchAt(text, index, variants) : undefined;
    if (variant) {
      result += variant.to;
      index += variant.from.length;
    } else {
      result += text[index];
      index += 1;
    }
  }
  return result;
}

/**
 * Builds the renamer for a renameMap.
 * @param {RenameEntry[]} renameMap
 * @returns {{ identity: boolean, text: (value: string) => string, path: (value: string) => string }}
 */
export function createRenamer(renameMap) {
  const identity = isIdentityMap(renameMap);
  const variants = identity ? [] : expandRenameMap(renameMap.filter(({ from }) => from.length > 0));
  const rename = (value) => (identity ? value : renameWithVariants(value, variants));
  return { identity, text: rename, path: rename };
}

/**
 * Adds an identity entry for each protected name the map does not already list, so a rename of "starter" never
 * touches "codelab.starter.json".
 * @param {RenameEntry[]} renameMap
 * @param {readonly string[]} protectedNames
 */
export function withProtectedNames(renameMap, protectedNames) {
  if (isIdentityMap(renameMap)) return [...renameMap];
  const listed = new Set(renameMap.map(({ from }) => from));
  const protections = protectedNames.filter((name) => !listed.has(name)).map((name) => ({ from: name, to: name }));
  return [...renameMap, ...protections];
}
