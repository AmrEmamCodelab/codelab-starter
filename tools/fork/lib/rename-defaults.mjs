// The default renameMap of init Step 1 (ADR-003): from the product prefix and the EN/AR display names it builds every
// { from, to } entry a full rename needs. The rename engine (rename.mjs) adds the lower- and upper-case variant of
// each entry and applies them on identifier boundaries, so one Pascal entry covers folders, files, C# namespaces,
// assembly and project names (.csproj, .slnx), TypeScript paths, the npm app name, Starter* database names,
// IIS site and app-pool names and the STARTER protocol prefix. The entries below add what case variants cannot reach:
// the display names, the kebab-case app names of a multi-word product, the lower-case concatenated rule-id prefixes
// (starterapi.*) and the manifest names that must never change.
import { RENAME_PROTECTED_NAMES } from './constants.mjs';
import { refusalForPrefix } from './prefix.mjs';

/** The starter's own names: the source side of every default entry. Written whole so a rename never builds them. */
export const STARTER_NAMES = Object.freeze({
  pascal: 'Starter',
  displayEn: 'Codelab Starter',
  displayAr: 'قالب كودلاب',
});

/**
 * Lower-case concatenations of a starter project name, used as module rule-id prefixes (module.rules.yaml: the id
 * prefix is the lower-cased module name, e.g. `starterapi.program-size` for module StarterApi). Identifier
 * boundaries cannot split them, so each one is listed.
 */
export const CONCATENATED_NAMES = Object.freeze([
  'starterapi',
  'starterapplication',
  'starterdomain',
  'starterinfrastructure',
  'starterdevidp',
  'starterdocsgen',
]);

/**
 * Kebab-case app names (npm packages, app folders, IIS physical paths). For a multi-word product (nameEn "Moh
 * Permits", prefix MOHPERMITS) they become moh-permits-web; a one-word product needs no entry (the lower-case
 * variant already gives acme-web). Longest first is the engine's job, not this list's.
 */
export const KEBAB_NAMES = Object.freeze([
  'starter-web',
  'starter-api',
  'codelab-starter',
  'starter-ops-console',
  'starter-ops-web',
  'starter-ops-api',
  'starter-attachments',
  'starter-gateway',
]);

const STARTER_LOWER = 'starter';
const KEBAB_SEPARATOR = '-';
const WORD_SPLITTER = /[^A-Za-z0-9]+/;
const EMPTY = '';

function requireName(value, field) {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new TypeError(`defaultRenameMap: ${field} must be a non-empty string.`);
  }
  return value.trim();
}

function capitalize(word) {
  return word.charAt(0).toUpperCase() + word.slice(1).toLowerCase();
}

/**
 * The product's identifier forms. Pascal and kebab take their word breaks from nameEn when its letters and digits
 * spell the prefix ("Moh Permits" + MOHPERMITS -> MohPermits, moh-permits); otherwise the prefix is one word
 * (ACME -> Acme, acme). Pascal upper-cased is always the prefix, so the STARTER variant lands on the protocol prefix.
 * @param {string} prefix a valid product prefix
 * @param {string} nameEn
 * @returns {{ pascal: string, lower: string, upper: string, kebab: string }}
 */
export function productNameForms(prefix, nameEn) {
  const words = nameEn.split(WORD_SPLITTER).filter((word) => word.length > 0);
  const spellsPrefix = words.length > 1 && words.join(EMPTY).toUpperCase() === prefix;
  const pascalWords = spellsPrefix ? words.map(capitalize) : [capitalize(prefix)];
  const pascal = pascalWords.join(EMPTY);
  return {
    pascal,
    lower: pascal.toLowerCase(),
    upper: prefix,
    kebab: pascalWords.map((word) => word.toLowerCase()).join(KEBAB_SEPARATOR),
  };
}

/**
 * The full default renameMap for a fork. Throws a RangeError for an invalid or reserved prefix and a TypeError for a
 * missing display name. The result is what init stores as codelab.starter.json renameMap.
 * @param {{ prefix: string, nameEn: string, nameAr: string }} names
 * @returns {Array<{ from: string, to: string }>}
 */
export function defaultRenameMap({ prefix, nameEn, nameAr }) {
  const refusal = refusalForPrefix(prefix);
  if (refusal) throw new RangeError(refusal);
  const english = requireName(nameEn, 'nameEn');
  const arabic = requireName(nameAr, 'nameAr');
  const forms = productNameForms(prefix, english);
  const entries = [
    { from: STARTER_NAMES.pascal, to: forms.pascal },
    { from: STARTER_NAMES.displayEn, to: english },
    { from: STARTER_NAMES.displayAr, to: arabic },
    ...CONCATENATED_NAMES.map((name) => ({ from: name, to: forms.lower + name.slice(STARTER_LOWER.length) })),
  ];
  if (forms.kebab !== forms.lower) {
    entries.push(...KEBAB_NAMES.map((name) => ({ from: name, to: name.replace(STARTER_LOWER, forms.kebab) })));
  }
  entries.push(...RENAME_PROTECTED_NAMES.map((name) => ({ from: name, to: name })));
  return entries;
}
