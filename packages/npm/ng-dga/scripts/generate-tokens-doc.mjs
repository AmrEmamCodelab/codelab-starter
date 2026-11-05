#!/usr/bin/env node
// Generates docs/design/tokens.md from the token source of @codelab/ng-dga: styles/_tokens.scss and, when present,
// styles/_branding.scss. Every --dga-* custom property is listed with its value and its resolved value (var()
// references followed), grouped into colour, spacing, typography, radius, borders and elevation, sizes, motion and
// branding; colours get a swatch. The Sass build-time values ($dga-breakpoints...) are listed too.
// --check regenerates in memory and fails when the committed document differs.
//
// Usage: node scripts/generate-tokens-doc.mjs [--check]
// Exit codes: 0 generated (or in sync), 1 drift found by --check or no tokens found, 2 usage error.
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { toPosix } from './lib/files.mjs';
import { REPO_ROOT, STYLES_DIR } from './lib/package-paths.mjs';

export const EXIT_OK = 0;
export const EXIT_FAILED = 1;
export const EXIT_USAGE = 2;
/** process.argv holds the node binary and the script path before the user arguments. */
const FIRST_USER_ARGUMENT = 2;
const CHECK_OPTION = '--check';

export const BRANDING_GROUP = 'Branding layer';
/** The token sources in order. Every token of a source with a group lands in that group (the branding overrides). */
export const TOKEN_SOURCES = Object.freeze([
  { file: path.join(STYLES_DIR, '_tokens.scss'), group: null },
  { file: path.join(STYLES_DIR, '_branding.scss'), group: BRANDING_GROUP },
]);
export const TOKENS_DOC_FILE = path.join(REPO_ROOT, 'docs', 'design', 'tokens.md');
const GENERATOR_PATH = 'packages/npm/ng-dga/scripts/generate-tokens-doc.mjs';
const PACKAGE_NAME = '@codelab/ng-dga';
const LINE_END = '\n';
const CRLF = /\r\n/g;
/** var() chains deeper than this are reported as unresolved instead of looping on a cycle. */
const MAX_VAR_DEPTH = 16;

/** Token groups in document order: the first matching prefix wins; anything else lands in Other. */
export const TOKEN_GROUPS = Object.freeze([
  { title: 'Colour', prefixes: ['--dga-palette-', '--dga-color-'] },
  { title: BRANDING_GROUP, prefixes: ['--dga-brand-'] },
  { title: 'Spacing', prefixes: ['--dga-space-'] },
  { title: 'Typography', prefixes: ['--dga-font-', '--dga-line-height-'] },
  { title: 'Radius', prefixes: ['--dga-radius-'] },
  { title: 'Borders and elevation', prefixes: ['--dga-border-', '--dga-focus-', '--dga-shadow-'] },
  { title: 'Sizes', prefixes: ['--dga-size-'] },
  { title: 'Motion', prefixes: ['--dga-duration-', '--dga-easing-'] },
]);
export const OTHER_GROUP = 'Other';

/** A sentence under a group heading, for groups that need one. */
export const GROUP_NOTES = Object.freeze({
  [BRANDING_GROUP]:
    'The branding layer maps the optional `--dga-brand-*` properties (set at run time by `applyDgaBranding()` / `provideDgaBranding()` from the app configuration) onto the semantic primary tokens. Without branding every fallback is the DGA palette, so the resolved values below are the defaults.',
});

// ---------------------------------------------------------------------------------------------------------------
// Parsing (pure)
// ---------------------------------------------------------------------------------------------------------------

const SECTION_COMMENT = /^\s*\/\/\s*-{3}\s*(.+?)\s*-*\s*$/;
const CUSTOM_PROPERTY = /^\s*(--dga-[a-z0-9-]+)\s*:\s*(.+?)\s*;\s*$/;
const SASS_VARIABLE_START = /^\s*(\$dga-[a-z0-9-]+)\s*:\s*(.*)$/;
const SASS_DEFAULT_FLAG = /\s*!default\s*;?\s*$/;
const TRAILING_SEMICOLON = /;\s*$/;
const WHITESPACE_RUN = /\s+/g;
const OPEN_PAREN = '(';
const CLOSE_PAREN = ')';

/**
 * The tokens of one SCSS source: { tokens: [{ name, value, section, source, group }], sassValues: [{ name, value, section }] }.
 * section is the last `// --- Title ---` comment above the declaration (the file name when there is none); group is the
 * source's fixed group (null: by name prefix).
 */
export function parseTokenSource(text, sourceName, group = null) {
  const tokens = [];
  const sassValues = [];
  let section = sourceName;
  let pendingSass = null;
  for (const line of text.replace(CRLF, LINE_END).split(LINE_END)) {
    if (pendingSass) {
      pendingSass.parts.push(line.trim());
      if (balanced(pendingSass.parts.join(' '))) {
        sassValues.push(finishSass(pendingSass));
        pendingSass = null;
      }
      continue;
    }
    const heading = SECTION_COMMENT.exec(line);
    if (heading) {
      section = heading[1];
      continue;
    }
    const property = CUSTOM_PROPERTY.exec(line);
    if (property) {
      tokens.push({ name: property[1], value: property[2], section, source: sourceName, group });
      continue;
    }
    const sass = SASS_VARIABLE_START.exec(line);
    if (sass) {
      const pending = { name: sass[1], parts: [sass[2].trim()], section };
      if (balanced(sass[2])) sassValues.push(finishSass(pending));
      else pendingSass = pending;
    }
  }
  return { tokens, sassValues };
}

function balanced(text) {
  let depth = 0;
  for (const character of text) {
    if (character === OPEN_PAREN) depth += 1;
    else if (character === CLOSE_PAREN) depth -= 1;
  }
  return depth === 0;
}

function finishSass({ name, parts, section }) {
  const value = parts.join(' ').replace(SASS_DEFAULT_FLAG, '').replace(TRAILING_SEMICOLON, '').replace(WHITESPACE_RUN, ' ').trim();
  return { name, value, section };
}

const VAR_REFERENCE = /var\(\s*(--[a-z0-9-]+)\s*(?:,\s*([^()]*))?\)/g;

/**
 * The value with every var(--x) replaced by x's resolved value (or its fallback when x is unknown). An unknown
 * reference without a fallback, or a cycle, stays as written.
 */
export function resolveValue(value, byName) {
  let current = value;
  // Each pass resolves the innermost var() references; a nested fallback needs one pass per level.
  for (let pass = 0; pass < MAX_VAR_DEPTH; pass += 1) {
    const next = current.replace(VAR_REFERENCE, (match, name, fallback) => {
      const target = byName.get(name);
      if (target !== undefined) return target;
      return fallback === undefined ? match : fallback.trim();
    });
    if (next === current) return current;
    current = next;
  }
  return current;
}

const COLOUR_VALUE = /^(#[0-9a-f]{3,8}|rgba?\([^)]*\)|hsla?\([^)]*\))$/i;

export function isColour(value) {
  return COLOUR_VALUE.test(value.trim());
}

/** The group title of a token: its source's group, else by name prefix (TOKEN_GROUPS order), else OTHER_GROUP. */
export function groupOf(token) {
  if (token.group) return token.group;
  return TOKEN_GROUPS.find((group) => group.prefixes.some((prefix) => token.name.startsWith(prefix)))?.title ?? OTHER_GROUP;
}

// ---------------------------------------------------------------------------------------------------------------
// Rendering (pure)
// ---------------------------------------------------------------------------------------------------------------

const PIPE = /\|/g;

function cell(text) {
  return String(text).replace(PIPE, '\\|');
}

function code(text) {
  return `\`${cell(text)}\``;
}

function table(headers, rows) {
  const lines = [`| ${headers.join(' | ')} |`, `| ${headers.map(() => '---').join(' | ')} |`];
  for (const row of rows) lines.push(`| ${row.join(' | ')} |`);
  return lines.join(LINE_END);
}

/** An inline swatch for Markdown viewers that render HTML (VS Code, the showcase); others show the code beside it. */
export function swatch(colour) {
  return `<span style="display:inline-block;inline-size:2.5em;block-size:1.25em;border:1px solid currentColor;background:${colour}"></span>`;
}

function tokenTable(tokens, byName) {
  const colourTable = tokens.every((token) => isColour(resolveValue(token.value, byName)));
  if (colourTable) {
    return table(
      ['Swatch', 'Token', 'Value', 'Resolved'],
      tokens.map((token) => {
        const resolved = resolveValue(token.value, byName);
        return [swatch(resolved), code(token.name), code(token.value), code(resolved)];
      }),
    );
  }
  return table(
    ['Token', 'Value', 'Resolved'],
    tokens.map((token) => {
      const resolved = resolveValue(token.value, byName);
      return [code(token.name), code(token.value), resolved === token.value ? '' : code(resolved)];
    }),
  );
}

function sectionsOf(tokens) {
  const sections = new Map();
  for (const token of tokens) {
    const key = `${token.source}\u0000${token.section}`;
    if (!sections.has(key)) sections.set(key, { title: token.section, tokens: [] });
    sections.get(key).tokens.push(token);
  }
  return [...sections.values()];
}

/** The whole document from parsed sources ([{ tokens, sassValues }]). LF line endings, one final newline. */
export function renderTokens(parsedSources, sourceNames) {
  const tokens = parsedSources.flatMap((parsed) => parsed.tokens);
  const sassValues = parsedSources.flatMap((parsed) => parsed.sassValues);
  const byName = new Map(tokens.map((token) => [token.name, token.value]));
  const groupTitles = [...TOKEN_GROUPS.map((group) => group.title), OTHER_GROUP];
  const parts = [
    `<!-- GENERATED by ${GENERATOR_PATH} from ${sourceNames.join(' and ')}. Do not edit: change the token source and run npm run tokens:generate -w ${PACKAGE_NAME}. -->`,
    `# ${PACKAGE_NAME} design tokens`,
    `Every design token as a \`--dga-*\` CSS custom property on \`:root\`. Components and screens read \`var(--dga-*)\` and never write a literal colour, size or duration (rule \`base.fe-design-tokens\`). Use the semantic \`--dga-color-*\` tokens, not the \`--dga-palette-*\` raw colours; a project re-brands by overriding semantic or brand tokens, never component styles. ${tokens.length} tokens.`,
  ];
  for (const title of groupTitles) {
    const members = tokens.filter((token) => groupOf(token) === title);
    if (members.length === 0) continue;
    parts.push(`## ${title}`);
    if (GROUP_NOTES[title]) parts.push(GROUP_NOTES[title]);
    const sections = sectionsOf(members);
    for (const section of sections) {
      if (sections.length > 1) parts.push(`### ${section.title}`);
      parts.push(tokenTable(section.tokens, byName));
    }
  }
  if (sassValues.length > 0) {
    parts.push('## Sass build-time values');
    parts.push('Values the Sass helpers use at build time (`@use \'@codelab/ng-dga/styles\' as dga;` then `dga.media-up(md)`). They are not custom properties.');
    parts.push(table(['Variable', 'Value'], sassValues.map((value) => [code(value.name), code(value.value)])));
  }
  return parts.join(`${LINE_END}${LINE_END}`) + LINE_END;
}

// ---------------------------------------------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------------------------------------------

/** Text compared with line endings normalized to LF. */
export function sameText(left, right) {
  return left.replace(CRLF, LINE_END) === right.replace(CRLF, LINE_END);
}

/** Reads the existing token sources and renders the document: { document, tokenCount, sources }. */
export function buildTokensDoc(sources = TOKEN_SOURCES) {
  const present = sources.filter((source) => existsSync(source.file));
  const names = present.map((source) => toPosix(path.relative(REPO_ROOT, source.file)));
  const parsed = present.map((source, index) => parseTokenSource(readFileSync(source.file, 'utf8'), names[index], source.group));
  return { document: renderTokens(parsed, names), tokenCount: parsed.reduce((sum, item) => sum + item.tokens.length, 0), sources: names };
}

export function main(argv) {
  const unknown = argv.filter((argument) => argument !== CHECK_OPTION);
  if (unknown.length > 0) {
    console.error(`Unknown option(s): ${unknown.join(' ')}. Usage: node scripts/generate-tokens-doc.mjs [${CHECK_OPTION}]`);
    return EXIT_USAGE;
  }
  const { document, tokenCount, sources } = buildTokensDoc();
  if (tokenCount === 0) {
    console.error('No --dga-* tokens found in the token sources.');
    return EXIT_FAILED;
  }
  const relativeDoc = toPosix(path.relative(REPO_ROOT, TOKENS_DOC_FILE));
  const current = existsSync(TOKENS_DOC_FILE) ? readFileSync(TOKENS_DOC_FILE, 'utf8') : null;
  const inSync = current !== null && sameText(current, document);
  const summary = `tokens doc: ${tokenCount} token(s) from ${sources.join(', ')}`;
  if (argv.includes(CHECK_OPTION)) {
    if (!inSync) console.error(`${relativeDoc} is out of date; run npm run tokens:generate -w ${PACKAGE_NAME}.`);
    console.log(`${summary}, document ${inSync ? 'in sync' : 'out of date'}.`);
    return inSync ? EXIT_OK : EXIT_FAILED;
  }
  if (!inSync) {
    mkdirSync(path.dirname(TOKENS_DOC_FILE), { recursive: true });
    writeFileSync(TOKENS_DOC_FILE, document);
  }
  console.log(`${summary}, ${relativeDoc} ${inSync ? 'unchanged' : 'written'}.`);
  return EXIT_OK;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = main(process.argv.slice(FIRST_USER_ARGUMENT));
}
