// new-lookup as a plan over a ChangeSet: it validates every argument, renders the <Name>Ids constants and the seed
// migration, adds the LookupTypes constant, and records the writes; nothing touches the disk until the caller commits
// the set, so a refusal changes nothing. Deterministic: the migration id follows the newest Lookups migration, never
// the clock, so the same arguments on the same tree give the same bytes. Lookups are seeded ONLY by that migration
// (CLAUDE.md rule 10): the generator never writes HasData, an SQL script or a startup seeder. With the code it writes
// the seed test (ids constants vs migration rows), adds the migration to the model test's exact migration list, adds
// the row of the seeded-types table in the Lookups MODULE.md and keeps the add-lookup recipe in its rules.
import fs from 'node:fs';
import path from 'node:path';
import {
  CODE_LIST_SEPARATOR,
  KEBAB_NAME_PATTERN,
  LOOKUP_DOMAIN_DIR,
  LOOKUP_IDS_SUFFIX,
  LOOKUP_KEY_MAX_LENGTH,
  LOOKUP_MIGRATION_LIST,
  LOOKUP_MIGRATIONS_DIR,
  LOOKUP_MODEL_TESTS_FILE,
  LOOKUP_MODULE_DIR,
  LOOKUP_NAME_MAX_LENGTH,
  LOOKUP_SEEDED_TYPES_HEADER,
  LOOKUP_SORT_STEP,
  LOOKUP_TEMPLATE_SET,
  LOOKUP_TYPES_FILE,
  MAX_LOOKUP_CODES,
  MIGRATION_ID_PATTERN,
  MIGRATION_STEP_MINUTES,
  Markers,
  ModuleFiles,
  NAME_LIST_SEPARATOR,
  Recipes,
  lookupMigrationClass,
} from './constants.mjs';
import { ScaffoldRefusal, addStringConstant } from './csharp-edit.mjs';
import { addRecipe, addTableRow } from './module-docs.mjs';
import { refusalForPascalName, toPascalCase, toWords } from './names.mjs';
import { renderTemplateSet } from './templates.mjs';

const MEMBER_INDENT = '    ';
const ROW_INDENT = '        ';
const CS_EXTENSION = '.cs';
const LINE_BREAK = /[\r\n]/;
const MS_PER_MINUTE = 60_000;
/** yyyyMMddHHmmss: the digit offsets of each part of a migration timestamp. */
const TIMESTAMP_PARTS = Object.freeze({ year: [0, 4], month: [4, 6], day: [6, 8], hour: [8, 10], minute: [10, 12], second: [12, 14] });
const TWO_DIGITS = 2;
const FOUR_DIGITS = 4;

function refuseIf(message) {
  if (message) throw new ScaffoldRefusal(message);
}

/** Why `value` is not a kebab-case lookup key (type or code), or null. */
export function refusalForLookupKey(value, label) {
  if (typeof value !== 'string' || value.length === 0) return `${label} is required (kebab-case, e.g. contact-topics).`;
  if (value.length > LOOKUP_KEY_MAX_LENGTH) return `${label} '${value}' is longer than ${LOOKUP_KEY_MAX_LENGTH} characters.`;
  if (!KEBAB_NAME_PATTERN.test(value)) return `${label} '${value}' is not kebab-case (lower-case letters and digits joined by '-').`;
  return null;
}

/** Why `name` is not a display name (present, one line, at most the column size), or null. */
export function refusalForDisplayName(name, label) {
  if (typeof name !== 'string' || name.trim().length === 0) return `${label} is required.`;
  if (name.length > LOOKUP_NAME_MAX_LENGTH) return `${label} '${name}' is longer than ${LOOKUP_NAME_MAX_LENGTH} characters.`;
  if (LINE_BREAK.test(name)) return `${label} must be one line.`;
  return null;
}

/** "general-question" -> "General question" (the default English name of a code or type). */
export function sentenceOf(kebab) {
  const words = toWords(kebab);
  return words[0].toUpperCase() + words.slice(1);
}

/** Splits a list argument and trims each entry; an absent argument is null. */
export function splitList(value, separator) {
  if (value === undefined || value === null) return null;
  return String(value).split(separator).map((entry) => entry.trim());
}

/** The codes of --codes, checked: kebab-case, unique, unique as C# constants, 1 to MAX_LOOKUP_CODES of them. */
export function parseCodes(value) {
  const codes = splitList(value, CODE_LIST_SEPARATOR);
  refuseIf(codes === null && '--codes is required: the value codes, comma-separated (e.g. general,request,account).');
  refuseIf(codes.length > MAX_LOOKUP_CODES && `--codes lists ${codes.length} values; at most ${MAX_LOOKUP_CODES} per run.`);
  for (const code of codes) refuseIf(refusalForLookupKey(code, 'The value code'));
  const duplicate = codes.find((code, index) => codes.indexOf(code) !== index);
  refuseIf(duplicate && `The value code '${duplicate}' is listed twice.`);
  const constants = codes.map(toPascalCase);
  const clash = constants.find((name, index) => constants.indexOf(name) !== index);
  refuseIf(clash && `Two codes give the same constant name '${clash}'.`);
  return codes;
}

/** The display names of a list argument (one per code), or the defaults when the argument is absent and allowed. */
export function parseNames(value, { codes, flag, defaults }) {
  const names = splitList(value, NAME_LIST_SEPARATOR);
  if (names === null) {
    refuseIf(defaults === null && `${flag} is required: one name per code, separated by '${NAME_LIST_SEPARATOR}'.`);
    return defaults;
  }
  refuseIf(names.length !== codes.length && `${flag} has ${names.length} names for ${codes.length} codes.`);
  names.forEach((name, index) => refuseIf(refusalForDisplayName(name, `The ${flag} name of '${codes[index]}'`)));
  return names;
}

/** A C# string literal body: backslashes and quotes escaped. */
export function csharpString(value) {
  return value.replaceAll('\\', '\\\\').replaceAll('"', '\\"');
}

/** Text for an XML doc comment: &, < and > escaped. */
export function xmlText(value) {
  return value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
}

/** The newest migration timestamp in the Lookups migrations folder, or null. */
export function newestMigrationTimestamp(root) {
  const dir = path.join(root, LOOKUP_MIGRATIONS_DIR);
  if (!fs.existsSync(dir)) return null;
  const stamps = fs.readdirSync(dir)
    .filter((file) => file.endsWith(CS_EXTENSION))
    .flatMap((file) => [...fs.readFileSync(path.join(dir, file), 'utf8').matchAll(MIGRATION_ID_PATTERN)].map((match) => match[1]));
  return stamps.length === 0 ? null : stamps.sort().at(-1);
}

function part(stamp, [start, end]) {
  return Number(stamp.slice(start, end));
}

/** `stamp` (yyyyMMddHHmmss, UTC) plus `minutes`, in the same format. */
export function addMinutes(stamp, minutes) {
  const time = Date.UTC(
    part(stamp, TIMESTAMP_PARTS.year),
    part(stamp, TIMESTAMP_PARTS.month) - 1,
    part(stamp, TIMESTAMP_PARTS.day),
    part(stamp, TIMESTAMP_PARTS.hour),
    part(stamp, TIMESTAMP_PARTS.minute),
    part(stamp, TIMESTAMP_PARTS.second)) + minutes * MS_PER_MINUTE;
  const date = new Date(time);
  const pad = (value, width = TWO_DIGITS) => String(value).padStart(width, '0');
  return pad(date.getUTCFullYear(), FOUR_DIGITS) + pad(date.getUTCMonth() + 1) + pad(date.getUTCDate())
    + pad(date.getUTCHours()) + pad(date.getUTCMinutes()) + pad(date.getUTCSeconds());
}

/** The seed migration file of `migrationClass` already in the folder (any timestamp), or null. */
function existingMigration(root, migrationClass) {
  const dir = path.join(root, LOOKUP_MIGRATIONS_DIR);
  const suffix = `_${migrationClass}${CS_EXTENSION}`;
  const file = fs.readdirSync(dir).find((name) => name.endsWith(suffix));
  return file ? `${LOOKUP_MIGRATIONS_DIR}/${file}` : null;
}

function idConstants(rows) {
  return rows.map((row) => [
    `${MEMBER_INDENT}/// <summary>${xmlText(row.nameEn)} (code <c>${row.code}</c>).</summary>`,
    `${MEMBER_INDENT}public const int ${row.constName} = ${row.id};`,
  ].join('\n')).join('\n\n');
}

function seedRows(idsClass, rows) {
  return rows.map((row) =>
    `${ROW_INDENT}new(Id: ${idsClass}.${row.constName}, Code: "${row.code}", Names: new(Ar: "${csharpString(row.nameAr)}", En: "${csharpString(row.nameEn)}"), SortOrder: ${row.sortOrder}),`)
    .join('\n');
}

function validateRequest({ type, name, codes, nameEn, nameAr, namesEn, namesAr }) {
  refuseIf(refusalForLookupKey(type, 'The lookup type (--type)'));
  refuseIf(refusalForPascalName(name, 'The lookup name (--name)'));
  refuseIf(name.endsWith(LOOKUP_IDS_SUFFIX) && `--name '${name}' must not end with '${LOOKUP_IDS_SUFFIX}'; the generator adds it.`);
  const codeList = parseCodes(codes);
  const typeNameEn = nameEn ?? sentenceOf(type);
  refuseIf(refusalForDisplayName(typeNameEn, 'The English type name (--name-en)'));
  refuseIf(refusalForDisplayName(nameAr, 'The Arabic type name (--name-ar)'));
  const englishNames = parseNames(namesEn, { codes: codeList, flag: '--names-en', defaults: codeList.map(sentenceOf) });
  const arabicNames = parseNames(namesAr, { codes: codeList, flag: '--names-ar', defaults: null });
  const rows = codeList.map((code, index) => ({
    code,
    constName: toPascalCase(code),
    id: index + 1,
    sortOrder: (index + 1) * LOOKUP_SORT_STEP,
    nameEn: englishNames[index],
    nameAr: arabicNames[index],
  }));
  return { rows, typeNameEn };
}

/** The state of an earlier run for the same type: what of the three outputs is present. */
function priorState(changes, root, { typeConst, type, idsPath, migrationClass }) {
  const typesText = changes.read(LOOKUP_TYPES_FILE);
  refuseIf(typesText === null && `${LOOKUP_TYPES_FILE} is missing (the kernel lookup constants).`);
  refuseIf(!fs.existsSync(path.join(root, LOOKUP_MIGRATIONS_DIR)) && `${LOOKUP_MIGRATIONS_DIR} is missing (is the Lookups module installed?).`);
  const declared = new RegExp(`const\\s+string\\s+${typeConst}\\s*=\\s*"([^"]*)"`).exec(typesText);
  refuseIf(declared && declared[1] !== type && `LookupTypes.${typeConst} already holds "${declared[1]}", not "${type}".`);
  return {
    typesText,
    constant: Boolean(declared),
    ids: changes.read(idsPath) !== null,
    migration: existingMigration(root, migrationClass),
  };
}

/**
 * new-lookup: the <Name>Ids constants, the LookupTypes constant and the seed migration of a new lookup type, its seed
 * test, the model test's migration list, the Lookups MODULE.md row and the add-lookup recipe.
 * Idempotent: when the constant, the ids class and the migration already exist for this type, nothing is written
 * (`unchanged`). A partial earlier run is refused (nothing written), naming what is there.
 * @param {import('./workspace.mjs').ChangeSet} changes
 * @returns {{ unchanged: boolean, typeConst: string, idsClass: string, migrationClass: string, migrationId: string | null, notes: string[] }}
 */
export function planLookup(changes, request) {
  const { rows, typeNameEn } = validateRequest(request);
  const { type, name, nameAr } = request;
  const typeConst = toPascalCase(type);
  const idsClass = `${name}${LOOKUP_IDS_SUFFIX}`;
  const migrationClass = lookupMigrationClass(typeConst);
  const idsPath = `${LOOKUP_DOMAIN_DIR}/${idsClass}${CS_EXTENSION}`;
  refuseIf(rows.some((row) => row.constName === idsClass) && `A code may not give the class name '${idsClass}'.`);
  const prior = priorState(changes, changes.root, { typeConst, type, idsPath, migrationClass });
  if (prior.constant && prior.ids && prior.migration) {
    return { unchanged: true, typeConst, idsClass, migrationClass, migrationId: null, notes: [] };
  }
  const present = [prior.constant && `LookupTypes.${typeConst}`, prior.ids && idsPath, prior.migration].filter(Boolean);
  refuseIf(present.length > 0 && `An earlier run left part of this lookup (${present.join(', ')}); finish or remove it by hand, then run again.`);

  const newest = newestMigrationTimestamp(changes.root);
  refuseIf(newest === null && `${LOOKUP_MIGRATIONS_DIR} holds no migration (InitialCreate is missing).`);
  const migrationId = `${addMinutes(newest, MIGRATION_STEP_MINUTES)}_${migrationClass}`;
  const summary = `The ${xmlText(typeNameEn.toLowerCase())} lookup (<see cref="${idsClass}"/>).`;
  changes.write(LOOKUP_TYPES_FILE, addStringConstant(prior.typesText, { constName: typeConst, value: type, summary, marker: Markers.lookupTypes }).text);
  const files = renderTemplateSet(LOOKUP_TEMPLATE_SET, {
    IdsClass: idsClass,
    TypeConst: typeConst,
    MigrationClass: migrationClass,
    MigrationFile: migrationId,
    MigrationId: migrationId,
    TypeNameEn: csharpString(typeNameEn),
    TypeNameAr: csharpString(nameAr),
    IdConstants: idConstants(rows),
    SeedRows: seedRows(idsClass, rows),
    CodeCases: codeCases(idsClass, rows),
    TypeKey: type,
  });
  for (const file of files) {
    refuseIf(changes.read(file.path) !== null && `${file.path} already exists.`);
    changes.write(file.path, file.content);
  }
  const notes = [
    ...planModelTest(changes, migrationClass),
    ...planModuleDocs(changes, { type, idsClass, migrationClass, rows }),
  ];
  return { unchanged: false, typeConst, idsClass, migrationClass, migrationId, notes };
}

/** The [InlineData] rows of the generated seed test: one per code, its id through the constants class. */
function codeCases(idsClass, rows) {
  return rows.map((row) => `${MEMBER_INDENT}[InlineData("${row.code}", ${idsClass}.${row.constName})]`).join('\n');
}

/**
 * Appends the new migration to the model test's exact migration list (the newest sorts last). A tree without that
 * test or with another shape gets a note naming the line to add by hand.
 */
function planModelTest(changes, migrationClass) {
  const entry = `${migrationClass}.MigrationId`;
  const text = changes.read(LOOKUP_MODEL_TESTS_FILE);
  const match = text === null ? null : LOOKUP_MIGRATION_LIST.exec(text);
  if (!match) return [`add ${entry} to the migration list the Lookups model test expects (${LOOKUP_MODEL_TESTS_FILE})`];
  if (match[1].includes(entry)) return [];
  changes.write(LOOKUP_MODEL_TESTS_FILE, text.slice(0, match.index) + `${match[1]}, ${entry}${match[2]}` + text.slice(match.index + match[0].length));
  return [];
}

/** The seeded-types row in the Lookups MODULE.md and the add-lookup recipe in its rules (notes when missing). */
function planModuleDocs(changes, { type, idsClass, migrationClass, rows }) {
  const notes = [];
  const values = rows.map((row) => `\`${row.code}\` ${row.id}`).join(', ');
  const row = `| \`${type}\` | \`${idsClass}\` | ${values} (migration \`${migrationClass}\`) |`;
  const docPath = `${LOOKUP_MODULE_DIR}/${ModuleFiles.doc}`;
  const doc = changes.read(docPath);
  const updated = doc === null ? null : addTableRow(doc, LOOKUP_SEEDED_TYPES_HEADER, row);
  if (updated === null) notes.push(`add this row to the seeded-types table of ${docPath}: ${row}`);
  else changes.write(docPath, updated);
  const rulesPath = `${LOOKUP_MODULE_DIR}/${ModuleFiles.rules}`;
  const rules = changes.read(rulesPath);
  if (rules === null) notes.push(`no ${rulesPath}: list ${Recipes.lookup} in its recipes by hand`);
  else changes.write(rulesPath, addRecipe(rules, Recipes.lookup).text);
  return notes;
}
