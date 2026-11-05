#!/usr/bin/env node
// Generates docs/design/component-catalog.md from the component metadata of @codelab/ng-dga: one
// src/lib/<name>/<selector>.catalog.json per component (schema: catalog.schema.json). Every entry is validated
// against the schema and against the component source (class name, signal inputs/models and outputs), and every
// component exported from src/public-api.ts must have an entry.
// --check regenerates in memory and fails when the committed document differs or any entry is missing or wrong.
//
// Usage: node scripts/generate-catalog.mjs [--check]
// Exit codes: 0 generated (or in sync), 1 an entry problem or drift found, 2 usage error.
import { existsSync, readdirSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { Ajv2020 } from 'ajv/dist/2020.js';
import { readJson, toPosix } from './lib/files.mjs';
import { PACKAGE_ROOT, REPO_ROOT, SOURCE_DIR } from './lib/package-paths.mjs';

export const EXIT_OK = 0;
export const EXIT_FAILED = 1;
export const EXIT_USAGE = 2;
/** process.argv holds the node binary and the script path before the user arguments. */
const FIRST_USER_ARGUMENT = 2;
const CHECK_OPTION = '--check';

export const CATALOG_SUFFIX = '.catalog.json';
export const SCHEMA_FILE = path.join(PACKAGE_ROOT, 'catalog.schema.json');
export const LIB_DIR = path.join(SOURCE_DIR, 'lib');
export const PUBLIC_API_FILE = path.join(SOURCE_DIR, 'public-api.ts');
export const CATALOG_DOC_FILE = path.join(REPO_ROOT, 'docs', 'design', 'component-catalog.md');
/** The live showcase of the local dev server (run-local.ps1); each component section is anchored by its selector. */
export const SHOWCASE_URL = 'http://localhost:4280/showcase';
export const PACKAGE_NAME = '@codelab/ng-dga';
const GENERATOR_PATH = 'packages/npm/ng-dga/scripts/generate-catalog.mjs';
const TS_EXTENSION = '.ts';
const LINE_END = '\n';
const CRLF = /\r\n/g;
const MODEL_CHANGE_SUFFIX = 'Change';

/** The catalog sections in reading order, with their headings. Keys are the schema's category values. */
export const CATEGORY_TITLES = Object.freeze({
  layout: 'Layout',
  navigation: 'Navigation',
  content: 'Content',
  form: 'Form controls',
  data: 'Data display',
  feedback: 'Feedback',
  media: 'Media',
});

/** Problem kinds, so callers and tests can tell an invalid entry from missing coverage. */
export const ProblemKind = Object.freeze({
  Schema: 'schema',
  FileName: 'file-name',
  Duplicate: 'duplicate',
  UnknownComponent: 'unknown-component',
  ClassName: 'class-name',
  Inputs: 'inputs',
  Outputs: 'outputs',
  MissingEntry: 'missing-entry',
});

// ---------------------------------------------------------------------------------------------------------------
// Component source parsing (pure)
// ---------------------------------------------------------------------------------------------------------------

const EXPORT_FROM = /export\s+(?:\*|\{[^}]*\})\s+from\s+'(\.\/[^']+)'/g;
const DECORATOR_START = /@(Component|Directive)\(\s*\{/g;
const SELECTOR = /selector:\s*'([^']+)'/;
const EXPORT_CLASS = /export\s+class\s+(\w+)/;
const SIGNAL_INPUT = /readonly\s+(\w+)\s*=\s*(input|model)(?:\.required)?\s*[<(]/g;
const SIGNAL_OUTPUT = /readonly\s+(\w+)\s*=\s*output(?:FromObservable)?\s*[<(]/g;
const COMPONENT_DECORATOR = 'Component';

/** The relative module paths ('./lib/x/y') that public-api.ts re-exports. */
export function exportedModulePaths(publicApiSource) {
  return [...publicApiSource.matchAll(EXPORT_FROM)].map((match) => match[1]);
}

/**
 * The components declared in one TypeScript source: [{ selector, className, inputs, models, outputs }], where inputs
 * holds signal inputs and models, models only the two-way models, outputs the output() members. Directives are
 * skipped. A class body ends at the next decorator or at the end of the file.
 */
export function parseComponents(source) {
  const decorators = [...source.matchAll(DECORATOR_START)];
  return decorators.flatMap((decorator, index) => {
    if (decorator[1] !== COMPONENT_DECORATOR) return [];
    const end = index + 1 < decorators.length ? decorators[index + 1].index : source.length;
    const block = source.slice(decorator.index, end);
    const selector = SELECTOR.exec(block)?.[1];
    const classMatch = EXPORT_CLASS.exec(block);
    if (!selector || !classMatch) return [];
    const body = block.slice(classMatch.index);
    const signals = [...body.matchAll(SIGNAL_INPUT)];
    return [
      {
        selector,
        className: classMatch[1],
        inputs: signals.map((match) => match[1]),
        models: signals.filter((match) => match[2] === 'model').map((match) => match[1]),
        outputs: [...body.matchAll(SIGNAL_OUTPUT)].map((match) => match[1]),
      },
    ];
  });
}

/** Every component exported from the package: reads public-api.ts and each re-exported source file. */
export function exportedComponents(publicApiFile = PUBLIC_API_FILE) {
  const folder = path.dirname(publicApiFile);
  return exportedModulePaths(readFileSync(publicApiFile, 'utf8')).flatMap((modulePath) => {
    const file = path.join(folder, `${modulePath}${TS_EXTENSION}`);
    return existsSync(file) ? parseComponents(readFileSync(file, 'utf8')) : [];
  });
}

// ---------------------------------------------------------------------------------------------------------------
// Catalog entries
// ---------------------------------------------------------------------------------------------------------------

/** Every <selector>.catalog.json below src/lib: [{ file (absolute), entry }], sorted by path. */
export function readCatalogEntries(libDir = LIB_DIR) {
  if (!existsSync(libDir)) return [];
  return readdirSync(libDir, { withFileTypes: true, recursive: true })
    .filter((item) => item.isFile() && item.name.endsWith(CATALOG_SUFFIX))
    .map((item) => path.join(item.parentPath, item.name))
    .sort()
    .map((file) => ({ file, entry: readJson(file) }));
}

/** A schema validator: (entry) => [message]. */
export function createSchemaValidator(schema = readJson(SCHEMA_FILE)) {
  const ajv = new Ajv2020({ allErrors: true, strict: true });
  const validate = ajv.compile(schema);
  return (entry) => {
    if (validate(entry)) return [];
    return validate.errors.map((error) => `${error.instancePath || '/'} ${error.message}`);
  };
}

function difference(left, right) {
  const exclude = new Set(right);
  return left.filter((item) => !exclude.has(item));
}

/** Problems of one entry against its component (class name, input and output names). */
export function memberProblems(entry, component) {
  const problems = [];
  const where = entry.selector;
  if (entry.className !== component.className) {
    problems.push({ kind: ProblemKind.ClassName, selector: where, message: `className is ${entry.className}, the component class is ${component.className}` });
  }
  const catalogInputs = entry.inputs.map((input) => input.name);
  const undocumented = difference(component.inputs, catalogInputs);
  const unknownInputs = difference(catalogInputs, component.inputs);
  if (undocumented.length > 0) problems.push({ kind: ProblemKind.Inputs, selector: where, message: `inputs missing from the catalog: ${undocumented.join(', ')}` });
  if (unknownInputs.length > 0) problems.push({ kind: ProblemKind.Inputs, selector: where, message: `catalog inputs the component does not declare: ${unknownInputs.join(', ')}` });
  const catalogOutputs = entry.outputs.map((output) => output.name);
  const allowedOutputs = [...component.outputs, ...component.models.map((model) => `${model}${MODEL_CHANGE_SUFFIX}`)];
  const missingOutputs = difference(component.outputs, catalogOutputs);
  const unknownOutputs = difference(catalogOutputs, allowedOutputs);
  if (missingOutputs.length > 0) problems.push({ kind: ProblemKind.Outputs, selector: where, message: `outputs missing from the catalog: ${missingOutputs.join(', ')}` });
  if (unknownOutputs.length > 0) problems.push({ kind: ProblemKind.Outputs, selector: where, message: `catalog outputs the component does not declare: ${unknownOutputs.join(', ')}` });
  return problems;
}

/**
 * Every problem of the catalog: schema errors, a file name that is not <selector>.catalog.json, duplicates, entries
 * for components that are not exported, member mismatches and exported components without an entry.
 * `items` is [{ file, entry }]; `components` is exportedComponents(); `validate` is createSchemaValidator().
 */
export function catalogProblems(items, components, validate) {
  const problems = [];
  const bySelector = new Map(components.map((component) => [component.selector, component]));
  const seen = new Set();
  for (const { file, entry } of items) {
    const name = toPosix(path.relative(PACKAGE_ROOT, file));
    const schemaErrors = validate(entry);
    if (schemaErrors.length > 0) {
      for (const message of schemaErrors) problems.push({ kind: ProblemKind.Schema, selector: name, message });
      continue;
    }
    if (path.basename(file) !== `${entry.selector}${CATALOG_SUFFIX}`) {
      problems.push({ kind: ProblemKind.FileName, selector: entry.selector, message: `${name} must be named ${entry.selector}${CATALOG_SUFFIX}` });
    }
    if (seen.has(entry.selector)) {
      problems.push({ kind: ProblemKind.Duplicate, selector: entry.selector, message: `${name} repeats an entry for ${entry.selector}` });
      continue;
    }
    seen.add(entry.selector);
    const component = bySelector.get(entry.selector);
    if (!component) {
      problems.push({ kind: ProblemKind.UnknownComponent, selector: entry.selector, message: `${name} describes a component that src/public-api.ts does not export` });
      continue;
    }
    problems.push(...memberProblems(entry, component));
  }
  for (const component of components) {
    if (!seen.has(component.selector)) {
      problems.push({ kind: ProblemKind.MissingEntry, selector: component.selector, message: `${component.className} has no ${component.selector}${CATALOG_SUFFIX}` });
    }
  }
  return problems;
}

// ---------------------------------------------------------------------------------------------------------------
// Markdown rendering (pure)
// ---------------------------------------------------------------------------------------------------------------

const PIPE = /\|/g;
const NEWLINES = /\r?\n/g;
const NONE = 'None.';
const EMPTY_CELL = '';

const HTML_TAG = /<\/?[A-Za-z][^<>]*>/g;

/** Prose from an entry: an HTML-like tag (<dga-button>) becomes a code span, so Markdown shows it instead of hiding it. */
export function prose(text) {
  return String(text ?? EMPTY_CELL).replace(HTML_TAG, (tag) => `\`${tag}\``);
}

/** A table cell: pipes escaped, line breaks flattened. */
export function cell(text) {
  return String(text ?? EMPTY_CELL).replace(NEWLINES, ' ').replace(PIPE, '\\|');
}

function code(text) {
  return `\`${text}\``;
}

function table(headers, rows) {
  const lines = [`| ${headers.join(' | ')} |`, `| ${headers.map(() => '---').join(' | ')} |`];
  for (const row of rows) lines.push(`| ${row.join(' | ')} |`);
  return lines.join(LINE_END);
}

function inputsSection(entry) {
  if (entry.inputs.length === 0) return NONE;
  return table(
    ['Name', 'Type', 'Required', 'Default', 'Description'],
    entry.inputs.map((input) => [
      code(input.name),
      code(cell(input.type)),
      input.required ? 'yes' : 'no',
      input.default === undefined ? EMPTY_CELL : code(cell(input.default)),
      cell(prose(input.description)),
    ]),
  );
}

function outputsSection(entry) {
  if (entry.outputs.length === 0) return NONE;
  return table(['Name', 'Payload', 'Description'], entry.outputs.map((output) => [code(output.name), code(cell(output.type)), cell(prose(output.description))]));
}

function slotsSection(entry) {
  if (entry.slots.length === 0) return NONE;
  return table(['Selector', 'Description'], entry.slots.map((slot) => [code(cell(slot.selector)), cell(prose(slot.description))]));
}

/** One component section. */
export function renderEntry(entry, showcaseUrl = SHOWCASE_URL) {
  return [
    `### ${entry.selector}`,
    prose(entry.summary),
    table(
      ['Fact', 'Value'],
      [
        ['Class', code(entry.className)],
        ['Import', code(`import { ${entry.className} } from '${PACKAGE_NAME}';`)],
        ['Live example', `[${code(`/showcase#${entry.selector}`)}](${showcaseUrl}#${entry.selector})`],
        ['Since', entry.since],
      ],
    ),
    '#### Inputs',
    inputsSection(entry),
    '#### Outputs',
    outputsSection(entry),
    '#### Slots',
    slotsSection(entry),
    '#### Accessibility',
    prose(entry.a11y),
  ].join(`${LINE_END}${LINE_END}`);
}

/** Entries grouped by category in CATEGORY_TITLES order, each group sorted by selector. Empty groups are dropped. */
export function groupByCategory(entries) {
  return Object.keys(CATEGORY_TITLES)
    .map((category) => ({
      category,
      title: CATEGORY_TITLES[category],
      entries: entries.filter((entry) => entry.category === category).sort((left, right) => left.selector.localeCompare(right.selector)),
    }))
    .filter((group) => group.entries.length > 0);
}

/** The whole document, LF line endings, one final newline. Deterministic for the same entries. */
export function renderCatalog(entries, showcaseUrl = SHOWCASE_URL) {
  const groups = groupByCategory(entries);
  const index = table(
    ['Category', 'Components'],
    groups.map((group) => [group.title, group.entries.map((entry) => `[${entry.selector}](#${entry.selector})`).join(', ')]),
  );
  const parts = [
    `<!-- GENERATED by ${GENERATOR_PATH} from src/lib/*/<selector>${CATALOG_SUFFIX}. Do not edit: change the entry and run npm run catalog:generate -w ${PACKAGE_NAME}. -->`,
    `# ${PACKAGE_NAME} component catalog`,
    `Every component of \`${PACKAGE_NAME}\` with its inputs, outputs, slots and accessibility contract, generated from the metadata beside each component (schema: \`packages/npm/ng-dga/catalog.schema.json\`). Screens use only these components and the [page patterns](page-patterns.md). Components carry no text: every label arrives translated through an input or projected content. Live examples in Arabic and English are on the showcase (\`/showcase\`, dev and staging only); each section links to its anchor on the local dev server.`,
    `${entries.length} components.`,
    index,
    ...groups.flatMap((group) => [`## ${group.title}`, ...group.entries.map((entry) => renderEntry(entry, showcaseUrl))]),
  ];
  return parts.join(`${LINE_END}${LINE_END}`) + LINE_END;
}

// ---------------------------------------------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------------------------------------------

/** Text compared with line endings normalized to LF. */
export function sameText(left, right) {
  return left.replace(CRLF, LINE_END) === right.replace(CRLF, LINE_END);
}

/** Everything the generator needs, read from disk: { items, components, problems, document }. */
export function buildCatalog() {
  const items = readCatalogEntries();
  const components = exportedComponents();
  const validate = createSchemaValidator();
  const problems = catalogProblems(items, components, validate);
  const validEntries = items.filter(({ entry }) => validate(entry).length === 0).map(({ entry }) => entry);
  const unique = [...new Map(validEntries.map((entry) => [entry.selector, entry])).values()];
  return { items, components, problems, document: renderCatalog(unique) };
}

export function main(argv) {
  const unknown = argv.filter((argument) => argument !== CHECK_OPTION);
  if (unknown.length > 0) {
    console.error(`Unknown option(s): ${unknown.join(' ')}. Usage: node scripts/generate-catalog.mjs [${CHECK_OPTION}]`);
    return EXIT_USAGE;
  }
  const { items, components, problems, document } = buildCatalog();
  for (const problem of problems) console.error(`${problem.kind} ${problem.selector}: ${problem.message}`);
  const relativeDoc = toPosix(path.relative(REPO_ROOT, CATALOG_DOC_FILE));
  const current = existsSync(CATALOG_DOC_FILE) ? readFileSync(CATALOG_DOC_FILE, 'utf8') : null;
  const inSync = current !== null && sameText(current, document);
  const summary = `component catalog: ${items.length} entr(ies) for ${components.length} exported component(s), ${problems.length} problem(s)`;
  if (argv.includes(CHECK_OPTION)) {
    if (!inSync) console.error(`${relativeDoc} is out of date; run npm run catalog:generate -w ${PACKAGE_NAME}.`);
    console.log(`${summary}, document ${inSync ? 'in sync' : 'out of date'}.`);
    return inSync && problems.length === 0 ? EXIT_OK : EXIT_FAILED;
  }
  if (!inSync) {
    mkdirSync(path.dirname(CATALOG_DOC_FILE), { recursive: true });
    writeFileSync(CATALOG_DOC_FILE, document);
  }
  console.log(`${summary}, ${relativeDoc} ${inSync ? 'unchanged' : 'written'}.`);
  return problems.length === 0 ? EXIT_OK : EXIT_FAILED;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = main(process.argv.slice(FIRST_USER_ARGUMENT));
}
