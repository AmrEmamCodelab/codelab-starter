// A symbol index of the names declared in the repository's sources (C# and PowerShell types, members and enum
// members; TypeScript declarations), and the decision of which inline code spans name a type or member at all.
import { readFileSync } from 'node:fs';
import { join, posix } from 'node:path';
import { PATH_SEPARATOR } from './constants.mjs';

const CSHARP_EXTENSION = '.cs';
const POWERSHELL_EXTENSIONS = new Set(['.ps1', '.psm1']);
const CSHARP_DECLARATION = /\b(?:class|interface|struct|enum|record)\s+(?:class\s+|struct\s+)?@?([A-Za-z_]\w*)/g;
const CSHARP_DELEGATE = /\bdelegate\s+[\w<>[\],.?\s]+?\s+([A-Za-z_]\w*)\s*[<(]/g;
// Properties, fields, constants and methods: "<modifiers> <type> Name {" / "= " / "=>" / ";" / "(".
const CSHARP_MEMBER = /\b(?:public|internal|protected|private)\s+(?:(?:static|virtual|override|required|abstract|sealed|new|readonly|const|async|partial|extern|unsafe|volatile)\s+)*[\w<>[\],.?]+\s+([A-Z]\w*)\s*(?:\{|=>|=|;|\(|<)/g;
const POWERSHELL_DECLARATION = /^\s*(?:enum|class|function)\s+([A-Za-z_][\w-]*)/gim;
const TYPESCRIPT_DECLARATION = /\b(?:class|interface|enum|type|function|const|let)\s+([A-Za-z_$][\w$]*)/g;
const ENUM_BODY = /\benum\s+[A-Za-z_]\w*[^{]*\{([^}]*)\}/g;
const ENUM_COMMENTS = /\/\/.*$|\/\*[\s\S]*?\*\/|#.*$|\[[^\]]*\]/gm;
const ENUM_SEPARATORS = /[,\n]/;
const ENUM_MEMBER = /^([A-Za-z_]\w*)/;
const ATTRIBUTE_SUFFIX = 'Attribute';

const ATTRIBUTE_SPAN = /^\[([A-Z][A-Za-z0-9]*)(?:\(.*\))?\]$/;
const GENERIC_ARGUMENTS = /<\s*[A-Z][\w\s,<>?]*>$/;
const CALL_SUFFIX = /\(\)$/;
const QUALIFIED_MEMBER = /^([A-Z][A-Za-z0-9]*)(?:\.[A-Za-z_]\w*)+$/;
const TYPE_NAME = /^[A-Z][A-Za-z0-9]*$/;
const HAS_LOWERCASE = /[a-z]/;
const INTERFACE_NAME = /^I[A-Z][a-z]/;
/** "HasPermission": two or more capitalised words. "[Unreleased]" (a changelog heading) is one word: not an attribute. */
const MULTI_WORD_NAME = /^[A-Z][a-z0-9]+[A-Z]/;

function isIndexedSource(relativePath, { sourceRoots, sourceExtensions, excludedDirectories }) {
  const segments = relativePath.split(PATH_SEPARATOR);
  if (!sourceRoots.includes(segments[0])) return false;
  if (segments.slice(0, -1).some((segment) => excludedDirectories.has(segment))) return false;
  return sourceExtensions.has(posix.extname(relativePath).toLowerCase());
}

function addEnumMembers(text, names) {
  for (const match of text.matchAll(ENUM_BODY)) {
    for (const part of match[1].replace(ENUM_COMMENTS, '').split(ENUM_SEPARATORS)) {
      const member = ENUM_MEMBER.exec(part.trim());
      if (member !== null) names.add(member[1]);
    }
  }
}

/** The names one source file declares. */
export function declaredNames(relativePath, text) {
  const names = new Set();
  const extension = posix.extname(relativePath).toLowerCase();
  let patterns = [TYPESCRIPT_DECLARATION];
  if (extension === CSHARP_EXTENSION) patterns = [CSHARP_DECLARATION, CSHARP_DELEGATE, CSHARP_MEMBER];
  else if (POWERSHELL_EXTENSIONS.has(extension)) patterns = [POWERSHELL_DECLARATION];
  for (const pattern of patterns) {
    for (const match of text.matchAll(pattern)) names.add(match[1]);
  }
  if (extension === CSHARP_EXTENSION || POWERSHELL_EXTENSIONS.has(extension)) addEnumMembers(text, names);
  return names;
}

/**
 * Indexes every declared name under the configured source roots (bin, obj, node_modules... excluded).
 * @param {{ root: string, files: string[] }} index the repository index
 * @param {{ sourceRoots: string[], sourceExtensions: Set<string>, excludedDirectories: Set<string> }} typeConfig
 * @returns {Set<string>}
 */
export function buildSymbolIndex(index, typeConfig) {
  const symbols = new Set();
  for (const file of index.files) {
    if (!isIndexedSource(file, typeConfig)) continue;
    for (const name of declaredNames(file, readFileSync(join(index.root, file), 'utf8'))) symbols.add(name);
  }
  return symbols;
}

/**
 * The names a code span refers to, or null when the span does not look like a type.
 * Looks like a type: "[HasPermission]" (an attribute of two or more words), "Name<T>" (generic), a PascalCase name
 * ending with a configured suffix ("ApiService"), an interface name ("IUserDirectory"), or a configured extra name.
 * A trailing "()" is ignored. Every name may also resolve as "<name>Attribute" (docs name attributes without it).
 * @param {string} content
 * @param {{ suffixes: string[], extraTypeNames: Set<string> }} typeConfig
 * @returns {{ display: string, names: string[] } | null}
 */
export function classifyTypeCandidate(content, { suffixes, extraTypeNames }) {
  const text = content.trim();
  const attribute = ATTRIBUTE_SPAN.exec(text);
  if (attribute !== null) {
    return MULTI_WORD_NAME.test(attribute[1]) ? withAttributeSpelling(attribute[1]) : null;
  }
  const withoutCall = text.replace(CALL_SUFFIX, '');
  const generic = GENERIC_ARGUMENTS.test(withoutCall);
  // "SlaOptions.MaxEscalations", "PermissionKeys.ServiceRequests.Approve": the type is the first segment.
  const qualified = QUALIFIED_MEMBER.exec(withoutCall);
  const name = qualified !== null ? qualified[1] : withoutCall.replace(GENERIC_ARGUMENTS, '');
  if (!TYPE_NAME.test(name) || !HAS_LOWERCASE.test(name)) return null;
  const qualifies = generic
    || extraTypeNames.has(name)
    || INTERFACE_NAME.test(name)
    || suffixes.some((suffix) => name.length > suffix.length && name.endsWith(suffix));
  return qualifies ? withAttributeSpelling(name) : null;
}

function withAttributeSpelling(name) {
  return { display: name, names: name.endsWith(ATTRIBUTE_SUFFIX) ? [name] : [name, `${name}${ATTRIBUTE_SUFFIX}`] };
}

/** True when one of the names is declared in the repository or is a known external (framework) symbol. */
export function typeResolves(candidate, symbols, externalSymbols) {
  return candidate.names.some((name) => symbols.has(name) || externalSymbols.has(name));
}
