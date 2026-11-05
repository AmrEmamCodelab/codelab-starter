// The product prefix: validation (Codelab.Security SecurityProtocol.IsValidPrefix plus reserved names) and setting
// it at a prefixTargets jsonPath while keeping the file's indentation, BOM and final newline.
import { JSON_INDENT, JSON_PATH_SEPARATOR, PRODUCT_PREFIX_PATTERN, RESERVED_PREFIXES, UTF8_BOM } from './constants.mjs';

const INDENT_PROBE = /^[{[]\r?\n([ \t]+)\S/;
const CRLF = '\r\n';
const LF = '\n';

/** Why a prefix is refused, or null when it is valid. */
export function refusalForPrefix(prefix) {
  if (typeof prefix !== 'string' || !PRODUCT_PREFIX_PATTERN.test(prefix)) {
    return `Product prefix '${prefix}' is invalid: 2-16 characters, an upper-case letter A-Z first, then A-Z or 0-9.`;
  }
  if (RESERVED_PREFIXES.includes(prefix)) {
    return `Product prefix '${prefix}' is reserved (the starter's own prefix or a golden-vector prefix).`;
  }
  return null;
}

/** Splits a prefixTargets jsonPath ("Codelab:Security:Protocol:Prefix" or "security.protocolPrefix"). */
export function splitJsonPath(jsonPath) {
  return jsonPath.split(JSON_PATH_SEPARATOR);
}

/**
 * Sets `value` at the path inside a parsed JSON object, creating missing objects.
 * @returns {{ previous: unknown, created: boolean }}
 */
export function setAtPath(document, segments, value) {
  let node = document;
  let created = false;
  for (const segment of segments.slice(0, -1)) {
    if (node[segment] === undefined) {
      node[segment] = {};
      created = true;
    }
    if (node[segment] === null || typeof node[segment] !== 'object' || Array.isArray(node[segment])) {
      throw new Error(`'${segment}' is not an object.`);
    }
    node = node[segment];
  }
  const last = segments[segments.length - 1];
  const previous = node[last];
  node[last] = value;
  return { previous, created: created || previous === undefined };
}

/**
 * Sets a JSON value in file text, keeping its indentation, line endings, BOM and final newline.
 * Throws when the text is not strict JSON (comments are not supported) or the path crosses a non-object.
 * @returns {{ text: string, previous: unknown, created: boolean }}
 */
export function setJsonValue(text, jsonPath, value) {
  const bom = text.startsWith(UTF8_BOM) ? UTF8_BOM : '';
  const body = text.slice(bom.length);
  const document = JSON.parse(body);
  const result = setAtPath(document, splitJsonPath(jsonPath), value);
  const indent = INDENT_PROBE.exec(body)?.[1] ?? ' '.repeat(JSON_INDENT);
  const newline = body.includes(CRLF) ? CRLF : LF;
  const finalNewline = body.endsWith(LF) ? newline : '';
  const serialized = JSON.stringify(document, null, indent).split(LF).join(newline);
  return { text: bom + serialized + finalNewline, previous: result.previous, created: result.created };
}
