// Which icons an app uses. The scan is deliberately generous: every quoted token in a template or a TypeScript file
// that equals a known icon name counts as used (`name="search"`, `[name]="open ? 'chevron-up' : 'chevron-down'"`,
// `{ icon: 'home' }`, `leadIcon="plus"`), so an icon chosen at run time from a table of literals is kept. A name built
// by concatenation (`'chevron-' + side`) is not seen: write icon names whole. Comments are ignored, and so are
// `*.generated.ts` files (the library's own glyph table names every icon).
//
// It also reports icon names the map does not know, where a template gives them literally (`<dga-icon name="serch">`,
// `leadIcon="pluss"`): those render nothing.
import { extractInlineTemplates } from '../permission-gates/inline-templates.mjs';
import { scanTemplate } from '../permission-gates/template-scanner.mjs';
import { lexScript } from '../shared/script-lexer.mjs';
import { TEMPLATE_EXTENSION, isTemplateOrScript } from '../shared/source-files.mjs';
import { iconNamesOfAttribute, isIconNameInput } from './icon-inputs.mjs';

/** Generated TypeScript (the icon glyph table among them) is never scanned for usage. */
export const GENERATED_SCRIPT_SUFFIX = '.generated.ts';
/** A quoted kebab-case token; group 2 is the token. */
const QUOTED_TOKEN = /(['"`])([a-z][a-z0-9]*(?:-[a-z0-9]+)*)\1/g;
const HTML_COMMENT = /<!--[\s\S]*?-->/g;
const TOKEN_GROUP = 2;
const LINE_OFFSET = 1;
const OPEN_EVENT = 'open';

/** True for the files the icon scan reads: templates and TypeScript sources, minus generated TypeScript. */
export function isIconSource(name) {
  return isTemplateOrScript(name) && !name.endsWith(GENERATED_SCRIPT_SUFFIX);
}

function quotedTokens(text) {
  return [...text.matchAll(QUOTED_TOKEN)].map((match) => match[TOKEN_GROUP]);
}

/** The known icon names a file uses (see the header). `knownNames` is a Set. */
export function findUsedIconNames(fileName, text, knownNames) {
  const tokens = fileName.endsWith(TEMPLATE_EXTENSION)
    ? quotedTokens(text.replace(HTML_COMMENT, ''))
    : lexScript(text).strings.flatMap(({ raw }) => [raw, ...quotedTokens(raw)]);
  return new Set(tokens.filter((token) => knownNames.has(token)));
}

function unknownInTemplate(text, knownNames) {
  const findings = [];
  for (const event of scanTemplate(text)) {
    if (event.type !== OPEN_EVENT) continue;
    for (const [name, value] of event.attributes) {
      if (!isIconNameInput(event.name, name)) continue;
      for (const icon of iconNamesOfAttribute(name, value)) {
        if (!knownNames.has(icon)) findings.push({ line: event.line, column: event.column, element: event.name, detail: icon });
      }
    }
  }
  return findings;
}

/** Literal icon names in a file's templates that the map does not know, as `{ line, column, element, detail }`. */
export function findUnknownIconNames(fileName, text, knownNames) {
  if (fileName.endsWith(TEMPLATE_EXTENSION)) return unknownInTemplate(text, knownNames);
  return extractInlineTemplates(text).flatMap((template) => unknownInTemplate(template.text, knownNames)
    .map((finding) => ({ ...finding, line: finding.line + template.line - LINE_OFFSET })));
}
