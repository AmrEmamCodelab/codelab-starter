// Which translation keys the code uses, read lexically (no compiler). A key is a dotted word list such as
// `patterns.list.empty`. It counts as used when it is written literally:
// - in a template (an `.html` file or an inline `template:`), piped through the translate pipe: {{ 'a.b' | t }},
//   [label]="'a.b' | t: { n: 1 }";
// - in a template, as the value of a `<x>Key` input: labelKey="a.b", [titleKey]="'a.b'";
// - in TypeScript, as the first argument of translate(...) or instant(...);
// - in a translation-key catalogue (`*translation-keys.ts`): every string literal shaped like a key.
// Keys built at run time (`${prefix}${code}`, a ternary before the pipe) are not seen; list them in a catalogue.
import { extractInlineTemplates } from '../permission-gates/inline-templates.mjs';
import { scanTemplate } from '../permission-gates/template-scanner.mjs';
import { literalValuesOf } from '../shared/template-values.mjs';
import { lexScript } from '../shared/script-lexer.mjs';
import { TEMPLATE_EXTENSION, positionFinder } from '../shared/source-files.mjs';

/** The translate pipe names looked for by default (`t` in the starter, `translate` in many apps). */
export const DEFAULT_TRANSLATE_PIPES = Object.freeze(['t', 'translate']);
/** Files that list translation keys as constants. */
export const KEY_CATALOGUE_SUFFIX = 'translation-keys.ts';
const KEY_SOURCE = '[A-Za-z][\\w-]*(?:\\.[\\w-]+)+';
const KEY_SHAPE = new RegExp(`^${KEY_SOURCE}$`);
const KEY_INPUT = /^\[?[a-z]\w*Key\]?$/;
const TRANSLATE_CALL = new RegExp(`\\b(?:translate|instant)\\(\\s*(['"\`])(${KEY_SOURCE})\\1`, 'g');
const KEY_GROUP = 2;
const LINE_OFFSET = 1;
const OPEN_EVENT = 'open';
const ESCAPE_SPECIALS = /[.*+?^${}()|[\]\\]/g;

/** True when `text` is shaped like a translation key. */
export function isKeyShaped(text) {
  return KEY_SHAPE.test(text);
}

function pipePattern(pipes) {
  const names = pipes.map((pipe) => pipe.replace(ESCAPE_SPECIALS, '\\$&')).join('|');
  return new RegExp(`(['"])(${KEY_SOURCE})\\1\\s*\\|(?!\\|)\\s*(?:${names})(?![\\w$])`, 'g');
}

function keysInTemplate(text, pipes) {
  const positionOf = positionFinder(text);
  const piped = [...text.matchAll(pipePattern(pipes))].map((match) => ({ key: match[KEY_GROUP], line: positionOf(match.index).line }));
  const inputs = scanTemplate(text).filter((event) => event.type === OPEN_EVENT).flatMap((event) => [...event.attributes]
    .filter(([name, value]) => KEY_INPUT.test(name) && value !== null)
    .flatMap(([name, value]) => literalValuesOf(name, value))
    .filter(isKeyShaped)
    .map((key) => ({ key, line: event.line })));
  return [...piped, ...inputs];
}

function keysInScript(fileName, text, pipes) {
  const positionOf = positionFinder(text);
  const { code, strings } = lexScript(text);
  const calls = [...code.matchAll(TRANSLATE_CALL)].map((match) => ({ key: match[KEY_GROUP], line: positionOf(match.index).line }));
  const catalogue = fileName.endsWith(KEY_CATALOGUE_SUFFIX)
    ? strings.filter((literal) => !literal.substitution && isKeyShaped(literal.raw)).map((literal) => ({ key: literal.raw, line: positionOf(literal.offset).line }))
    : [];
  const templates = extractInlineTemplates(text).flatMap((template) => keysInTemplate(template.text, pipes)
    .map((usage) => ({ ...usage, line: usage.line + template.line - LINE_OFFSET })));
  return [...templates, ...calls, ...catalogue];
}

/**
 * The translation keys a file uses, as `{ key, line }` (in source order per kind; duplicates kept).
 * @param {string} fileName decides the language: `.html` or TypeScript
 * @param {string} text
 * @param {{ pipes?: string[] }} [options] the translate pipe names
 */
export function findUsedKeys(fileName, text, { pipes = DEFAULT_TRANSLATE_PIPES } = {}) {
  return fileName.endsWith(TEMPLATE_EXTENSION) ? keysInTemplate(text, pipes) : keysInScript(fileName, text, pipes);
}
